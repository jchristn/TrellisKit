namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The access declaration of one content route (docs/ACCESS_MODELS_CONTRACT.md section 2): its feature (or none),
    /// its action, whether it is an account route (not a feature), and whether signed-out callers may reach it when
    /// the property allows anonymous browsing. Built by <see cref="AccessRoutes"/>.
    /// </summary>
    public class RouteAccess
    {
        #region Public-Members

        /// <summary>
        /// Feature of the route, or null when the route belongs to no feature (media, reactions set, account routes).
        /// </summary>
        public FeatureKeyEnum? Feature { get; set; } = null;

        /// <summary>
        /// Action. Default Read.
        /// </summary>
        public AccessActionEnum Action { get; set; } = AccessActionEnum.Read;

        /// <summary>
        /// Whether the route is an account route (notifications, reports, blocks, link previews, labels and tags):
        /// Member in FullyGated, SignedIn otherwise.
        /// </summary>
        public bool Account { get; set; } = false;

        /// <summary>
        /// Whether signed-out callers may reach the route when the property allows anonymous browsing (Read and List
        /// routes of content; each object's audience is checked again).
        /// </summary>
        public bool AnonymousEligible { get; set; } = false;

        /// <summary>
        /// The declaration's path pattern (for documentation and test failures), for example GET /api/v1/feed/{id}.
        /// </summary>
        public string Pattern { get; set; } = String.Empty;

        /// <summary>
        /// Whether the route's objects decide the caller tier (docs/PHASE4_CONTRACT.md section 0.4): reads show locked
        /// stubs instead of a route gate when the feature is gated in PartiallyGated, and participation (progress,
        /// comments, RSVPs) is checked per object by the service (sample lessons need only an account). The route then
        /// requires a signed-in caller (or none, for anonymous-eligible reads under anonymous browsing). Default false.
        /// </summary>
        public bool ObjectGated { get; set; } = false;

        #endregion
    }
}
