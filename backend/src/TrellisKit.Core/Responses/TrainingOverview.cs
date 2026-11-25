namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/me/training: section availability plus bounded cards in one round trip (docs/TRELLISKIT.md section
    /// 15). Every list card is an EnumerationResult with a fixed maximum (training.* settings); a card whose provider
    /// failed or has no data is an empty, terminal page.
    /// </summary>
    public class TrainingOverview : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Every section in a fixed order (Overview, Library, Schedule, Programs, Progress).
        /// </summary>
        public List<TrainingSectionView> Sections { get; set; } = new List<TrainingSectionView>();

        /// <summary>
        /// Upcoming events the member RSVPed Going to, by start (at most training.upNextLimit).
        /// </summary>
        public EnumerationResult<TrainingUpNextItem> UpNext { get; set; } = LibrarySearchResponse.Empty<TrainingUpNextItem>();

        /// <summary>
        /// Lessons in progress, most recently watched first (at most training.continueLimit).
        /// </summary>
        public EnumerationResult<ContinueWatchingItem> Continue { get; set; } = LibrarySearchResponse.Empty<ContinueWatchingItem>();

        /// <summary>
        /// Most recent saves (at most training.recentSavesLimit).
        /// </summary>
        public EnumerationResult<FavoriteView> RecentSaves { get; set; } = LibrarySearchResponse.Empty<FavoriteView>();

        /// <summary>
        /// Lessons published within training.newForYouDays in series the member follows and has not started (at most
        /// training.newForYouLimit).
        /// </summary>
        public EnumerationResult<LessonSummaryView> NewForYou { get; set; } = LibrarySearchResponse.Empty<LessonSummaryView>();

        /// <summary>
        /// The Your rank card { rank, next, curriculum, showVerificationLabels }, or null (ranks.enabled off, no Rank profile field, or provider failure).
        /// </summary>
        public TrainingRankCard? Rank { get; set; } = null;

        /// <summary>
        /// The Ask your coach card, or null when its provider failed.
        /// </summary>
        public TrainingCoachCard? Coach { get; set; } = null;

        /// <summary>
        /// Whether the member has no lesson progress, saves, or RSVPs yet. Clients then show a first-run card built from
        /// real featured content (never sample content); the server adds none.
        /// </summary>
        public bool FirstRun { get; set; } = false;

        /// <summary>
        /// Labels (always empty; the overview is computed).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (always empty; the overview is computed).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
