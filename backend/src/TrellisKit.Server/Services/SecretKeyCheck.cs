namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// The startup check of the engine secrets key: outside local development (secrets.allowGeneratedKey false) the
    /// server refuses to start when TK_SECRETS_ENCRYPTION_KEY is missing or invalid, because each instance would
    /// otherwise encrypt with its own generated key and fail to decrypt the others' values. An invalid configured key
    /// or previous key is always a problem (it is never silently replaced).
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class SecretKeyCheck
    {
        #region Public-Methods

        /// <summary>
        /// Problems with the secrets key settings.
        /// </summary>
        /// <param name="settings">Secret settings (effective: file and environment).</param>
        /// <returns>Problems, empty when the server may start. Never null.</returns>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public static List<string> Problems(SecretSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            List<string> problems = new List<string>();
            string key = (settings.EncryptionKey ?? String.Empty).Trim();
            if (key.Length == 0)
            {
                if (!settings.AllowGeneratedKey)
                    problems.Add("secrets.encryptionKey is not set: supply TK_SECRETS_ENCRYPTION_KEY (base64 of 32 bytes, the same value on every instance). " +
                        "Only local development may let the server generate one (TK_SECRETS_ALLOW_GENERATED_KEY=true).");
            }
            else if (!SecretBox.IsValidKey(key))
            {
                problems.Add("secrets.encryptionKey (TK_SECRETS_ENCRYPTION_KEY) is not base64 of 32 bytes.");
            }
            string[] previous = (settings.PreviousEncryptionKeys ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < previous.Length; i++)
            {
                if (!SecretBox.IsValidKey(previous[i])) problems.Add("secrets.previousEncryptionKeys (TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS) entry " + (i + 1) + " is not base64 of 32 bytes.");
            }
            return problems;
        }

        #endregion
    }
}
