namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Account lifecycle: registration, password sign-in with lockout, social sign-in and account linking, email
    /// verification, password reset and change, age attestation, the bootstrap administrator, export, and deletion.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AccountService
    {
        #region Public-Members

        /// <summary>Optional property-scoped product metrics; set by the composition root.</summary>
        public TrellisKit.Server.Services.Metrics.ProductMetricEventService? ProductEvents { get; set; } = null;

        /// <summary>
        /// Called with the member id after an account is deleted or closed, to remove data owned by other services (for
        /// example account export files). Set by the composition root; null does nothing.
        /// </summary>
        public Func<string, CancellationToken, Task>? AccountClosed { get; set; } = null;

        /// <summary>
        /// Word filter that screens the display name at registration (Block is 400 ContentFiltered naming displayName;
        /// Flag files a system report on the new member). Set by the composition root; null skips screening.
        /// </summary>
        public WordFilterService? WordFilter { get; set; } = null;

        /// <summary>
        /// Property documents (docs/TRELLISKIT.md section 14): registration requires every separately accepted sign-up
        /// document at its current version and records the accepted documents; age attestation records the documents it
        /// carries. Set by the composition root; null skips documents.
        /// </summary>
        public TrellisKit.Server.Services.Documents.PropertyDocumentService? Documents { get; set; } = null;

        #endregion

        #region Private-Members

        private const string _InvalidCredentials = "The email or password is incorrect.";
        private static readonly TimeSpan _Hour = TimeSpan.FromHours(1);
        private readonly DatabaseDriverBase _Database;
        private readonly IntegrationSet _Integrations;
        private readonly AppSettings _Settings;
        private readonly PropertyContext _Property;
        private readonly SessionService _Sessions;
        private readonly TokenService _Tokens;
        private readonly EmailService _Email;
        private readonly AuditService _Audit;
        private readonly MemberViewBuilder _Views;
        private readonly MembershipService _Membership;
        private readonly AvatarStore _Avatars;
        private readonly SlidingWindowRateLimiter _Limiter;
        private readonly AppLogger _Logger;
        private readonly Lazy<string> _DummyHash;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="integrations">Integrations.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="property">Active property context (minimum age).</param>
        /// <param name="sessions">Session service.</param>
        /// <param name="tokens">Token service.</param>
        /// <param name="email">Email service.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="views">View builder.</param>
        /// <param name="membership">Membership service.</param>
        /// <param name="avatars">Avatar store.</param>
        /// <param name="limiter">Rate limiter.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AccountService(
            DatabaseDriverBase database,
            IntegrationSet integrations,
            AppSettings settings,
            PropertyContext property,
            SessionService sessions,
            TokenService tokens,
            EmailService email,
            AuditService audit,
            MemberViewBuilder views,
            MembershipService membership,
            AvatarStore avatars,
            SlidingWindowRateLimiter limiter,
            AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Email = email ?? throw new ArgumentNullException(nameof(email));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Views = views ?? throw new ArgumentNullException(nameof(views));
            _Membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _Avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            _Limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _DummyHash = new Lazy<string>(() => _Integrations.PasswordHasher.Hash(Guid.NewGuid().ToString("N")));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register with email and password. Nothing is stored for members under 18.
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Issued session (isNewAccount true).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 AgeRequirementNotMet, or 409 EmailInUse.</exception>
        public async Task<IssuedSession> RegisterAsync(RegisterRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(info);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("auth.register"))
            {
                DateTime now = DateTime.UtcNow;
                Dictionary<string, string> errors = new Dictionary<string, string>();
                string? email = Validators.NormalizeEmail(request.Email);
                Put(errors, "email", Validators.Email(request.Email));
                Put(errors, "password", Validators.Password(request.Password, email));
                Put(errors, "displayName", Validators.DisplayName(request.DisplayName));
                Put(errors, "dateOfBirth", Validators.DateOfBirth(request.DateOfBirth, now, out DateTime dateOfBirth));
                if (!request.AcceptTerms) errors["acceptTerms"] = "Accept the terms to create an account.";
                List<TrellisKit.Server.Services.Documents.DocumentState> documents = Documents == null
                    ? new List<TrellisKit.Server.Services.Documents.DocumentState>()
                    : Documents.Check(request.AcceptTerms, request.AcceptedDocuments, true, "acceptedDocuments", errors);
                if (errors.Count > 0)
                {
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignUps, "method", "Email", "outcome", "validation_failed");
                    throw ApiErrorException.Validation(errors);
                }
                if (!Validators.IsAdult(dateOfBirth, now, _Property.MinimumAge))
                {
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignUps, "method", "Email", "outcome", "underage");
                    throw new ApiErrorException(403, "AgeRequirementNotMet", "You must be " + _Property.MinimumAge + " or older to join.");
                }
                if (await _Database.Users.ReadByEmailAsync(email!, token).ConfigureAwait(false) != null) throw EmailInUse();
                string? refused = await _Email.ValidateAddressAsync(email, token).ConfigureAwait(false);
                if (refused != null) throw ApiErrorException.Field("email", refused);
                WordFilterResult? flags = null;
                if (WordFilter != null)
                {
                    flags = await WordFilter.CheckAsync(new ContentActor(), TargetTypeEnum.User,
                        new Dictionary<string, string?> { { "displayName", request.DisplayName!.Trim() } }, token).ConfigureAwait(false);
                }

                User user = new User
                {
                    Id = IdGenerator.GenerateUserId(),
                    Email = email,
                    DisplayName = request.DisplayName!.Trim(),
                    AgeAttestedUtc = now,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                try
                {
                    await _Database.Users.CreateAsync(user, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    throw EmailInUse();
                }
                await _Database.MemberSearch.ReplaceWordsAsync(user.Id, user.DisplayName, token).ConfigureAwait(false);
                if (WordFilter != null) await WordFilter.RecordFlagsAsync(flags, TargetTypeEnum.User, user.Id, user.Id, token).ConfigureAwait(false);
                await _Database.Credentials.UpsertAsync(new UserCredential
                {
                    Id = IdGenerator.GenerateCredentialId(),
                    UserId = user.Id,
                    PasswordHash = _Integrations.PasswordHasher.Hash(request.Password!),
                    CreatedUtc = now,
                    UpdatedUtc = now
                }, token).ConfigureAwait(false);

                if (Documents != null) await Documents.RecordAsync(user.Id, documents, true, info, token).ConfigureAwait(false);
                await SendVerificationAsync(user, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignUps, "method", "Email", "outcome", "success");
                activity?.SetTag("outcome", "success");
                await NotifyNewMemberAsync(user, info, documents.Select(d => d.Config.Title + " " + d.Current.Version).ToList(), token).ConfigureAwait(false);
                if (ProductEvents != null)
                {
                    await ProductEvents.ObserveAsync("signup.registered", user.Id, token).ConfigureAwait(false);
                    if (user.EmailVerified) await ProductEvents.ObserveAsync("signup.verified", user.Id, token).ConfigureAwait(false);
                }
                return await _Sessions.CreateAsync(user, info, true, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sign in with email and password. Ten failures within the lockout window lock password sign-in.
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Issued session.</returns>
        /// <exception cref="ApiErrorException">401 InvalidCredentials, 423 AccountLocked, or 403 AccountBanned.</exception>
        public async Task<IssuedSession> LoginAsync(LoginRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(info);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("auth.signin"))
            {
                activity?.SetTag("method", "Email");
                DateTime now = DateTime.UtcNow;
                string? email = Validators.NormalizeEmail(request.Email);
                User? user = email == null ? null : await _Database.Users.ReadByEmailAsync(email, token).ConfigureAwait(false);
                UserCredential? credential = user == null ? null : await _Database.Credentials.ReadByUserAsync(user.Id, token).ConfigureAwait(false);

                if (user == null || credential == null || user.IsDeleted)
                {
                    _Integrations.PasswordHasher.Verify(request.Password ?? String.Empty, _DummyHash.Value, out bool _);
                    throw SignInFailed("Email", "invalid_credentials", new ApiErrorException(401, "InvalidCredentials", _InvalidCredentials));
                }

                if (user.LockedUntilUtc != null && user.LockedUntilUtc.Value > now) throw Locked(user.LockedUntilUtc.Value, now);

                if (!_Integrations.PasswordHasher.Verify(request.Password ?? String.Empty, credential.PasswordHash, out bool needsRehash))
                {
                    if (user.FailedLoginWindowUtc == null || user.FailedLoginWindowUtc.Value <= now.AddMinutes(-_Settings.Auth.LockoutMinutes))
                    {
                        user.FailedLoginWindowUtc = now;
                        user.FailedLoginCount = 1;
                    }
                    else
                    {
                        user.FailedLoginCount++;
                    }

                    bool locked = user.FailedLoginCount >= _Settings.Auth.LockoutThreshold;
                    if (locked)
                    {
                        user.LockedUntilUtc = now.AddMinutes(_Settings.Auth.LockoutMinutes);
                        user.FailedLoginCount = 0;
                        user.FailedLoginWindowUtc = null;
                        TrellisKitTelemetry.Add(TrellisKitTelemetry.Lockouts, 1);
                        _Logger.Warn("[AccountService] password sign-in locked for " + user.Id + " until " + user.LockedUntilUtc.Value.ToString("o", CultureInfo.InvariantCulture));
                    }
                    user.UpdatedUtc = now;
                    await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                    if (locked) throw Locked(user.LockedUntilUtc!.Value, now);
                    throw SignInFailed("Email", "invalid_credentials", new ApiErrorException(401, "InvalidCredentials", _InvalidCredentials));
                }

                if (user.IsBanned) throw SignInFailed("Email", "banned", Banned());

                if (needsRehash) credential.PasswordHash = _Integrations.PasswordHasher.Hash(request.Password!);
                if (needsRehash)
                {
                    credential.UpdatedUtc = now;
                    await _Database.Credentials.UpsertAsync(credential, token).ConfigureAwait(false);
                }
                if (user.FailedLoginCount != 0 || user.LockedUntilUtc != null || user.FailedLoginWindowUtc != null)
                {
                    user.FailedLoginCount = 0;
                    user.FailedLoginWindowUtc = null;
                    user.LockedUntilUtc = null;
                    user.UpdatedUtc = now;
                    await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                }

                await GrantBootstrapAdminIfEligibleAsync(user, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignIns, "method", "Email", "outcome", "success");
                return await _Sessions.CreateAsync(user, info, false, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sign in (or sign up) with Apple, Google, or Facebook. New accounts start with ageAttested false.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="presentedToken">Identity token or access token.</param>
        /// <param name="tokenType">Token type.</param>
        /// <param name="nonce">Raw nonce, or null.</param>
        /// <param name="givenName">Given name from the client (Apple), or null.</param>
        /// <param name="familyName">Family name from the client (Apple), or null.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Issued session.</returns>
        /// <exception cref="ApiErrorException">400, 401 InvalidIdentityToken, 403 AccountBanned, 409 AccountLinkRequired, or 503 IntegrationNotConfigured.</exception>
        public async Task<IssuedSession> SocialSignInAsync(ExternalProviderEnum provider, string? presentedToken, ExternalTokenTypeEnum tokenType, string? nonce,
            string? givenName, string? familyName, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("auth.signin"))
            {
                string method = provider.ToString();
                activity?.SetTag("method", method);
                ExternalIdentityClaims claims = await VerifyAsync(provider, presentedToken, tokenType, nonce, true, token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;

                ExternalIdentity? identity = await _Database.ExternalIdentities.ReadByProviderSubjectAsync(provider, claims.Subject, token).ConfigureAwait(false);
                if (identity != null)
                {
                    User? existing = await _Database.Users.ReadAsync(identity.UserId, token).ConfigureAwait(false);
                    if (existing != null && !existing.IsDeleted)
                    {
                        if (existing.IsBanned) throw SignInFailed(method, "banned", Banned());
                        identity.LastUsedUtc = now;
                        if (claims.Email != null) identity.Email = Validators.NormalizeEmail(claims.Email);
                        identity.EmailVerified = claims.EmailVerified;
                        await _Database.ExternalIdentities.UpdateAsync(identity, token).ConfigureAwait(false);
                        await MarkVerifiedFromProviderAsync(existing, claims, now, token).ConfigureAwait(false);
                        await GrantBootstrapAdminIfEligibleAsync(existing, token).ConfigureAwait(false);
                        TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignIns, "method", method, "outcome", "success");
                        return await _Sessions.CreateAsync(existing, info, false, token).ConfigureAwait(false);
                    }
                    await _Database.ExternalIdentities.DeleteAsync(identity.UserId, provider, token).ConfigureAwait(false);
                }

                string? email = Validators.NormalizeEmail(claims.Email);
                if (email != null)
                {
                    User? byEmail = await _Database.Users.ReadByEmailAsync(email, token).ConfigureAwait(false);
                    if (byEmail != null)
                    {
                        if (byEmail.IsBanned) throw SignInFailed(method, "banned", Banned());
                        List<SignInMethodEnum> methods = await MethodsAsync(byEmail.Id, token).ConfigureAwait(false);
                        AccountLinkErrorResponse body = new AccountLinkErrorResponse
                        {
                            Error = "AccountLinkRequired",
                            Message = "An account with this email already exists. Sign in with " + String.Join(" or ", methods) + ", then link " + method + " from Account settings.",
                            ExistingMethods = methods
                        };
                        throw SignInFailed(method, "link_required", new ApiErrorException(409, "AccountLinkRequired", body.Message, null, body));
                    }
                }

                User user = new User
                {
                    Id = IdGenerator.GenerateUserId(),
                    Email = email,
                    EmailVerified = email != null && claims.EmailVerified,
                    EmailVerifiedUtc = email != null && claims.EmailVerified ? now : null,
                    DisplayName = SocialDisplayName(claims, givenName, familyName),
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                try
                {
                    await _Database.Users.CreateAsync(user, token).ConfigureAwait(false);
                    await _Database.ExternalIdentities.CreateAsync(new ExternalIdentity
                    {
                        Id = IdGenerator.GenerateExternalIdentityId(),
                        UserId = user.Id,
                        Provider = provider,
                        Subject = claims.Subject,
                        Email = email,
                        EmailVerified = claims.EmailVerified,
                        CreatedUtc = now,
                        LastUsedUtc = now
                    }, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    throw SignInFailed(method, "conflict", new ApiErrorException(409, "AccountLinkRequired", "An account with this email already exists. Sign in with your existing method first."));
                }
                await _Database.MemberSearch.ReplaceWordsAsync(user.Id, user.DisplayName, token).ConfigureAwait(false);

                await GrantBootstrapAdminIfEligibleAsync(user, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignUps, "method", method, "outcome", "success");
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignIns, "method", method, "outcome", "success");
                if (user.EmailVerified) await _Email.SendToMemberAsync("welcome", user, "welcome:" + user.Id, token).ConfigureAwait(false);
                await NotifyNewMemberAsync(user, info, null, token).ConfigureAwait(false);
                if (ProductEvents != null)
                {
                    await ProductEvents.ObserveAsync("signup.registered", user.Id, token).ConfigureAwait(false);
                    if (user.EmailVerified) await ProductEvents.ObserveAsync("signup.verified", user.Id, token).ConfigureAwait(false);
                }
                return await _Sessions.CreateAsync(user, info, true, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Link an external identity to the signed-in member.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="provider">Provider.</param>
        /// <param name="presentedToken">Token.</param>
        /// <param name="tokenType">Token type.</param>
        /// <param name="nonce">Raw nonce, or null.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Me.</returns>
        /// <exception cref="ApiErrorException">401 InvalidIdentityToken, 409 IdentityInUse, or 503 IntegrationNotConfigured.</exception>
        public async Task<MeResponse> LinkIdentityAsync(string userId, ExternalProviderEnum provider, string? presentedToken, ExternalTokenTypeEnum tokenType, string? nonce,
            RequestInfo info, CancellationToken token = default)
        {
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            ExternalIdentityClaims claims = await VerifyAsync(provider, presentedToken, tokenType, nonce, false, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            ExternalIdentity? existing = await _Database.ExternalIdentities.ReadByProviderSubjectAsync(provider, claims.Subject, token).ConfigureAwait(false);
            if (existing != null && existing.UserId != user.Id)
                throw new ApiErrorException(409, "IdentityInUse", "That " + provider + " account is already linked to another member.");

            if (existing == null)
            {
                await _Database.ExternalIdentities.DeleteAsync(user.Id, provider, token).ConfigureAwait(false);
                try
                {
                    await _Database.ExternalIdentities.CreateAsync(new ExternalIdentity
                    {
                        Id = IdGenerator.GenerateExternalIdentityId(),
                        UserId = user.Id,
                        Provider = provider,
                        Subject = claims.Subject,
                        Email = Validators.NormalizeEmail(claims.Email),
                        EmailVerified = claims.EmailVerified,
                        CreatedUtc = now,
                        LastUsedUtc = now
                    }, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    throw new ApiErrorException(409, "IdentityInUse", "That " + provider + " account is already linked to another member.");
                }
                await _Audit.WriteAsync(info, "account.identity.link", "User", user.Id, "Linked " + provider, token).ConfigureAwait(false);
                await _Email.SendToMemberAsync("sign_in_method_linked", user, null, token, ("method", provider.ToString()), ("link", _Email.Link("/app/settings"))).ConfigureAwait(false);
            }
            await MarkVerifiedFromProviderAsync(user, claims, now, token).ConfigureAwait(false);
            return await _Views.BuildMeAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Unlink an external identity from the signed-in member.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="provider">Provider.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Me.</returns>
        /// <exception cref="ApiErrorException">409 LastSignInMethod when nothing else could sign in.</exception>
        public async Task<MeResponse> UnlinkIdentityAsync(string userId, ExternalProviderEnum provider, RequestInfo info, CancellationToken token = default)
        {
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            List<ExternalIdentity> identities = await _Database.ExternalIdentities.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            if (identities.Any(i => i.Provider == provider))
            {
                bool hasPassword = await _Database.Credentials.ReadByUserAsync(user.Id, token).ConfigureAwait(false) != null && user.Email != null;
                if (!hasPassword && identities.Count(i => i.Provider != provider) == 0)
                    throw new ApiErrorException(409, "LastSignInMethod", "Add another sign-in method before removing " + provider + ".");
                await _Database.ExternalIdentities.DeleteAsync(user.Id, provider, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "account.identity.unlink", "User", user.Id, "Unlinked " + provider, token).ConfigureAwait(false);
                await _Email.SendToMemberAsync("sign_in_method_removed", user, null, token, ("method", provider.ToString()), ("link", _Email.Link("/app/settings"))).ConfigureAwait(false);
            }
            return await _Views.BuildMeAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Verify an email address with an emailed token.
        /// </summary>
        /// <param name="rawToken">Token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 InvalidToken.</exception>
        public async Task VerifyEmailAsync(string? rawToken, CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            OneTimeToken stored = await ReadOneTimeAsync(rawToken, OneTimeTokenPurposeEnum.VerifyEmail, now, token).ConfigureAwait(false);
            User? user = await _Database.Users.ReadAsync(stored.UserId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted || user.Email == null || !String.Equals(user.Email, stored.Email, StringComparison.OrdinalIgnoreCase)) throw InvalidToken();
            if (!await _Database.OneTimeTokens.TryConsumeAsync(stored.Id, now, token).ConfigureAwait(false)) throw InvalidToken();
            if (!user.EmailVerified)
            {
                user.EmailVerified = true;
                user.EmailVerifiedUtc = now;
                user.UpdatedUtc = now;
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                await _Email.SendToMemberAsync("welcome", user, "welcome:" + user.Id, token).ConfigureAwait(false);
            }
            await GrantBootstrapAdminIfEligibleAsync(user, token).ConfigureAwait(false);
            if (ProductEvents != null) await ProductEvents.ObserveAsync("signup.verified", user.Id, token).ConfigureAwait(false);
            _Tokens.InvalidateUser(user.Id);
        }

        /// <summary>
        /// Send a new verification email to the signed-in member. No-op when already verified.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public async Task ResendVerificationAsync(string userId, CancellationToken token = default)
        {
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            if (user.EmailVerified || user.Email == null) return;
            if (!_Limiter.TryAcquire("verify:" + user.Id, _Settings.Auth.VerificationResendsPerHour, _Hour, DateTime.UtcNow, out int retryAfter)) throw RateLimited(retryAfter);
            await SendVerificationAsync(user, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Start a password reset. Always succeeds for unknown emails (no account enumeration).
        /// </summary>
        /// <param name="emailAddress">Email.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public async Task ForgotPasswordAsync(string? emailAddress, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            DateTime now = DateTime.UtcNow;
            string? email = Validators.NormalizeEmail(emailAddress);
            int limit = _Settings.Auth.PasswordResetRequestsPerHour;
            if (!_Limiter.TryAcquire("forgot-ip:" + (info.SourceIp ?? "unknown"), limit * 4, _Hour, now, out int ipRetryAfter)) throw RateLimited(ipRetryAfter);
            if (email == null || Validators.Email(email) != null) return;
            if (!_Limiter.TryAcquire("forgot-email:" + email, limit, _Hour, now, out int emailRetryAfter)) throw RateLimited(emailRetryAfter);

            User? user = await _Database.Users.ReadByEmailAsync(email, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted || user.IsBanned) return;
            await _Database.OneTimeTokens.InvalidateAsync(user.Id, OneTimeTokenPurposeEnum.ResetPassword, now, token).ConfigureAwait(false);
            string raw = await CreateOneTimeAsync(user, OneTimeTokenPurposeEnum.ResetPassword, TimeSpan.FromHours(1), token).ConfigureAwait(false);
            await _Email.SendPasswordResetAsync(user.Email!, user.DisplayName, raw, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Reset a password with an emailed token and revoke every session.
        /// </summary>
        /// <param name="rawToken">Token.</param>
        /// <param name="newPassword">New password.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 InvalidToken or 400 ValidationFailed.</exception>
        public async Task ResetPasswordAsync(string? rawToken, string? newPassword, RequestInfo info, CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            OneTimeToken stored = await ReadOneTimeAsync(rawToken, OneTimeTokenPurposeEnum.ResetPassword, now, token).ConfigureAwait(false);
            User? user = await _Database.Users.ReadAsync(stored.UserId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw InvalidToken();
            string? error = Validators.Password(newPassword, user.Email);
            if (error != null) throw ApiErrorException.Field("newPassword", error);
            if (!await _Database.OneTimeTokens.TryConsumeAsync(stored.Id, now, token).ConfigureAwait(false)) throw InvalidToken();

            await SetPasswordAsync(user.Id, newPassword!, now, token).ConfigureAwait(false);
            user.FailedLoginCount = 0;
            user.FailedLoginWindowUtc = null;
            user.LockedUntilUtc = null;
            if (!user.EmailVerified && user.Email != null && String.Equals(user.Email, stored.Email, StringComparison.OrdinalIgnoreCase))
            {
                user.EmailVerified = true;
                user.EmailVerifiedUtc = now;
            }
            user.UpdatedUtc = now;
            await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            await _Sessions.RevokeAllAsync(user.Id, null, "password reset", token).ConfigureAwait(false);
            await GrantBootstrapAdminIfEligibleAsync(user, token).ConfigureAwait(false);
            await _Audit.WriteAsync(null, info.SourceIp, "account.password.reset", "User", user.Id, "Password reset by email", token).ConfigureAwait(false);
            await _Email.SendToMemberAsync("password_changed", user, null, token, ("changedDate", EmailService.FormatDate(now)), ("link", _Email.Link("/forgot-password"))).ConfigureAwait(false);
        }

        /// <summary>
        /// Change or set the signed-in member's password and revoke their other sessions.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="sessionId">Current session to keep, or null.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 401 InvalidCredentials.</exception>
        public async Task ChangePasswordAsync(string userId, string? sessionId, ChangePasswordRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            UserCredential? credential = await _Database.Credentials.ReadByUserAsync(user.Id, token).ConfigureAwait(false);
            if (credential != null)
            {
                if (String.IsNullOrEmpty(request.CurrentPassword)) throw ApiErrorException.Field("currentPassword", "Enter your current password.");
                if (!_Integrations.PasswordHasher.Verify(request.CurrentPassword, credential.PasswordHash, out bool _))
                    throw new ApiErrorException(401, "InvalidCredentials", "The current password is incorrect.");
            }
            if (user.Email == null) throw ApiErrorException.Field("newPassword", "Add an email address to your account before setting a password.");
            string? error = Validators.Password(request.NewPassword, user.Email);
            if (error != null) throw ApiErrorException.Field("newPassword", error);

            await SetPasswordAsync(user.Id, request.NewPassword!, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Sessions.RevokeAllAsync(user.Id, sessionId, "password changed", token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "account.password.change", "User", user.Id, credential == null ? "Password set" : "Password changed", token).ConfigureAwait(false);
            await _Email.SendToMemberAsync("password_changed", user, null, token, ("changedDate", EmailService.FormatDate(DateTime.UtcNow)), ("link", _Email.Link("/forgot-password"))).ConfigureAwait(false);
        }

        /// <summary>
        /// Record the age attestation. A member under the property minimum age (membership.minimumAge) has their account closed and is signed out.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="dateOfBirth">Date of birth as YYYY-MM-DD (never stored).</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Me.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 403 AgeRequirementNotMet.</exception>
        public Task<MeResponse> AttestAgeAsync(string userId, string? dateOfBirth, RequestInfo info, CancellationToken token = default)
        {
            return AttestAgeAsync(userId, new AgeAttestationRequest { DateOfBirth = dateOfBirth }, info, token);
        }

        /// <summary>
        /// Record the age attestation and the documents the request accepts (docs/TRELLISKIT.md section 14: the
        /// onboarding step for accounts created by social sign-in). Documents are checked before anything is stored and
        /// recorded only after a successful attestation. A member under the property minimum age has their account closed.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="request">Body (date of birth, never stored; acceptTerms; acceptedDocuments).</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Me.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 403 AgeRequirementNotMet.</exception>
        public async Task<MeResponse> AttestAgeAsync(string userId, AgeAttestationRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            string? error = Validators.DateOfBirth(request.DateOfBirth, now, out DateTime dob);
            if (error != null) throw ApiErrorException.Field("dateOfBirth", error);
            Dictionary<string, string> documentErrors = new Dictionary<string, string>(StringComparer.Ordinal);
            List<TrellisKit.Server.Services.Documents.DocumentState> documents = Documents == null
                ? new List<TrellisKit.Server.Services.Documents.DocumentState>()
                : Documents.Check(request.AcceptTerms, request.AcceptedDocuments, false, "acceptedDocuments", documentErrors);
            if (documentErrors.Count > 0) throw ApiErrorException.Validation(documentErrors);
            if (!Validators.IsAdult(dob, now, _Property.MinimumAge))
            {
                await CloseAccountAsync(user, info, "account.close.underage", token).ConfigureAwait(false);
                throw new ApiErrorException(403, "AgeRequirementNotMet", "You must be " + _Property.MinimumAge + " or older to join. Your account has been closed.");
            }
            if (user.AgeAttestedUtc == null)
            {
                user.AgeAttestedUtc = now;
                user.UpdatedUtc = now;
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                _Tokens.InvalidateUser(user.Id);
            }
            if (Documents != null && documents.Count > 0) await Documents.RecordAsync(user.Id, documents, true, info, token).ConfigureAwait(false);
            return await _Views.BuildMeAsync(user, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete the signed-in member's account: anonymize it, revoke sessions, and cancel Stripe at period end.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response with a note about store subscriptions the member must cancel.</returns>
        public async Task<DeleteAccountResponse> DeleteAccountAsync(string userId, RequestInfo info, CancellationToken token = default)
        {
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            User copy = new User { Id = user.Id, Email = user.Email, DisplayName = user.DisplayName };
            string? note = await CloseAccountAsync(user, info, "account.delete", token).ConfigureAwait(false);
            await _Email.SendToMemberAsync("account_deleted", copy, "account_deleted:" + copy.Id, token).ConfigureAwait(false);
            return new DeleteAccountResponse { Message = "Your account has been deleted.", StoreSubscriptionNote = note };
        }

        /// <summary>
        /// Delete another member's account on behalf of staff: the same anonymization as a self-service deletion (sessions
        /// revoked, sign-in methods and personal data removed, Stripe canceled at period end), audited as member.delete
        /// with the reason. The member is told by email. Administrators cannot delete their own account here, and a
        /// member who holds SystemAdmin must have that role removed first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="reason">Reason (required, at most 500 characters).</param>
        /// <param name="info">Request info (the acting administrator).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response with a note about store subscriptions the member still has to cancel.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 409 CannotDeleteSelf, or 409 CannotDeleteAdmin.</exception>
        public async Task<DeleteAccountResponse> AdminDeleteAccountAsync(string userId, string? reason, RequestInfo info, CancellationToken token = default)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            string why = (reason ?? String.Empty).Trim();
            if (why.Length == 0) throw ApiErrorException.Field("reason", "Give a reason.");
            if (why.Length > 500) throw ApiErrorException.Field("reason", "Use at most 500 characters.");
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw new ApiErrorException(404, "NotFound", "Member not found.");
            if (info.Context?.UserId == user.Id) throw new ApiErrorException(409, "CannotDeleteSelf", "You cannot delete your own account here. Use Account settings instead.");
            List<RoleAssignment> roles = await _Database.RoleAssignments.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            if (roles.Any(r => r.Role == RoleEnum.SystemAdmin)) throw new ApiErrorException(409, "CannotDeleteAdmin", "Remove this member's administrator role before deleting the account.");
            User copy = new User { Id = user.Id, Email = user.Email, DisplayName = user.DisplayName };
            string? note = await CloseAccountAsync(user, info, "member.delete", token, "Account deleted by staff and anonymized: " + why).ConfigureAwait(false);
            if (copy.Email != null) await _Email.SendToMemberAsync("account_deleted", copy, "account_deleted:" + copy.Id, token).ConfigureAwait(false);
            return new DeleteAccountResponse { Message = "The account has been deleted.", StoreSubscriptionNote = note };
        }

        /// <summary>
        /// Create the bootstrap administrator when auth.bootstrapAdminEmail and auth.bootstrapAdminPassword are both set
        /// and the property has no members: the account is created with a verified email, the configured password, and
        /// SystemAdmin, and the property's current documents recorded as accepted. The password is taken as configured (the sign-up length rule does not apply) and the email only
        /// needs one @, so local addresses work. Does nothing once any member exists.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created member, or null when nothing was created.</returns>
        public async Task<User?> CreateBootstrapAdminIfNoMembersAsync(CancellationToken token = default)
        {
            string email = Validators.NormalizeEmail(_Settings.Auth.BootstrapAdminEmail) ?? String.Empty;
            string password = _Settings.Auth.BootstrapAdminPassword ?? String.Empty;
            if (email.Length == 0 || password.Length == 0) return null;
            int at = email.IndexOf('@');
            if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1 || email.Any(Char.IsWhiteSpace))
            {
                _Logger.Warn("[AccountService] auth.bootstrapAdminEmail is not an email address; the bootstrap administrator was not created");
                return null;
            }
            if (await _Database.Users.CountAsync(null, token).ConfigureAwait(false) > 0) return null;
            if (await _Database.Users.ReadByEmailAsync(email, token).ConfigureAwait(false) != null) return null;
            if (password.Length < Validators.MinPasswordLength)
                _Logger.Warn("[AccountService] auth.bootstrapAdminPassword is shorter than " + Validators.MinPasswordLength + " characters; use it for local development only");

            DateTime now = DateTime.UtcNow;
            User user = new User
            {
                Id = IdGenerator.GenerateUserId(),
                Email = email,
                EmailVerified = true,
                EmailVerifiedUtc = now,
                DisplayName = "Administrator",
                AgeAttestedUtc = now,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                await _Database.Users.CreateAsync(user, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                return null;
            }
            await _Database.MemberSearch.ReplaceWordsAsync(user.Id, user.DisplayName, token).ConfigureAwait(false);
            await _Database.Credentials.UpsertAsync(new UserCredential
            {
                Id = IdGenerator.GenerateCredentialId(),
                UserId = user.Id,
                PasswordHash = _Integrations.PasswordHasher.Hash(password),
                CreatedUtc = now,
                UpdatedUtc = now
            }, token).ConfigureAwait(false);
            // The configured administrator accepts the property's current documents by configuration, so the console
            // opens without the acceptance prompt.
            if (Documents != null) await Documents.RecordAsync(user.Id, Documents.Snapshot.ToList(), true, new RequestInfo(), token).ConfigureAwait(false);
            await _Audit.WriteAsync(null, null, "member.bootstrap", "User", user.Id, "Bootstrap administrator created at startup", token).ConfigureAwait(false);
            await GrantBootstrapAdminIfEligibleAsync(user, token).ConfigureAwait(false);
            _Logger.Info("[AccountService] created the bootstrap administrator " + email + " (" + user.Id + ")");
            return user;
        }

        /// <summary>
        /// Grant SystemAdmin to the member whose verified email matches auth.bootstrapAdminEmail.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the role was granted by this call.</returns>
        public async Task<bool> GrantBootstrapAdminIfEligibleAsync(User user, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            string configured = _Settings.Auth.BootstrapAdminEmail?.Trim() ?? String.Empty;
            if (configured.Length == 0 || !user.EmailVerified || user.Email == null || user.IsDeleted) return false;
            if (!String.Equals(configured, user.Email, StringComparison.OrdinalIgnoreCase)) return false;
            bool added = await _Database.RoleAssignments.AddAsync(new RoleAssignment
            {
                Id = IdGenerator.GenerateRoleAssignmentId(),
                UserId = user.Id,
                Role = RoleEnum.SystemAdmin,
                ResourceType = ResourceTypeEnum.Global,
                ResourceId = "*",
                CreatedUtc = DateTime.UtcNow
            }, token).ConfigureAwait(false);
            if (added)
            {
                _Tokens.InvalidateUser(user.Id);
                _Logger.Info("[AccountService] granted SystemAdmin to the bootstrap administrator " + user.Id);
                await _Audit.WriteAsync(null, null, "member.roles", "User", user.Id, "SystemAdmin granted to the bootstrap administrator email", token).ConfigureAwait(false);
            }
            return added;
        }

        /// <summary>
        /// Sign-in methods available to a member.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Methods.</returns>
        public async Task<List<SignInMethodEnum>> MethodsAsync(string userId, CancellationToken token = default)
        {
            bool hasPassword = await _Database.Credentials.ReadByUserAsync(userId, token).ConfigureAwait(false) != null;
            List<ExternalIdentity> identities = await _Database.ExternalIdentities.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            return MemberViewBuilder.SignInMethods(hasPassword, identities);
        }

        /// <summary>
        /// Message for a provider that is not configured, naming the missing settings.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <returns>Message.</returns>
        public static string NotConfiguredMessage(ExternalProviderEnum provider)
        {
            switch (provider)
            {
                case ExternalProviderEnum.Apple:
                    return "Sign in with Apple is not configured. Set oauth.appleBundleId or oauth.appleServicesId.";
                case ExternalProviderEnum.Google:
                    return "Google Sign-In is not configured. Set oauth.googleWebClientId, oauth.googleIosClientId, or oauth.googleAndroidClientId.";
                default:
                    return "Facebook Login is not configured. Set oauth.facebookAppId and oauth.facebookAppSecret.";
            }
        }

        #endregion

        #region Private-Methods

        private async Task<ExternalIdentityClaims> VerifyAsync(ExternalProviderEnum provider, string? presentedToken, ExternalTokenTypeEnum tokenType, string? nonce, bool signIn, CancellationToken token)
        {
            string method = provider.ToString();
            IIdentityTokenVerifier verifier = _Integrations.VerifierFor(provider);
            if (!verifier.IsConfigured) throw Count(signIn, method, "not_configured", new ApiErrorException(503, "IntegrationNotConfigured", NotConfiguredMessage(provider)));
            if (String.IsNullOrWhiteSpace(presentedToken)) throw Count(signIn, method, "invalid_token", ApiErrorException.Field(provider == ExternalProviderEnum.Apple ? "identityToken" : provider == ExternalProviderEnum.Google ? "idToken" : "token", "A token is required."));
            try
            {
                ExternalIdentityClaims claims = await verifier.VerifyAsync(presentedToken.Trim(), tokenType, String.IsNullOrWhiteSpace(nonce) ? null : nonce, token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(claims.Subject)) throw new IdentityTokenException("The token has no subject.");
                return claims;
            }
            catch (IdentityTokenException e)
            {
                _Logger.Debug("[AccountService] " + method + " token rejected: " + e.Message);
                throw Count(signIn, method, "invalid_token", new ApiErrorException(401, "InvalidIdentityToken", "The " + method + " sign-in could not be verified. Try again."));
            }
            catch (IntegrationNotConfiguredException)
            {
                throw Count(signIn, method, "not_configured", new ApiErrorException(503, "IntegrationNotConfigured", NotConfiguredMessage(provider)));
            }
        }

        private static ApiErrorException Count(bool signIn, string method, string outcome, ApiErrorException error)
        {
            if (signIn) TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignIns, "method", method, "outcome", outcome);
            return error;
        }

        private async Task MarkVerifiedFromProviderAsync(User user, ExternalIdentityClaims claims, DateTime now, CancellationToken token)
        {
            if (user.EmailVerified || user.Email == null || !claims.EmailVerified || claims.Email == null) return;
            if (!String.Equals(user.Email, claims.Email.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            user.EmailVerified = true;
            user.EmailVerifiedUtc = now;
            user.UpdatedUtc = now;
            await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
        }

        private async Task<string?> CloseAccountAsync(User user, RequestInfo info, string action, CancellationToken token, string? auditDetail = null)
        {
            DateTime now = DateTime.UtcNow;
            string? note = null;
            List<Subscription> subscriptions = await _Database.Subscriptions.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            foreach (Subscription subscription in subscriptions.Where(s => s.Status == SubscriptionStatusEnum.Active || s.Status == SubscriptionStatusEnum.InGrace || s.Status == SubscriptionStatusEnum.InBillingRetry))
            {
                if (subscription.Store == StoreEnum.Apple && subscription.AutoRenew) note = "Cancel your App Store subscription in iPhone Settings.";
                else if (subscription.Store == StoreEnum.Google && subscription.AutoRenew) note = "Cancel your Google Play subscription in the Play Store app.";
                else if (subscription.Store == StoreEnum.Stripe && subscription.AutoRenew)
                {
                    try
                    {
                        if (_Integrations.Stripe.IsConfigured) await _Integrations.Stripe.CancelAtPeriodEndAsync(subscription.StoreSubscriptionId, token).ConfigureAwait(false);
                        subscription.AutoRenew = false;
                        subscription.UpdatedUtc = now;
                        await _Database.Subscriptions.UpdateAsync(subscription, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is StoreVerificationException || e is IntegrationNotConfiguredException || e is System.Net.Http.HttpRequestException)
                    {
                        _Logger.Error("[AccountService] could not cancel Stripe subscription " + subscription.Id + " for deleted account " + user.Id, e);
                    }
                }
            }

            _Avatars.Delete(user.AvatarFile);
            await _Database.Notifications.DeleteForUserAsync(user.Id, token).ConfigureAwait(false);
            await _Database.PushTokens.DeleteByUserAsync(user.Id, token).ConfigureAwait(false);
            await _Database.NotificationPreferences.DeleteByUserAsync(user.Id, token).ConfigureAwait(false);
            await _Database.EmailSends.DeleteByUserAsync(user.Id, token).ConfigureAwait(false);
            await _Database.Goals.ReplaceForUserAsync(user.Id, new List<string>(), token).ConfigureAwait(false);
            await _Database.RankHistory.ReplaceForUserAsync(user.Id, new List<RankHistoryEntry>(), token).ConfigureAwait(false);
            await _Database.ProfileValues.DeleteForUserAsync(user.Id, token).ConfigureAwait(false);
            await _Database.Metadata.DeletePublicAsync(TargetTypeEnum.User, user.Id, token).ConfigureAwait(false);
            await _Database.ExternalIdentities.DeleteByUserAsync(user.Id, token).ConfigureAwait(false);
            await _Database.Credentials.DeleteByUserAsync(user.Id, token).ConfigureAwait(false);
            await _Database.OneTimeTokens.InvalidateAsync(user.Id, OneTimeTokenPurposeEnum.VerifyEmail, now, token).ConfigureAwait(false);
            await _Database.OneTimeTokens.InvalidateAsync(user.Id, OneTimeTokenPurposeEnum.ResetPassword, now, token).ConfigureAwait(false);

            user.Email = null;
            user.EmailVerified = false;
            user.EmailVerifiedUtc = null;
            user.DisplayName = "Deleted member";
            user.Handle = null;
            user.AvatarFile = null;
            user.Bio = null;
            user.Rank = null;
            user.RankLevel = 0;
            user.Affiliation = null;
            user.City = null;
            user.Region = null;
            user.Country = null;
            user.GoalsOther = null;
            user.IsDeleted = true;
            user.DeletedUtc = now;
            user.UpdatedUtc = now;
            await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            await _Database.Users.SetPalettePresetAsync(user.Id, null, token).ConfigureAwait(false);
            await _Database.MemberSearch.DeleteWordsAsync(user.Id, token).ConfigureAwait(false);
            await _Database.Blocks.DeleteByUserAsync(user.Id, token).ConfigureAwait(false);
            await _Sessions.RevokeAllAsync(user.Id, null, "account deleted", token).ConfigureAwait(false);
            if (AccountClosed != null) await AccountClosed(user.Id, token).ConfigureAwait(false);
            string detail = auditDetail ?? (action == "account.delete" ? "Account deleted and anonymized" : "Account closed: under 18");
            await _Audit.WriteAsync(info.Context, info.SourceIp, action, "User", user.Id, detail, token).ConfigureAwait(false);
            return note;
        }

        private async Task SetPasswordAsync(string userId, string password, DateTime now, CancellationToken token)
        {
            UserCredential? existing = await _Database.Credentials.ReadByUserAsync(userId, token).ConfigureAwait(false);
            await _Database.Credentials.UpsertAsync(new UserCredential
            {
                Id = existing?.Id ?? IdGenerator.GenerateCredentialId(),
                UserId = userId,
                PasswordHash = _Integrations.PasswordHasher.Hash(password),
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            }, token).ConfigureAwait(false);
        }

        private async Task SendVerificationAsync(User user, CancellationToken token)
        {
            if (user.Email == null) return;
            await _Database.OneTimeTokens.InvalidateAsync(user.Id, OneTimeTokenPurposeEnum.VerifyEmail, DateTime.UtcNow, token).ConfigureAwait(false);
            string raw = await CreateOneTimeAsync(user, OneTimeTokenPurposeEnum.VerifyEmail, TimeSpan.FromHours(48), token).ConfigureAwait(false);
            await _Email.SendVerificationAsync(user.Email, user.DisplayName, raw, token).ConfigureAwait(false);
        }

        private async Task<string> CreateOneTimeAsync(User user, OneTimeTokenPurposeEnum purpose, TimeSpan lifetime, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            string raw = TokenService.NewOpaqueToken();
            await _Database.OneTimeTokens.CreateAsync(new OneTimeToken
            {
                Id = IdGenerator.GenerateOneTimeTokenId(),
                UserId = user.Id,
                Purpose = purpose,
                TokenHash = TokenService.Hash(raw),
                Email = user.Email,
                CreatedUtc = now,
                ExpiresUtc = now.Add(lifetime)
            }, token).ConfigureAwait(false);
            return raw;
        }

        private async Task<OneTimeToken> ReadOneTimeAsync(string? rawToken, OneTimeTokenPurposeEnum purpose, DateTime now, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 200) throw InvalidToken();
            OneTimeToken? stored = await _Database.OneTimeTokens.ReadByHashAsync(TokenService.Hash(rawToken.Trim()), token).ConfigureAwait(false);
            if (stored == null || stored.Purpose != purpose || stored.UsedUtc != null || stored.ExpiresUtc <= now) throw InvalidToken();
            return stored;
        }

        private async Task<User> RequireUserAsync(string userId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(userId)) throw ApiErrorException.Forbidden("This route needs a member session.");
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw new ApiErrorException(401, "Unauthorized", "Sign in again.");
            return user;
        }

        private static string SocialDisplayName(ExternalIdentityClaims claims, string? givenName, string? familyName)
        {
            string candidate = String.Join(" ", new[] { givenName, familyName }.Where(n => !String.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()));
            if (candidate.Length < 2) candidate = claims.DisplayName?.Trim() ?? String.Empty;
            if (candidate.Length < 2) candidate = String.Join(" ", new[] { claims.GivenName, claims.FamilyName }.Where(n => !String.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()));
            if (candidate.Length < 2) candidate = "Member";
            return candidate.Length > 60 ? candidate.Substring(0, 60).Trim() : candidate;
        }

        private static void Put(Dictionary<string, string> errors, string field, string? message)
        {
            if (message != null) errors[field] = message;
        }

        private static ApiErrorException SignInFailed(string method, string outcome, ApiErrorException error)
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignIns, "method", method, "outcome", outcome);
            return error;
        }

        private static ApiErrorException Locked(DateTime lockedUntilUtc, DateTime now)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((lockedUntilUtc - now).TotalMinutes));
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignIns, "method", "Email", "outcome", "locked");
            return new ApiErrorException(423, "AccountLocked", "Too many failed sign-in attempts. Try again in " + minutes + " minute" + (minutes == 1 ? String.Empty : "s") +
                " (after " + lockedUntilUtc.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC), or reset your password.");
        }

        private static ApiErrorException Banned()
        {
            return new ApiErrorException(403, "AccountBanned", "This account has been suspended.");
        }

        private static ApiErrorException EmailInUse()
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.SignUps, "method", "Email", "outcome", "email_in_use");
            return new ApiErrorException(409, "EmailInUse", "An account with this email already exists. Sign in instead.");
        }

        private static ApiErrorException InvalidToken()
        {
            return new ApiErrorException(400, "InvalidToken", "This link is invalid or has expired. Request a new one.");
        }

        private static ApiErrorException RateLimited(int retryAfterSeconds)
        {
            return ApiErrorException.RateLimited(retryAfterSeconds);
        }

        private async Task NotifyNewMemberAsync(User user, RequestInfo info, List<string>? documents, CancellationToken token)
        {
            // Staff email details (docs/EMAIL.md): an audit record per sign-up (the same-IP signal) and request and member details for staff.
            // The new member is the actor: they created the account themselves.
            await _Database.Audit.CreateAsync(new AuditRecord
            {
                Id = IdGenerator.GenerateAuditRecordId(),
                ActorUserId = user.Id,
                ActorName = user.DisplayName,
                Action = Email.StaffEmailDetailsService.SignupAuditAction,
                TargetType = "User",
                TargetId = user.Id,
                Summary = "Account created",
                SourceIp = info.SourceIp,
                CreatedUtc = DateTime.UtcNow
            }, token).ConfigureAwait(false);
            string details = String.Empty;
            Email.StaffEmailDetailsService? builder = _Email.StaffDetails;
            if (builder != null)
            {
                try
                {
                    Email.StaffEmailDetails sections = new Email.StaffEmailDetails();
                    Email.StaffEmailSection signup = new Email.StaffEmailSection("Sign-up");
                    int? sameIp = await builder.SameIpSignupsAsync(info.SourceIp, token).ConfigureAwait(false);
                    if (sameIp.HasValue) signup.Row("Accounts from this IP in 24 hours", sameIp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + (sameIp.Value > 1 ? " (including this one)" : " (this one)"));
                    if (documents != null && documents.Count > 0) signup.Row("Documents accepted", String.Join(", ", documents));
                    sections.Add(await builder.MemberAsync(user.Id, "Member", token).ConfigureAwait(false));
                    sections.Add(signup);
                    sections.Add(await builder.RequestAsync(info, token).ConfigureAwait(false));
                    details = sections.Serialize();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Logger.Warn("[AccountService] staff details for " + user.Id + " unavailable: " + e.Message);
                }
            }
            await _Email.NotifyStaffAsync("staff_new_member", "new_member:" + user.Id, token, ("subjectName", user.DisplayName), ("subjectEmail", user.Email ?? String.Empty),
                ("link", _Email.Link("/admin/members/" + user.Id)), (Email.StaffEmailDetails.Token, details)).ConfigureAwait(false);
        }

        #endregion
    }
}
