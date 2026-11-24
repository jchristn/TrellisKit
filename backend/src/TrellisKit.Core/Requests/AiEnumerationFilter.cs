namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Collection filters of the AI enumerations (docs/AI_CONTRACT.md). Each enumeration reads only the fields its collection
    /// declares; null means not filtered.
    /// </summary>
    public class AiEnumerationFilter
    {
        #region Public-Members

        /// <summary>
        /// Endpoint kind.
        /// </summary>
        public ModelEndpointKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Endpoint API type.
        /// </summary>
        public ModelApiTypeEnum? ApiType { get; set; } = null;

        /// <summary>
        /// Enabled state.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Endpoint health status.
        /// </summary>
        public ModelHealthStatusEnum? HealthStatus { get; set; } = null;

        /// <summary>
        /// Endpoint auth type.
        /// </summary>
        public ModelAuthTypeEnum? AuthType { get; set; } = null;

        /// <summary>
        /// Endpoint scope.
        /// </summary>
        public ModelEndpointScopeEnum? Scope { get; set; } = null;

        /// <summary>
        /// Assistant audience.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Endpoint referenced by an assistant (any purpose).
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Assistant embed state.
        /// </summary>
        public bool? EmbedEnabled { get; set; } = null;

        /// <summary>
        /// Assistant.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        /// <summary>
        /// Conversation owner member.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Conversation owner visitor.
        /// </summary>
        public string? VisitorId { get; set; } = null;

        /// <summary>
        /// Member or Visitor.
        /// </summary>
        public string? OwnerType { get; set; } = null;

        /// <summary>
        /// Conversation status.
        /// </summary>
        public AssistantConversationStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Archived state.
        /// </summary>
        public bool? Archived { get; set; } = null;

        /// <summary>
        /// Conversation surface.
        /// </summary>
        public AiConversationSurfaceEnum? Surface { get; set; } = null;

        /// <summary>
        /// Feedback rating.
        /// </summary>
        public AssistantFeedbackRatingEnum? Rating { get; set; } = null;

        /// <summary>
        /// Feedback with a note.
        /// </summary>
        public bool? HasNote { get; set; } = null;

        /// <summary>
        /// Feedback reviewed.
        /// </summary>
        public bool? Reviewed { get; set; } = null;

        /// <summary>
        /// Health check result.
        /// </summary>
        public bool? Success { get; set; } = null;

        /// <summary>
        /// Knowledge item status.
        /// </summary>
        public AiKnowledgeItemStatusEnum? ItemStatus { get; set; } = null;

        /// <summary>
        /// Knowledge item content type.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Start of an analytics window.
        /// </summary>
        public DateTime? WindowStartUtc { get; set; } = null;

        /// <summary>
        /// User or Visitor.
        /// </summary>
        public string? SubjectType { get; set; } = null;

        #endregion
    }
}
