namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Type of a member directory submission (docs/PHASE4_CONTRACT.md section 2.4).
    /// </summary>
    public enum DirectorySubmissionTypeEnum
    {
        /// <summary>
        /// Propose a new venue.
        /// </summary>
        NewVenue,

        /// <summary>
        /// Propose changes to a venue.
        /// </summary>
        EditVenue,

        /// <summary>
        /// Propose a new instructor.
        /// </summary>
        NewInstructor,

        /// <summary>
        /// Propose changes to an instructor.
        /// </summary>
        EditInstructor,

        /// <summary>
        /// Claim an instructor entry as yourself.
        /// </summary>
        ClaimInstructor,

        /// <summary>
        /// Report a venue as permanently closed.
        /// </summary>
        ReportClosed
    }
}
