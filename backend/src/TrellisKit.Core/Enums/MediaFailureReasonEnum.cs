namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Why a media item failed.
    /// </summary>
    public enum MediaFailureReasonEnum
    {
        /// <summary>
        /// The bytes are not an accepted format, or do not match the declared content type.
        /// </summary>
        UnsupportedFormat,

        /// <summary>
        /// The file could not be decoded.
        /// </summary>
        Corrupt,

        /// <summary>
        /// The file exceeds the size limit.
        /// </summary>
        TooLarge,

        /// <summary>
        /// The video exceeds the duration cap.
        /// </summary>
        VideoTooLong,

        /// <summary>
        /// The image exceeds the pixel limit.
        /// </summary>
        ImageTooLarge,

        /// <summary>
        /// The processor failed.
        /// </summary>
        ProcessingError,

        /// <summary>
        /// The upload session expired before it was completed.
        /// </summary>
        UploadExpired
    }
}
