namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Where an encrypted value is stored (secrets key rotation).
    /// </summary>
    public enum SecretStoreEnum
    {
        /// <summary>
        /// A per-property integration secret (propertysettings ciphertext).
        /// </summary>
        PropertySetting,

        /// <summary>
        /// A queued email payload (emailmessages payloadciphertext).
        /// </summary>
        EmailPayload,

        /// <summary>
        /// A stored live update code signing private key (updatesigningkeys privatekeyciphertext; row key is the key id).
        /// </summary>
        UpdateSigningKey,

        /// <summary>
        /// An AI model endpoint credential (aiendpoints authvaluesecret, a SecretBoxProtector envelope; row key is the
        /// endpoint id).
        /// </summary>
        AiEndpointAuthValue,

        /// <summary>
        /// An AI model endpoint extra header value (aiendpointheaders valuesecret, a SecretBoxProtector envelope; row key
        /// is "&lt;endpointId&gt;|&lt;normalized header name&gt;").
        /// </summary>
        AiEndpointHeader,

        /// <summary>
        /// The stored credential of a CLI agent harness profile (aiharnessprofiles.credentialsecret).
        /// </summary>
        AiHarnessCredential
    }
}
