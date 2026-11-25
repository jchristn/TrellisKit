namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of notification.
    /// </summary>
    public enum NotificationTypeEnum
    {
        /// <summary>
        /// A top-level comment on the recipient's post.
        /// </summary>
        PostComment,

        /// <summary>
        /// A reply to the recipient's comment.
        /// </summary>
        CommentReply,

        /// <summary>
        /// The recipient was mentioned.
        /// </summary>
        Mention,

        /// <summary>
        /// Reactions to the recipient's content (aggregated per target while unread).
        /// </summary>
        Reaction,

        /// <summary>
        /// A feed post was published.
        /// </summary>
        NewFeedPost,

        /// <summary>
        /// A moderator hid, removed, or restored the recipient's content.
        /// </summary>
        ModerationAction,

        /// <summary>
        /// A report in the recipient's moderation scope passed the review target.
        /// </summary>
        ModerationOverdue,

        /// <summary>
        /// The recipient's code membership ends soon.
        /// </summary>
        MembershipExpiring,

        /// <summary>
        /// A visible comment on a thread the recipient follows (aggregated per thread while unread).
        /// </summary>
        ThreadReply,

        /// <summary>
        /// A thread published in a forum the recipient follows (aggregated per forum while unread).
        /// </summary>
        NewThreadInForum,

        /// <summary>
        /// The recipient's comment was accepted as the answer.
        /// </summary>
        AnswerAccepted,

        /// <summary>
        /// The recipient's Feedback thread was reviewed.
        /// </summary>
        FeedbackReviewed,

        /// <summary>
        /// A poll the recipient created or voted in closed.
        /// </summary>
        PollClosed,

        /// <summary>
        /// The recipient's thread was promoted to the feed.
        /// </summary>
        ThreadPromoted,

        /// <summary>
        /// A moderator moved the recipient's thread.
        /// </summary>
        ThreadMoved,

        /// <summary>
        /// A directory submission was approved or rejected.
        /// </summary>
        SubmissionDecision,

        /// <summary>
        /// A directory submission passed the review target.
        /// </summary>
        SubmissionOverdue,

        /// <summary>
        /// The member was linked to an instructor entry.
        /// </summary>
        InstructorLinked,

        /// <summary>
        /// An event reminder came due.
        /// </summary>
        EventReminder,

        /// <summary>
        /// An event's time, location, or format changed.
        /// </summary>
        EventUpdated,

        /// <summary>
        /// An event was canceled.
        /// </summary>
        EventCanceled,

        /// <summary>
        /// The member was promoted from an event waitlist.
        /// </summary>
        EventWaitlistPromoted,

        /// <summary>
        /// An event was published with notifyMembers.
        /// </summary>
        NewEvent,

        /// <summary>
        /// A lesson was published in a followed series.
        /// </summary>
        NewLessonInSeries,

        /// <summary>
        /// An event proposal was approved or rejected.
        /// </summary>
        EventProposalDecision,

        /// <summary>
        /// A new direct message (push only).
        /// </summary>
        DirectMessage,

        /// <summary>
        /// A new message request (in-app only).
        /// </summary>
        MessageRequest,

        /// <summary>
        /// Staff replied to or changed the status of the member's ticket.
        /// </summary>
        TicketUpdate,

        /// <summary>
        /// A ticket was opened (support staff).
        /// </summary>
        TicketOpened,

        /// <summary>
        /// The requester replied to a ticket (assignee).
        /// </summary>
        TicketMemberReplied,

        /// <summary>
        /// A ticket missed its first response target (support staff).
        /// </summary>
        TicketOverdue,

        /// <summary>
        /// An idea the member voted for or follows changed status.
        /// </summary>
        IdeaStatusChanged,

        /// <summary>
        /// A staff broadcast (announcement).
        /// </summary>
        Broadcast,

        /// <summary>
        /// A member reviewed a venue you are a linked instructor of.
        /// </summary>
        VenueReview,

        /// <summary>
        /// A linked instructor replied to your venue review.
        /// </summary>
        VenueReviewReply,

        /// <summary>
        /// Administrators: the content reviewer flagged a member contribution (docs/INTEGRATION.md content review contract).
        /// </summary>
        ContentFlagged,

        /// <summary>An administrator metric threshold was crossed.</summary>
        MetricAlert,

        /// <summary>
        /// A rank awarder promoted you (verified rank award).
        /// </summary>
        RankPromoted,

        /// <summary>
        /// A member named you as the promoter in their rank lineage and asks you to verify it.
        /// </summary>
        LineageVerificationRequested
    }
}
