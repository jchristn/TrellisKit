namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings registry entries of the venue and instructor directory (venues, instructors, programs, submissions, geocoding) (docs/PHASE4_CONTRACT.md section 12).
    /// The per-property geocoder, map, and public listing keys are in <see cref="PropertySettingsRegistry"/>.
    /// </summary>
    public static class DirectorySettingFields
    {
        #region Public-Methods

        /// <summary>
        /// The definitions.
        /// </summary>
        /// <returns>Definitions.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const string section = "Directory";
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Integer("directory.maxPrograms", section, "Max programs",
                    "Program options a property may define.", live, null, 1, 200,
                    s => s.Directory.MaxPrograms, (s, v) => s.Directory.MaxPrograms = v),
                SettingFieldDefinition.Integer("directory.maxDescriptionLength", section, "Max description length",
                    "Longest venue description and instructor bio, in characters.", live, null, 100, 50000,
                    s => s.Directory.MaxDescriptionLength, (s, v) => s.Directory.MaxDescriptionLength = v),
                SettingFieldDefinition.Integer("directory.maxPhotos", section, "Max photos per venue",
                    "Photos a venue may carry.", live, null, 0, 30,
                    s => s.Directory.MaxPhotos, (s, v) => s.Directory.MaxPhotos = v),
                SettingFieldDefinition.Integer("directory.maxSocialLinks", section, "Max social links",
                    "Social links a venue or instructor may carry.", live, null, 0, 20,
                    s => s.Directory.MaxSocialLinks, (s, v) => s.Directory.MaxSocialLinks = v),
                SettingFieldDefinition.Decimal("directory.defaultRadiusKm", section, "Default near radius (km)",
                    "Radius of a near query without radiusKm, in kilometers.", live, null, 0.1, 500.0,
                    s => s.Directory.DefaultRadiusKm, (s, v) => s.Directory.DefaultRadiusKm = v),
                SettingFieldDefinition.Decimal("directory.maxRadiusKm", section, "Max near radius (km)",
                    "Largest near radius, in kilometers.", live, null, 1.0, 2000.0,
                    s => s.Directory.MaxRadiusKm, (s, v) => s.Directory.MaxRadiusKm = v),
                SettingFieldDefinition.Integer("directory.maxPendingSubmissionsPerMember", section, "Pending submissions per member",
                    "Pending submissions a member may have at a time.", live, null, 1, 100,
                    s => s.Directory.MaxPendingSubmissionsPerMember, (s, v) => s.Directory.MaxPendingSubmissionsPerMember = v),
                SettingFieldDefinition.Integer("directory.reviewTargetHours", section, "Submission review target (hours)",
                    "Review target of a pending submission, in hours.", live, null, 1, 720,
                    s => s.Directory.ReviewTargetHours, (s, v) => s.Directory.ReviewTargetHours = v),
                SettingFieldDefinition.Decimal("directory.duplicateNameSimilarity", section, "Duplicate name similarity",
                    "Name similarity at or above which an entry is a possible duplicate.", live, null, 0.5, 1.0,
                    s => s.Directory.DuplicateNameSimilarity, (s, v) => s.Directory.DuplicateNameSimilarity = v),
                SettingFieldDefinition.Decimal("directory.duplicateDistanceKm", section, "Duplicate distance (km)",
                    "Distance within which a similarly named venue is a possible duplicate, in kilometers.", live, null, 0.0, 10.0,
                    s => s.Directory.DuplicateDistanceKm, (s, v) => s.Directory.DuplicateDistanceKm = v),
                SettingFieldDefinition.Choice<AddressVisibilityEnum>("directory.submissionDefaultAddressVisibility", section, "Submitted address visibility",
                    "Address visibility of member-submitted venues when the submission does not set one.", live, null,
                    s => s.Directory.SubmissionDefaultAddressVisibility, (s, v) => s.Directory.SubmissionDefaultAddressVisibility = v),
                SettingFieldDefinition.Integer("directory.submissionsPerDay", section, "Submissions per day",
                    "Submissions a member may make per day.", live, null, 1, 1000,
                    s => s.Directory.SubmissionsPerDay, (s, v) => s.Directory.SubmissionsPerDay = v),
                SettingFieldDefinition.Integer("directory.newAccountSubmissionsPerDay", section, "New account submissions per day",
                    "Submissions a new account (comments.newAccountHours) may make per day.", live, null, 1, 1000,
                    s => s.Directory.NewAccountSubmissionsPerDay, (s, v) => s.Directory.NewAccountSubmissionsPerDay = v),
                SettingFieldDefinition.Integer("directory.geocodePerMinute", section, "Place searches per minute",
                    "Place searches per member per minute.", live, null, 1, 10000,
                    s => s.Directory.GeocodePerMinute, (s, v) => s.Directory.GeocodePerMinute = v),
                SettingFieldDefinition.Integer("directory.geocodePerDay", section, "Place searches per day",
                    "Place searches per member per day.", live, null, 1, 100000,
                    s => s.Directory.GeocodePerDay, (s, v) => s.Directory.GeocodePerDay = v),
                SettingFieldDefinition.Integer("directory.mapPerMinute", section, "Map queries per minute",
                    "Map enumerations per member per minute.", live, null, 1, 10000,
                    s => s.Directory.MapPerMinute, (s, v) => s.Directory.MapPerMinute = v),
                SettingFieldDefinition.Integer("directory.tilesPerMinute", section, "Map tiles per minute",
                    "Tile proxy requests per account per minute; signed-out callers get half per IP.", live, null, 1, 100000,
                    s => s.Directory.TilesPerMinute, (s, v) => s.Directory.TilesPerMinute = v),
                SettingFieldDefinition.Integer("directory.geocodeIntervalSeconds", section, "Geocode worker interval (seconds)",
                    "How often GeocodeWorker geocodes queued venues and checks overdue submissions.", live, null, 5, 3600,
                    s => s.Directory.GeocodeIntervalSeconds, (s, v) => s.Directory.GeocodeIntervalSeconds = v),
                SettingFieldDefinition.Integer("directory.geocodeBatchSize", section, "Geocode worker batch",
                    "Venues GeocodeWorker geocodes per pass.", live, null, 1, 500,
                    s => s.Directory.GeocodeBatchSize, (s, v) => s.Directory.GeocodeBatchSize = v),
                SettingFieldDefinition.Integer("directory.tileCacheEntries", section, "Tile cache entries",
                    "Tiles the tile proxy keeps in memory.", live, null, 0, 100000,
                    s => s.Directory.TileCacheEntries, (s, v) => s.Directory.TileCacheEntries = v),
                SettingFieldDefinition.Integer("directory.tileTimeoutSeconds", section, "Tile request timeout (seconds)",
                    "Timeout of one upstream tile request, in seconds.", live, null, 1, 120,
                    s => s.Directory.TileTimeoutSeconds, (s, v) => s.Directory.TileTimeoutSeconds = v),
                SettingFieldDefinition.Integer("directory.geocodeTimeoutSeconds", section, "Geocoder request timeout (seconds)",
                    "Timeout of one geocoder request, in seconds.", live, null, 1, 120,
                    s => s.Directory.GeocodeTimeoutSeconds, (s, v) => s.Directory.GeocodeTimeoutSeconds = v),
                SettingFieldDefinition.Integer("directory.reviewMinLength", section, "Review minimum length",
                    "Shortest venue review text, in characters.", live, null, 0, 1000,
                    s => s.Directory.ReviewMinLength, (s, v) => s.Directory.ReviewMinLength = v),
                SettingFieldDefinition.Integer("directory.reviewMaxLength", section, "Review maximum length",
                    "Longest venue review text, in characters.", live, null, 100, 10000,
                    s => s.Directory.ReviewMaxLength, (s, v) => s.Directory.ReviewMaxLength = v),
                SettingFieldDefinition.Integer("directory.reviewReplyMaxLength", section, "Review reply maximum length",
                    "Longest instructor reply to a venue review, in characters.", live, null, 100, 10000,
                    s => s.Directory.ReviewReplyMaxLength, (s, v) => s.Directory.ReviewReplyMaxLength = v),
                SettingFieldDefinition.Integer("directory.reviewsPerDay", section, "Reviews per day",
                    "Venue reviews a member may write per day.", live, null, 1, 1000,
                    s => s.Directory.ReviewsPerDay, (s, v) => s.Directory.ReviewsPerDay = v),
                SettingFieldDefinition.Integer("directory.newAccountReviewsPerDay", section, "New account reviews per day",
                    "Venue reviews a new account (comments.newAccountHours) may write per day.", live, null, 1, 1000,
                    s => s.Directory.NewAccountReviewsPerDay, (s, v) => s.Directory.NewAccountReviewsPerDay = v)
            };
        }

        #endregion
    }
}
