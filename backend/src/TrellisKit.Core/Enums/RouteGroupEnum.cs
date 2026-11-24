namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Authorization group of a route (contract section 4).
    /// </summary>
    public enum RouteGroupEnum
    {
        /// <summary>
        /// No authentication.
        /// </summary>
        Anonymous,

        /// <summary>
        /// Any signed-in member.
        /// </summary>
        SignedIn,

        /// <summary>
        /// Age attested and an active membership (or SystemAdmin).
        /// </summary>
        Member,

        /// <summary>
        /// SystemAdmin, or any Moderator or FeedModerator role assignment (per-item scope is checked in the service).
        /// </summary>
        Moderator,

        /// <summary>
        /// SystemAdmin or the break-glass admin token.
        /// </summary>
        Admin
    }
}
