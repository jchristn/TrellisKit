namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// New feed post emails (Phase 2 contract 8.9) and one-click unsubscribe tokens. Messages are queued through the
    /// property's email pipeline with template feed_post (FeedPostEmail): the property's brand layout, the author, the
    /// title, the excerpt, and a button to {web.publicBaseUrl}/app/feed/{id}. The post image is omitted in Phase 2
    /// (no signed media URLs yet). Every message carries an unsubscribe link plus List-Unsubscribe and
    /// List-Unsubscribe-Post: List-Unsubscribe=One-Click headers (RFC 8058) pointing at
    /// {web.publicBaseUrl}/api/v1/email/unsubscribe?token=... The token is
    /// base64url(payload) "." base64url(HMAC-SHA256(payload)), payload {"u":userId,"c":category}, keyed by
    /// HKDF-SHA256 of auth.tokenEncryptionKey with info "tk-email-unsubscribe" (read live, so rotating the key
    /// invalidates old links).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class FeedPostEmailService
    {
        #region Public-Members

        /// <summary>
        /// Email category label for telemetry and the in-memory test sender.
        /// </summary>
        public const string EmailCategory = FeedPostEmail.TemplateKey;

        /// <summary>
        /// HKDF info string of the unsubscribe key.
        /// </summary>
        public const string UnsubscribeKeyInfo = "tk-email-unsubscribe";

        #endregion

        #region Private-Members

        private readonly EmailService _Email;
        private readonly PropertyContext _Property;
        private readonly AppSettings _Settings;
        private readonly NotificationPreferenceService _Preferences;
        private readonly AppLogger _Logger;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="email">The property's email service (queue and templates).</param>
        /// <param name="property">Active property.</param>
        /// <param name="settings">Running settings (read live).</param>
        /// <param name="preferences">Preference service (unsubscribe).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public FeedPostEmailService(EmailService email, PropertyContext property, AppSettings settings, NotificationPreferenceService preferences, AppLogger logger)
        {
            _Email = email ?? throw new ArgumentNullException(nameof(email));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Queue the new post email for one recipient through the property's email pipeline (template feed_post: the
        /// property's brand and template overrides, suppressions, retries, and the message log), with List-Unsubscribe
        /// and List-Unsubscribe-Post: List-Unsubscribe=One-Click headers. Never throws for delivery problems.
        /// </summary>
        /// <param name="recipient">Recipient (Email required).</param>
        /// <param name="post">Post.</param>
        /// <param name="authorDisplayName">Author display name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the message was queued (the fan-out records one send per member and post first).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="IntegrationNotConfiguredException">auth.tokenEncryptionKey is empty.</exception>
        public async Task<bool> QueueAsync(FanoutRecipient recipient, NotificationSourcePost post, string authorDisplayName, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(recipient);
            ArgumentNullException.ThrowIfNull(post);
            if (String.IsNullOrWhiteSpace(recipient.Email)) return false;
            string author = String.IsNullOrWhiteSpace(authorDisplayName) ? "a member" : authorDisplayName.Trim();
            string title = String.IsNullOrWhiteSpace(post.Title) ? String.Empty : post.Title.Trim();
            string unsubscribe = UnsubscribeUrl(CreateUnsubscribeToken(recipient.UserId, NotificationCategoryEnum.FeedPosts));
            EmailRequest request = new EmailRequest
            {
                Template = FeedPostEmail.TemplateKey,
                To = recipient.Email,
                UserId = recipient.UserId,
                IdempotencyKey = "feed_post:" + post.Id + ":" + recipient.UserId
            };
            request.Tokens["memberName"] = recipient.DisplayName ?? String.Empty;
            request.Tokens["authorName"] = author;
            request.Tokens["postTitle"] = title;
            request.Tokens["headline"] = title.Length > 0 ? title : "New post from " + author;
            request.Tokens["excerpt"] = String.IsNullOrWhiteSpace(post.Excerpt) ? String.Empty : post.Excerpt.Trim();
            request.Tokens["link"] = _Property.Link("/app/feed/" + Uri.EscapeDataString(post.Id));
            request.Tokens["unsubscribeLink"] = unsubscribe;
            request.Headers["List-Unsubscribe"] = "<" + unsubscribe + ">";
            request.Headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
            using (Activity? activity = TrellisKitTelemetry.StartSpan("email.queue"))
            {
                activity?.SetTag("email.category", EmailCategory);
                try
                {
                    EmailMessageRecord? queued = await _Email.QueueAsync(request, token).ConfigureAwait(false);
                    return queued != null;
                }
                catch (Exception e) when (e is not OperationCanceledException && e is not IntegrationNotConfiguredException)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
                    _Logger.Warn("[FeedPostEmailService] feed post email not queued: " + e.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// The one-click unsubscribe URL for a token.
        /// </summary>
        /// <param name="unsubscribeToken">Token.</param>
        /// <returns>Absolute URL.</returns>
        public string UnsubscribeUrl(string unsubscribeToken)
        {
            return _Property.Link("/api/v1/email/unsubscribe?token=" + Uri.EscapeDataString(unsubscribeToken ?? String.Empty));
        }

        /// <summary>
        /// Create an unsubscribe token for a member and category.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="category">Category.</param>
        /// <returns>Token (base64url, URL safe).</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        /// <exception cref="IntegrationNotConfiguredException">auth.tokenEncryptionKey is empty.</exception>
        public string CreateUnsubscribeToken(string userId, NotificationCategoryEnum category)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            Dictionary<string, string> payload = new Dictionary<string, string> { { "u", userId }, { "c", category.ToString() } };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            return Base64Url.Encode(bytes) + "." + Base64Url.Encode(Sign(bytes));
        }

        /// <summary>
        /// Verify an unsubscribe token.
        /// </summary>
        /// <param name="unsubscribeToken">Token.</param>
        /// <param name="userId">Member when valid.</param>
        /// <param name="category">Category when valid.</param>
        /// <returns>True when the signature and payload are valid.</returns>
        /// <exception cref="IntegrationNotConfiguredException">auth.tokenEncryptionKey is empty.</exception>
        public bool TryReadUnsubscribeToken(string? unsubscribeToken, out string userId, out NotificationCategoryEnum category)
        {
            userId = String.Empty;
            category = NotificationCategoryEnum.FeedPosts;
            if (String.IsNullOrWhiteSpace(unsubscribeToken) || unsubscribeToken.Length > 1024) return false;
            string[] parts = unsubscribeToken.Trim().Split('.');
            if (parts.Length != 2) return false;
            try
            {
                byte[] payload = Base64Url.Decode(parts[0]);
                byte[] signature = Base64Url.Decode(parts[1]);
                if (!CryptographicOperations.FixedTimeEquals(signature, Sign(payload))) return false;
                using (JsonDocument doc = JsonDocument.Parse(payload))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                    if (!doc.RootElement.TryGetProperty("u", out JsonElement u) || u.ValueKind != JsonValueKind.String) return false;
                    if (!doc.RootElement.TryGetProperty("c", out JsonElement c) || c.ValueKind != JsonValueKind.String) return false;
                    NotificationCategoryEnum? parsed = NotificationCatalog.ParseCategory(c.GetString());
                    string? id = u.GetString();
                    if (parsed == null || String.IsNullOrEmpty(id)) return false;
                    userId = id;
                    category = parsed.Value;
                    return true;
                }
            }
            catch (FormatException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Apply an unsubscribe token: turn the category's email off for the member.
        /// </summary>
        /// <param name="unsubscribeToken">Token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The category turned off.</returns>
        /// <exception cref="ApiErrorException">400 InvalidToken.</exception>
        /// <exception cref="IntegrationNotConfiguredException">auth.tokenEncryptionKey is empty.</exception>
        public async Task<NotificationCategoryEnum> UnsubscribeAsync(string? unsubscribeToken, CancellationToken token = default)
        {
            if (!TryReadUnsubscribeToken(unsubscribeToken, out string userId, out NotificationCategoryEnum category) || !NotificationCatalog.EmailEditable(category))
            {
                throw new ApiErrorException(400, "InvalidToken", "The unsubscribe link is invalid.");
            }
            await _Preferences.TurnEmailOffAsync(userId, category, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(TrellisKitTelemetry.EmailUnsubscribes, 1, "category", category.ToString());
            return category;
        }

        #endregion

        #region Private-Methods

        private byte[] Sign(byte[] payload)
        {
            using (HMACSHA256 hmac = new HMACSHA256(Key()))
            {
                return hmac.ComputeHash(payload);
            }
        }

        private byte[] Key()
        {
            string configured = (_Settings.Auth.TokenEncryptionKey ?? String.Empty).Trim();
            if (configured.Length == 0) throw new IntegrationNotConfiguredException("Unsubscribe links need auth.tokenEncryptionKey.");
            byte[] material;
            try
            {
                material = Convert.FromBase64String(configured);
            }
            catch (FormatException)
            {
                material = Encoding.UTF8.GetBytes(configured);
            }
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, material, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes(UnsubscribeKeyInfo));
        }

        #endregion
    }
}
