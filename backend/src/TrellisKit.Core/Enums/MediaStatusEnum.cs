namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle state of an uploaded media item.
    /// </summary>
    public enum MediaStatusEnum
    {
        /// <summary>
        /// Upload session created; bytes not confirmed.
        /// </summary>
        Uploading,

        /// <summary>
        /// Upload completed; the processing worker is running or queued.
        /// </summary>
        Processing,

        /// <summary>
        /// Processed and servable.
        /// </summary>
        Ready,

        /// <summary>
        /// Processing failed; see the failure reason.
        /// </summary>
        Failed
    }
}
