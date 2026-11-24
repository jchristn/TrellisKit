namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Storage location (bucket in S3 mode, folder under storage.directory in Local mode) of a media object.
    /// One location per purpose (contract section 4.0).
    /// </summary>
    public enum MediaLocationEnum
    {
        /// <summary>
        /// Original image uploads, kept and never served (media.sourceBucket, local folder source/).
        /// </summary>
        Source,

        /// <summary>
        /// Original video uploads, kept and never served (media.videoSourceBucket, local folder video-source/).
        /// </summary>
        VideoSource,

        /// <summary>
        /// Image variants, video posters, and link preview images (media.imagesBucket, local folder images/).
        /// </summary>
        Images,

        /// <summary>
        /// HLS playlists and segments, and passthrough MP4 files (media.transcodedBucket, local folder transcoded/).
        /// </summary>
        Transcoded,

        /// <summary>
        /// Served objects of moderator-removed content (media.retainedBucket, local folder retained/).
        /// </summary>
        Retained
    }
}
