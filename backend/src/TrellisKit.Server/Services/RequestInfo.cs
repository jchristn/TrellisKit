namespace TrellisKit.Server.Services
{
    using System;
    using System.Net;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using WatsonWebserver.Core;

    /// <summary>
    /// Caller details services need for sessions, rate limits, and audit records.
    /// </summary>
    public class RequestInfo
    {
        #region Public-Members

        /// <summary>
        /// Source IP, or null.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// User agent (truncated to 512 characters), or null.
        /// </summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>
        /// Client kind from the X-Client header.
        /// </summary>
        public ClientTypeEnum ClientType { get; set; } = ClientTypeEnum.Other;

        /// <summary>
        /// Client platform from the X-Client-Platform header (for example ios or android), trimmed and lowercased, or null.
        /// </summary>
        public string? ClientPlatform { get; set; } = null;

        /// <summary>
        /// Client app version from the X-Client-Version header (trimmed, at most 32 characters), or null.
        /// </summary>
        public string? ClientVersion { get; set; } = null;

        /// <summary>
        /// Request history id of this request (see <see cref="RequestIds"/>), or null when not built from an HTTP request.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// When the request arrived (UTC).
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Authenticated context, or null for anonymous routes.
        /// </summary>
        public RequestContext? Context { get; set; } = null;

        /// <summary>
        /// Whether the request reached the server over plain HTTP on a loopback host (cookies are then not marked Secure).
        /// </summary>
        public bool IsInsecureLoopback { get; set; } = false;

        /// <summary>
        /// Header that names the client platform (ios, android, or web).
        /// </summary>
        public const string ClientPlatformHeader = "X-Client-Platform";

        /// <summary>
        /// Header that names the client app version.
        /// </summary>
        public const string ClientVersionHeader = "X-Client-Version";

        #endregion

        #region Private-Members

        private const int _MaxUserAgent = 512;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build from an HTTP context. The source IP honors X-Forwarded-For only from trusted proxies
        /// (<see cref="RestSettings.TrustedProxies"/>; see <see cref="ClientAddressResolver"/>).
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="rest">Listener settings (TLS and trusted proxies).</param>
        /// <returns>Request info.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static RequestInfo From(HttpContextBase ctx, RestSettings rest)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(rest);
            string? agent = ctx.Request.RetrieveHeaderValue("User-Agent");
            if (agent != null && agent.Length > _MaxUserAgent) agent = agent.Substring(0, _MaxUserAgent);
            string? forwardedProto = ctx.Request.RetrieveHeaderValue("X-Forwarded-Proto");
            bool https = rest.Ssl || String.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase);
            return new RequestInfo
            {
                SourceIp = ClientAddressResolver.Resolve(ctx, rest),
                UserAgent = String.IsNullOrWhiteSpace(agent) ? null : agent,
                ClientType = ParseClient(ctx.Request.RetrieveHeaderValue(Constants.ClientHeader)),
                ClientPlatform = Platform(ctx.Request.RetrieveHeaderValue(ClientPlatformHeader)),
                ClientVersion = Platform(ctx.Request.RetrieveHeaderValue(ClientVersionHeader)),
                RequestId = RequestIds.Of(ctx),
                ReceivedUtc = ctx.Timestamp.Start,
                Context = ctx.Metadata as RequestContext,
                IsInsecureLoopback = !https && IsLoopbackHost(ctx.Request.RetrieveHeaderValue("Host"))
            };
        }

        /// <summary>
        /// Parse an X-Client header value.
        /// </summary>
        /// <param name="value">Header value, for example web or mobile.</param>
        /// <returns>Client type; Other when missing or unknown.</returns>
        public static ClientTypeEnum ParseClient(string? value)
        {
            if (String.Equals(value?.Trim(), "web", StringComparison.OrdinalIgnoreCase)) return ClientTypeEnum.Web;
            if (String.Equals(value?.Trim(), "mobile", StringComparison.OrdinalIgnoreCase)) return ClientTypeEnum.Mobile;
            return ClientTypeEnum.Other;
        }

        #endregion

        #region Private-Methods

        private static string? Platform(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string trimmed = value.Trim().ToLowerInvariant();
            return trimmed.Length > 32 ? trimmed.Substring(0, 32) : trimmed;
        }

        private static bool IsLoopbackHost(string? host)
        {
            if (String.IsNullOrWhiteSpace(host)) return false;
            string name = host.Trim();
            if (name.StartsWith("[", StringComparison.Ordinal))
            {
                int close = name.IndexOf(']');
                name = close > 0 ? name.Substring(1, close - 1) : name;
            }
            else
            {
                int colon = name.LastIndexOf(':');
                if (colon > 0) name = name.Substring(0, colon);
            }
            if (String.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(name, out IPAddress? address) && IPAddress.IsLoopback(address);
        }

        #endregion
    }
}
