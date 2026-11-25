namespace TrellisKit.Server.Services
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Rotating six-digit check-in codes (docs/TRELLISKIT.md, Ranks, curricula, and attendance): an HMAC of the property,
    /// the target occurrence, and the rotation period, keyed from auth.tokenEncryptionKey (else secrets.encryptionKey,
    /// else a key that lives as long as the process). The current and the previous period's codes are accepted.
    /// Thread safety: stateless apart from the process key, which is created once.
    /// </summary>
    public static class CheckInCodes
    {
        #region Private-Members

        private static readonly byte[] _ProcessKey = RandomNumberGenerator.GetBytes(32);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The rotation period of an instant.
        /// </summary>
        /// <param name="utc">Instant.</param>
        /// <param name="rotationSeconds">Seconds per period (at least 1).</param>
        /// <returns>Period number.</returns>
        public static long Period(DateTime utc, int rotationSeconds)
        {
            long seconds = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            return seconds / Math.Max(1, rotationSeconds);
        }

        /// <summary>
        /// The code of one target occurrence in one period.
        /// </summary>
        /// <param name="secret">Key material (empty for the process key).</param>
        /// <param name="propertyId">Property.</param>
        /// <param name="target">Target occurrence.</param>
        /// <param name="period">Period number.</param>
        /// <returns>Six digits.</returns>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        public static string Code(string? secret, string propertyId, CheckInTarget target, long period)
        {
            ArgumentNullException.ThrowIfNull(target);
            byte[] key = Key(secret);
            string message = propertyId + "|" + target.TargetType + "|" + target.TargetId + "|" + target.OccurrenceDate + "|" + period.ToString(CultureInfo.InvariantCulture);
            byte[] mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message));
            uint value = ((uint)mac[0] << 24) | ((uint)mac[1] << 16) | ((uint)mac[2] << 8) | mac[3];
            return (value % 1000000u).ToString("D6", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Whether a code matches the current or the previous period.
        /// </summary>
        /// <param name="secret">Key material.</param>
        /// <param name="propertyId">Property.</param>
        /// <param name="target">Target occurrence.</param>
        /// <param name="code">Submitted code.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="rotationSeconds">Seconds per period.</param>
        /// <returns>True when valid.</returns>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        public static bool Matches(string? secret, string propertyId, CheckInTarget target, string? code, DateTime nowUtc, int rotationSeconds)
        {
            ArgumentNullException.ThrowIfNull(target);
            string submitted = (code ?? String.Empty).Trim();
            if (submitted.Length != 6) return false;
            long period = Period(nowUtc, rotationSeconds);
            byte[] given = Encoding.ASCII.GetBytes(submitted);
            for (long p = period; p >= period - 1; p--)
            {
                if (CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(Code(secret, propertyId, target, p)))) return true;
            }
            return false;
        }

        #endregion

        #region Private-Methods

        private static byte[] Key(string? secret)
        {
            string configured = (secret ?? String.Empty).Trim();
            if (configured.Length == 0) return _ProcessKey;
            byte[] material;
            try
            {
                material = Convert.FromBase64String(configured);
            }
            catch (FormatException)
            {
                material = Encoding.UTF8.GetBytes(configured);
            }
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, material, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes("tk-attendance-code"));
        }

        #endregion
    }
}
