namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Thread enumeration filter (docs/PHASE3_CONTRACT.md sections 4.2, 4.3, 5.3, and the admin threads list).
    /// </summary>
    public class ThreadFilter
    {
        #region Public-Members

        /// <summary>
        /// Only threads in this forum.
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// Only threads in these forums (empty means every forum).
        /// </summary>
        public List<string> ForumIds
        {
            get { return _ForumIds; }
            set { _ForumIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// Forums the caller moderates (admin and trending lists), or null for every forum.
        /// </summary>
        public List<string>? ScopeForumIds { get; set; } = null;

        /// <summary>
        /// Only these types (empty means every type).
        /// </summary>
        public List<ThreadTypeEnum> Types
        {
            get { return _Types; }
            set { _Types = value ?? new List<ThreadTypeEnum>(); }
        }

        /// <summary>
        /// Questions with (true) or without (false) an accepted answer.
        /// </summary>
        public bool? Answered { get; set; } = null;

        /// <summary>
        /// Questions without an accepted answer and without visible comments.
        /// </summary>
        public bool Unanswered { get; set; } = false;

        /// <summary>
        /// Only Feedback threads with these statuses.
        /// </summary>
        public List<FeedbackStatusEnum> FeedbackStatuses
        {
            get { return _FeedbackStatuses; }
            set { _FeedbackStatuses = value ?? new List<FeedbackStatusEnum>(); }
        }

        /// <summary>
        /// Only polls that are open (true) or closed (false).
        /// </summary>
        public bool? PollOpen { get; set; } = null;

        /// <summary>
        /// Only threads by this member.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Only threads this member follows.
        /// </summary>
        public string? FollowedByUserId { get; set; } = null;

        /// <summary>
        /// Only threads unread by this member.
        /// </summary>
        public string? UnreadForUserId { get; set; } = null;

        /// <summary>
        /// Activity after this time counts as unread when the member has no read row.
        /// </summary>
        public DateTime? UnreadWindowStartUtc { get; set; } = null;

        /// <summary>
        /// Only threads with attachments.
        /// </summary>
        public bool HasMedia { get; set; } = false;

        /// <summary>
        /// Only threads with a video.
        /// </summary>
        public bool HasVideo { get; set; } = false;

        /// <summary>
        /// Audiences the caller may read, or null for every audience.
        /// </summary>
        public List<ContentAudienceEnum>? Audiences { get; set; } = null;

        /// <summary>
        /// Only these audiences (empty means every readable audience).
        /// </summary>
        public List<ContentAudienceEnum> AudienceFilter
        {
            get { return _AudienceFilter; }
            set { _AudienceFilter = value ?? new List<ContentAudienceEnum>(); }
        }

        /// <summary>
        /// Authors whose threads are omitted (blocks).
        /// </summary>
        public List<string> HiddenAuthorIds
        {
            get { return _HiddenAuthorIds; }
            set { _HiddenAuthorIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// States to include, or null for Published only.
        /// </summary>
        public List<ThreadStateEnum>? States { get; set; } = null;

        /// <summary>
        /// When set, non-Published states are limited to this author's threads.
        /// </summary>
        public string? StateOwnerUserId { get; set; } = null;

        /// <summary>
        /// Only pinned (true) or unpinned (false) threads.
        /// </summary>
        public bool? Pinned { get; set; } = null;

        /// <summary>Put pinned threads first within the same bounded SQL page and keyset.</summary>
        public bool PinnedFirst { get; set; } = false;

        /// <summary>
        /// Only locked (true) or unlocked (false) threads.
        /// </summary>
        public bool? Locked { get; set; } = null;

        /// <summary>
        /// Only promoted (true) or unpromoted (false) threads.
        /// </summary>
        public bool? Promoted { get; set; } = null;

        /// <summary>
        /// Only threads with at least this trending score.
        /// </summary>
        public double? MinScore { get; set; } = null;

        /// <summary>
        /// Omit threads in archived forums.
        /// </summary>
        public bool ExcludeArchivedForums { get; set; } = false;

        /// <summary>
        /// Only these threads (search candidates), or null.
        /// </summary>
        public List<string>? CandidateIds { get; set; } = null;

        /// <summary>
        /// Evaluation time.
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _ForumIds = new List<string>();
        private List<ThreadTypeEnum> _Types = new List<ThreadTypeEnum>();
        private List<FeedbackStatusEnum> _FeedbackStatuses = new List<FeedbackStatusEnum>();
        private List<ContentAudienceEnum> _AudienceFilter = new List<ContentAudienceEnum>();
        private List<string> _HiddenAuthorIds = new List<string>();

        #endregion
    }
}
