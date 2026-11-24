namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Issues and validates access tokens (ITokenProtector over a small payload: session id, member id, issued, expiry,
    /// nonce) and generates opaque refresh and emailed tokens, stored only as SHA-256 hashes. Validated sessions are
    /// cached for <see cref="AuthSettings.SessionCacheSeconds"/>; revocations on this instance clear the cache at once.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class TokenService
    {
        #region Public-Members

        /// <summary>
        /// Property this service issues and accepts tokens for. Default empty.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Whether version 1 access tokens (issued before property binding, no property id) are accepted and treated
        /// as this property. True only in SingleProperty mode. Default true.
        /// </summary>
        public bool AcceptLegacyTokens { get; set; } = true;

        /// <summary>
        /// Called with the session id after <see cref="InvalidateSession"/> (revocations); the realtime module publishes
        /// session.revoked so every task closes that session's sockets. Never throws into the caller.
        /// </summary>
        public Action<string>? SessionInvalidated { get; set; } = null;

        /// <summary>
        /// Called with the member id after <see cref="InvalidateUser"/> (bans, deletion, roles, membership, flags); the
        /// realtime module publishes access.changed so every task re-validates that member's sockets.
        /// </summary>
        public Action<string>? UserInvalidated { get; set; } = null;

        /// <summary>
        /// Minimum minutes between recorded uses of a session (last used and last seen). Default 5. Minimum 0, maximum 1440.
        /// </summary>
        public int TouchIntervalMinutes
        {
            get { return _TouchIntervalMinutes; }
            set { _TouchIntervalMinutes = Math.Clamp(value, 0, 1440); }
        }

        #endregion

        #region Private-Members

        private const int _OpaqueTokenBytes = 32;
        private readonly DatabaseDriverBase _Database;
        private readonly AuthSettings _Settings;
        private readonly ITokenProtector _Protector;
        private readonly AppLogger _Logger;
        private readonly ConcurrentDictionary<string, SessionPrincipal> _Cache = new ConcurrentDictionary<string, SessionPrincipal>(StringComparer.Ordinal);
        private int _TouchIntervalMinutes = 5;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Authentication settings (live instance).</param>
        /// <param name="protector">Token protector.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public TokenService(DatabaseDriverBase database, AuthSettings settings, ITokenProtector protector, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Protector = protector ?? throw new ArgumentNullException(nameof(protector));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a random URL-safe token (256 bits).
        /// </summary>
        /// <returns>Token.</returns>
        public static string NewOpaqueToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(_OpaqueTokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// SHA-256 of a token as lowercase hex.
        /// </summary>
        /// <param name="value">Token.</param>
        /// <returns>Hash.</returns>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public static string Hash(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        /// <summary>
        /// Issue an access token for a session.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Token and expiry. The expiry never exceeds the session's.</returns>
        /// <exception cref="ArgumentNullException">The session is null.</exception>
        /// <exception cref="InvalidOperationException">The token encryption key is not configured correctly.</exception>
        public IssuedAccessToken IssueAccessToken(AuthSession session, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(session);
            DateTime expires = nowUtc.AddMinutes(_Settings.AccessTokenMinutes);
            if (expires > session.ExpiresUtc) expires = session.ExpiresUtc;
            AccessTokenPayload payload = new AccessTokenPayload
            {
                Pid = PropertyId,
                Pk = session.PrincipalKind.ToString(),
                Sid = session.Id,
                Uid = session.UserId,
                Iat = new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds(),
                Exp = new DateTimeOffset(expires).ToUnixTimeMilliseconds(),
                N = Convert.ToHexString(RandomNumberGenerator.GetBytes(8))
            };
            string token = _Protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload));
            return new IssuedAccessToken { Token = token, ExpiresUtc = DateTimeOffset.FromUnixTimeMilliseconds(payload.Exp).UtcDateTime };
        }

        /// <summary>
        /// Validate an access token: decrypt, check expiry, then check the session and member (cached briefly).
        /// </summary>
        /// <param name="token">Access token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Principal, or null when the token is invalid, expired, revoked, or the member is banned or deleted.</returns>
        public async Task<SessionPrincipal?> ValidateAccessTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrWhiteSpace(token)) return null;
            byte[]? plaintext;
            try
            {
                plaintext = _Protector.Unprotect(token);
            }
            catch (InvalidOperationException e)
            {
                _Logger.Warn("[TokenService] access token validation unavailable: " + e.Message);
                return null;
            }
            if (plaintext == null) return null;

            AccessTokenPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AccessTokenPayload>(plaintext);
            }
            catch (JsonException)
            {
                return null;
            }
            if (payload == null || String.IsNullOrEmpty(payload.Sid) || String.IsNullOrEmpty(payload.Uid)) return null;
            if (payload.V == 1)
            {
                if (!AcceptLegacyTokens) return null;
            }
            else if (payload.V != 2) return null;
            else if (!String.Equals(payload.Pid, PropertyId, StringComparison.Ordinal))
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.IsolationViolations, "kind", "cross_property_token");
                return null;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (DateTimeOffset.FromUnixTimeMilliseconds(payload.Exp).UtcDateTime <= nowUtc) return null;

            if (_Settings.SessionCacheSeconds > 0 && _Cache.TryGetValue(payload.Sid, out SessionPrincipal? cached) && cached.CachedUntilUtc > nowUtc)
            {
                return cached.UserId == payload.Uid && cached.SessionExpiresUtc > nowUtc ? cached : null;
            }

            SessionPrincipal? principal = await LoadPrincipalAsync(payload.Sid, payload.Uid, nowUtc, cancellationToken).ConfigureAwait(false);
            if (principal == null)
            {
                _Cache.TryRemove(payload.Sid, out SessionPrincipal? _);
                return null;
            }
            if (_Settings.SessionCacheSeconds > 0) _Cache[payload.Sid] = principal;
            return principal;
        }

        /// <summary>
        /// Drop a session from the cache (after revocation).
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        public void InvalidateSession(string sessionId)
        {
            if (String.IsNullOrEmpty(sessionId)) return;
            _Cache.TryRemove(sessionId, out SessionPrincipal? _);
            NotifySafely(SessionInvalidated, sessionId);
        }

        /// <summary>
        /// Drop every cached session of a member (after a ban, role, attestation, or membership change).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        public void InvalidateUser(string userId)
        {
            if (String.IsNullOrEmpty(userId)) return;
            DropCachedUser(userId);
            NotifySafely(UserInvalidated, userId);
        }

        /// <summary>
        /// Drop a session from the cache without raising <see cref="SessionInvalidated"/> (the realtime gateway
        /// re-validates sockets after a revocation event from another task).
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        public void DropCachedSession(string sessionId)
        {
            if (String.IsNullOrEmpty(sessionId)) return;
            _Cache.TryRemove(sessionId, out SessionPrincipal? _);
        }

        /// <summary>
        /// Drop every cached session of a member without raising <see cref="UserInvalidated"/>.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        public void DropCachedUser(string userId)
        {
            if (String.IsNullOrEmpty(userId)) return;
            foreach (KeyValuePair<string, SessionPrincipal> pair in _Cache.Where(p => p.Value.UserId == userId).ToList())
            {
                _Cache.TryRemove(pair.Key, out SessionPrincipal? _);
            }
        }

        /// <summary>
        /// The expiry of an access token issued by this property, without validating its session (the realtime gateway
        /// schedules auth.expiring and the expiry close from it). Null when the token does not decrypt or names another
        /// property.
        /// </summary>
        /// <param name="token">Access token.</param>
        /// <returns>UTC expiry, or null.</returns>
        public DateTime? ReadAccessTokenExpiry(string token)
        {
            if (String.IsNullOrWhiteSpace(token)) return null;
            try
            {
                byte[]? plaintext = _Protector.Unprotect(token);
                if (plaintext == null) return null;
                AccessTokenPayload? payload = JsonSerializer.Deserialize<AccessTokenPayload>(plaintext);
                if (payload == null) return null;
                if (payload.V == 2 && !String.Equals(payload.Pid, PropertyId, StringComparison.Ordinal)) return null;
                return DateTimeOffset.FromUnixTimeMilliseconds(payload.Exp).UtcDateTime;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private void NotifySafely(Action<string>? hook, string id)
        {
            if (hook == null) return;
            try
            {
                hook(id);
            }
            catch (Exception e)
            {
                _Logger.Warn("[TokenService] invalidation hook failed: " + e.Message);
            }
        }

        private async Task<SessionPrincipal?> LoadPrincipalAsync(string sessionId, string userId, DateTime nowUtc, CancellationToken token)
        {
            AuthSession? session = await _Database.Sessions.ReadAsync(sessionId, token).ConfigureAwait(false);
            if (session == null || session.UserId != userId || !session.IsActiveAt(nowUtc)) return null;
            if (session.PrincipalKind == PrincipalKindEnum.Delegated) return await LoadDelegatedPrincipalAsync(session, nowUtc, token).ConfigureAwait(false);
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsBanned || user.IsDeleted) return null;

            List<RoleAssignment> roles = await _Database.RoleAssignments.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            List<Entitlement> entitlements = await _Database.Entitlements.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            Dictionary<string, int> acceptedDocuments = await _Database.Documents.AcceptedSequencesAsync(userId, token).ConfigureAwait(false);

            if (session.LastUsedUtc < nowUtc.AddMinutes(-_TouchIntervalMinutes))
            {
                await _Database.Sessions.TouchAsync(sessionId, nowUtc, token).ConfigureAwait(false);
                await _Database.Users.TouchLastSeenAsync(userId, nowUtc, token).ConfigureAwait(false);
                // Usage metering: one activity row per member per UTC hour (docs/USAGE_METERING_CONTRACT.md section 4.4).
                await _Database.Usage.RecordActivityAsync(userId, nowUtc, token).ConfigureAwait(false);
            }

            return new SessionPrincipal
            {
                SessionId = sessionId,
                UserId = userId,
                Email = user.Email,
                DisplayName = user.DisplayName,
                IsSystemAdmin = roles.Any(r => r.Role == RoleEnum.SystemAdmin),
                IsModerator = roles.Any(r => r.Role == RoleEnum.Moderator || r.Role == RoleEnum.FeedModerator || r.Role == RoleEnum.DirectoryModerator || r.Role == RoleEnum.EventManager ||
                    r.Role == RoleEnum.LibraryEditor),
                IsSupportAgent = roles.Any(r => r.Role == RoleEnum.SupportAgent),
                IsCommunicator = roles.Any(r => r.Role == RoleEnum.Communicator),
                IsReleaseManager = roles.Any(r => r.Role == RoleEnum.ReleaseManager),
                AgeAttested = user.AgeAttestedUtc != null,
                HasMembership = entitlements.Any(e => e.IsActiveAt(nowUtc)),
                AcceptedDocuments = acceptedDocuments,
                ClientType = session.ClientType,
                SessionExpiresUtc = session.ExpiresUtc,
                CachedUntilUtc = nowUtc.AddSeconds(_Settings.SessionCacheSeconds)
            };
        }

        private async Task<SessionPrincipal?> LoadDelegatedPrincipalAsync(AuthSession session, DateTime nowUtc, CancellationToken token)
        {
            PlatformAdmin? admin = await _Database.PlatformAdmins.ReadAsync(session.UserId, token).ConfigureAwait(false);
            if (admin == null || admin.RemovedUtc != null) return null;
            return new SessionPrincipal
            {
                PrincipalKind = PrincipalKindEnum.Delegated,
                SessionId = session.Id,
                UserId = admin.Id,
                Email = admin.Email,
                DisplayName = admin.DisplayName,
                IsSystemAdmin = true,
                AgeAttested = true,
                HasMembership = true,
                ClientType = session.ClientType,
                SessionExpiresUtc = session.ExpiresUtc,
                CachedUntilUtc = nowUtc.AddSeconds(_Settings.SessionCacheSeconds)
            };
        }

        #endregion
    }
}
