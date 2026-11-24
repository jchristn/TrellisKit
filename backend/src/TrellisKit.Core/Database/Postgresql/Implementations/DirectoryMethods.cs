namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using System;
    using TrellisKit.Core.Database.Interfaces;

    /// <summary>
    /// PostgreSQL data access of the venue and instructor directory (venues, instructors, programs, submissions, geocoding).
    /// </summary>
    internal sealed class DirectoryMethods : IDirectoryMethods
    {
        public IDirectoryConfigMethods Config { get; }

        public IVenueMethods Venues { get; }

        public IInstructorMethods Instructors { get; }

        public IDirectorySubmissionMethods Submissions { get; }

        public IGeocodeCacheMethods GeocodeCache { get; }

        public IVenueReviewMethods Reviews { get; }

        public IVenueMediaMethods Media { get; }

        internal DirectoryMethods(DatabaseDriverBase driver, string? propertyId)
        {
            ArgumentNullException.ThrowIfNull(driver);
            Config = new DirectoryConfigMethods(driver, propertyId);
            Venues = new VenueMethods(driver, propertyId);
            Instructors = new InstructorMethods(driver, propertyId);
            Submissions = new DirectorySubmissionMethods(driver, propertyId);
            GeocodeCache = new GeocodeCacheMethods(driver, propertyId);
            Reviews = new VenueReviewMethods(driver, propertyId);
            Media = new VenueMediaMethods(driver, propertyId);
        }
    }
}
