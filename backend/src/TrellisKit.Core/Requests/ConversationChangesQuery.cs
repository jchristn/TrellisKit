namespace TrellisKit.Core.Requests
{
    /// <summary>Standard change enumeration with a separate persisted synchronization watermark.</summary>
    public class ConversationChangesQuery : EnumerationQuery
    {
        /// <summary>Last completed synchronization watermark. Ignored while a continuation token pages the current retrieval.</summary>
        public string? SyncToken { get; set; } = null;
    }
}
