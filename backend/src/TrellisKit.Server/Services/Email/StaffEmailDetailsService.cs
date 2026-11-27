namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Usage;

    /// <summary>
    /// Builds the shared sections of staff notification emails (docs/EMAIL.md, "Staff email details"): request details (IP address,
    /// client, platform, app version, browser, time, request id), member details (name, handle, email, account, sign-up
    /// method, created, location, roles, membership, sign-up code), and the origin of system-originated emails (source,
    /// site, server version, trace id). No location is derived from an IP address. Every lookup is bounded.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public class StaffEmailDetailsService
    {
        #region Public-Members

        /// <summary>
        /// Property setting that turns the request details section on or off (default true).
        /// </summary>
        public const string RequestDetailsSetting = "email.staffRequestDetails";

        /// <summary>
        /// Audit action written for every new account, counted for the same-IP sign-up signal.
        /// </summary>
        public const string SignupAuditAction = "account.signup";

        /// <summary>
        /// Clock (UTC), replaceable in tests.
        /// </summary>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? (() => DateTime.UtcNow); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for one property.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">services is null.</exception>
        public StaffEmailDetailsService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the request details section is on (<see cref="RequestDetailsSetting"/>, default true).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when on.</returns>
        public async Task<bool> RequestDetailsEnabledAsync(CancellationToken token = default)
        {
            string value = await _Services.PropertySettings.GetOrDefaultAsync(RequestDetailsSetting, token).ConfigureAwait(false);
            return !String.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The "Request" section for an email triggered by a member request, or null when there is no request or the
        /// setting is off.
        /// </summary>
        /// <param name="info">Request info, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Section, or null.</returns>
        public async Task<StaffEmailSection?> RequestAsync(RequestInfo? info, CancellationToken token = default)
        {
            if (info == null || !await RequestDetailsEnabledAsync(token).ConfigureAwait(false)) return null;
            StaffEmailSection section = new StaffEmailSection("Request");
            section.Row("IP address", info.SourceIp);
            section.Row("Client", Client(info.ClientType));
            section.Row("Platform", info.ClientPlatform);
            section.Row("App version", info.ClientVersion);
            section.Row("Browser", UserAgentSummary.Describe(info.UserAgent));
            section.Row("Time", await TimeAsync(info.ReceivedUtc, token).ConfigureAwait(false));
            if (!String.IsNullOrEmpty(info.RequestId)) section.Row("Request id", info.RequestId + " (" + _Services.Email.Link("/admin/requests?id=" + Uri.EscapeDataString(info.RequestId)) + ")");
            return section.Rows.Count == 0 ? null : section;
        }

        /// <summary>
        /// A member details section, or null when the member does not exist.
        /// </summary>
        /// <param name="userId">Member id, or null.</param>
        /// <param name="title">Section title, for example Member, Reporter, or Author.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Section, or null.</returns>
        public async Task<StaffEmailSection?> MemberAsync(string? userId, string title, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(userId)) return null;
            User? user = await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null) return null;
            DateTime now = _Clock();
            StaffEmailSection section = new StaffEmailSection(String.IsNullOrWhiteSpace(title) ? "Member" : title);
            section.Row("Name", user.DisplayName);
            section.Row("Handle", String.IsNullOrWhiteSpace(user.Handle) ? null : "@" + user.Handle);
            section.Row("Email", String.IsNullOrWhiteSpace(user.Email) ? null : user.Email + (user.EmailVerified ? " (verified)" : " (not verified)"));
            section.Row("Account id", user.Id);
            section.Row("Sign-up method", await SignupMethodAsync(user, token).ConfigureAwait(false));
            section.Row("Created", await TimeAsync(user.CreatedUtc, token).ConfigureAwait(false) + ", " + Age(now - user.CreatedUtc) + " ago");
            section.Row("Location", Join(user.City, user.Region, user.Country));
            section.Row("Age attested", user.AgeAttestedUtc.HasValue ? "Yes" : "No");
            section.Row("Roles", await RolesAsync(user.Id, token).ConfigureAwait(false));
            section.Row("Membership", await MembershipAsync(user.Id, token).ConfigureAwait(false));
            section.Row("Sign-up code", await SignupCodeAsync(user.Id, token).ConfigureAwait(false));
            section.Row("Status", Status(user, now));
            section.Row("Admin page", _Services.Email.Link("/admin/members/" + user.Id));
            return section;
        }

        /// <summary>
        /// A reporter's member details plus the reports they filed in the last 90 days.
        /// </summary>
        /// <param name="reporterUserId">Reporter, or null for a system report.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Section, or null.</returns>
        public async Task<StaffEmailSection?> ReporterAsync(string? reporterUserId, CancellationToken token = default)
        {
            StaffEmailSection? section = await MemberAsync(reporterUserId, "Reporter", token).ConfigureAwait(false);
            if (section == null) return null;
            long filed = await _Services.Database.Reports.CountByReporterAsync(reporterUserId!, _Clock().AddDays(-ModerationService.AuthorHistoryDays), token).ConfigureAwait(false);
            section.Row("Reports filed in 90 days", filed.ToString(CultureInfo.InvariantCulture));
            return section;
        }

        /// <summary>
        /// A content author's member details plus the moderation author history of the last 90 days (removals, hides
        /// including automatic hides, and reports against the author).
        /// </summary>
        /// <param name="authorUserId">Author, or null (assistant replies have none).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Section, or null.</returns>
        public async Task<StaffEmailSection?> AuthorAsync(string? authorUserId, CancellationToken token = default)
        {
            StaffEmailSection? section = await MemberAsync(authorUserId, "Author", token).ConfigureAwait(false);
            if (section == null) return null;
            DateTime since = _Clock().AddDays(-ModerationService.AuthorHistoryDays);
            long removed = await _Services.Database.ModerationActions.CountByAuthorAsync(authorUserId!, new[] { ModerationActionTypeEnum.Remove }, since, token).ConfigureAwait(false);
            long hidden = await _Services.Database.ModerationActions.CountByAuthorAsync(authorUserId!, new[] { ModerationActionTypeEnum.Hide, ModerationActionTypeEnum.AutoHide }, since, token).ConfigureAwait(false);
            long against = await _Services.Database.Reports.CountAgainstAuthorAsync(authorUserId!, since, token).ConfigureAwait(false);
            section.Row("History (90 days)", Plural(removed, "removal") + ", " + Plural(hidden, "hide") + ", " + Plural(against, "report") + " against");
            return section;
        }

        /// <summary>
        /// The reported or hidden content: type, id, scope, moderation state, an excerpt of at most 300 characters, and
        /// the open reports on it now.
        /// </summary>
        /// <param name="target">Moderation target.</param>
        /// <param name="scopeType">Scope (Feed, Forum, Directory, Events, Library, Messaging), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Section.</returns>
        /// <exception cref="ArgumentNullException">target is null.</exception>
        public async Task<StaffEmailSection> ContentAsync(ModerationTarget target, string? scopeType, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            StaffEmailSection section = new StaffEmailSection("Content");
            section.Row("Type", EmailLabels.TargetType(target.TargetType));
            section.Row("Id", target.TargetId);
            section.Row("Scope", scopeType ?? target.ScopeType);
            section.Row("Title", target.Thread?.Title ?? target.Post?.Title ?? target.Title);
            section.Row("Excerpt", Excerpt(ExcerptOf(target), 300));
            section.Row("State", target.Deleted ? "Deleted" : target.State.ToString());
            long open = await _Services.Database.Reports.CountOpenOnTargetAsync(target.TargetType, target.TargetId, null, token).ConfigureAwait(false);
            section.Row("Open reports", open.ToString(CultureInfo.InvariantCulture));
            return section;
        }

        /// <summary>
        /// The text of a moderation target for an excerpt (post, comment, thread, provider content, or member bio).
        /// </summary>
        /// <param name="target">Target.</param>
        /// <returns>Text, or null.</returns>
        public static string? ExcerptOf(ModerationTarget target)
        {
            if (target == null) return null;
            if (target.TargetType == TargetTypeEnum.User) return target.User?.Bio;
            return FirstText(target.Post?.Excerpt, target.Comment?.Excerpt, target.Thread?.Excerpt, target.ExtensionExcerpt, target.Excerpt);
        }

        /// <summary>
        /// The "Origin" section of an email that no member request triggered (a store notification, a worker, or a job):
        /// where it came from, the site, the server version, and the current trace id when one exists. Never an IP.
        /// </summary>
        /// <param name="source">Where the event came from, for example "App Store notification" or "Scheduled job".</param>
        /// <returns>Section.</returns>
        public StaffEmailSection Origin(string source)
        {
            StaffEmailSection section = new StaffEmailSection("Origin");
            section.Row("Source", source);
            section.Row("Site", _Services.Property.PublicBaseUrl);
            section.Row("Server version", ServerVersion.Current);
            Activity? activity = Activity.Current;
            section.Row("Trace id", activity == null || activity.TraceId == default ? null : activity.TraceId.ToString());
            return section;
        }

        /// <summary>
        /// Accounts created from an IP address in the 24 hours before now (from <see cref="SignupAuditAction"/> audit
        /// records), counting at most 1000.
        /// </summary>
        /// <param name="sourceIp">IP address, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count, or null when the IP is unknown.</returns>
        public async Task<int?> SameIpSignupsAsync(string? sourceIp, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(sourceIp)) return null;
            return await _Services.Database.Audit.CountByActionAndIpSinceAsync(SignupAuditAction, sourceIp, _Clock().AddHours(-24), 1000, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A time in the property's time zone with UTC, for example "Wed Nov 25, 2026 1:04 PM America/Los_Angeles (21:04 UTC)",
        /// or "Wed Nov 25, 2026 21:04 UTC" when the property's zone is UTC.
        /// </summary>
        /// <param name="utc">Time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Text.</returns>
        public async Task<string> TimeAsync(DateTime utc, CancellationToken token = default)
        {
            DateTime value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            UsageZone zone = await _Services.Usage.ZoneAsync(token).ConfigureAwait(false);
            if (String.Equals(zone.Id, "UTC", StringComparison.OrdinalIgnoreCase) || String.Equals(zone.Id, "Etc/UTC", StringComparison.OrdinalIgnoreCase))
                return value.ToString("ddd MMM d, yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";
            DateTime local = zone.Local(value);
            return local.ToString("ddd MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) + " " + zone.Id + " (" + value.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC)";
        }

        /// <summary>
        /// A short age, for example "3 minutes", "5 hours", "12 days", or "2 years".
        /// </summary>
        /// <param name="age">Age; negative is treated as zero.</param>
        /// <returns>Text.</returns>
        public static string Age(TimeSpan age)
        {
            TimeSpan a = age < TimeSpan.Zero ? TimeSpan.Zero : age;
            if (a.TotalMinutes < 1) return "less than a minute";
            if (a.TotalHours < 1) return Plural((int)a.TotalMinutes, "minute");
            if (a.TotalDays < 1) return Plural((int)a.TotalHours, "hour");
            if (a.TotalDays < 365) return Plural((int)a.TotalDays, "day");
            return Plural((int)(a.TotalDays / 365), "year");
        }

        /// <summary>
        /// A submission history from status counts, for example "7 (Approved 5, Pending 1, Rejected 1), 71% approved".
        /// </summary>
        /// <param name="counts">Status name to count.</param>
        /// <param name="accepted">Status that counts as accepted, for example Approved.</param>
        /// <returns>Text; "None" when empty.</returns>
        public static string History(IReadOnlyDictionary<string, long> counts, string accepted)
        {
            long total = counts == null ? 0 : counts.Values.Sum();
            if (total == 0) return "None";
            long yes = counts!.TryGetValue(accepted, out long a) ? a : 0;
            string parts = String.Join(", ", counts.Where(c => c.Value > 0).OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value.ToString(CultureInfo.InvariantCulture)));
            return total.ToString(CultureInfo.InvariantCulture) + " (" + parts + "), " + ((int)Math.Round(100.0 * yes / total)).ToString(CultureInfo.InvariantCulture) + "% " + accepted.ToLowerInvariant();
        }

        /// <summary>
        /// "1 day" or "3 days".
        /// </summary>
        /// <param name="count">Count.</param>
        /// <param name="noun">Singular noun.</param>
        /// <returns>Text.</returns>
        public static string Plural(long count, string noun)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? String.Empty : "s");
        }

        /// <summary>
        /// The first characters of a text on one line, with an ellipsis when cut.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <param name="max">Maximum characters (at least 10).</param>
        /// <returns>Excerpt, or null when blank.</returns>
        public static string? Excerpt(string? text, int max)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string flat = String.Join(" ", text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            int limit = Math.Max(10, max);
            return flat.Length > limit ? flat.Substring(0, limit).TrimEnd() + "..." : flat;
        }

        #endregion

        #region Private-Methods

        private static string Client(ClientTypeEnum client)
        {
            if (client == ClientTypeEnum.Web) return "Web";
            if (client == ClientTypeEnum.Mobile) return "Mobile";
            return "API or other";
        }

        private async Task<string?> SignupMethodAsync(User user, CancellationToken token)
        {
            List<ExternalIdentity> identities = await _Services.Database.ExternalIdentities.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            UserCredential? credential = await _Services.Database.Credentials.ReadByUserAsync(user.Id, token).ConfigureAwait(false);
            ExternalIdentity? first = identities.OrderBy(i => i.CreatedUtc).FirstOrDefault();
            bool externalFirst = first != null && first.CreatedUtc <= user.CreatedUtc.AddMinutes(1) && (credential == null || first.CreatedUtc <= credential.CreatedUtc);
            string method = externalFirst ? first!.Provider.ToString() : credential != null ? "Email and password" : first != null ? first.Provider.ToString() : "Unknown";
            List<string> linked = identities.Select(i => i.Provider.ToString()).Distinct(StringComparer.Ordinal).Where(p => p != method).ToList();
            if (credential != null && method != "Email and password") linked.Add("password");
            return linked.Count == 0 ? method : method + " (also " + String.Join(", ", linked) + ")";
        }

        private async Task<string?> RolesAsync(string userId, CancellationToken token)
        {
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            List<string> names = roles.Where(r => r.Role != RoleEnum.User).Take(10)
                .Select(r => r.ResourceType == ResourceTypeEnum.Global ? r.Role.ToString() : r.Role + " (" + r.ResourceType + " " + r.ResourceId + ")").ToList();
            return names.Count == 0 ? "Member" : String.Join(", ", names);
        }

        private async Task<string?> MembershipAsync(string userId, CancellationToken token)
        {
            MembershipState state = await _Services.Membership.GetStateAsync(userId, token).ConfigureAwait(false);
            if (!state.Summary.IsActive) return "None";
            string text = "Active";
            if (state.Summary.Source.HasValue) text += ", " + state.Summary.Source.Value;
            if (state.Summary.Store.HasValue) text += " (" + state.Summary.Store.Value + ")";
            if (state.Summary.ExpiresUtc.HasValue) text += ", until " + state.Summary.ExpiresUtc.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
            if (state.Summary.AutoRenew) text += ", renews";
            return text;
        }

        private async Task<string?> SignupCodeAsync(string userId, CancellationToken token)
        {
            List<SignupCodeRedemption> redemptions = await _Services.Database.SignupCodeRedemptions.EnumerateByUserAfterAsync(userId, null, 1, token).ConfigureAwait(false);
            SignupCodeRedemption? first = redemptions.FirstOrDefault();
            if (first == null) return null;
            SignupCode? code = await _Services.Database.SignupCodes.ReadAsync(first.SignupCodeId, token).ConfigureAwait(false);
            if (code == null) return null;
            return String.IsNullOrWhiteSpace(code.Name) ? code.Code : code.Code + " (" + code.Name + ")";
        }

        private static string? Status(User user, DateTime now)
        {
            List<string> flags = new List<string>();
            if (user.IsDeleted) flags.Add("Deleted");
            if (user.IsBanned) flags.Add("Banned" + (String.IsNullOrWhiteSpace(user.BannedReason) ? String.Empty : ": " + user.BannedReason));
            if (user.IsMutedAt(now)) flags.Add("Muted");
            if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > now) flags.Add("Sign-in locked");
            return flags.Count == 0 ? null : String.Join(", ", flags);
        }

        private static string? FirstText(params string?[] values)
        {
            return values.FirstOrDefault(v => !String.IsNullOrWhiteSpace(v));
        }

        private static string? Join(params string?[] parts)
        {
            List<string> present = parts.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
            return present.Count == 0 ? null : String.Join(", ", present);
        }

        #endregion
    }
}
