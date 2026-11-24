namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// One <see cref="SecretBox"/> per configured secrets key (settings secrets.encryptionKey and
    /// secrets.previousEncryptionKeys). When no valid key is configured (only possible when settings are built in code,
    /// because the settings store generates one), a random process-wide key is used and secrets do not survive a restart.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public static class SecretBoxProvider
    {
        #region Private-Members

        private static readonly ConcurrentDictionary<string, SecretBox> _Boxes = new ConcurrentDictionary<string, SecretBox>(StringComparer.Ordinal);
        private static readonly Lazy<string> _Ephemeral = new Lazy<string>(SecretBox.GenerateKey);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The secret box of the running settings.
        /// </summary>
        /// <param name="settings">Secret settings.</param>
        /// <returns>Secret box.</returns>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public static SecretBox For(SecretSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            string key = SecretBox.IsValidKey(settings.EncryptionKey) ? settings.EncryptionKey.Trim() : _Ephemeral.Value;
            string previous = settings.PreviousEncryptionKeys ?? String.Empty;
            return _Boxes.GetOrAdd(key + "|" + previous, _ => new SecretBox(key, ValidPrevious(previous)));
        }

        #endregion

        #region Private-Methods

        private static string ValidPrevious(string previous)
        {
            return String.Join(",", previous.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(SecretBox.IsValidKey));
        }

        #endregion
    }
}
