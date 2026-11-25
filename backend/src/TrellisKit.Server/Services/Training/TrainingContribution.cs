namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// What one section provider adds to the Training overview. Null cards keep the hub's empty defaults. The activity
    /// flags feed firstRun (true when no provider reports lesson progress, saves, or RSVPs).
    /// </summary>
    public sealed class TrainingContribution
    {
        #region Public-Members

        /// <summary>
        /// Sections the provider decided (only sections it owns are used).
        /// </summary>
        public List<TrainingSectionView> Sections { get; set; } = new List<TrainingSectionView>();

        /// <summary>
        /// Up next card, or null.
        /// </summary>
        public EnumerationResult<TrainingUpNextItem>? UpNext { get; set; } = null;

        /// <summary>
        /// Continue training card, or null.
        /// </summary>
        public EnumerationResult<ContinueWatchingItem>? Continue { get; set; } = null;

        /// <summary>
        /// Recently saved card, or null.
        /// </summary>
        public EnumerationResult<FavoriteView>? RecentSaves { get; set; } = null;

        /// <summary>
        /// New for you card, or null.
        /// </summary>
        public EnumerationResult<LessonSummaryView>? NewForYou { get; set; } = null;

        /// <summary>
        /// Your rank card, or null.
        /// </summary>
        public TrainingRankCard? Rank { get; set; } = null;

        /// <summary>
        /// Ask your coach card, or null.
        /// </summary>
        public TrainingCoachCard? Coach { get; set; } = null;

        /// <summary>
        /// Whether the member has any lesson progress, saves, or RSVPs that this provider knows of (clears firstRun).
        /// </summary>
        public bool HasActivity { get; set; } = false;

        #endregion
    }
}
