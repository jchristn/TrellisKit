namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// Plaintext of an access token before encryption. Carries only platform identifiers and timestamps.
    /// </summary>
    public class AccessTokenPayload
    {
        #region Public-Members

        /// <summary>
        /// Payload version. 2 carries <see cref="Pid"/> and <see cref="Pk"/>; version 1 tokens (no property) are
        /// accepted only in SingleProperty mode.
        /// </summary>
        public int V { get; set; } = 2;

        /// <summary>
        /// Property the token was issued for. A token is only ever accepted for this property.
        /// </summary>
        public string Pid { get; set; } = String.Empty;

        /// <summary>
        /// Principal kind: Member or Delegated.
        /// </summary>
        public string Pk { get; set; } = "Member";

        /// <summary>
        /// Session identifier.
        /// </summary>
        public string Sid { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string Uid { get; set; } = String.Empty;

        /// <summary>
        /// Issued at, Unix milliseconds.
        /// </summary>
        public long Iat { get; set; } = 0;

        /// <summary>
        /// Expires at, Unix milliseconds.
        /// </summary>
        public long Exp { get; set; } = 0;

        /// <summary>
        /// Random nonce.
        /// </summary>
        public string N { get; set; } = String.Empty;

        #endregion
    }
}
