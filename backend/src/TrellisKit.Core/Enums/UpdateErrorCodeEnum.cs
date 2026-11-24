namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Bounded error codes of client-reported live update failures.
    /// </summary>
    public enum UpdateErrorCodeEnum
    {
        /// <summary>
        /// Network failure.
        /// </summary>
        Network,

        /// <summary>
        /// The manifest signature did not verify.
        /// </summary>
        SignatureInvalid,

        /// <summary>
        /// An asset hash did not match the manifest.
        /// </summary>
        HashMismatch,

        /// <summary>
        /// Device storage failure.
        /// </summary>
        Storage,

        /// <summary>
        /// The operation timed out.
        /// </summary>
        Timeout,

        /// <summary>
        /// The server answered with an error.
        /// </summary>
        Server,

        /// <summary>
        /// Anything else.
        /// </summary>
        Unknown
    }
}
