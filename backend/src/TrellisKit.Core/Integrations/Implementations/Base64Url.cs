namespace TrellisKit.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Base64url (RFC 4648 section 5) encoding without padding.
    /// Thread safe.
    /// </summary>
    public static class Base64Url
    {
        /// <summary>
        /// Encode bytes as base64url without padding.
        /// </summary>
        /// <param name="data">Bytes.</param>
        /// <returns>Encoded text.</returns>
        /// <exception cref="ArgumentNullException">The data is null.</exception>
        public static string Encode(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Decode base64url text, with or without padding.
        /// </summary>
        /// <param name="text">Encoded text.</param>
        /// <returns>Bytes.</returns>
        /// <exception cref="ArgumentNullException">The text is null.</exception>
        /// <exception cref="FormatException">The text is not valid base64url.</exception>
        public static byte[] Decode(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            string normalized = text.Trim().Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 0:
                    break;
                case 2:
                    normalized += "==";
                    break;
                case 3:
                    normalized += "=";
                    break;
                default:
                    throw new FormatException("The value is not valid base64url.");
            }
            return Convert.FromBase64String(normalized);
        }
    }
}
