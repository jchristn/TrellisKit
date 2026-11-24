namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// How directory views are built for a request.
    /// </summary>
    public class DirectoryViewOptions
    {
        #region Public-Members

        /// <summary>
        /// Public anonymous route: no email, no phone unless <see cref="ShowContact"/>, no linked users, no viewer.
        /// </summary>
        public bool PublicRoute { get; set; } = false;

        /// <summary>
        /// Phone on public routes (directory.publicShowsContact).
        /// </summary>
        public bool ShowContact { get; set; } = false;

        /// <summary>
        /// Admin view (exact address and staff counters).
        /// </summary>
        public bool Admin { get; set; } = false;

        /// <summary>
        /// Include detail parts (venue instructors, instructor venues).
        /// </summary>
        public bool Detail { get; set; } = false;

        /// <summary>
        /// Venue whose instructor list is being built (adds each instructor's role), or null.
        /// </summary>
        public string? RoleVenueId { get; set; } = null;

        #endregion
    }
}
