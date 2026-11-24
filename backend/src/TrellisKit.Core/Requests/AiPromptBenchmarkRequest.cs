namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Queue the scope benchmark (job ai.scopeBenchmark) for a prompt's pending draft, or for the prompts in use when the
    /// prompt has no draft.
    /// </summary>
    public class AiPromptBenchmarkRequest
    {
        #region Public-Members

        /// <summary>
        /// Dry run: deterministic layers only, no model calls; a dry run never clears the publish gate. Default false.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Assistant whose moderation model classifies member input when the Moderation role has no executors, or null.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        /// <summary>
        /// Completion task role that writes the guarded replies, or null for CommunityAssistant.
        /// </summary>
        public string? ReplyRole { get; set; } = null;

        #endregion
    }
}
