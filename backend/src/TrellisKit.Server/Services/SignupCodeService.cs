namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Sign-up codes: member validation and redemption (anti-enumeration, rate limited per IP and per account,
    /// race-safe), and every admin operation including batches, revocation, outcomes, and CSV export.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class SignupCodeService
    {
        #region Public-Members

        /// <summary>
        /// Message shown to members for every invalid code, whatever the reason.
        /// </summary>
        public const string InvalidMessage = "This code isn't valid.";

        /// <summary>
        /// <see cref="SignupCodeValidationResponse.Kind"/> of a free-membership code.
        /// </summary>
        public const string KindFreeMembership = "FreeMembership";

        /// <summary>
        /// <see cref="SignupCodeValidationResponse.Kind"/> of a discount code.
        /// </summary>
        public const string KindDiscount = "Discount";

        /// <summary>
        /// Alphabet of generated codes (no 0, O, 1, I, or L).
        /// </summary>
        public const string GeneratedAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        #endregion

        #region Private-Members

        private static readonly Regex _CodePattern = new Regex("^[A-Z0-9-]{6,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly TimeSpan _Hour = TimeSpan.FromHours(1);
        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly PropertyContext _Property;
        private readonly MembershipService _Membership;
        private readonly TokenService _Tokens;
        private readonly EmailService _Email;
        private readonly AuditService _Audit;
        private readonly SlidingWindowRateLimiter _Limiter;
        private readonly AppLogger _Logger;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="property">Active property context (join link base URL).</param>
        /// <param name="membership">Membership service.</param>
        /// <param name="tokens">Token service (cache invalidation).</param>
        /// <param name="email">Email service.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="limiter">Rate limiter.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SignupCodeService(DatabaseDriverBase database, AppSettings settings, PropertyContext property, MembershipService membership, TokenService tokens, EmailService email,
            AuditService audit, SlidingWindowRateLimiter limiter, AppLogger logger, EnumerationService enumeration, MetadataService metadata)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Email = email ?? throw new ArgumentNullException(nameof(email));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Describe a code's grant in words.
        /// </summary>
        /// <param name="grantType">Grant type.</param>
        /// <param name="months">Months.</param>
        /// <param name="endUtc">Fixed end.</param>
        /// <returns>Summary, for example "Free membership for 12 months".</returns>
        public static string GrantSummary(GrantTypeEnum grantType, int? months, DateTime? endUtc)
        {
            switch (grantType)
            {
                case GrantTypeEnum.Lifetime:
                    return "Lifetime free membership";
                case GrantTypeEnum.FixedEndDate:
                    return "Free membership until " + (endUtc ?? DateTime.UtcNow).ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                default:
                    int m = months ?? 0;
                    return "Free membership for " + m + " month" + (m == 1 ? String.Empty : "s");
            }
        }

        /// <summary>
        /// Describe a discount in words.
        /// </summary>
        /// <param name="percent">Percent off, or null.</param>
        /// <param name="amountCents">Amount off in cents (USD), or null.</param>
        /// <param name="duration">Duration.</param>
        /// <returns>Summary, for example "40% off the first year" or "$10.00 off every renewal".</returns>
        public static string DiscountSummary(int? percent, int? amountCents, DiscountDurationEnum? duration)
        {
            string off = percent != null
                ? percent.Value.ToString(CultureInfo.InvariantCulture) + "% off"
                : "$" + ((amountCents ?? 0) / 100m).ToString("0.00", CultureInfo.InvariantCulture) + " off";
            return off + (duration == DiscountDurationEnum.Forever ? " every renewal" : " the first year");
        }

        /// <summary>
        /// Describe what a code gives: its free membership or its discount.
        /// </summary>
        /// <param name="code">Code.</param>
        /// <returns>Summary.</returns>
        public static string Summary(SignupCode code)
        {
            ArgumentNullException.ThrowIfNull(code);
            if (code.GrantType == GrantTypeEnum.Discount) return DiscountSummary(code.DiscountPercent, code.DiscountAmountCents, code.DiscountDuration);
            return GrantSummary(code.GrantType, code.GrantMonths, code.GrantEndUtc);
        }

        /// <summary>
        /// Normalize a code as entered: trim and upper-case.
        /// </summary>
        /// <param name="code">Code.</param>
        /// <returns>Normalized code, or null when blank.</returns>
        public static string? Normalize(string? code)
        {
            if (String.IsNullOrWhiteSpace(code)) return null;
            return code.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Generate a random code from the unambiguous alphabet.
        /// </summary>
        /// <param name="length">Length.</param>
        /// <returns>Code.</returns>
        public static string Generate(int length)
        {
            return RandomNumberGenerator.GetString(GeneratedAlphabet, Math.Clamp(length, 8, 32));
        }

        /// <summary>
        /// Check a code for the signed-in member without redeeming it.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="code">Code.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result; invalid codes return valid false with the same message for every reason.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public async Task<SignupCodeValidationResponse> ValidateAsync(string userId, string? code, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            DateTime now = DateTime.UtcNow;
            if (!Acquire(userId, info, now))
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeValidations, "outcome", "rate_limited");
                throw RateLimited(RetryAfter(userId, info, now));
            }
            SignupCode? found = await FindRedeemableAsync(code, now, token).ConfigureAwait(false);
            if (found == null)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeValidations, "outcome", "invalid");
                return new SignupCodeValidationResponse { Valid = false, Message = InvalidMessage };
            }
            if (await _Database.SignupCodeRedemptions.ReadByCodeAndUserAsync(found.Id, userId, token).ConfigureAwait(false) != null)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeValidations, "outcome", "already_redeemed");
                return new SignupCodeValidationResponse { Valid = false, Message = "You have already redeemed this code." };
            }

            if (found.GrantType == GrantTypeEnum.Discount)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeValidations, "outcome", "valid");
                return new SignupCodeValidationResponse
                {
                    Valid = true,
                    Name = found.Name,
                    Description = found.Description,
                    Kind = KindDiscount,
                    GrantSummary = Summary(found),
                    DiscountSummary = Summary(found)
                };
            }

            MembershipState state = await _Membership.GetStateAsync(userId, token).ConfigureAwait(false);
            DateTime? paidEnd = MembershipService.PaidCoverageEnd(state.Entitlements, now);
            DateTime starts = paidEnd != null && paidEnd.Value > now ? paidEnd.Value : now;
            DateTime? ends = Ends(found, starts);
            if (ends != null && ends.Value <= starts)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeValidations, "outcome", "invalid");
                return new SignupCodeValidationResponse { Valid = false, Message = InvalidMessage };
            }

            TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeValidations, "outcome", "valid");
            return new SignupCodeValidationResponse
            {
                Valid = true,
                Name = found.Name,
                Description = found.Description,
                Kind = KindFreeMembership,
                GrantSummary = GrantSummary(found.GrantType, found.GrantMonths, found.GrantEndUtc),
                MembershipEndsUtc = ends,
                Note = paidEnd != null ? "Your free membership starts when your current membership ends on " + paidEnd.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) + "." : null
            };
        }

        /// <summary>
        /// Redeem a code for the signed-in member, in one race-safe transaction.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="code">Code.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Membership and grant.</returns>
        /// <exception cref="ApiErrorException">400 InvalidCode (every reason), 400 DiscountCodeAtCheckout (a discount code, used at web
        /// checkout instead), 409 AlreadyRedeemed, or 429 RateLimited.</exception>
        public async Task<SignupCodeRedeemResponse> RedeemAsync(string userId, string? code, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("signup_code.redeem"))
            {
                DateTime now = DateTime.UtcNow;
                if (!Acquire(userId, info, now)) throw Redemption("rate_limited", RateLimited(RetryAfter(userId, info, now)));

                string? normalized = Normalize(code);
                SignupCode? found = normalized == null || normalized.Length > 64 ? null : await _Database.SignupCodes.ReadByCodeAsync(normalized, token).ConfigureAwait(false);
                if (found == null) throw Redemption("invalid", InvalidCode());
                SignupCodeStatusEnum status = found.StatusAt(now);
                if (status != SignupCodeStatusEnum.Active) throw Redemption(status == SignupCodeStatusEnum.Exhausted ? "exhausted" : "invalid", InvalidCode());
                if (found.GrantType == GrantTypeEnum.Discount) throw Redemption("discount", new ApiErrorException(400, "DiscountCodeAtCheckout", "This code is a discount. Use it at checkout when you buy your membership on the website."));
                if (await _Database.SignupCodeRedemptions.ReadByCodeAndUserAsync(found.Id, userId, token).ConfigureAwait(false) != null) throw Redemption("already_redeemed", AlreadyRedeemed());

                User user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false) ?? throw new ApiErrorException(401, "Unauthorized", "Sign in again.");
                MembershipState state = await _Membership.GetStateAsync(userId, token).ConfigureAwait(false);
                DateTime? paidEnd = MembershipService.PaidCoverageEnd(state.Entitlements, now);
                DateTime starts = paidEnd != null && paidEnd.Value > now ? paidEnd.Value : now;
                DateTime? ends = Ends(found, starts);
                if (ends != null && ends.Value <= starts) throw Redemption("invalid", InvalidCode());

                SignupCodeRedemption redemption = new SignupCodeRedemption
                {
                    Id = IdGenerator.GenerateSignupCodeRedemptionId(),
                    SignupCodeId = found.Id,
                    UserId = userId,
                    RedeemedUtc = now,
                    MembershipStartsUtc = starts,
                    MembershipEndsUtc = ends,
                    SourceIp = info.SourceIp,
                    UserAgent = info.UserAgent,
                    IsNewAccount = user.CreatedUtc >= now.AddHours(-1)
                };
                Entitlement entitlement = new Entitlement
                {
                    Id = IdGenerator.GenerateEntitlementId(),
                    UserId = userId,
                    Source = MembershipSourceEnum.SignupCode,
                    SourceId = redemption.Id,
                    StartsUtc = starts,
                    EndsUtc = ends,
                    Reason = "Code " + found.Code,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                redemption.EntitlementId = entitlement.Id;

                bool redeemed;
                try
                {
                    redeemed = await _Database.SignupCodes.TryRedeemAsync(redemption, entitlement, now, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    throw Redemption("already_redeemed", AlreadyRedeemed());
                }
                if (!redeemed)
                {
                    if (await _Database.SignupCodeRedemptions.ReadByCodeAndUserAsync(found.Id, userId, token).ConfigureAwait(false) != null) throw Redemption("already_redeemed", AlreadyRedeemed());
                    throw Redemption("exhausted", InvalidCode());
                }

                _Tokens.InvalidateUser(userId);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeRedemptions, "outcome", "success");
                activity?.SetTag("outcome", "success");
                await _Audit.WriteAsync(info, "signupCode.redeem", "SignupCode", found.Id, user.DisplayName + " redeemed " + found.Code, token).ConfigureAwait(false);
                await _Email.SendToMemberAsync("free_membership_granted", user, "code_grant:" + found.Id + ":" + user.Id, token, ("grantSummary", GrantSummary(found.GrantType, found.GrantMonths, found.GrantEndUtc)), ("link", _Email.Link("/app"))).ConfigureAwait(false);
                return new SignupCodeRedeemResponse
                {
                    Membership = await _Membership.GetSummaryAsync(userId, token).ConfigureAwait(false),
                    GrantSummary = GrantSummary(found.GrantType, found.GrantMonths, found.GrantEndUtc),
                    MembershipEndsUtc = ends
                };
            }
        }

        /// <summary>
        /// Resolve a discount code the member entered for web checkout (rate limited like validation).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="code">Code as entered.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The active discount code.</returns>
        /// <exception cref="ApiErrorException">400 InvalidCode (every reason: unknown, inactive, not a discount, or already used by
        /// the member) or 429 RateLimited.</exception>
        public async Task<SignupCode> RequireDiscountForCheckoutAsync(string userId, string? code, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            DateTime now = DateTime.UtcNow;
            if (!Acquire(userId, info, now)) throw RateLimited(RetryAfter(userId, info, now));
            SignupCode? found = await FindRedeemableAsync(code, now, token).ConfigureAwait(false);
            if (found == null || found.GrantType != GrantTypeEnum.Discount) throw InvalidCode();
            if (await _Database.SignupCodeRedemptions.ReadByCodeAndUserAsync(found.Id, userId, token).ConfigureAwait(false) != null) throw InvalidCode();
            return found;
        }

        /// <summary>
        /// Store the Stripe coupon created for a discount code.
        /// </summary>
        /// <param name="code">Code.</param>
        /// <param name="couponId">Stripe coupon id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">code is null.</exception>
        public async Task SaveCouponAsync(SignupCode code, string couponId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(code);
            code.StripeCouponId = couponId;
            code.UpdatedUtc = DateTime.UtcNow;
            await _Database.SignupCodes.UpdateAsync(code, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record that a member used a discount code, after Stripe reports the checkout complete. Idempotent: a second call
        /// for the same code and member records nothing. When the code ran out or was deactivated in the meantime the
        /// purchase still stands (Stripe already applied the discount); nothing is recorded and a warning is logged.
        /// </summary>
        /// <param name="signupCodeId">Code identifier from the checkout session metadata.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a redemption was recorded.</returns>
        public async Task<bool> RecordDiscountAsync(string? signupCodeId, string? userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(signupCodeId) || String.IsNullOrWhiteSpace(userId)) return false;
            SignupCode? code = await _Database.SignupCodes.ReadAsync(signupCodeId, token).ConfigureAwait(false);
            if (code == null || code.GrantType != GrantTypeEnum.Discount) return false;
            if (await _Database.SignupCodeRedemptions.ReadByCodeAndUserAsync(code.Id, userId, token).ConfigureAwait(false) != null) return false;
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null) return false;
            DateTime now = DateTime.UtcNow;
            SignupCodeRedemption redemption = new SignupCodeRedemption
            {
                Id = IdGenerator.GenerateSignupCodeRedemptionId(),
                SignupCodeId = code.Id,
                UserId = userId,
                RedeemedUtc = now,
                MembershipStartsUtc = now,
                MembershipEndsUtc = null,
                EntitlementId = String.Empty,
                IsNewAccount = user.CreatedUtc >= now.AddHours(-1)
            };
            bool recorded;
            try
            {
                recorded = await _Database.SignupCodes.TryRecordDiscountAsync(redemption, now, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                return false;
            }
            if (!recorded)
            {
                _Logger.Warn("[SignupCodeService] discount code " + code.Code + " was used at checkout but is no longer redeemable; the purchase stands");
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeRedemptions, "outcome", "discount_not_recorded");
                return false;
            }
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeRedemptions, "outcome", "discount");
            await _Audit.WriteAsync(null, null, "signupCode.discount", "SignupCode", code.Id, user.DisplayName + " used discount code " + code.Code + " at checkout", token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Admin view of a code.
        /// </summary>
        /// <param name="code">Code.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>View.</returns>
        public SignupCodeView ToView(SignupCode code, DateTime nowUtc)
        {
            SignupCodeView view = new SignupCodeView();
            Fill(view, code, nowUtc);
            return view;
        }

        /// <summary>
        /// GET /api/v1/admin/signup-codes (collection <see cref="PhaseOneCollections.SignupCodes"/>).
        /// </summary>
        /// <param name="query">Enumeration query; filters status, batchId; labels replace the old tag filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with labels and tags (staff view).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<SignupCodeView>> ListAsync(EnumerationQuery query, CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            EnumerationResult<SignupCode> page = await CodesPageAsync(_Enumeration.Prepare(query, PhaseOneCollections.SignupCodes, true), now, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(page.Objects, now, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Create a code (vanity or generated).
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="actor">Caller (labels and tags; staff for sign-up codes).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 409 CodeInUse.</exception>
        public async Task<SignupCodeView> CreateAsync(SignupCodeCreateRequest request, RequestInfo info, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? vanity = Normalize(request.Code);
            if (vanity != null && !_CodePattern.IsMatch(vanity)) errors["code"] = "Use 6 to 40 of A-Z, 0-9, and hyphen.";
            SignupCode code = new SignupCode
            {
                Id = IdGenerator.GenerateSignupCodeId(),
                Code = vanity ?? Generate(_Settings.SignupCodes.GeneratedLength),
                CreatedByUserId = info.Context?.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            ApplyCommon(code, request.Name, request.Description, request.Notes, request.MaxRedemptions, request.StartsUtc, request.ExpiresUtc, errors, true);
            ApplyGrant(code, request.GrantType, request.GrantMonths, request.GrantEndUtc, request.DiscountPercent, request.DiscountAmountCents, request.DiscountDuration, errors, now);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            ObjectMetadata metadata = await _Metadata.PrepareNewAsync(actor, TargetTypeEnum.SignupCode, request.Labels, request.Tags, token).ConfigureAwait(false);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await _Database.SignupCodes.CreateAsync(code, token).ConfigureAwait(false);
                    break;
                }
                catch (DuplicateRecordException)
                {
                    if (vanity != null || attempt >= 5) throw new ApiErrorException(409, "CodeInUse", "That code is already in use.");
                    code.Code = Generate(_Settings.SignupCodes.GeneratedLength);
                }
            }
            if (metadata.Labels.Count > 0 || metadata.Tags.Count > 0) await _Database.Metadata.WriteAsync(TargetTypeEnum.SignupCode, code.Id, metadata, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "signupCode.create", "SignupCode", code.Id, "Created " + code.Code + " (" + Summary(code) + ")", token).ConfigureAwait(false);
            return await ViewAsync(code, now, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Code detail with redemptions per day (last 90 days) and outcomes.
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">404.</exception>
        public async Task<SignupCodeDetail> GetAsync(string id, CancellationToken token = default)
        {
            SignupCode code = await RequireAsync(id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            SignupCodeDetail detail = new SignupCodeDetail();
            Fill(detail, code, now);
            DateTime from = now.Date.AddDays(-89);
            Dictionary<string, long> counts = (await _Database.SignupCodeRedemptions.CountByDayAsync(code.Id, from, token).ConfigureAwait(false)).ToDictionary(d => d.Date, d => d.Count);
            for (DateTime day = from; day <= now.Date; day = day.AddDays(1))
            {
                string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                detail.RedemptionsByDay.Add(new DailyCount { Date = key, Count = counts.TryGetValue(key, out long c) ? c : 0 });
            }
            detail.Outcomes = await _Database.SignupCodeRedemptions.CountOutcomesAsync(code.Id, now, token).ConfigureAwait(false);
            await _Metadata.AttachOneAsync(TargetTypeEnum.SignupCode, detail, code.Id, true, token).ConfigureAwait(false);
            return detail;
        }

        /// <summary>
        /// Update a code. Code and grant fields change only while no redemptions exist.
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">camelCase keys present in the body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="actor">Caller (labels and tags; staff for sign-up codes).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404, 409 CodeLocked, or 409 CodeInUse.</exception>
        public async Task<SignupCodeView> UpdateAsync(string id, SignupCodeUpdateRequest request, HashSet<string> present, RequestInfo info, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            SignupCode code = await RequireAsync(id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> errors = new Dictionary<string, string>();

            bool touchesGrant = present.Contains("grantType") || present.Contains("grantMonths") || present.Contains("grantEndUtc") ||
                present.Contains("discountPercent") || present.Contains("discountAmountCents") || present.Contains("discountDuration");
            bool touchesLocked = present.Contains("code") || touchesGrant;
            if (touchesLocked && code.RedemptionCount > 0) throw new ApiErrorException(409, "CodeLocked", "The code, its grant, and its discount cannot change after the first redemption.");

            if (present.Contains("code"))
            {
                string? normalized = Normalize(request.Code);
                if (normalized == null || !_CodePattern.IsMatch(normalized)) errors["code"] = "Use 6 to 40 of A-Z, 0-9, and hyphen.";
                else code.Code = normalized;
            }
            ApplyCommon(code,
                present.Contains("name") ? request.Name : code.Name,
                present.Contains("description") ? request.Description : code.Description,
                present.Contains("notes") ? request.Notes : code.Notes,
                present.Contains("maxRedemptions") ? request.MaxRedemptions : code.MaxRedemptions,
                present.Contains("startsUtc") ? request.StartsUtc : code.StartsUtc,
                present.Contains("expiresUtc") ? request.ExpiresUtc : code.ExpiresUtc,
                errors, false);
            if (touchesGrant)
            {
                int? oldPercent = code.DiscountPercent;
                int? oldAmount = code.DiscountAmountCents;
                DiscountDurationEnum? oldDuration = code.DiscountDuration;
                ApplyGrant(code,
                    present.Contains("grantType") ? request.GrantType : code.GrantType.ToString(),
                    present.Contains("grantMonths") ? request.GrantMonths : code.GrantMonths,
                    present.Contains("grantEndUtc") ? request.GrantEndUtc : code.GrantEndUtc,
                    present.Contains("discountPercent") ? request.DiscountPercent : code.DiscountPercent,
                    present.Contains("discountAmountCents") ? request.DiscountAmountCents : code.DiscountAmountCents,
                    present.Contains("discountDuration") ? request.DiscountDuration : code.DiscountDuration?.ToString(),
                    errors, now);
                // A changed discount needs a new Stripe coupon (coupons cannot be edited).
                if (code.DiscountPercent != oldPercent || code.DiscountAmountCents != oldAmount || code.DiscountDuration != oldDuration) code.StripeCouponId = null;
            }
            if (present.Contains("isActive") && request.IsActive != null) code.IsActive = request.IsActive.Value;
            if (code.MaxRedemptions != null && code.MaxRedemptions.Value < code.RedemptionCount) errors["maxRedemptions"] = "Cannot be below the current redemption count (" + code.RedemptionCount + ").";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            ArgumentNullException.ThrowIfNull(actor);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.SignupCode), token).ConfigureAwait(false);

            code.UpdatedUtc = now;
            try
            {
                await _Database.SignupCodes.UpdateAsync(code, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "CodeInUse", "That code is already in use.");
            }
            await _Metadata.ApplyAsync(actor, TargetTypeEnum.SignupCode, code.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "signupCode.update", "SignupCode", code.Id, "Updated " + code.Code + ": " + String.Join(", ", present.OrderBy(p => p)), token).ConfigureAwait(false);
            return await ViewAsync(code, now, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a code that has never been redeemed, with its labels and tags. A redeemed code keeps its redemption history
        /// and is deactivated instead.
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or 409 CodeRedeemed.</exception>
        public async Task DeleteAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            SignupCode code = await RequireAsync(id, token).ConfigureAwait(false);
            if (code.RedemptionCount > 0 || !await _Database.SignupCodes.DeleteUnredeemedAsync(code.Id, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "CodeRedeemed", "This code has been redeemed, so it is kept for its redemption history. Deactivate it instead.");
            await _Database.Metadata.DeleteAsync(TargetTypeEnum.SignupCode, code.Id, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "signupCode.delete", "SignupCode", code.Id, "Deleted " + code.Code, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Deactivate a code (existing memberships continue).
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<SignupCodeView> DeactivateAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            SignupCode code = await RequireAsync(id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (code.IsActive)
            {
                code.IsActive = false;
                code.UpdatedUtc = now;
                await _Database.SignupCodes.UpdateAsync(code, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "signupCode.deactivate", "SignupCode", code.Id, "Deactivated " + code.Code, token).ConfigureAwait(false);
            }
            return await ViewAsync(code, now, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a batch of single-use codes.
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="actor">Caller (labels and tags; staff for sign-up codes).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Batch.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<SignupCodeBatchResponse> CreateBatchAsync(SignupCodeBatchRequest request, RequestInfo info, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            int quantity = request.Quantity ?? 0;
            if (quantity < 1 || quantity > 1000) errors["quantity"] = "Use 1 to 1000.";
            SignupCode template = new SignupCode();
            ApplyCommon(template, request.Name, null, null, 1, null, request.ExpiresUtc, errors, true);
            ApplyGrant(template, request.GrantType, request.GrantMonths, request.GrantEndUtc, request.DiscountPercent, request.DiscountAmountCents, request.DiscountDuration, errors, now);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            ObjectMetadata metadata = await _Metadata.PrepareNewAsync(actor, TargetTypeEnum.SignupCode, request.Labels, request.Tags, token).ConfigureAwait(false);

            SignupCodeBatch batch = new SignupCodeBatch
            {
                Id = IdGenerator.GenerateSignupCodeBatchId(),
                Name = template.Name,
                Quantity = quantity,
                GrantType = template.GrantType,
                GrantMonths = template.GrantMonths,
                GrantEndUtc = template.GrantEndUtc,
                DiscountPercent = template.DiscountPercent,
                DiscountAmountCents = template.DiscountAmountCents,
                DiscountDuration = template.DiscountDuration,
                ExpiresUtc = template.ExpiresUtc,
                CreatedByUserId = info.Context?.UserId,
                CreatedUtc = now
            };
            for (int attempt = 0; ; attempt++)
            {
                HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
                List<SignupCode> codes = new List<SignupCode>();
                while (codes.Count < quantity)
                {
                    string text = Generate(_Settings.SignupCodes.GeneratedLength);
                    if (!used.Add(text)) continue;
                    codes.Add(new SignupCode
                    {
                        Id = IdGenerator.GenerateSignupCodeId(),
                        Code = text,
                        Name = template.Name,
                        GrantType = template.GrantType,
                        GrantMonths = template.GrantMonths,
                        GrantEndUtc = template.GrantEndUtc,
                        DiscountPercent = template.DiscountPercent,
                        DiscountAmountCents = template.DiscountAmountCents,
                        DiscountDuration = template.DiscountDuration,
                        MaxRedemptions = 1,
                        ExpiresUtc = template.ExpiresUtc,
                        BatchId = batch.Id,
                        CreatedByUserId = batch.CreatedByUserId,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    });
                }
                try
                {
                    await _Database.SignupCodes.CreateBatchAsync(batch, codes, token).ConfigureAwait(false);
                    if (metadata.Labels.Count > 0 || metadata.Tags.Count > 0)
                    {
                        await _Database.Metadata.WriteAsync(TargetTypeEnum.SignupCodeBatch, batch.Id, metadata, token).ConfigureAwait(false);
                        await _Database.Metadata.WriteManyAsync(TargetTypeEnum.SignupCode, codes.Select(c => c.Id), metadata, token).ConfigureAwait(false);
                    }
                    await _Audit.WriteAsync(info, "signupCode.batch", "SignupCodeBatch", batch.Id, "Created " + quantity + " single-use codes: " + batch.Name, token).ConfigureAwait(false);
                    List<SignupCodeView> views = codes.Select(c => ToView(c, now)).ToList();
                    foreach (SignupCodeView view in views)
                    {
                        view.Labels = new List<string>(metadata.Labels);
                        view.Tags = new Dictionary<string, string>(metadata.Tags);
                    }
                    return new SignupCodeBatchResponse { BatchId = batch.Id, Codes = views, Labels = new List<string>(metadata.Labels), Tags = new Dictionary<string, string>(metadata.Tags) };
                }
                catch (DuplicateRecordException) when (attempt < 3)
                {
                    _Logger.Debug("[SignupCodeService] generated code collision; regenerating batch");
                }
            }
        }

        /// <summary>
        /// GET /api/v1/admin/signup-codes/{id}/redemptions (collection <see cref="PhaseOneCollections.Redemptions"/>
        /// scoped to the code; filter status).
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with labels and tags (staff view).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound.</exception>
        public async Task<EnumerationResult<RedemptionView>> ListRedemptionsAsync(string id, EnumerationQuery query, CancellationToken token = default)
        {
            SignupCode code = await RequireAsync(id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            EnumerationResult<SignupCodeRedemption> page = await RedemptionsPageAsync(code.Id, _Enumeration.Prepare(query, PhaseOneCollections.Redemptions, true, "code|" + code.Id), now, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(page.Objects, now, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Redemption views with member and code details and labels and tags (one metadata load for the page).
        /// </summary>
        /// <param name="redemptions">Redemptions (one page).</param>
        /// <param name="nowUtc">Current time (status).</param>
        /// <param name="includeStaff">Whether staff: labels and tags are included.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views.</returns>
        /// <exception cref="ArgumentNullException">The redemptions are null.</exception>
        public async Task<List<RedemptionView>> ViewsAsync(List<SignupCodeRedemption> redemptions, DateTime nowUtc, bool includeStaff, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(redemptions);
            Dictionary<string, User> users = (await _Database.Users.ReadManyAsync(redemptions.Select(r => r.UserId), token).ConfigureAwait(false)).ToDictionary(u => u.Id);
            Dictionary<string, SignupCode> codes = (await _Database.SignupCodes.ReadManyAsync(redemptions.Select(r => r.SignupCodeId), token).ConfigureAwait(false)).ToDictionary(c => c.Id);
            List<RedemptionView> views = redemptions.Select(r => MemberViewBuilder.ToView(r, users.GetValueOrDefault(r.UserId), codes.GetValueOrDefault(r.SignupCodeId), nowUtc)).ToList();
            return await _Metadata.AttachAsync(TargetTypeEnum.SignupCodeRedemption, views, v => v.Id, includeStaff, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Revoke one redemption and end its membership.
        /// </summary>
        /// <param name="redemptionId">Redemption identifier.</param>
        /// <param name="reason">Reason (required).</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 404.</exception>
        public async Task<RedemptionView> RevokeRedemptionAsync(string redemptionId, string? reason, RequestInfo info, CancellationToken token = default)
        {
            string why = RequireReason(reason);
            SignupCodeRedemption redemption = await _Database.SignupCodeRedemptions.ReadAsync(redemptionId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Redemption");
            await RevokeAsync(redemption, why, info, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "signupCode.revoke", "SignupCodeRedemption", redemption.Id, "Revoked redemption by " + redemption.UserId + ": " + why, token).ConfigureAwait(false);
            List<RedemptionView> views = await ViewsAsync(new List<SignupCodeRedemption> { redemption }, DateTime.UtcNow, true, token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// Revoke every active redemption of a code.
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="reason">Reason (required).</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count revoked.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 404.</exception>
        public async Task<RevokeAllResponse> RevokeAllAsync(string id, string? reason, RequestInfo info, CancellationToken token = default)
        {
            string why = RequireReason(reason);
            SignupCode code = await RequireAsync(id, token).ConfigureAwait(false);
            int count = 0;
            EnumerationQuery query = new EnumerationQuery { MaxResults = _Settings.Enumeration.MaxResultsAdmin, Ordering = "CreatedAscending" };
            query.SetFilter("status", RedemptionStatusEnum.Active.ToString());
            while (true)
            {
                EnumerationResult<SignupCodeRedemption> page = await RedemptionsPageAsync(code.Id, _Enumeration.Prepare(query, PhaseOneCollections.Redemptions, true, "revoke|" + code.Id), DateTime.UtcNow, token).ConfigureAwait(false);
                foreach (SignupCodeRedemption redemption in page.Objects)
                {
                    if (redemption.RevokedUtc != null) continue;
                    await RevokeAsync(redemption, why, info, token).ConfigureAwait(false);
                    count++;
                }
                if (page.EndOfResults || page.ContinuationToken == null) break;
                query.ContinuationToken = page.ContinuationToken;
            }
            await _Audit.WriteAsync(info, "signupCode.revoke", "SignupCode", code.Id, "Revoked " + count + " redemptions of " + code.Code + ": " + why, token).ConfigureAwait(false);
            return new RevokeAllResponse { RevokedCount = count };
        }

        /// <summary>
        /// Summary across every code.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        public async Task<SignupCodeSummary> SummaryAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            long total = await _Database.SignupCodeRedemptions.CountAsync(null, null, token).ConfigureAwait(false);
            RedemptionOutcomes outcomes = await _Database.SignupCodeRedemptions.CountOutcomesAsync(null, now, token).ConfigureAwait(false);
            return new SignupCodeSummary
            {
                ActiveCodes = await _Database.SignupCodes.CountByStatusAsync(SignupCodeStatusEnum.Active, now, token).ConfigureAwait(false),
                TotalRedemptions = total,
                Redemptions30d = await _Database.SignupCodeRedemptions.CountAsync(null, now.AddDays(-30), token).ConfigureAwait(false),
                ActiveFreeMembers = await _Database.Entitlements.CountActiveUsersAsync(now, MembershipSourceEnum.SignupCode, token).ConfigureAwait(false),
                ConvertedToPaid = outcomes.ConvertedToPaid,
                ConversionRate = total == 0 ? 0 : Math.Round(outcomes.ConvertedToPaid / (double)total, 4)
            };
        }

        /// <summary>
        /// GET /api/v1/admin/signup-codes/export.csv: one bounded CSV page (at most enumeration.maxResultsAdmin rows,
        /// the default when maxResults is absent) of the codes matching the same query as the list.
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>CSV page and the next continuation token.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<CsvPage> ExportCodesCsvAsync(EnumerationQuery query, CancellationToken token = default)
        {
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = _Settings.Enumeration.MaxResultsAdmin;
            DateTime now = DateTime.UtcNow;
            EnumerationResult<SignupCode> page = await CodesPageAsync(_Enumeration.Prepare(q, PhaseOneCollections.SignupCodes, true, "csv"), now, token).ConfigureAwait(false);
            List<SignupCodeView> views = await ViewsAsync(page.Objects, now, token).ConfigureAwait(false);
            CsvWriter csv = new CsvWriter().Row("id", "code", "name", "status", "grantType", "grantMonths", "grantEndUtc", "grantSummary", "maxRedemptions", "redemptionCount",
                "startsUtc", "expiresUtc", "isActive", "batchId", "labels", "tags", "joinUrl", "lastRedeemedUtc", "createdUtc");
            foreach (SignupCodeView v in views)
            {
                csv.Row(v.Id, v.Code, v.Name, v.Status, v.GrantType, v.GrantMonths, v.GrantEndUtc, v.GrantSummary, v.MaxRedemptions, v.RedemptionCount,
                    v.StartsUtc, v.ExpiresUtc, v.IsActive, v.BatchId, String.Join("; ", v.Labels), String.Join("; ", v.Tags.Select(t => t.Key + "=" + t.Value)), v.JoinUrl, v.LastRedeemedUtc, v.CreatedUtc);
            }
            return new CsvPage { Csv = csv.ToString(), ContinuationToken = page.ContinuationToken, TotalRecords = page.TotalRecords };
        }

        /// <summary>
        /// GET /api/v1/admin/signup-codes/{id}/redemptions/export.csv: one bounded CSV page of the code's redemptions
        /// (same query as the redemptions list; at most enumeration.maxResultsAdmin rows).
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>CSV page and the next continuation token.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound.</exception>
        public async Task<CsvPage> ExportRedemptionsCsvAsync(string id, EnumerationQuery query, CancellationToken token = default)
        {
            SignupCode code = await RequireAsync(id, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = _Settings.Enumeration.MaxResultsAdmin;
            DateTime now = DateTime.UtcNow;
            EnumerationResult<SignupCodeRedemption> page = await RedemptionsPageAsync(code.Id, _Enumeration.Prepare(q, PhaseOneCollections.Redemptions, true, "csv|" + code.Id), now, token).ConfigureAwait(false);
            List<RedemptionView> views = await ViewsAsync(page.Objects, now, true, token).ConfigureAwait(false);
            CsvWriter csv = new CsvWriter().Row("id", "code", "userId", "memberDisplayName", "memberEmail", "redeemedUtc", "membershipEndsUtc", "isNewAccount", "status", "convertedToPaid", "revokedUtc", "revokeReason", "labels", "tags");
            foreach (RedemptionView v in views)
            {
                csv.Row(v.Id, v.Code, v.UserId, v.MemberDisplayName, v.MemberEmail, v.RedeemedUtc, v.MembershipEndsUtc, v.IsNewAccount, v.Status, v.ConvertedToPaid, v.RevokedUtc, v.RevokeReason,
                    String.Join("; ", v.Labels), String.Join("; ", v.Tags.Select(t => t.Key + "=" + t.Value)));
            }
            return new CsvPage { Csv = csv.ToString(), ContinuationToken = page.ContinuationToken, TotalRecords = page.TotalRecords };
        }

        #endregion

        #region Private-Methods

        private async Task<EnumerationResult<SignupCode>> CodesPageAsync(EnumerationRequest request, DateTime now, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            SignupCodeFilter filter = new SignupCodeFilter
            {
                Status = request.Query.FilterEnum<SignupCodeStatusEnum>("status", errors),
                BatchId = request.Query.FilterString("batchId")
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            return await _Database.SignupCodes.EnumerateAsync(request, filter, now, token).ConfigureAwait(false);
        }

        private async Task<EnumerationResult<SignupCodeRedemption>> RedemptionsPageAsync(string codeId, EnumerationRequest request, DateTime now, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            RedemptionFilter filter = new RedemptionFilter { SignupCodeId = codeId, Status = request.Query.FilterEnum<RedemptionStatusEnum>("status", errors) };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            return await _Database.SignupCodeRedemptions.EnumerateAsync(request, filter, now, token).ConfigureAwait(false);
        }

        private async Task<List<SignupCodeView>> ViewsAsync(List<SignupCode> codes, DateTime now, CancellationToken token)
        {
            List<SignupCodeView> views = codes.Select(c => ToView(c, now)).ToList();
            return await _Metadata.AttachAsync(TargetTypeEnum.SignupCode, views, v => v.Id, true, token).ConfigureAwait(false);
        }

        private async Task<SignupCodeView> ViewAsync(SignupCode code, DateTime now, CancellationToken token)
        {
            return await _Metadata.AttachOneAsync(TargetTypeEnum.SignupCode, ToView(code, now), code.Id, true, token).ConfigureAwait(false);
        }

        private bool Acquire(string userId, RequestInfo info, DateTime now)
        {
            int limit = _Settings.SignupCodes.AttemptsPerHour;
            string ipKey = "code-ip:" + (info.SourceIp ?? "unknown");
            string userKey = "code-user:" + userId;
            if (_Limiter.IsLimited(ipKey, limit, _Hour, now) || _Limiter.IsLimited(userKey, limit, _Hour, now)) return false;
            _Limiter.TryAcquire(ipKey, limit, _Hour, now);
            _Limiter.TryAcquire(userKey, limit, _Hour, now);
            return true;
        }

        private async Task<SignupCode?> FindRedeemableAsync(string? code, DateTime now, CancellationToken token)
        {
            string? normalized = Normalize(code);
            if (normalized == null || normalized.Length > 64) return null;
            SignupCode? found = await _Database.SignupCodes.ReadByCodeAsync(normalized, token).ConfigureAwait(false);
            if (found == null || found.StatusAt(now) != SignupCodeStatusEnum.Active) return null;
            return found;
        }

        private static DateTime? Ends(SignupCode code, DateTime starts)
        {
            switch (code.GrantType)
            {
                case GrantTypeEnum.Lifetime:
                    return null;
                case GrantTypeEnum.FixedEndDate:
                    return code.GrantEndUtc;
                default:
                    return starts.AddMonths(Math.Max(1, code.GrantMonths ?? 1));
            }
        }

        private async Task RevokeAsync(SignupCodeRedemption redemption, string reason, RequestInfo info, CancellationToken token)
        {
            if (redemption.RevokedUtc != null) return;
            DateTime now = DateTime.UtcNow;
            redemption.RevokedUtc = now;
            redemption.RevokedByUserId = info.Context?.UserId;
            redemption.RevokeReason = reason;
            await _Database.SignupCodeRedemptions.UpdateAsync(redemption, token).ConfigureAwait(false);
            // A discount redemption has no entitlement: revoking it only marks the record (the Stripe discount stays applied).
            if (String.IsNullOrEmpty(redemption.EntitlementId)) return;
            Entitlement? entitlement = await _Database.Entitlements.ReadAsync(redemption.EntitlementId, token).ConfigureAwait(false);
            if (entitlement != null && entitlement.RevokedUtc == null)
            {
                entitlement.RevokedUtc = now;
                entitlement.RevokeReason = reason;
                entitlement.UpdatedUtc = now;
                await _Database.Entitlements.UpdateAsync(entitlement, token).ConfigureAwait(false);
            }
            _Tokens.InvalidateUser(redemption.UserId);
            User? user = await _Database.Users.ReadAsync(redemption.UserId, token).ConfigureAwait(false);
            if (user?.Email != null && !user.IsDeleted) await _Email.SendCodeRevokedAsync(user.Email, user.DisplayName, token).ConfigureAwait(false);
        }

        private void Fill(SignupCodeView view, SignupCode code, DateTime now)
        {
            view.Id = code.Id;
            view.Code = code.Code;
            view.Name = code.Name;
            view.Description = code.Description;
            view.Notes = code.Notes;
            view.GrantType = code.GrantType;
            view.GrantMonths = code.GrantMonths;
            view.GrantEndUtc = code.GrantEndUtc;
            view.GrantSummary = Summary(code);
            view.DiscountPercent = code.DiscountPercent;
            view.DiscountAmountCents = code.DiscountAmountCents;
            view.DiscountDuration = code.DiscountDuration;
            view.StripeCouponId = code.StripeCouponId;
            view.MaxRedemptions = code.MaxRedemptions;
            view.RedemptionCount = code.RedemptionCount;
            view.StartsUtc = code.StartsUtc;
            view.ExpiresUtc = code.ExpiresUtc;
            view.IsActive = code.IsActive;
            view.Status = code.StatusAt(now);
            view.BatchId = code.BatchId;
            view.JoinUrl = _Property.Link("/join?code=" + Uri.EscapeDataString(code.Code));
            view.LastRedeemedUtc = code.LastRedeemedUtc;
            view.CreatedByUserId = code.CreatedByUserId;
            view.CreatedUtc = code.CreatedUtc;
            view.UpdatedUtc = code.UpdatedUtc;
        }

        private static void ApplyCommon(SignupCode code, string? name, string? description, string? notes, int? maxRedemptions, DateTime? startsUtc, DateTime? expiresUtc,
            Dictionary<string, string> errors, bool nameRequired)
        {
            string trimmedName = name?.Trim() ?? String.Empty;
            if (trimmedName.Length == 0 || trimmedName.Length > 100) errors["name"] = "Use 1 to 100 characters.";
            else code.Name = trimmedName;
            string? d = String.IsNullOrWhiteSpace(description) ? null : description.Trim();
            if (d != null && d.Length > 500) errors["description"] = "Use at most 500 characters.";
            else code.Description = d;
            string? n = String.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            if (n != null && n.Length > 2000) errors["notes"] = "Use at most 2000 characters.";
            else code.Notes = n;
            if (maxRedemptions != null && maxRedemptions.Value < 1) errors["maxRedemptions"] = "Use 1 or more, or null for unlimited.";
            else code.MaxRedemptions = maxRedemptions;
            code.StartsUtc = startsUtc?.ToUniversalTime();
            code.ExpiresUtc = expiresUtc?.ToUniversalTime();
            if (code.StartsUtc != null && code.ExpiresUtc != null && code.StartsUtc.Value >= code.ExpiresUtc.Value) errors["expiresUtc"] = "Must be after startsUtc.";
        }

        private static void ApplyGrant(SignupCode code, string? grantType, int? months, DateTime? endUtc, int? discountPercent, int? discountAmountCents, string? discountDuration,
            Dictionary<string, string> errors, DateTime now)
        {
            if (String.IsNullOrWhiteSpace(grantType) || !Enum.TryParse(grantType.Trim(), true, out GrantTypeEnum type) || !Enum.IsDefined(type) || Int32.TryParse(grantType, out int _))
            {
                errors["grantType"] = "Use DurationMonths, FixedEndDate, Lifetime, or Discount.";
                return;
            }
            code.GrantType = type;
            code.GrantMonths = null;
            code.GrantEndUtc = null;
            code.DiscountPercent = null;
            code.DiscountAmountCents = null;
            code.DiscountDuration = null;
            if (type == GrantTypeEnum.Discount)
            {
                if ((discountPercent == null) == (discountAmountCents == null)) errors["discountPercent"] = "Set either discountPercent or discountAmountCents.";
                else if (discountPercent != null && (discountPercent.Value < 1 || discountPercent.Value > 100)) errors["discountPercent"] = "Use 1 to 100.";
                else if (discountAmountCents != null && (discountAmountCents.Value < 1 || discountAmountCents.Value > 1000000)) errors["discountAmountCents"] = "Use 1 to 1000000 cents.";
                else
                {
                    code.DiscountPercent = discountPercent;
                    code.DiscountAmountCents = discountAmountCents;
                }
                if (String.IsNullOrWhiteSpace(discountDuration) || !Enum.TryParse(discountDuration.Trim(), true, out DiscountDurationEnum duration) || !Enum.IsDefined(duration) || Int32.TryParse(discountDuration, out int _))
                    errors["discountDuration"] = "Use Once or Forever.";
                else code.DiscountDuration = duration;
                return;
            }
            if (type == GrantTypeEnum.DurationMonths)
            {
                if (months == null || months.Value < 1 || months.Value > 120) errors["grantMonths"] = "Use 1 to 120.";
                else code.GrantMonths = months;
            }
            else if (type == GrantTypeEnum.FixedEndDate)
            {
                if (endUtc == null || endUtc.Value.ToUniversalTime() <= now) errors["grantEndUtc"] = "Choose a date in the future.";
                else code.GrantEndUtc = endUtc.Value.ToUniversalTime();
            }
        }

        private async Task<SignupCode> RequireAsync(string id, CancellationToken token)
        {
            SignupCode? code = String.IsNullOrWhiteSpace(id) ? null : await _Database.SignupCodes.ReadAsync(id, token).ConfigureAwait(false);
            return code ?? throw ApiErrorException.NotFound("Sign-up code");
        }

        private static string RequireReason(string? reason)
        {
            if (String.IsNullOrWhiteSpace(reason)) throw ApiErrorException.Field("reason", "A reason is required.");
            string trimmed = reason.Trim();
            if (trimmed.Length > 500) throw ApiErrorException.Field("reason", "Use at most 500 characters.");
            return trimmed;
        }

        private static ApiErrorException Redemption(string outcome, ApiErrorException error)
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.CodeRedemptions, "outcome", outcome);
            return error;
        }

        private static ApiErrorException InvalidCode()
        {
            return new ApiErrorException(400, "InvalidCode", InvalidMessage);
        }

        private static ApiErrorException AlreadyRedeemed()
        {
            return new ApiErrorException(409, "AlreadyRedeemed", "You have already redeemed this code.");
        }

        private int RetryAfter(string userId, RequestInfo info, DateTime now)
        {
            int limit = _Settings.SignupCodes.AttemptsPerHour;
            return Math.Max(_Limiter.RetryAfterSeconds("code-ip:" + (info.SourceIp ?? "unknown"), limit, _Hour, now), _Limiter.RetryAfterSeconds("code-user:" + userId, limit, _Hour, now));
        }

        private static ApiErrorException RateLimited(int retryAfterSeconds)
        {
            return ApiErrorException.RateLimited(retryAfterSeconds, "Too many attempts. Try again later.");
        }

        #endregion
    }
}
