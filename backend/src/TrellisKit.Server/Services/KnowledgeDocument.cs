namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// One object a content target provider offers as assistant knowledge (docs/AI_CONTRACT.md section 7,
    /// docs/PHASE4_CONTRACT.md section 5.11): plain text the knowledge index chunks and embeds, with the object's audience
    /// stored on each chunk. Retrieval re-checks every hit as the chatting member.
    /// </summary>
    public class KnowledgeDocument
    {
        #region Public-Members

        /// <summary>
        /// Object identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Plain text (never contact details, join URLs, attendee data, progress, or anything a member could not open).
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// The object's audience at index time (Public, Members, ...).
        /// </summary>
        public string Audience { get; set; } = String.Empty;

        #endregion
    }
}
