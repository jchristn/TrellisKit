namespace TrellisKit.Core.Security
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// PBKDF2-HMAC-SHA256 password hashing with a random 16-byte salt and a 32-byte derived key.
    /// Thread safety: stateless after construction; safe to call concurrently.
    /// </summary>
    public class Pbkdf2PasswordHasher : IPasswordHasher
    {
        #region Public-Members

        /// <summary>
        /// Iterations for new hashes. Default 600000 (OWASP 2023 guidance for PBKDF2-HMAC-SHA256).
        /// Minimum 100000, maximum 10000000. Hashes with fewer iterations report needsRehash on verify.
        /// </summary>
        public int Iterations
        {
            get { return _Iterations; }
            set { _Iterations = Math.Clamp(value, 100000, 10000000); }
        }

        #endregion

        #region Private-Members

        private const string _Prefix = "pbkdf2-sha256";
        private const int _SaltBytes = 16;
        private const int _KeyBytes = 32;
        private int _Iterations = 600000;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public string Hash(string password)
        {
            ArgumentNullException.ThrowIfNull(password);
            byte[] salt = RandomNumberGenerator.GetBytes(_SaltBytes);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, _Iterations, HashAlgorithmName.SHA256, _KeyBytes);
            return _Prefix + "$" + _Iterations.ToString(CultureInfo.InvariantCulture) + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(key);
        }

        /// <inheritdoc />
        public bool Verify(string password, string encodedHash, out bool needsRehash)
        {
            needsRehash = false;
            if (password == null || String.IsNullOrEmpty(encodedHash)) return false;

            string[] parts = encodedHash.Split('$');
            if (parts.Length != 4 || parts[0] != _Prefix) return false;
            if (!Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) || iterations < 1) return false;

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            bool match = CryptographicOperations.FixedTimeEquals(actual, expected);
            needsRehash = match && iterations < _Iterations;
            return match;
        }

        #endregion
    }
}
