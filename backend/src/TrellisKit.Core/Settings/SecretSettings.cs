namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// The engine secrets key that encrypts stored secrets (per-property integration secrets, queued email payloads).
    /// Deployment-wide; supplied by TK_SECRETS_ENCRYPTION_KEY (Secrets Manager in AWS), or generated on first start only
    /// when AllowGeneratedKey is set (local development).
    /// </summary>
    public class SecretSettings
    {
        #region Public-Members

        /// <summary>
        /// Current key: base64 of 32 bytes. Default empty (generated and saved to the settings file on first start).
        /// </summary>
        public string EncryptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Previous keys still accepted for decryption: comma-separated base64 values. Default empty.
        /// </summary>
        public string PreviousEncryptionKeys { get; set; } = String.Empty;

        /// <summary>
        /// Whether the server may generate the secrets key itself when none is configured (saved to the settings file).
        /// Only for local development (the Docker stack sets TK_SECRETS_ALLOW_GENERATED_KEY=true): without it, a missing
        /// or invalid key stops startup, because each instance would otherwise generate its own key and could not
        /// decrypt the others' values. Default false.
        /// </summary>
        public bool AllowGeneratedKey { get; set; } = false;

        /// <summary>
        /// Seconds between re-encryption passes (values encrypted with a previous key are rewritten with the current
        /// one). Default 300. Minimum 30, maximum 86400.
        /// </summary>
        public int ReencryptIntervalSeconds
        {
            get { return _ReencryptIntervalSeconds; }
            set { _ReencryptIntervalSeconds = Math.Clamp(value, 30, 86400); }
        }

        /// <summary>
        /// Most stored values one re-encryption pass rewrites per property. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int ReencryptBatchSize
        {
            get { return _ReencryptBatchSize; }
            set { _ReencryptBatchSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private int _ReencryptIntervalSeconds = 300;
        private int _ReencryptBatchSize = 100;

        #endregion
    }
}
