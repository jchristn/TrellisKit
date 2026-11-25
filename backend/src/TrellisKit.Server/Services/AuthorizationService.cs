namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services.Documents;

    /// <summary>
    /// The single authorization stage for every authenticated route (contract section 4, Phase 2 section 1). Paths are
    /// classified into route groups; Admin needs SystemAdmin or the break-glass token, Moderator also admits Moderator and
    /// FeedModerator role holders (per-item scope is checked in the moderation service), SignedIn needs a session (purchases and codes
    /// also need the age attestation), and everything else needs the age attestation and an active membership.
    /// SystemAdmin members and the break-glass token pass the age and membership checks. Because the check runs for
    /// every post-authentication request, a route cannot bypass the paywall by forgetting it.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public class AuthorizationService
    {
        #region Public-Members

        /// <summary>
        /// The property's minimum age in years, quoted by AgeAttestationRequired.
        /// </summary>
        public int MinimumAge
        {
            get { return _MinimumAge; }
        }

        /// <summary>
        /// Supplies the property's current access configuration. Default FullyGated.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<AccessPolicy> Access
        {
            get { return _Access; }
            set { _Access = value ?? throw new ArgumentNullException(nameof(Access)); }
        }

        /// <summary>
        /// Supplies property.json features.signupCodes (sign-up code member routes are 404 NotAvailable when false).
        /// Default true.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<bool> SignupCodesFeature
        {
            get { return _SignupCodesFeature; }
            set { _SignupCodesFeature = value ?? throw new ArgumentNullException(nameof(SignupCodesFeature)); }
        }

        /// <summary>
        /// Supplies property.json features.directory (the member directory routes are 404 NotAvailable when false).
        /// Default true.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<bool> DirectoryFeature
        {
            get { return _DirectoryFeature; }
            set { _DirectoryFeature = value ?? throw new ArgumentNullException(nameof(DirectoryFeature)); }
        }

        /// <summary>
        /// Supplies property.json features.search (the global search routes are 404 NotAvailable when false).
        /// Default true.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<bool> SearchFeature
        {
            get { return _SearchFeature; }
            set { _SearchFeature = value ?? throw new ArgumentNullException(nameof(SearchFeature)); }
        }

        /// <summary>
        /// The property's events switch (features.events): when false, the events, calendar, me events, public events,
        /// and admin events routes answer 404 NotAvailable.
        /// </summary>
        public Func<bool> EventsFeature
        {
            get { return _EventsFeature; }
            set { _EventsFeature = value ?? throw new ArgumentNullException(nameof(EventsFeature)); }
        }

        /// <summary>
        /// The property's library switch (features.library): when false, the series, lessons, me library, public
        /// lessons, and admin library routes answer 404 NotAvailable.
        /// </summary>
        public Func<bool> LibraryFeature
        {
            get { return _LibraryFeature; }
            set { _LibraryFeature = value ?? throw new ArgumentNullException(nameof(LibraryFeature)); }
        }

        /// <summary>
        /// Supplies the gating documents members must have accepted (docs/TRELLISKIT.md section 14). Default none.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<IReadOnlyList<DocumentRequirement>> DocumentRequirements
        {
            get { return _DocumentRequirements; }
            set { _DocumentRequirements = value ?? throw new ArgumentNullException(nameof(DocumentRequirements)); }
        }

        #endregion

        #region Private-Members

        private const string _Api = Constants.ApiPrefix;
        private readonly int _MinimumAge;
        private Func<AccessPolicy> _Access = () => AccessPolicy.FullyGatedDefault;
        private Func<bool> _SignupCodesFeature = () => true;
        private Func<bool> _DirectoryFeature = () => true;
        private Func<bool> _SearchFeature = () => true;
        private Func<bool> _EventsFeature = () => true;
        private Func<bool> _LibraryFeature = () => true;
        private Func<IReadOnlyList<DocumentRequirement>> _DocumentRequirements = () => new List<DocumentRequirement>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="minimumAge">The property's minimum age in years (property.json membership.minimumAge), used in the
        /// AgeAttestationRequired message. Minimum 0, maximum 150.</param>
        /// <exception cref="ArgumentOutOfRangeException">The age is outside 0 to 150.</exception>
        public AuthorizationService(int minimumAge)
        {
            if (minimumAge < 0 || minimumAge > 150) throw new ArgumentOutOfRangeException(nameof(minimumAge), "The minimum age must be 0 to 150.");
            _MinimumAge = minimumAge;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Classify a request path.
        /// </summary>
        /// <param name="path">Path without query.</param>
        /// <returns>Route group.</returns>
        public static RouteGroupEnum Classify(string? path)
        {
            string p = (path ?? String.Empty).TrimEnd('/');
            if (p.Length == 0) return RouteGroupEnum.Member;
            if (StartsWith(p, _Api + "/me/export/") && p.EndsWith("/download", StringComparison.Ordinal)) return RouteGroupEnum.Anonymous;
            if (StartsWith(p, _Api + "/admin/moderation/word-filter")) return RouteGroupEnum.Admin;
            if (StartsWith(p, _Api + "/admin/moderation")) return RouteGroupEnum.Moderator;
            if (StartsWith(p, _Api + "/admin/trending") || StartsWith(p, _Api + "/admin/threads")) return RouteGroupEnum.Moderator;
            // Phase 4 staff routes (docs/PHASE4_CONTRACT.md sections 3.9, 4.7, 5.10): directory staff, event staff and
            // hosts, and library staff reach them with a member session; each service checks the role (or host) itself.
            if (IsPhase4Staff(p)) return RouteGroupEnum.SignedIn;
            if (StartsWith(p, _Api + "/admin") || StartsWith(p, _Api + "/settings") || StartsWith(p, _Api + "/request-history")) return RouteGroupEnum.Admin;
            if (Equals(p, _Api + "/auth/me") || Equals(p, _Api + "/auth/email/resend")) return RouteGroupEnum.SignedIn;
            // Public landing page slug checks (docs/PHASE4_CONTRACT.md section 18.3): members, hosts, staff, and admin principals.
            if (StartsWith(p, _Api + "/public-pages")) return RouteGroupEnum.SignedIn;
            if (StartsWith(p, _Api + "/me") || StartsWith(p, _Api + "/handles") || StartsWith(p, _Api + "/membership") || StartsWith(p, _Api + "/signup-codes")) return RouteGroupEnum.SignedIn;
            // Support is an account route in every model (docs/PHASE5_CONTRACT.md section 0.2), so a lapsed or unpaid member can
            // always reach billing help; announcements are read by any signed-in recipient of a broadcast.
            if (StartsWith(p, _Api + "/tickets") || StartsWith(p, _Api + "/support") || StartsWith(p, _Api + "/announcements")) return RouteGroupEnum.SignedIn;
            if (Equals(p, _Api + "/ws")) return RouteGroupEnum.Anonymous;
            if (StartsWith(p, _Api + "/health") || StartsWith(p, _Api + "/auth") || StartsWith(p, _Api + "/webhooks") || Equals(p, "/openapi.json") || Equals(p, "/swagger")) return RouteGroupEnum.Anonymous;
            if (StartsWith(p, _Api + "/media/files") || StartsWith(p, _Api + "/media/private") || StartsWith(p, _Api + "/media/local-uploads") || StartsWith(p, _Api + "/public") || StartsWith(p, _Api + "/email/unsubscribe")) return RouteGroupEnum.Anonymous;
            // Personal calendar feeds authenticate with the token in the path (docs/PHASE4_CONTRACT.md section 4.5).
            if (StartsWith(p, _Api + "/calendar")) return RouteGroupEnum.Anonymous;
            return RouteGroupEnum.Member;
        }

        /// <summary>
        /// Whether a signed-in route also needs the age attestation (purchases and sign-up codes).
        /// </summary>
        /// <param name="path">Path without query.</param>
        /// <returns>True when attestation is required.</returns>
        public static bool RequiresAttestation(string? path)
        {
            string p = (path ?? String.Empty).TrimEnd('/');
            if (StartsWith(p, _Api + "/membership/history")) return false;
            return StartsWith(p, _Api + "/membership/") || StartsWith(p, _Api + "/signup-codes");
        }

        /// <summary>
        /// Authorize an authenticated GET request (route groups only; see the overload with the method).
        /// </summary>
        /// <param name="context">Authenticated context.</param>
        /// <param name="path">Path without query.</param>
        /// <returns>Decision.</returns>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        public AuthorizationDecision Authorize(RequestContext context, string? path)
        {
            return Authorize(context, "GET", path);
        }

        /// <summary>
        /// Authorize a request under the property's access model (docs/ACCESS_MODELS_CONTRACT.md sections 2 to 4):
        /// routes that do not exist in the model are 404 NotAvailable for everyone; signed-out callers pass only on
        /// routes anonymous browsing opens; Admin and Moderator groups are unchanged; SignedIn routes need a session
        /// (and the age attestation for purchases and codes); Member routes need the age attestation and, in
        /// FullyGated, an active membership, while PartiallyGated and Open require the tier of
        /// <see cref="AccessDecider.RouteTier"/> for the route's declaration in <see cref="AccessRoutes"/>.
        /// </summary>
        /// <param name="context">Request context (signed out when IsAuthenticated is false).</param>
        /// <param name="method">HTTP method, or null for GET.</param>
        /// <param name="path">Path without query.</param>
        /// <returns>Decision.</returns>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        public AuthorizationDecision Authorize(RequestContext context, string? method, string? path)
        {
            ArgumentNullException.ThrowIfNull(context);
            AuthorizationDecision decision = Decide(context, method, path);
            // Pending documents come before the paywall (the client order is age, documents, profile, membership).
            if (!decision.IsPermitted && decision.Outcome != AuthorizationOutcomeEnum.MembershipRequired) return decision;
            return DocumentGate(context, decision.Group, path) ?? decision;
        }

        #endregion

        #region Private-Methods

        private AuthorizationDecision Decide(RequestContext context, string? method, string? path)
        {
            ArgumentNullException.ThrowIfNull(context);
            RouteGroupEnum group = Classify(path);
            AccessPolicy policy = _Access();
            if (AccessRoutes.IsNotAvailable(policy.Model, _SignupCodesFeature(), _DirectoryFeature(), _SearchFeature(), _EventsFeature(), _LibraryFeature(), method, path))
                return Deny(group, AuthorizationOutcomeEnum.Forbidden, 404, "NotAvailable", policy.Model == AccessModelEnum.Open && !(path ?? String.Empty).Contains("/signup-codes", StringComparison.OrdinalIgnoreCase)
                    ? "This property has no paid memberships, so this route is not available."
                    : "This route is not available for this property.");
            RouteAccess? route = group == RouteGroupEnum.Member ? AccessRoutes.Find(method, path) : null;
            if (!context.IsAuthenticated)
            {
                // RouteTier is Anonymous only for anonymous-eligible routes under anonymous browsing, and never for a feature
                // whose reads need a membership (forums gated in PartiallyGated).
                if (group == RouteGroupEnum.Member && route != null && AccessDecider.RouteTier(policy, route) == AccessTierEnum.Anonymous) return new AuthorizationDecision { Group = group };
                AuthorizationDecision denied = Deny(group, AuthorizationOutcomeEnum.Unauthorized, 401, "Unauthorized", "A valid bearer token is required.");
                if (group == RouteGroupEnum.Member && policy.Model != AccessModelEnum.FullyGated)
                {
                    denied.Required = "SignIn";
                    denied.Feature = route?.Feature == null ? null : AccessPolicy.FeatureKey(route.Feature.Value);
                }
                return denied;
            }
            if (context.IsAdmin) return new AuthorizationDecision { Group = group };

            switch (group)
            {
                case RouteGroupEnum.Admin:
                    if (context.IsSupportAgent && IsSupportStaffPath(path)) return new AuthorizationDecision { Group = group };
                    if (context.IsCommunicator && IsBroadcastPath(path)) return new AuthorizationDecision { Group = group };
                    if (context.IsReleaseManager && IsReleasePath(path)) return new AuthorizationDecision { Group = group };
                    return Deny(group, AuthorizationOutcomeEnum.Forbidden, 403, "Forbidden", "This account is not an administrator.");
                case RouteGroupEnum.Moderator:
                    if (context.IsModerator) return new AuthorizationDecision { Group = group };
                    // SupportAgent handles reports on ideas (docs/PHASE5_CONTRACT.md section 11); the moderation service scopes them.
                    if (context.IsSupportAgent && StartsWith((path ?? String.Empty).TrimEnd('/'), _Api + "/admin/moderation")) return new AuthorizationDecision { Group = group };
                    return Deny(group, AuthorizationOutcomeEnum.Forbidden, 403, "Forbidden", "This account is not a moderator.");
                case RouteGroupEnum.SignedIn:
                case RouteGroupEnum.Anonymous:
                    if (RequiresAttestation(path) && !context.AgeAttested) return AttestationRequired(group);
                    return new AuthorizationDecision { Group = group };
                default:
                    if (!context.AgeAttested) return AttestationRequired(group);
                    if (policy.Model == AccessModelEnum.FullyGated)
                    {
                        if (!context.HasMembership) return Deny(group, AuthorizationOutcomeEnum.MembershipRequired, 402, "MembershipRequired", "An active membership is required.");
                        return new AuthorizationDecision { Group = group };
                    }
                    AccessTierEnum required = AccessDecider.RouteTier(policy, route);
                    if (required == AccessTierEnum.Member && !context.HasMembership)
                    {
                        AuthorizationDecision denied = Deny(group, AuthorizationOutcomeEnum.MembershipRequired, 402, "MembershipRequired", "This is for members.");
                        denied.Required = "Membership";
                        denied.Feature = route?.Feature == null ? null : AccessPolicy.FeatureKey(route.Feature.Value);
                        return denied;
                    }
                    return new AuthorizationDecision { Group = group };
            }
        }

        private AuthorizationDecision? DocumentGate(RequestContext context, RouteGroupEnum group, string? path)
        {
            // docs/TRELLISKIT.md section 14: a member who has not accepted the required version of a gating document is
            // stopped on content, staff, purchase, and code routes; account routes stay open so the client can prompt.
            if (context.AcceptedDocuments == null || context.IsBreakGlass || !context.IsAuthenticated) return null;
            bool gated = group == RouteGroupEnum.Member || group == RouteGroupEnum.Admin || group == RouteGroupEnum.Moderator ||
                (group == RouteGroupEnum.SignedIn && (RequiresAttestation(path) || IsPhase4Staff((path ?? String.Empty).TrimEnd('/'))));
            if (!gated) return null;
            List<string> pending = new List<string>();
            foreach (DocumentRequirement requirement in _DocumentRequirements())
            {
                if (!context.AcceptedDocuments.TryGetValue(requirement.Key, out int accepted) || accepted < requirement.RequiredSequence) pending.Add(requirement.Key);
            }
            if (pending.Count == 0) return null;
            AuthorizationDecision denied = Deny(group, AuthorizationOutcomeEnum.DocumentAcceptanceRequired, 403, "DocumentAcceptanceRequired", "Review and accept the updated documents to continue.");
            denied.Documents = pending;
            return denied;
        }

        /// <summary>
        /// Admin routes a SupportAgent may use (docs/PHASE5_CONTRACT.md section 1.1): the ticket inbox, the ideas board,
        /// and support metrics. Ticket type and idea status administration stay SystemAdmin (the services check).
        /// </summary>
        /// <param name="path">Path without query.</param>
        /// <returns>True when the path is a support staff route.</returns>
        public static bool IsSupportStaffPath(string? path)
        {
            string p = (path ?? String.Empty).TrimEnd('/');
            return StartsWith(p, _Api + "/admin/tickets") || StartsWith(p, _Api + "/admin/ideas") || StartsWith(p, _Api + "/admin/support") || StartsWith(p, _Api + "/admin/metrics/support");
        }

        /// <summary>
        /// Admin routes a Communicator may use (as-built note D): broadcasts and their segments.
        /// </summary>
        /// <param name="path">Path without query.</param>
        /// <returns>True when the path is a broadcast route.</returns>
        public static bool IsBroadcastPath(string? path)
        {
            return StartsWith((path ?? String.Empty).TrimEnd('/'), _Api + "/admin/broadcasts");
        }

        /// <summary>
        /// Admin routes a ReleaseManager may use (docs/LIVE_UPDATES_CONTRACT.md as-built notes): update groups, updates,
        /// channels, runtime versions, health, failures and symbolication, and every app configuration route (flags,
        /// kill switches, strings, banners, assets, drafts, publish, versions). Signing and deploy keys stay SystemAdmin only.
        /// </summary>
        /// <param name="path">Path without query.</param>
        /// <returns>True when the path is a release route.</returns>
        public static bool IsReleasePath(string? path)
        {
            string p = (path ?? String.Empty).TrimEnd('/');
            if (StartsWith(p, _Api + "/admin/updates/signing-keys") || StartsWith(p, _Api + "/admin/updates/deploy-keys")) return false;
            return StartsWith(p, _Api + "/admin/update-groups") || StartsWith(p, _Api + "/admin/updates") || StartsWith(p, _Api + "/admin/app");
        }

        private AuthorizationDecision AttestationRequired(RouteGroupEnum group)
        {
            return Deny(group, AuthorizationOutcomeEnum.AgeAttestationRequired, 403, "AgeAttestationRequired", "Confirm you are " + _MinimumAge + " or older to continue.");
        }

        private static AuthorizationDecision Deny(RouteGroupEnum group, AuthorizationOutcomeEnum outcome, int status, string error, string message)
        {
            return new AuthorizationDecision { Group = group, Outcome = outcome, StatusCode = status, Error = error, Message = message };
        }

        private static bool IsPhase4Staff(string p)
        {
            return StartsWith(p, _Api + "/admin/venues") || StartsWith(p, _Api + "/admin/instructors") || StartsWith(p, _Api + "/admin/directory") ||
                StartsWith(p, _Api + "/admin/events") || StartsWith(p, _Api + "/admin/series") || StartsWith(p, _Api + "/admin/lessons") ||
                StartsWith(p, _Api + "/admin/library") || StartsWith(p, _Api + "/admin/metrics/events") || StartsWith(p, _Api + "/admin/metrics/library") ||
                StartsWith(p, _Api + "/admin/ranks") || StartsWith(p, _Api + "/admin/lineage") || StartsWith(p, _Api + "/admin/curricula") || StartsWith(p, _Api + "/admin/attendance");
        }

        private static bool StartsWith(string path, string prefix)
        {
            return path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix + "?", StringComparison.OrdinalIgnoreCase) || (prefix.EndsWith("/", StringComparison.Ordinal) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Equals(string path, string value)
        {
            return String.Equals(path, value, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
