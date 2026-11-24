namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// Parameters of an ai.scopeBenchmark run (stored as the run's parameters JSON). A run without parameters measures
    /// the prompts in use.
    /// </summary>
    public class ScopeBenchmarkParameters
    {
        #region Public-Members

        /// <summary>
        /// Prompt catalog key whose draft is measured, or null for the prompts in use.
        /// </summary>
        public string? PromptKey { get; set; } = null;

        /// <summary>
        /// Draft version identifier (apv_) measured in place of the key's current text, or null.
        /// </summary>
        public string? VersionId { get; set; } = null;

        /// <summary>
        /// Assistant whose moderation endpoint classifies member input when the Moderation role has no executors (the
        /// assistant chat production path), or null.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        /// <summary>
        /// Task role that writes the guarded reply. Default CommunityAssistant.
        /// </summary>
        public string ReplyRole { get; set; } = DefaultReplyRole;

        /// <summary>
        /// Default reply role.
        /// </summary>
        public const string DefaultReplyRole = "CommunityAssistant";

        #endregion
    }
}
