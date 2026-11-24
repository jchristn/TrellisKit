namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How media access grants are issued.
    /// </summary>
    public enum MediaDeliveryModeEnum
    {
        /// <summary>
        /// CloudFront signed cookies (S3 mode).
        /// </summary>
        CloudFront,

        /// <summary>
        /// Backend-verified tk_media cookie (Local mode).
        /// </summary>
        Local
    }
}
