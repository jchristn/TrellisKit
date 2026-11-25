namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Subject of a polymorphic feature (reactions, comments, reports, notifications, moderation).
    /// </summary>
    public enum TargetTypeEnum
    {
        /// <summary>
        /// A feed post (fpo_).
        /// </summary>
        FeedPost,

        /// <summary>
        /// A comment (cmt_).
        /// </summary>
        Comment,

        /// <summary>
        /// A member account (usr_); reports only.
        /// </summary>
        User,

        /// <summary>
        /// A media item (med_).
        /// </summary>
        Media,

        /// <summary>
        /// A reaction (rxn_).
        /// </summary>
        Reaction,

        /// <summary>
        /// A notification (ntf_).
        /// </summary>
        Notification,

        /// <summary>
        /// A report (rpt_).
        /// </summary>
        Report,

        /// <summary>
        /// A moderation action (mac_).
        /// </summary>
        ModerationAction,

        /// <summary>
        /// A word filter term (wft_).
        /// </summary>
        WordFilterTerm,

        /// <summary>
        /// A push token (ptk_).
        /// </summary>
        PushToken,

        /// <summary>
        /// A sign-up code (suc_).
        /// </summary>
        SignupCode,

        /// <summary>
        /// A sign-up code redemption (scr_).
        /// </summary>
        SignupCodeRedemption,

        /// <summary>
        /// A store subscription (sub_).
        /// </summary>
        Subscription,

        /// <summary>
        /// A membership entitlement or comp (ent_).
        /// </summary>
        Entitlement,

        /// <summary>
        /// A sign-in session (ses_).
        /// </summary>
        Session,

        /// <summary>
        /// An audit record (aud_).
        /// </summary>
        AuditRecord,

        /// <summary>
        /// A captured request history entry (req_).
        /// </summary>
        RequestHistory,

        /// <summary>
        /// A batch of generated single-use sign-up codes (scb_).
        /// </summary>
        SignupCodeBatch,

        /// <summary>
        /// A forum thread (reserved).
        /// </summary>
        Thread,

        /// <summary>
        /// A lesson (reserved).
        /// </summary>
        Lesson,

        /// <summary>
        /// An event (reserved).
        /// </summary>
        Event,

        /// <summary>
        /// A direct message (reserved).
        /// </summary>
        Message,

        /// <summary>
        /// A stored previous version of a feed post (fpr_). Carries the labels and tags the post had in that version,
        /// copied when the revision is stored; read-only afterwards.
        /// </summary>
        FeedPostRevision,

        /// <summary>
        /// A stored previous version of a comment (cmr_). Carries the labels and tags the comment had in that version,
        /// copied when the revision is stored; read-only afterwards.
        /// </summary>
        CommentRevision,

        /// <summary>
        /// A registered property (app_). Platform metadata only (stored under the platform realm).
        /// </summary>
        Property,

        /// <summary>
        /// A platform administrator (pad_). Platform metadata only (stored under the platform realm).
        /// </summary>
        PlatformAdmin,

        /// <summary>
        /// Profile field definition.
        /// </summary>
        ProfileField,

        /// <summary>
        /// Administrative email message record.
        /// </summary>
        EmailMessage,

        /// <summary>
        /// Suppressed email address.
        /// </summary>
        EmailSuppression,

        /// <summary>
        /// A live update group: the iOS and Android updates of one publish (upg_).
        /// </summary>
        UpdateGroup,

        /// <summary>
        /// One platform's live update (upd_).
        /// </summary>
        Update,

        /// <summary>
        /// A live update deploy key for CI (udk_).
        /// </summary>
        UpdateDeployKey,

        /// <summary>
        /// A live update code signing key (usk_).
        /// </summary>
        UpdateSigningKey,

        /// <summary>
        /// A feature flag or kill switch (ffl_).
        /// </summary>
        FeatureFlag,

        /// <summary>
        /// A published app content version (cve_).
        /// </summary>
        ContentVersion,

        /// <summary>
        /// A forum (frm_).
        /// </summary>
        Forum,

        /// <summary>
        /// A forum thread edit history entry (thv_).
        /// </summary>
        ThreadRevision,

        /// <summary>
        /// A poll of a Poll thread (pol_).
        /// </summary>
        Poll,

        /// <summary>
        /// An option of a poll (pop_).
        /// </summary>
        PollOption,

        /// <summary>
        /// One voter choice of one poll option (pov_).
        /// </summary>
        PollVote,

        /// <summary>
        /// A member following a forum (ffw_).
        /// </summary>
        ForumFollow,

        /// <summary>
        /// A member following a thread (tfw_).
        /// </summary>
        ThreadFollow,

        /// <summary>
        /// A moderator mute of a member in one forum (fmu_).
        /// </summary>
        ForumMute,

        /// <summary>
        /// A model endpoint (mep_, docs/AI_CONTRACT.md).
        /// </summary>
        ModelEndpoint,

        /// <summary>
        /// An assistant (ast_).
        /// </summary>
        Assistant,

        /// <summary>
        /// An assistant conversation (acv_).
        /// </summary>
        AssistantConversation,

        /// <summary>
        /// An assistant conversation message (acm_).
        /// </summary>
        AssistantMessage,

        /// <summary>
        /// A daily usage record of one meter (usd_; docs/USAGE_METERING_CONTRACT.md).
        /// </summary>
        UsageRecord,

        /// <summary>
        /// A place in the directory (ven_).
        /// </summary>
        Venue,

        /// <summary>
        /// A person listed in the directory (ins_).
        /// </summary>
        Instructor,

        /// <summary>
        /// A link between a venue and an instructor (vin_).
        /// </summary>
        VenueInstructor,

        /// <summary>
        /// An admin-defined program option a venue offers (dpg_).
        /// </summary>
        DirectoryProgram,

        /// <summary>
        /// A member proposal for the directory (dsb_).
        /// </summary>
        DirectorySubmission,

        /// <summary>
        /// A cached geocoder answer (gcr_).
        /// </summary>
        GeocodeResult,

        /// <summary>
        /// An admin-defined event category (ecg_).
        /// </summary>
        EventCategory,

        /// <summary>
        /// A member's RSVP to an event occurrence (rsv_).
        /// </summary>
        EventRsvp,

        /// <summary>
        /// A scheduled reminder offset of an event (erm_).
        /// </summary>
        EventReminder,

        /// <summary>
        /// A member's private calendar subscription (cfd_).
        /// </summary>
        CalendarFeed,

        /// <summary>
        /// A course of ordered lessons (ser_).
        /// </summary>
        Series,

        /// <summary>
        /// A document, link, or reference attached to a lesson (lrs_).
        /// </summary>
        LessonResource,

        /// <summary>
        /// A chapter marker inside a lesson video (lmk_).
        /// </summary>
        LessonMarker,

        /// <summary>
        /// A member's progress on a lesson (lpr_).
        /// </summary>
        LessonProgress,

        /// <summary>
        /// A member following a series (sfw_).
        /// </summary>
        SeriesFollow,

        /// <summary>
        /// An admin-defined library level (llv_).
        /// </summary>
        LibraryLevel,

        /// <summary>
        /// A mobile offline download license (ldl_).
        /// </summary>
        LessonDownload,

        /// <summary>
        /// A recurrence rule shared by the occurrences of a repeating event (erc_).
        /// </summary>
        EventRecurrence,

        /// <summary>
        /// A WebVTT caption track of a lesson (lcp_).
        /// </summary>
        LessonCaption,

        /// <summary>
        /// A direct conversation (cnv_).
        /// </summary>
        Conversation,

        /// <summary>
        /// A member's state in a conversation (cvm_).
        /// </summary>
        ConversationMember,

        /// <summary>
        /// A frozen copy of reported messages for moderators (mrs_).
        /// </summary>
        MessageReportSnapshot,

        /// <summary>
        /// A live WebSocket connection (rtc_).
        /// </summary>
        RealtimeConnection,

        /// <summary>
        /// A support ticket (tkt_).
        /// </summary>
        Ticket,

        /// <summary>
        /// A ticket reply or internal note (tkm_).
        /// </summary>
        TicketMessage,

        /// <summary>
        /// An admin-defined ticket type (tty_).
        /// </summary>
        TicketType,

        /// <summary>
        /// An ideas board entry (ida_).
        /// </summary>
        Idea,

        /// <summary>
        /// A member's vote on an idea (idv_).
        /// </summary>
        IdeaVote,

        /// <summary>
        /// An admin-defined idea status (ids_).
        /// </summary>
        IdeaStatus,

        /// <summary>
        /// A staff announcement over in-app, push, and email (bct_).
        /// </summary>
        Broadcast,

        /// <summary>
        /// A saved broadcast audience (bsg_).
        /// </summary>
        BroadcastSegment,

        /// <summary>
        /// A per recipient broadcast delivery record (bdl_).
        /// </summary>
        BroadcastDelivery,

        /// <summary>
        /// Property document (docs/TRELLISKIT.md section 14); the target id is the document key.
        /// </summary>
        PropertyDocument,

        /// <summary>
        /// Property document version.
        /// </summary>
        PropertyDocumentVersion,

        /// <summary>
        /// A member's acceptance of a property document version.
        /// </summary>
        DocumentAcceptance,

        /// <summary>
        /// A remote string override (rst_); labels and tags attach to the catalog key.
        /// </summary>
        RemoteString,

        /// <summary>
        /// An app banner (announcement, ban_).
        /// </summary>
        Banner,

        /// <summary>
        /// A remote content asset (ras_).
        /// </summary>
        RemoteAsset,

        /// <summary>
        /// A client-reported live update event (uev_).
        /// </summary>
        UpdateEvent,

        /// <summary>
        /// A member review of a venue (vrv_).
        /// </summary>
        VenueReview,

        /// <summary>
        /// A photo or video a member added to a venue (vmd_).
        /// </summary>
        VenueMedia,



        /// <summary>
        /// A version of an administrator-editable AI prompt template (apv_).
        /// </summary>
        AiPromptVersion,

        /// <summary>
        /// A background job run (jbr_), administrators only.
        /// </summary>
        JobRun,

        /// <summary>
        /// One item of a background job run (jbi_), administrators only.
        /// </summary>
        JobItem,

        /// <summary>
        /// One model call in the AI call ledger (aic_), administrators only.
        /// </summary>
        AiCall,

        /// <summary>
        /// One content changelog row (chg_, docs/INTEGRATION.md content review contract), administrators only.
        /// </summary>
        ContentChange,




        /// <summary>A background job definition (jbd_), administrators only.</summary>
        JobDefinition,

        /// <summary>A photo or video in a member profile gallery (pmd_).</summary>
        ProfileMedia,

        /// <summary>
        /// A member's current verified rank in one Rank profile field.
        /// </summary>
        MemberRank,

        /// <summary>
        /// One rank award or revoke.
        /// </summary>
        RankPromotion,

        /// <summary>
        /// Requirements to be awarded a rank.
        /// </summary>
        RankRequirement,

        /// <summary>
        /// A member's self-reported rank lineage entry.
        /// </summary>
        LineageEntry,

        /// <summary>
        /// A curriculum (rank-grouped list of lessons, series, catalog entries, and custom items).
        /// </summary>
        Curriculum,

        /// <summary>
        /// One item of a curriculum.
        /// </summary>
        CurriculumItem,

        /// <summary>
        /// A member's self-completion or verification of a curriculum item.
        /// </summary>
        CurriculumProgress,

        /// <summary>
        /// A member's check-in to an event or venue session occurrence.
        /// </summary>
        Attendance
    }
}
