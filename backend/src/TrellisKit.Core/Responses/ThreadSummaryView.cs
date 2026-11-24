namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A thread list item (docs/PHASE3_CONTRACT.md section 2.3).
    /// </summary>
    public class ThreadSummaryView : ThreadCoreView
    {
        #region Public-Members

        /// <summary>
        /// Whether an accepted answer is visible.
        /// </summary>
        public bool HasAcceptedAnswer { get; set; } = false;

        /// <summary>
        /// At most the first 4 attachments.
        /// </summary>
        public List<MediaView> AttachmentPreview
        {
            get { return _AttachmentPreview; }
            set { _AttachmentPreview = value ?? new List<MediaView>(); }
        }

        /// <summary>
        /// Attachments.
        /// </summary>
        public int AttachmentCount { get; set; } = 0;

        /// <summary>
        /// Poll state, or null.
        /// </summary>
        public PollSummaryView? Poll { get; set; } = null;

        /// <summary>
        /// Feedback status, or null.
        /// </summary>
        public FeedbackSummaryView? Feedback { get; set; } = null;

        /// <summary>
        /// Search snippet (search results only), or null.
        /// </summary>
        public SearchSnippetView? SearchSnippet { get; set; } = null;

        #endregion

        #region Private-Members

        private List<MediaView> _AttachmentPreview = new List<MediaView>();

        #endregion
    }
}
