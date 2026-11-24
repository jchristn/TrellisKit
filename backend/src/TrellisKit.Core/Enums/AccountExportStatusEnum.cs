namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// State of an asynchronous account export job.
    /// </summary>
    public enum AccountExportStatusEnum
    {
        /// <summary>
        /// Requested; waiting for the export worker.
        /// </summary>
        Pending,

        /// <summary>
        /// The worker is writing the file.
        /// </summary>
        Running,

        /// <summary>
        /// The file is ready to download.
        /// </summary>
        Ready,

        /// <summary>
        /// Generation failed; request a new export.
        /// </summary>
        Failed,

        /// <summary>
        /// The file was deleted after its retention period.
        /// </summary>
        Expired
    }
}
