namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A lesson detail (Lesson).
    /// </summary>
    public class LessonView : LessonBaseView
    {
        #region Public-Members

        /// <summary>
        /// Description (empty on locked stubs).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Video media, or null (locked stubs and articles).
        /// </summary>
        public MediaView? Video { get; set; } = null;

        /// <summary>
        /// Markers.
        /// </summary>
        public List<LessonMarkerView> Markers { get; set; } = new List<LessonMarkerView>();

        /// <summary>
        /// Resources.
        /// </summary>
        public List<LessonResourceView> Resources { get; set; } = new List<LessonResourceView>();

        /// <summary>
        /// Caption tracks.
        /// </summary>
        public List<LessonCaptionView> Captions { get; set; } = new List<LessonCaptionView>();

        /// <summary>
        /// Staff are notified of comments.
        /// </summary>
        public bool NotifyStaffOnComment { get; set; } = false;

        /// <summary>
        /// The conversation target (the lesson).
        /// </summary>
        public CommentTargetRef CommentTarget { get; set; } = new CommentTargetRef();

        /// <summary>
        /// Reaction counts by key.
        /// </summary>
        public Dictionary<string, int> ReactionCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// The caller's reactions.
        /// </summary>
        public List<string> MyReactions { get; set; } = new List<string>();

        /// <summary>
        /// Previous lesson, or null.
        /// </summary>
        public LessonNeighbor? Previous { get; set; } = null;

        /// <summary>
        /// Next lesson, or null.
        /// </summary>
        public LessonNeighbor? Next { get; set; } = null;

        /// <summary>
        /// Active promotion, or null.
        /// </summary>
        public object? Promotion { get; set; } = null;

        /// <summary>
        /// Video media status (staff only), or null.
        /// </summary>
        public MediaStatusEnum? MediaStatus { get; set; } = null;

        /// <summary>
        /// Moderation state (staff).
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        #endregion
    }
}
