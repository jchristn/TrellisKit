namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How the client uploads the bytes.
    /// </summary>
    public enum UploadModeEnum
    {
        /// <summary>
        /// One PUT of the whole object.
        /// </summary>
        Single,

        /// <summary>
        /// Several part PUTs, then complete.
        /// </summary>
        Multipart
    }
}
