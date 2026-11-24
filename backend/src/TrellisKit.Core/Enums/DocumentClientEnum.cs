namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// The client a document acceptance was made from (X-Client and X-Client-Platform headers).
    /// </summary>
    public enum DocumentClientEnum
    {
        /// <summary>
        /// The website.
        /// </summary>
        Web,

        /// <summary>
        /// The iPhone or iPad app.
        /// </summary>
        Ios,

        /// <summary>
        /// The Android app.
        /// </summary>
        Android,

        /// <summary>
        /// Any other or unknown client.
        /// </summary>
        Other
    }
}
