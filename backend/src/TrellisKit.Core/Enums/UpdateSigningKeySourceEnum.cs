namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Where the private key of an update signing key comes from.
    /// </summary>
    public enum UpdateSigningKeySourceEnum
    {
        /// <summary>
        /// Deployment configuration (TK_UPDATES_SIGNING_KEY or TK_UPDATES_STANDBY_SIGNING_KEY, or their _FILE variants).
        /// </summary>
        Environment,

        /// <summary>
        /// Uploaded in the admin console, stored encrypted with secrets.encryptionKey.
        /// </summary>
        Stored
    }
}
