namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member following a series (seriesfollows).
    /// </summary>
    public class SeriesFollow
    {
        #region Public-Members

        /// <summary>
        /// Identifier (sfw_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Series.
        /// </summary>
        public string SeriesId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// How the follow started.
        /// </summary>
        public SeriesFollowSourceEnum Source { get; set; } = SeriesFollowSourceEnum.Explicit;

        /// <summary>
        /// Whether the member unfollowed (kept so automatic follows do not return).
        /// </summary>
        public bool IsOptedOut { get; set; } = false;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
