namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A media item the storage accounting measures or reconciles.
    /// </summary>
    public class UsageMediaItem
    {
        #region Public-Members

        /// <summary>
        /// Media id.
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Recorded original size.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Object store location of the original.
        /// </summary>
        public MediaLocationEnum SourceLocation { get; set; } = MediaLocationEnum.Source;

        /// <summary>
        /// Object key of the original.
        /// </summary>
        public string SourceKey { get; set; } = String.Empty;

        #endregion
    }
}
