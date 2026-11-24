namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One of the caller's follows (docs/PHASE3_CONTRACT.md section 9.2).
    /// </summary>
    public class FollowItemView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Follow identifier (ffw_ or tfw_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum or Thread.
        /// </summary>
        public string TargetType { get; set; } = "Thread";

        /// <summary>
        /// ForumView or ThreadSummaryView.
        /// </summary>
        public object? Target { get; set; } = null;

        /// <summary>
        /// When the follow started.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
