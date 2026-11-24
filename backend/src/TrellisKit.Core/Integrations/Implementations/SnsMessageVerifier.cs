namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Verifies Amazon SNS HTTP(S) messages: SignatureVersion 2 (RSA SHA256) or 1 (RSA SHA1) over the canonical string
    /// of the message type, a SigningCertURL over https on sns.&lt;region&gt;.amazonaws.com (the region of
    /// media.snsTopicArn) ending in .pem, and a TopicArn equal to media.snsTopicArn. Certificates are fetched through
    /// the injected HttpClient and cached by URL.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class SnsMessageVerifier : ISnsMessageVerifier
    {
        #region Public-Members

        /// <summary>
        /// Largest accepted certificate download in bytes. Default 65536. Minimum 4096, maximum 1048576.
        /// </summary>
        public int MaxCertificateBytes
        {
            get { return _MaxCertificateBytes; }
            set { _MaxCertificateBytes = Math.Clamp(value, 4096, 1048576); }
        }

        #endregion

        #region Private-Members

        private const string _Service = "sns";
        private readonly MediaSettings _Settings;
        private readonly HttpClient _Http;
        private readonly ConcurrentDictionary<string, X509Certificate2> _Certificates = new ConcurrentDictionary<string, X509Certificate2>(StringComparer.Ordinal);
        private int _MaxCertificateBytes = 65536;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the verifier.
        /// </summary>
        /// <param name="settings">Media settings (media.snsTopicArn is read on each call).</param>
        /// <param name="http">HTTP client for certificate downloads and subscription confirmation.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SnsMessageVerifier(MediaSettings settings, HttpClient http)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Http = http ?? throw new ArgumentNullException(nameof(http));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The canonical string SNS signs for a message.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>String to sign.</returns>
        /// <exception cref="ArgumentNullException">The message is null.</exception>
        public static string StringToSign(SnsMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            StringBuilder builder = new StringBuilder();
            builder.Append("Message\n").Append(message.Message).Append('\n');
            builder.Append("MessageId\n").Append(message.MessageId).Append('\n');
            if (message.Type == "Notification")
            {
                if (message.Subject != null) builder.Append("Subject\n").Append(message.Subject).Append('\n');
                builder.Append("Timestamp\n").Append(message.Timestamp).Append('\n');
                builder.Append("TopicArn\n").Append(message.TopicArn).Append('\n');
                builder.Append("Type\n").Append(message.Type).Append('\n');
            }
            else
            {
                builder.Append("SubscribeURL\n").Append(message.SubscribeURL).Append('\n');
                builder.Append("Timestamp\n").Append(message.Timestamp).Append('\n');
                builder.Append("Token\n").Append(message.Token).Append('\n');
                builder.Append("TopicArn\n").Append(message.TopicArn).Append('\n');
                builder.Append("Type\n").Append(message.Type).Append('\n');
            }
            return builder.ToString();
        }

        /// <summary>
        /// Parse an SNS message body.
        /// </summary>
        /// <param name="rawJson">Body.</param>
        /// <returns>Message.</returns>
        /// <exception cref="WebhookSignatureException">The body is not an SNS message.</exception>
        public static SnsMessage Parse(string rawJson)
        {
            if (String.IsNullOrWhiteSpace(rawJson)) throw new WebhookSignatureException("The SNS message is empty.");
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(rawJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw new WebhookSignatureException("The SNS message is not a JSON object.");
                    return new SnsMessage
                    {
                        Type = Text(root, "Type") ?? String.Empty,
                        MessageId = Text(root, "MessageId") ?? String.Empty,
                        TopicArn = Text(root, "TopicArn") ?? String.Empty,
                        Subject = Text(root, "Subject"),
                        Message = Text(root, "Message") ?? String.Empty,
                        Timestamp = Text(root, "Timestamp") ?? String.Empty,
                        SignatureVersion = Text(root, "SignatureVersion") ?? String.Empty,
                        Signature = Text(root, "Signature") ?? String.Empty,
                        SigningCertURL = Text(root, "SigningCertURL") ?? Text(root, "SigningCertUrl") ?? String.Empty,
                        SubscribeURL = Text(root, "SubscribeURL"),
                        Token = Text(root, "Token"),
                        UnsubscribeURL = Text(root, "UnsubscribeURL")
                    };
                }
            }
            catch (JsonException e)
            {
                throw new WebhookSignatureException("The SNS message is not valid JSON.", e);
            }
        }

        /// <inheritdoc />
        public async Task<SnsMessage> VerifyAsync(string rawJson, CancellationToken token = default)
        {
            SnsMessage message = Parse(rawJson);
            string topic = _Settings.SnsTopicArn;
            if (String.IsNullOrWhiteSpace(topic)) throw new WebhookSignatureException("media.snsTopicArn is not configured.");
            if (!String.Equals(message.TopicArn, topic, StringComparison.Ordinal)) throw new WebhookSignatureException("The TopicArn does not match media.snsTopicArn.");
            if (message.Type != "Notification" && message.Type != "SubscriptionConfirmation" && message.Type != "UnsubscribeConfirmation")
                throw new WebhookSignatureException("Unknown SNS message type.");
            RequireSnsUrl(message.SigningCertURL, true);

            HashAlgorithmName hash;
            if (message.SignatureVersion == "2") hash = HashAlgorithmName.SHA256;
            else if (message.SignatureVersion == "1") hash = HashAlgorithmName.SHA1;
            else throw new WebhookSignatureException("Unsupported SNS SignatureVersion.");

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(message.Signature);
            }
            catch (FormatException e)
            {
                throw new WebhookSignatureException("The SNS signature is not base64.", e);
            }

            X509Certificate2 certificate = await CertificateAsync(message.SigningCertURL, token).ConfigureAwait(false);
            using (RSA? rsa = certificate.GetRSAPublicKey())
            {
                if (rsa == null) throw new WebhookSignatureException("The SNS signing certificate has no RSA key.");
                byte[] data = Encoding.UTF8.GetBytes(StringToSign(message));
                if (!rsa.VerifyData(data, signature, hash, RSASignaturePadding.Pkcs1)) throw new WebhookSignatureException("The SNS signature is invalid.");
            }
            return message;
        }

        /// <inheritdoc />
        public async Task ConfirmSubscriptionAsync(SnsMessage message, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            string url = message.SubscribeURL ?? String.Empty;
            RequireSnsUrl(url, false);
            Activity? activity = TrellisKitTelemetry.StartIntegrationSpan(_Service, "ConfirmSubscription");
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "failure";
            try
            {
                using (HttpResponseMessage response = await _Http.GetAsync(url, token).ConfigureAwait(false))
                {
                    outcome = response.IsSuccessStatusCode ? "success" : "http_" + (int)response.StatusCode;
                    response.EnsureSuccessStatusCode();
                }
            }
            finally
            {
                TrellisKitTelemetry.RecordIntegration(activity, _Service, "ConfirmSubscription", stopwatch.Elapsed, outcome);
                activity?.Dispose();
            }
        }

        #endregion

        #region Private-Methods

        private static string? Text(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private string Region()
        {
            string[] parts = (_Settings.SnsTopicArn ?? String.Empty).Split(':');
            if (parts.Length >= 6 && parts[0] == "arn" && !String.IsNullOrEmpty(parts[3])) return parts[3];
            return _Settings.S3Region;
        }

        private void RequireSnsUrl(string url, bool certificate)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new WebhookSignatureException((certificate ? "SigningCertURL" : "SubscribeURL") + " must be an https URL.");
            string expectedHost = "sns." + Region() + ".amazonaws.com";
            if (!String.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort)
                throw new WebhookSignatureException((certificate ? "SigningCertURL" : "SubscribeURL") + " must be on " + expectedHost + ".");
            if (certificate && !uri.AbsolutePath.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)) throw new WebhookSignatureException("SigningCertURL must point at a .pem certificate.");
        }

        private async Task<X509Certificate2> CertificateAsync(string url, CancellationToken token)
        {
            if (_Certificates.TryGetValue(url, out X509Certificate2? cached) && cached.NotAfter.ToUniversalTime() > DateTime.UtcNow) return cached;
            Activity? activity = TrellisKitTelemetry.StartIntegrationSpan(_Service, "GetCertificate");
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "failure";
            try
            {
                string pem;
                try
                {
                    using (HttpResponseMessage response = await _Http.GetAsync(url, token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) throw new WebhookSignatureException("The SNS signing certificate could not be fetched (" + (int)response.StatusCode + ").");
                        byte[] bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                        if (bytes.Length > _MaxCertificateBytes) throw new WebhookSignatureException("The SNS signing certificate is too large.");
                        pem = Encoding.ASCII.GetString(bytes);
                    }
                }
                catch (HttpRequestException e)
                {
                    throw new WebhookSignatureException("The SNS signing certificate could not be fetched.", e);
                }
                X509Certificate2 certificate;
                try
                {
                    certificate = X509Certificate2.CreateFromPem(pem);
                }
                catch (CryptographicException e)
                {
                    throw new WebhookSignatureException("The SNS signing certificate is not a PEM certificate.", e);
                }
                if (certificate.NotAfter.ToUniversalTime() < DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                    throw new WebhookSignatureException("The SNS signing certificate is not currently valid.");
                _Certificates[url] = certificate;
                outcome = "success";
                return certificate;
            }
            finally
            {
                TrellisKitTelemetry.RecordIntegration(activity, _Service, "GetCertificate", stopwatch.Elapsed, outcome);
                activity?.Dispose();
            }
        }

        #endregion
    }
}
