namespace TrellisKit.Core.Responses
{
    /// <summary>Standard change page with a separate watermark to start the next synchronization.</summary>
    public class ConversationChangesResult : EnumerationResult<ConversationChangeView>
    {
        /// <summary>Watermark on the terminal page, otherwise null. It is not a page continuation token.</summary>
        public string? SyncToken { get; set; } = null;
    }
}
