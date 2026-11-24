namespace TrellisKit.Core.Database.Interfaces
{
    /// <summary>
    /// Data access of the venue and instructor directory (venues, instructors, programs, submissions, geocoding) (docs/PHASE4_CONTRACT.md section 14): a container of method sets, each bound to
    /// one property through the property-bound driver.
    /// </summary>
    public interface IDirectoryMethods
    {
        /// <summary>
        /// Directory configuration and program options.
        /// </summary>
        IDirectoryConfigMethods Config { get; }

        /// <summary>
        /// Venues, their child lists, links to instructors, and spatial queries.
        /// </summary>
        IVenueMethods Venues { get; }

        /// <summary>
        /// Instructors and their child lists.
        /// </summary>
        IInstructorMethods Instructors { get; }

        /// <summary>
        /// Member submissions and their field rows.
        /// </summary>
        IDirectorySubmissionMethods Submissions { get; }

        /// <summary>
        /// The geocoder cache.
        /// </summary>
        IGeocodeCacheMethods GeocodeCache { get; }

        /// <summary>
        /// Member reviews of venues.
        /// </summary>
        IVenueReviewMethods Reviews { get; }

        /// <summary>
        /// Member media items of venues.
        /// </summary>
        IVenueMediaMethods Media { get; }
    }
}
