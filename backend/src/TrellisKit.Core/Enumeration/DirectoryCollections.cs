namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Phase 4 directory enumeration collections (docs/PHASE4_CONTRACT.md section 3). DistanceAscending and
    /// Relevance are computed per request (the venue data methods replace the placeholder expression with the
    /// Haversine distance or the match rank, both whole numbers, so keyset continuation works over (key, id)).
    /// </summary>
    public static class DirectoryCollections
    {
        #region Public-Members

        /// <summary>
        /// Venue list filters (members).
        /// </summary>
        public static readonly string[] VenueFilters = { "near", "bbox", "programs", "countryCode", "region", "locality", "visitorPolicy", "verifiedOnly", "includeClosed", "instructorId", "audience" };

        /// <summary>
        /// Member venue list.
        /// </summary>
        public static readonly EnumerationCollection Venues = VenueCollection("venues", false)
            .Filters(VenueFilters)
            .DefaultTo("NameAscending");

        /// <summary>
        /// Admin venue list (every status).
        /// </summary>
        public static readonly EnumerationCollection AdminVenues = VenueCollection("admin-venues", true)
            .Filters(VenueFilters)
            .Filters("status", "geoSource", "verified")
            .DefaultTo("NameAscending");

        /// <summary>
        /// Public venue list (anonymous).
        /// </summary>
        public static readonly EnumerationCollection PublicVenues = VenueCollection("public-venues", false)
            .Filters("near", "bbox", "programs", "countryCode", "region", "locality", "visitorPolicy", "verifiedOnly")
            .DefaultTo("NameAscending");

        /// <summary>
        /// The venues of an instructor.
        /// </summary>
        public static readonly EnumerationCollection InstructorVenues = VenueCollection("instructor-venues", false)
            .Filters("includeClosed")
            .DefaultTo("NameAscending");

        /// <summary>
        /// Map features (bounded generated source, standard keyset pages).
        /// </summary>
        public static readonly EnumerationCollection Map = new EnumerationCollection("directory-map", TargetTypeEnum.Venue, "v.id", "v.createdutc", false)
            .Order("NameAscending", "lower(v.name)", false, EnumerationKeyKindEnum.Text)
            .Search("v.name", "v.summary", "v.description")
            .Filters("programs", "countryCode", "region", "locality", "visitorPolicy", "verifiedOnly", "instructorId", "audience")
            .DefaultTo("NameAscending");

        /// <summary>
        /// Member instructor list.
        /// </summary>
        public static readonly EnumerationCollection Instructors = InstructorCollection("instructors", false)
            .Filters("venueId", "rank", "claimed", "linkedUserId", "audience")
            .DefaultTo("NameAscending");

        /// <summary>
        /// Admin instructor list.
        /// </summary>
        public static readonly EnumerationCollection AdminInstructors = InstructorCollection("admin-instructors", true)
            .Filters("venueId", "rank", "claimed", "linkedUserId", "audience", "status")
            .DefaultTo("NameAscending");

        /// <summary>
        /// The instructors of a venue.
        /// </summary>
        public static readonly EnumerationCollection VenueInstructors = InstructorCollection("venue-instructors", false)
            .DefaultTo("NameAscending");

        /// <summary>
        /// Program options.
        /// </summary>
        public static readonly EnumerationCollection Programs = new EnumerationCollection("directory-programs", TargetTypeEnum.DirectoryProgram, "p.id", "p.createdutc", false)
            .Order("SortOrderAscending", "p.sortorder", false, EnumerationKeyKindEnum.Number)
            .Order("LabelAscending", "lower(p.label)", false, EnumerationKeyKindEnum.Text)
            .Search("p.label", "p.key")
            .Filters("includeRetired")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// Program options in the admin console (retired ones included by default).
        /// </summary>
        public static readonly EnumerationCollection AdminPrograms = new EnumerationCollection("admin-directory-programs", TargetTypeEnum.DirectoryProgram, "p.id", "p.createdutc", true)
            .Order("SortOrderAscending", "p.sortorder", false, EnumerationKeyKindEnum.Number)
            .Order("LabelAscending", "lower(p.label)", false, EnumerationKeyKindEnum.Text)
            .Search("p.label", "p.key")
            .Filters("includeRetired")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// A member's own submissions.
        /// </summary>
        public static readonly EnumerationCollection MySubmissions = new EnumerationCollection("me-directory-submissions", TargetTypeEnum.DirectorySubmission, "s.id", "s.createdutc", false)
            .Search("s.targetname", "s.note")
            .Filters("status", "type");

        /// <summary>
        /// The review queue (oldest first: overdue submissions lead).
        /// </summary>
        public static readonly EnumerationCollection AdminSubmissions = new EnumerationCollection("admin-directory-submissions", TargetTypeEnum.DirectorySubmission, "s.id", "s.createdutc", true)
            .Search("s.targetname", "s.note")
            .Filters("status", "type", "submitterUserId", "targetId")
            .DefaultTo("CreatedAscending");

        /// <summary>
        /// The reviews of a venue (section 3.12): newest first by default; RatingDescending is highest first and
        /// RatingAscending lowest first, both newest first within a rating (the key folds the rating and the creation
        /// second into one whole number so keyset continuation works over (key, id)).
        /// </summary>
        public static readonly EnumerationCollection VenueReviews = new EnumerationCollection("venue-reviews", TargetTypeEnum.VenueReview, "r.id", "r.createdutc", false)
            .Order("RatingDescending", "(r.rating::bigint * 10000000000 + floor(extract(epoch from r.createdutc))::bigint)", true, EnumerationKeyKindEnum.Number)
            .Order("RatingAscending", "(r.rating::bigint * 10000000000 + (9999999999 - floor(extract(epoch from r.createdutc))::bigint))", false, EnumerationKeyKindEnum.Number)
            .Search("r.body", "r.replybody")
            .Filters("rating", "authorUserId", "hasReply")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// The member media items of a venue (section 3.13), newest first by default.
        /// </summary>
        public static readonly EnumerationCollection VenueMedia = new EnumerationCollection("venue-media", TargetTypeEnum.VenueMedia, "m.id", "m.createdutc", false)
            .Filters("kind", "uploaderUserId")
            .DefaultTo("CreatedDescending");

        #endregion

        #region Private-Methods

        private static EnumerationCollection VenueCollection(string name, bool isAdmin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.Venue, "v.id", "v.createdutc", isAdmin)
                .Order("NameAscending", "lower(v.name)", false, EnumerationKeyKindEnum.Text)
                .Order("DistanceAscending", "0", false, EnumerationKeyKindEnum.Number)
                .Order("UpdatedDescending", "v.updatedutc", true, EnumerationKeyKindEnum.Time)
                .Order("Relevance", "0", false, EnumerationKeyKindEnum.Number)
                .Search("v.name", "v.affiliation", "v.summary", "v.locality", "v.region",
                    "(SELECT string_agg(si.displayname, ' ') FROM venueinstructors svi JOIN instructors si ON si.propertyid = svi.propertyid AND si.id = svi.instructorid AND si.deletedutc IS NULL WHERE svi.propertyid = v.propertyid AND svi.venueid = v.id)");
        }

        private static EnumerationCollection InstructorCollection(string name, bool isAdmin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.Instructor, "i.id", "i.createdutc", isAdmin)
                .Order("NameAscending", "lower(i.displayname)", false, EnumerationKeyKindEnum.Text)
                .Order("UpdatedDescending", "i.updatedutc", true, EnumerationKeyKindEnum.Time)
                .Search("i.displayname", "i.affiliation");
        }

        #endregion
    }
}
