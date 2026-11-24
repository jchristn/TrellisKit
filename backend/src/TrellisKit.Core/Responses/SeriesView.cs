namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A series detail (Series).
    /// </summary>
    public class SeriesView : SeriesSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Description (empty on locked stubs).
        /// </summary>
        public string? Description { get; set; } = null;

        #endregion
    }
}
