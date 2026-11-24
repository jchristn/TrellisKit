namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Where media objects are stored.
    /// </summary>
    public enum MediaStorageModeEnum
    {
        /// <summary>
        /// Folders under storage.directory, served by the backend.
        /// </summary>
        Local,

        /// <summary>
        /// Amazon S3 buckets, served by CloudFront.
        /// </summary>
        S3
    }
}
