namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Direct message settings (docs/PHASE5_CONTRACT.md section 14).
    /// </summary>
    public class MessagingSettings
    {
        #region Public-Members

        /// <summary>
        /// Most characters in a direct message after normalization. Default 4000. Minimum 100, maximum 20000.
        /// </summary>
        public int MaxBodyLength
        {
            get { return _MaxBodyLength; }
            set { _MaxBodyLength = Math.Clamp(value, 100, 20000); }
        }

        /// <summary>
        /// Whether direct messages may carry images and video. Default true.
        /// </summary>
        public bool AllowMedia { get; set; } = true;

        /// <summary>
        /// Most images on one message (a video is always alone). Default 4. Minimum 0, maximum 10.
        /// </summary>
        public int MaxAttachments
        {
            get { return _MaxAttachments; }
            set { _MaxAttachments = Math.Clamp(value, 0, 10); }
        }

        /// <summary>
        /// How long after sending the author may make the one allowed edit. 0 disables edits. Default 15. Minimum 0, maximum 1440.
        /// </summary>
        public int EditWindowMinutes
        {
            get { return _EditWindowMinutes; }
            set { _EditWindowMinutes = Math.Clamp(value, 0, 1440); }
        }

        /// <summary>
        /// Who may message a member who has not chosen a policy: Everyone (strangers land in Requests), Known, or Nobody (staff only). Default Everyone.
        /// </summary>
        public DmPolicyEnum DefaultDmPolicy { get; set; } = DmPolicyEnum.Everyone;

        /// <summary>
        /// What a message push shows for members who have not chosen: SenderOnly, Full (sender and excerpt), or Hidden. Default SenderOnly.
        /// </summary>
        public PushPreviewEnum DefaultPushPreview { get; set; } = PushPreviewEnum.SenderOnly;

        /// <summary>
        /// Messages a sender may send before the recipient accepts or replies. Default 3. Minimum 1, maximum 20.
        /// </summary>
        public int RequestMessageLimit
        {
            get { return _RequestMessageLimit; }
            set { _RequestMessageLimit = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Earlier messages copied into a report snapshot. Default 10. Minimum 0, maximum 50.
        /// </summary>
        public int ReportContextMessages
        {
            get { return _ReportContextMessages; }
            set { _ReportContextMessages = Math.Clamp(value, 0, 50); }
        }

        /// <summary>
        /// Messages older than this are removed. 0 keeps messages until they are deleted. Default 0. Minimum 0, maximum 3650.
        /// </summary>
        public int RetentionDays
        {
            get { return _RetentionDays; }
            set { _RetentionDays = Math.Clamp(value, 0, 3650); }
        }

        /// <summary>
        /// In gated access models, a member who lost access keeps their conversations this long; after that they are removed from their account unless they rejoin. 0 removes them on the next pass. Default 90. Minimum 0, maximum 3650.
        /// </summary>
        public int LapsedRetentionDays
        {
            get { return _LapsedRetentionDays; }
            set { _LapsedRetentionDays = Math.Clamp(value, 0, 3650); }
        }

        /// <summary>
        /// How long a signed direct message or ticket attachment URL stays valid. Default 60. Minimum 5, maximum 1440.
        /// </summary>
        public int MediaUrlMinutes
        {
            get { return _MediaUrlMinutes; }
            set { _MediaUrlMinutes = Math.Clamp(value, 5, 1440); }
        }

        /// <summary>
        /// At most one push per conversation per this many seconds while unread. Default 60. Minimum 0, maximum 3600.
        /// </summary>
        public int PushCoalesceSeconds
        {
            get { return _PushCoalesceSeconds; }
            set { _PushCoalesceSeconds = Math.Clamp(value, 0, 3600); }
        }

        /// <summary>
        /// A message must stay unread this long before the unread messages email (members who opted in). Default 60. Minimum 5, maximum 10080.
        /// </summary>
        public int EmailAfterMinutes
        {
            get { return _EmailAfterMinutes; }
            set { _EmailAfterMinutes = Math.Clamp(value, 5, 10080); }
        }

        /// <summary>
        /// At most one unread messages email per member per this many hours. Default 12. Minimum 1, maximum 168.
        /// </summary>
        public int EmailMinIntervalHours
        {
            get { return _EmailMinIntervalHours; }
            set { _EmailMinIntervalHours = Math.Clamp(value, 1, 168); }
        }

        /// <summary>
        /// Open conversation polling cadence when the socket is unavailable. Default 5. Minimum 2, maximum 60.
        /// </summary>
        public int PollSeconds
        {
            get { return _PollSeconds; }
            set { _PollSeconds = Math.Clamp(value, 2, 60); }
        }

        /// <summary>
        /// Older sync tokens are refused and the client starts fresh. Default 30. Minimum 1, maximum 365.
        /// </summary>
        public int SyncTokenDays
        {
            get { return _SyncTokenDays; }
            set { _SyncTokenDays = Math.Clamp(value, 1, 365); }
        }

        /// <summary>
        /// Reserved for group conversations, which are not enabled. Default 20. Minimum 3, maximum 100.
        /// </summary>
        public int MaxGroupMembers
        {
            get { return _MaxGroupMembers; }
            set { _MaxGroupMembers = Math.Clamp(value, 3, 100); }
        }

        /// <summary>
        /// Accounts younger than this have the lower first-contact limit. Default 72. Minimum 0, maximum 720.
        /// </summary>
        public int NewAccountHours
        {
            get { return _NewAccountHours; }
            set { _NewAccountHours = Math.Clamp(value, 0, 720); }
        }

        /// <summary>
        /// Messages a member may send each minute. Default 30. Minimum 1, maximum 10000.
        /// </summary>
        public int MessagesPerMinute
        {
            get { return _MessagesPerMinute; }
            set { _MessagesPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Messages a member may send each day. Default 1000. Minimum 1, maximum 100000.
        /// </summary>
        public int MessagesPerDay
        {
            get { return _MessagesPerDay; }
            set { _MessagesPerDay = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// First contacts a member may start each day. Default 20. Minimum 1, maximum 1000.
        /// </summary>
        public int NewConversationsPerDay
        {
            get { return _NewConversationsPerDay; }
            set { _NewConversationsPerDay = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// First contacts a new account may start each day. Default 3. Minimum 1, maximum 1000.
        /// </summary>
        public int NewAccountConversationsPerDay
        {
            get { return _NewAccountConversationsPerDay; }
            set { _NewAccountConversationsPerDay = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Pending requests a member may have waiting for acceptance. Default 20. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxPendingRequests
        {
            get { return _MaxPendingRequests; }
            set { _MaxPendingRequests = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Message edits and deletes a member may make each minute. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int EditsPerMinute
        {
            get { return _EditsPerMinute; }
            set { _EditsPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// History, search, and sync reads a member may make each minute. Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int ReadsPerMinute
        {
            get { return _ReadsPerMinute; }
            set { _ReadsPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Largest page of a direct message search. Default 50. Minimum 10, maximum 100.
        /// </summary>
        public int SearchMaxResults
        {
            get { return _SearchMaxResults; }
            set { _SearchMaxResults = Math.Clamp(value, 10, 100); }
        }

        #endregion

        #region Private-Members

        private int _MaxBodyLength = 4000;
        private int _MaxAttachments = 4;
        private int _EditWindowMinutes = 15;
        private int _RequestMessageLimit = 3;
        private int _ReportContextMessages = 10;
        private int _RetentionDays = 0;
        private int _LapsedRetentionDays = 90;
        private int _MediaUrlMinutes = 60;
        private int _PushCoalesceSeconds = 60;
        private int _EmailAfterMinutes = 60;
        private int _EmailMinIntervalHours = 12;
        private int _PollSeconds = 5;
        private int _SyncTokenDays = 30;
        private int _MaxGroupMembers = 20;
        private int _NewAccountHours = 72;
        private int _MessagesPerMinute = 30;
        private int _MessagesPerDay = 1000;
        private int _NewConversationsPerDay = 20;
        private int _NewAccountConversationsPerDay = 3;
        private int _MaxPendingRequests = 20;
        private int _EditsPerMinute = 60;
        private int _ReadsPerMinute = 120;
        private int _SearchMaxResults = 50;

        #endregion
    }
}
