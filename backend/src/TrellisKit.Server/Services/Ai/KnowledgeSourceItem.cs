namespace TrellisKit.Server.Services.Ai
{
    using System;

    /// <summary>
    /// One content item selected by an assistant's knowledge sources for ingestion.
    /// </summary>
    internal sealed class KnowledgeSourceItem
    {
        internal string SourceId { get; set; } = String.Empty;

        internal string TargetType { get; set; } = String.Empty;

        internal string TargetId { get; set; } = String.Empty;

        internal string Title { get; set; } = String.Empty;

        internal string Text { get; set; } = String.Empty;

        internal string Audience { get; set; } = String.Empty;
    }
}
