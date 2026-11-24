namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A link between a venue and an instructor (docs/PHASE4_CONTRACT.md section 14).
    /// </summary>
    public class VenueInstructorLink
    {
        #region Public-Members

        /// <summary>
        /// Identifier (vin_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Venue.
        /// </summary>
        public string VenueId { get; set; } = String.Empty;

        /// <summary>
        /// Instructor.
        /// </summary>
        public string InstructorId { get; set; } = String.Empty;

        /// <summary>
        /// Role at the venue.
        /// </summary>
        public VenueInstructorRoleEnum Role { get; set; } = VenueInstructorRoleEnum.Regular;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
