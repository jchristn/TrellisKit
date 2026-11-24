namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// What a content target provider tells the promotion service about an object that can be promoted to the feed
    /// (a lesson or an event; docs/PHASE4_CONTRACT.md section 5.8): the card's defaults, the conversation it points to,
    /// and whether the object is live.
    /// </summary>
    public class PromotionSource
    {
        #region Public-Members

        /// <summary>
        /// Title of the object (the card title unless the promoter overrides it).
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Plain excerpt of the object (the card body unless the promoter overrides it).
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// The object's audience. The card may only narrow it.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// App path of the object, for example /lessons/lsn_x.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// The member the card is attributed to, or null for content authored by the property (the promoter is used).
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Media of the object shown on the card (cover first). At most four are shown.
        /// </summary>
        public List<string> MediaIds
        {
            get { return _MediaIds; }
            set { _MediaIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// Whether the object is live (published and not hidden, removed, archived, or canceled). A card whose source is
        /// not live is hidden from the feed until the source is live again.
        /// </summary>
        public bool IsLive { get; set; } = true;

        /// <summary>
        /// The object's current feed card as the provider stored it through
        /// <see cref="ContentTargetProvider.PromotionChangedAsync"/>, or null.
        /// </summary>
        public string? PromotedFeedPostId { get; set; } = null;

        /// <summary>
        /// Default of notifyMembers when the request omits it (true for lessons, false for events).
        /// </summary>
        public bool NotifyMembersDefault { get; set; } = true;

        /// <summary>
        /// Whether the caller may comment on the object right now (drives the card's viewer.canComment).
        /// </summary>
        public bool CommentsOpen { get; set; } = true;

        /// <summary>
        /// Whether the caller may react to the object right now (drives the card's viewer.canReact).
        /// </summary>
        public bool ReactionsOpen { get; set; } = true;

        #endregion

        #region Private-Members

        private List<string> _MediaIds = new List<string>();

        #endregion
    }
}
