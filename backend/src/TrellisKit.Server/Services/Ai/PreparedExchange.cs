namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A member message that passed every check and was stored (or a test exchange), ready to generate.
    /// </summary>
    public class PreparedExchange
    {
        #region Public-Members

        /// <summary>
        /// Caller.
        /// </summary>
        public AssistantCaller Caller { get; set; } = new AssistantCaller();

        /// <summary>
        /// Assistant (a draft copy for tests).
        /// </summary>
        public Assistant Assistant { get; set; } = new Assistant();

        /// <summary>
        /// Endpoints the assistant references.
        /// </summary>
        public Dictionary<string, ModelEndpoint> Endpoints { get; set; } = new Dictionary<string, ModelEndpoint>();

        /// <summary>
        /// Conversation, or null for tests.
        /// </summary>
        public AssistantConversation? Conversation { get; set; } = null;

        /// <summary>
        /// Member message.
        /// </summary>
        public AssistantMessage UserMessage { get; set; } = new AssistantMessage();

        /// <summary>
        /// Pending reply.
        /// </summary>
        public AssistantMessage Reply { get; set; } = new AssistantMessage();

        /// <summary>
        /// Earlier turns (user or assistant, content), oldest first.
        /// </summary>
        public List<KeyValuePair<string, string>> History { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Whether messages are stored (false for the admin test console).
        /// </summary>
        public bool Persist { get; set; } = true;

        /// <summary>
        /// Admin test traffic.
        /// </summary>
        public bool IsTest { get; set; } = false;

        /// <summary>
        /// An idempotent repeat: the stored exchange is returned instead of generating.
        /// </summary>
        public bool Replay { get; set; } = false;

        /// <summary>
        /// Whether this is the conversation's first reply (titles).
        /// </summary>
        public bool FirstReply { get; set; } = false;

        /// <summary>
        /// Word filter Flag matches to record.
        /// </summary>
        public WordFilterResult? Flags { get; set; } = null;

        /// <summary>
        /// Moderation verdict of the member message (LogOnly).
        /// </summary>
        public AssistantModerationVerdictEnum? InputVerdict { get; set; } = null;

        /// <summary>
        /// Telemetry stages recorded before generation.
        /// </summary>
        public List<AssistantMessageStage> Stages { get; set; } = new List<AssistantMessageStage>();

        #endregion
    }
}
