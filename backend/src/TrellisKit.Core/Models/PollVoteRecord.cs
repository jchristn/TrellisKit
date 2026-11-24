namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One poll vote of a member, for the account export.
    /// </summary>
    public class PollVoteRecord
    {
        #region Public-Members

        /// <summary>
        /// Vote identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Poll identifier.
        /// </summary>
        public string PollId { get; set; } = String.Empty;

        /// <summary>
        /// Thread identifier.
        /// </summary>
        public string ThreadId { get; set; } = String.Empty;

        /// <summary>
        /// Option identifier.
        /// </summary>
        public string OptionId { get; set; } = String.Empty;

        /// <summary>
        /// Option label.
        /// </summary>
        public string OptionLabel { get; set; } = String.Empty;

        /// <summary>
        /// Vote time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
