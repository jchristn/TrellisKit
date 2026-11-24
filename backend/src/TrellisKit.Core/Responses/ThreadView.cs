namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A thread (docs/PHASE3_CONTRACT.md section 2.2).
    /// </summary>
    public class ThreadView : ThreadCoreView
    {
        #region Public-Members

        /// <summary>
        /// Body (for LinkedFeedPost threads, the feed post's current body).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Resolved mentions.
        /// </summary>
        public List<MentionView> Mentions
        {
            get { return _Mentions; }
            set { _Mentions = value ?? new List<MentionView>(); }
        }

        /// <summary>
        /// Attachments in order.
        /// </summary>
        public List<MediaView> Attachments
        {
            get { return _Attachments; }
            set { _Attachments = value ?? new List<MediaView>(); }
        }

        /// <summary>
        /// Link preview, or null.
        /// </summary>
        public LinkPreviewView? LinkPreview { get; set; } = null;

        /// <summary>
        /// The accepted comment (Question threads), or null.
        /// </summary>
        public CommentView? AcceptedAnswer { get; set; } = null;

        /// <summary>
        /// The poll (Poll threads), or null.
        /// </summary>
        public PollView? Poll { get; set; } = null;

        /// <summary>
        /// Feedback state (Feedback threads), or null.
        /// </summary>
        public FeedbackView? Feedback { get; set; } = null;

        #endregion

        #region Private-Members

        private List<MentionView> _Mentions = new List<MentionView>();
        private List<MediaView> _Attachments = new List<MediaView>();

        #endregion
    }
}
