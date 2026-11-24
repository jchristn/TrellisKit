namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Anonymous visitors of public assistants (docs/AI_CONTRACT.md section 13.4): the first anonymous use creates a
    /// visitor and an opaque token (AES-GCM, the access token key family) bound to the visitor and property; later calls
    /// send it as X-TK-Visitor-Token. New visitors are limited per IP per hour; IPs are stored only as a keyed hash.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AssistantVisitorService
    {
        #region Private-Members

        private const string _Version = "tkv1";
        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AssistantVisitorService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the visitor of a token, or null when the token is missing, invalid, or for another property.
        /// </summary>
        /// <param name="visitorToken">Token, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Visitor, or null.</returns>
        public async Task<AssistantVisitor?> ResolveAsync(string? visitorToken, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(visitorToken)) return null;
            byte[]? payload = _Ai.App.Integrations.TokenProtector.Unprotect(visitorToken.Trim());
            if (payload == null) return null;
            string[] parts = Encoding.UTF8.GetString(payload).Split('|');
            if (parts.Length != 4 || parts[0] != _Version || parts[2] != _Ai.App.PropertyId) return null;
            AssistantVisitor? visitor = await _Ai.App.Database.Ai.Conversations.ReadVisitorAsync(parts[1], token).ConfigureAwait(false);
            if (visitor == null) return null;
            if (visitor.LastSeenUtc < DateTime.UtcNow.AddMinutes(-10)) await _Ai.App.Database.Ai.Conversations.TouchVisitorAsync(visitor.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            return visitor;
        }

        /// <summary>
        /// Create a visitor (rate limited by rateLimits.aiNewVisitorsPerIpPerHour).
        /// </summary>
        /// <param name="sourceIp">Client IP.</param>
        /// <param name="surface">Surface of first use.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Visitor id and token.</returns>
        /// <exception cref="Core.Exceptions.ApiErrorException">429 RateLimited.</exception>
        public async Task<VisitorSessionView> CreateAsync(string? sourceIp, AiConversationSurfaceEnum surface, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            _Ai.App.Limits.EnforceIp(sourceIp, "ai-new-visitors", _Ai.App.Settings.Ai.NewVisitorsPerIpPerHour, RateLimitService.Hour);
            DateTime now = DateTime.UtcNow;
            AssistantVisitor visitor = new AssistantVisitor
            {
                Id = IdGenerator.Generate(AiConstants.VisitorIdPrefix),
                CreatedUtc = now,
                LastSeenUtc = now,
                IpHash = HashIp(sourceIp),
                Surface = surface
            };
            await _Ai.App.Database.Ai.Conversations.CreateVisitorAsync(visitor, token).ConfigureAwait(false);
            string payload = _Version + "|" + visitor.Id + "|" + _Ai.App.PropertyId + "|" + now.Ticks.ToString(CultureInfo.InvariantCulture);
            return new VisitorSessionView { VisitorId = visitor.Id, VisitorToken = _Ai.App.Integrations.TokenProtector.Protect(Encoding.UTF8.GetBytes(payload)) };
        }

        #endregion

        #region Private-Methods

        private string? HashIp(string? ip)
        {
            if (String.IsNullOrEmpty(ip)) return null;
            byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("tk-visitor-ip|" + _Ai.App.PropertyId + "|" + (_Ai.App.Settings.Auth.TokenEncryptionKey ?? String.Empty)));
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(ip))).Substring(0, 32).ToLowerInvariant();
        }

        #endregion
    }
}
