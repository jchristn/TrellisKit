namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Role of an update signing key in the property's key ring.
    /// </summary>
    public enum UpdateSigningKeyStatusEnum
    {
        /// <summary>
        /// Signs every manifest and directive.
        /// </summary>
        Active,

        /// <summary>
        /// Trusted by builds (certificate chain) but not signing; the next key to rotate to.
        /// </summary>
        Standby,

        /// <summary>
        /// Never used again; manifests requested with its key id get 503 SigningUnavailable.
        /// </summary>
        Retired
    }
}
