namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings of the venue and instructor directory (venues, instructors, programs, submissions, geocoding) (docs/PHASE4_CONTRACT.md section 12).
    /// The per-property geocoder, map, and public listing settings live in the per-property settings store instead.
    /// </summary>
    public class DirectorySettings
    {
        #region Public-Members

        /// <summary>
        /// Program options a property may define (directory.maxPrograms). Default 50. Minimum 1, maximum 200.
        /// </summary>
        public int MaxPrograms
        {
            get { return _MaxPrograms; }
            set { _MaxPrograms = Math.Clamp(value, 1, 200); }
        }

        /// <summary>
        /// Longest venue description and instructor bio, in characters (directory.maxDescriptionLength). Default 5000. Minimum 100, maximum 50000.
        /// </summary>
        public int MaxDescriptionLength
        {
            get { return _MaxDescriptionLength; }
            set { _MaxDescriptionLength = Math.Clamp(value, 100, 50000); }
        }

        /// <summary>
        /// Photos a venue may carry (directory.maxPhotos). Default 10. Minimum 0, maximum 30.
        /// </summary>
        public int MaxPhotos
        {
            get { return _MaxPhotos; }
            set { _MaxPhotos = Math.Clamp(value, 0, 30); }
        }

        /// <summary>
        /// Social links a venue or instructor may carry (directory.maxSocialLinks). Default 8. Minimum 0, maximum 20.
        /// </summary>
        public int MaxSocialLinks
        {
            get { return _MaxSocialLinks; }
            set { _MaxSocialLinks = Math.Clamp(value, 0, 20); }
        }

        /// <summary>
        /// Radius of a near query without radiusKm, in kilometers (directory.defaultRadiusKm). Default 50.0. Minimum 0.1, maximum 500.0.
        /// </summary>
        public double DefaultRadiusKm
        {
            get { return _DefaultRadiusKm; }
            set { _DefaultRadiusKm = Double.IsNaN(value) ? _DefaultRadiusKm : Math.Clamp(value, 0.1, 500.0); }
        }

        /// <summary>
        /// Largest near radius, in kilometers (directory.maxRadiusKm). Default 500.0. Minimum 1.0, maximum 2000.0.
        /// </summary>
        public double MaxRadiusKm
        {
            get { return _MaxRadiusKm; }
            set { _MaxRadiusKm = Double.IsNaN(value) ? _MaxRadiusKm : Math.Clamp(value, 1.0, 2000.0); }
        }

        /// <summary>
        /// Pending submissions a member may have at a time (directory.maxPendingSubmissionsPerMember). Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int MaxPendingSubmissionsPerMember
        {
            get { return _MaxPendingSubmissionsPerMember; }
            set { _MaxPendingSubmissionsPerMember = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Review target of a pending submission, in hours (directory.reviewTargetHours). Default 72. Minimum 1, maximum 720.
        /// </summary>
        public int ReviewTargetHours
        {
            get { return _ReviewTargetHours; }
            set { _ReviewTargetHours = Math.Clamp(value, 1, 720); }
        }

        /// <summary>
        /// Name similarity at or above which an entry is a possible duplicate (directory.duplicateNameSimilarity). Default 0.8. Minimum 0.5, maximum 1.0.
        /// </summary>
        public double DuplicateNameSimilarity
        {
            get { return _DuplicateNameSimilarity; }
            set { _DuplicateNameSimilarity = Double.IsNaN(value) ? _DuplicateNameSimilarity : Math.Clamp(value, 0.5, 1.0); }
        }

        /// <summary>
        /// Distance within which a similarly named venue is a possible duplicate, in kilometers (directory.duplicateDistanceKm). Default 0.5. Minimum 0.0, maximum 10.0.
        /// </summary>
        public double DuplicateDistanceKm
        {
            get { return _DuplicateDistanceKm; }
            set { _DuplicateDistanceKm = Double.IsNaN(value) ? _DuplicateDistanceKm : Math.Clamp(value, 0.0, 10.0); }
        }

        /// <summary>
        /// Address visibility of member-submitted venues when the submission does not set one (directory.submissionDefaultAddressVisibility; resolved decision 7: Exact). Default Exact.
        /// </summary>
        public AddressVisibilityEnum SubmissionDefaultAddressVisibility { get; set; } = AddressVisibilityEnum.Exact;

        /// <summary>
        /// Submissions a member may make per day (directory.submissionsPerDay). Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int SubmissionsPerDay
        {
            get { return _SubmissionsPerDay; }
            set { _SubmissionsPerDay = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Submissions a new account (comments.newAccountHours) may make per day (directory.newAccountSubmissionsPerDay). Default 2. Minimum 1, maximum 1000.
        /// </summary>
        public int NewAccountSubmissionsPerDay
        {
            get { return _NewAccountSubmissionsPerDay; }
            set { _NewAccountSubmissionsPerDay = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Place searches per member per minute (directory.geocodePerMinute). Default 30. Minimum 1, maximum 10000.
        /// </summary>
        public int GeocodePerMinute
        {
            get { return _GeocodePerMinute; }
            set { _GeocodePerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Place searches per member per day (directory.geocodePerDay). Default 300. Minimum 1, maximum 100000.
        /// </summary>
        public int GeocodePerDay
        {
            get { return _GeocodePerDay; }
            set { _GeocodePerDay = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Map enumerations per member per minute (directory.mapPerMinute). Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int MapPerMinute
        {
            get { return _MapPerMinute; }
            set { _MapPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Tile proxy requests per account per minute; signed-out callers get half per IP (directory.tilesPerMinute). Default 1200. Minimum 1, maximum 100000.
        /// </summary>
        public int TilesPerMinute
        {
            get { return _TilesPerMinute; }
            set { _TilesPerMinute = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// How often GeocodeWorker geocodes queued venues and checks overdue submissions (directory.geocodeIntervalSeconds). Default 30. Minimum 5, maximum 3600.
        /// </summary>
        public int GeocodeIntervalSeconds
        {
            get { return _GeocodeIntervalSeconds; }
            set { _GeocodeIntervalSeconds = Math.Clamp(value, 5, 3600); }
        }

        /// <summary>
        /// Venues GeocodeWorker geocodes per pass (directory.geocodeBatchSize). Default 25. Minimum 1, maximum 500.
        /// </summary>
        public int GeocodeBatchSize
        {
            get { return _GeocodeBatchSize; }
            set { _GeocodeBatchSize = Math.Clamp(value, 1, 500); }
        }

        /// <summary>
        /// Tiles the tile proxy keeps in memory (directory.tileCacheEntries; 0 turns the cache off). Default 2000. Minimum 0, maximum 100000.
        /// </summary>
        public int TileCacheEntries
        {
            get { return _TileCacheEntries; }
            set { _TileCacheEntries = Math.Clamp(value, 0, 100000); }
        }

        /// <summary>
        /// Timeout of one upstream tile request, in seconds (directory.tileTimeoutSeconds). Default 10. Minimum 1, maximum 120.
        /// </summary>
        public int TileTimeoutSeconds
        {
            get { return _TileTimeoutSeconds; }
            set { _TileTimeoutSeconds = Math.Clamp(value, 1, 120); }
        }

        /// <summary>
        /// Timeout of one geocoder request, in seconds (directory.geocodeTimeoutSeconds). Default 10. Minimum 1, maximum 120.
        /// </summary>
        public int GeocodeTimeoutSeconds
        {
            get { return _GeocodeTimeoutSeconds; }
            set { _GeocodeTimeoutSeconds = Math.Clamp(value, 1, 120); }
        }

        /// <summary>
        /// Shortest venue review text, in characters (directory.reviewMinLength). Default 10. Minimum 0, maximum 1000.
        /// </summary>
        public int ReviewMinLength
        {
            get { return _ReviewMinLength; }
            set { _ReviewMinLength = Math.Clamp(value, 0, 1000); }
        }

        /// <summary>
        /// Longest venue review text, in characters (directory.reviewMaxLength). Default 2000. Minimum 100, maximum 10000.
        /// </summary>
        public int ReviewMaxLength
        {
            get { return _ReviewMaxLength; }
            set { _ReviewMaxLength = Math.Clamp(value, 100, 10000); }
        }

        /// <summary>
        /// Longest instructor reply to a review, in characters (directory.reviewReplyMaxLength). Default 2000. Minimum 100, maximum 10000.
        /// </summary>
        public int ReviewReplyMaxLength
        {
            get { return _ReviewReplyMaxLength; }
            set { _ReviewReplyMaxLength = Math.Clamp(value, 100, 10000); }
        }

        /// <summary>
        /// Venue reviews a member may write per day (directory.reviewsPerDay). Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int ReviewsPerDay
        {
            get { return _ReviewsPerDay; }
            set { _ReviewsPerDay = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Venue reviews a new account (comments.newAccountHours) may write per day (directory.newAccountReviewsPerDay). Default 2. Minimum 1, maximum 1000.
        /// </summary>
        public int NewAccountReviewsPerDay
        {
            get { return _NewAccountReviewsPerDay; }
            set { _NewAccountReviewsPerDay = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private int _MaxPrograms = 50;
        private int _MaxDescriptionLength = 5000;
        private int _MaxPhotos = 10;
        private int _MaxSocialLinks = 8;
        private double _DefaultRadiusKm = 50.0;
        private double _MaxRadiusKm = 500.0;
        private int _MaxPendingSubmissionsPerMember = 5;
        private int _ReviewTargetHours = 72;
        private double _DuplicateNameSimilarity = 0.8;
        private double _DuplicateDistanceKm = 0.5;
        private int _SubmissionsPerDay = 10;
        private int _NewAccountSubmissionsPerDay = 2;
        private int _GeocodePerMinute = 30;
        private int _GeocodePerDay = 300;
        private int _MapPerMinute = 120;
        private int _TilesPerMinute = 1200;
        private int _GeocodeIntervalSeconds = 30;
        private int _GeocodeBatchSize = 25;
        private int _TileCacheEntries = 2000;
        private int _TileTimeoutSeconds = 10;
        private int _GeocodeTimeoutSeconds = 10;
        private int _ReviewMinLength = 10;
        private int _ReviewMaxLength = 2000;
        private int _ReviewReplyMaxLength = 2000;
        private int _ReviewsPerDay = 10;
        private int _NewAccountReviewsPerDay = 2;

        #endregion
    }
}
