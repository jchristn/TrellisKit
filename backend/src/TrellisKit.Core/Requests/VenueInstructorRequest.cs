namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Link an instructor to a venue.
    /// </summary>
    public class VenueInstructorRequest
    {
        #region Public-Members

        /// <summary>
        /// Role (default Regular).
        /// </summary>
        public VenueInstructorRoleEnum? Role { get; set; } = null;

        #endregion
    }
}
