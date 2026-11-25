namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// One entry in PUT /api/v1/me/lineage.
    /// </summary>
    public class LineageEntryInput
    {
        #region Public-Members

        /// <summary>
        /// Option key.
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Promoter when they are a member (gets a verification request).
        /// </summary>
        public string? PromotedByUserId { get; set; } = null;

        /// <summary>
        /// Promoter name when they are not a member (at most 120 characters).
        /// </summary>
        public string? PromotedByName { get; set; } = null;

        /// <summary>
        /// When, or null.
        /// </summary>
        public DateTime? PromotedUtc { get; set; } = null;

        #endregion
    }
}
