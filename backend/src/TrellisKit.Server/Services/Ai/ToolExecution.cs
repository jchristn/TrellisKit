namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The outcome of running one tool call in the chat loop: the stored record, the text handed to the model, and the
    /// citation candidates the output introduced.
    /// </summary>
    public class ToolExecution
    {
        #region Public-Members

        /// <summary>
        /// Stored tool call.
        /// </summary>
        public AssistantToolCall Call { get; set; } = new AssistantToolCall();

        /// <summary>
        /// Text appended to the conversation as the tool message.
        /// </summary>
        public string Output { get; set; } = String.Empty;

        /// <summary>
        /// Citation candidates numbered after the knowledge items.
        /// </summary>
        public List<KnowledgeCandidate> Citations { get; set; } = new List<KnowledgeCandidate>();

        #endregion
    }
}
