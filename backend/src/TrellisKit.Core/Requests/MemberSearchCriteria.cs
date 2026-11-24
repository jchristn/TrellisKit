namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Member search (mention autocomplete) criteria passed to the data layer (contract section 3.4).
    /// </summary>
    public class MemberSearchCriteria
    {
        #region Public-Members

        /// <summary>
        /// Caller, excluded from results together with both directions of their blocks, or null (admin token).
        /// </summary>
        public string? CallerUserId { get; set; } = null;

        /// <summary>
        /// Lowercase prefix matched against lower(handle) and display name words. Never null.
        /// </summary>
        public string Prefix
        {
            get { return _Prefix; }
            set { _Prefix = value ?? String.Empty; }
        }

        /// <summary>
        /// Feed post whose author and commenters rank first, or null.
        /// </summary>
        public string? ContextFeedPostId { get; set; } = null;

        /// <summary>
        /// Thread whose author and commenters rank first, or null.
        /// </summary>
        public string? ContextThreadId { get; set; } = null;

        /// <summary>
        /// Evaluation time for active memberships (UTC).
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Prefix = String.Empty;

        #endregion
    }
}
