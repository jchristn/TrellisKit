namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// CloudFront signed cookies with a custom policy (contract section 4.7): the policy allows
    /// https://&lt;media host&gt;/&lt;propertyId&gt;/* (every media key is property-scoped) until the grant expiry and is signed with RSA-SHA1 (PKCS#1 v1.5) by the key in
    /// media.cloudFrontPrivateKeyPem; values use CloudFront's URL-safe base64 (+ to -, = to _, / to ~). Cookies carry
    /// Domain media.cookieDomain (host-only when empty) and Path /.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class CloudFrontCookieSigner : IMediaDeliverySigner
    {
        #region Public-Members

        /// <inheritdoc />
        public MediaDeliveryModeEnum Mode
        {
            get { return MediaDeliveryModeEnum.CloudFront; }
        }

        #endregion

        #region Private-Members

        private readonly MediaSettings _Settings;
        private readonly string _PropertyId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the signer.
        /// </summary>
        /// <param name="settings">Media settings (key, key pair id, base URL, and cookie domain are read on each call).</param>
        /// <param name="propertyId">Property id; the policy covers only /&lt;propertyId&gt;/* on the media host.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public CloudFrontCookieSigner(MediaSettings settings, string propertyId)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// CloudFront's URL-safe base64.
        /// </summary>
        /// <param name="data">Bytes.</param>
        /// <returns>Encoded text.</returns>
        public static string UrlSafeBase64(byte[] data)
        {
            return Convert.ToBase64String(data).Replace('+', '-').Replace('=', '_').Replace('/', '~');
        }

        /// <summary>
        /// Decode CloudFront's URL-safe base64.
        /// </summary>
        /// <param name="text">Encoded text.</param>
        /// <returns>Bytes.</returns>
        /// <exception cref="FormatException">The text is not valid.</exception>
        public static byte[] FromUrlSafeBase64(string text)
        {
            return Convert.FromBase64String((text ?? String.Empty).Replace('-', '+').Replace('_', '=').Replace('~', '/'));
        }

        /// <summary>
        /// The custom policy JSON for a resource and expiry.
        /// </summary>
        /// <param name="resource">Resource, for example https://media.example.test/*.</param>
        /// <param name="expiresUtc">Expiry.</param>
        /// <returns>Policy JSON without whitespace.</returns>
        public static string Policy(string resource, DateTime expiresUtc)
        {
            long epoch = new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            return "{\"Statement\":[{\"Resource\":\"" + resource + "\",\"Condition\":{\"DateLessThan\":{\"AWS:EpochTime\":" + epoch.ToString(CultureInfo.InvariantCulture) + "}}}]}";
        }

        /// <inheritdoc />
        public List<MediaCookie> CreateCookies(string userId, DateTime expiresUtc, bool insecureLoopback)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            string keyPairId = _Settings.CloudFrontKeyPairId;
            string pem = _Settings.CloudFrontPrivateKeyPem;
            if (String.IsNullOrWhiteSpace(keyPairId) || String.IsNullOrWhiteSpace(pem)) throw new IntegrationNotConfiguredException("CloudFront media cookies need media.cloudFrontKeyPairId and media.cloudFrontPrivateKeyPem.");
            if (!Uri.TryCreate(_Settings.PublicBaseUrl, UriKind.Absolute, out Uri? baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
                throw new IntegrationNotConfiguredException("CloudFront media cookies need media.publicBaseUrl (an absolute https URL).");

            string policy = Policy("https://" + baseUri.Authority + "/" + TrellisKit.Core.Helpers.MediaKeys.Prefix(_PropertyId) + "*", expiresUtc);
            byte[] policyBytes = Encoding.UTF8.GetBytes(policy);
            byte[] signature;
            using (RSA rsa = RSA.Create())
            {
                try
                {
                    rsa.ImportFromPem(pem.Replace("\\n", "\n"));
                }
                catch (ArgumentException e)
                {
                    throw new IntegrationNotConfiguredException("media.cloudFrontPrivateKeyPem is not a PEM RSA private key.", e);
                }
                catch (CryptographicException e)
                {
                    throw new IntegrationNotConfiguredException("media.cloudFrontPrivateKeyPem is not a PEM RSA private key.", e);
                }
                signature = rsa.SignData(policyBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            }

            string? domain = String.IsNullOrWhiteSpace(_Settings.CookieDomain) ? null : _Settings.CookieDomain.Trim();
            return new List<MediaCookie>
            {
                new MediaCookie { Name = "CloudFront-Policy", Value = UrlSafeBase64(policyBytes), Domain = domain, Path = "/", ExpiresUtc = expiresUtc, Secure = true },
                new MediaCookie { Name = "CloudFront-Signature", Value = UrlSafeBase64(signature), Domain = domain, Path = "/", ExpiresUtc = expiresUtc, Secure = true },
                new MediaCookie { Name = "CloudFront-Key-Pair-Id", Value = keyPairId.Trim(), Domain = domain, Path = "/", ExpiresUtc = expiresUtc, Secure = true }
            };
        }

        #endregion
    }
}
