namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Creates sessions, rotates refresh tokens with reuse detection (presenting a used refresh token revokes the whole
    /// session), signs out, and revokes sessions.
    /// Thread safety: safe to call concurrently; rotation is guarded by an atomic conditional update.
    /// </summary>
    public class SessionService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuthSettings _Settings;
        private readonly TokenService _Tokens;
        private readonly MemberViewBuilder _Views;
        private readonly AppLogger _Logger;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Authentication settings (live instance).</param>
        /// <param name="tokens">Token service.</param>
        /// <param name="views">View builder.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SessionService(DatabaseDriverBase database, AuthSettings settings, TokenService tokens, MemberViewBuilder views, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Views = views ?? throw new ArgumentNullException(nameof(views));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a session for a member and issue its first access and refresh tokens.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="request">Request info (client, IP, user agent).</param>
        /// <param name="isNewAccount">Whether the account was just created.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Issued session.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<IssuedSession> CreateAsync(User user, RequestInfo request, bool isNewAccount, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(request);
            DateTime now = DateTime.UtcNow;
            AuthSession session = new AuthSession
            {
                Id = IdGenerator.GenerateSessionId(),
                UserId = user.Id,
                ClientType = request.ClientType,
                SourceIp = request.SourceIp,
                UserAgent = request.UserAgent,
                CreatedUtc = now,
                LastUsedUtc = now,
                ExpiresUtc = now.AddDays(_Settings.RefreshTokenDays)
            };
            await _Database.Sessions.CreateAsync(session, token).ConfigureAwait(false);
            string raw = await NewRefreshTokenAsync(session, now, null, token).ConfigureAwait(false);
            return await IssueAsync(user, session, raw, isNewAccount, now, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Rotate a refresh token. Reusing an already rotated token revokes the session, except when the token was
        /// rotated within auth.refreshReuseGraceSeconds (a concurrent refresh race from the same client): the late request gets 401 and the
        /// winner's new tokens stay valid.
        /// </summary>
        /// <param name="rawRefreshToken">Refresh token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Issued session with a new refresh token.</returns>
        /// <exception cref="ApiErrorException">401 InvalidRefreshToken when the token is unknown, expired, reused, or its session is revoked.</exception>
        public async Task<IssuedSession> RefreshAsync(string? rawRefreshToken, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("auth.refresh"))
            {
                DateTime now = DateTime.UtcNow;
                if (String.IsNullOrWhiteSpace(rawRefreshToken)) throw Invalid("missing");

                RefreshToken? stored = await _Database.RefreshTokens.ReadByHashAsync(TokenService.Hash(rawRefreshToken.Trim()), token).ConfigureAwait(false);
                if (stored == null) throw Invalid("unknown");
                if (stored.UsedUtc != null)
                {
                    if (IsRace(stored.UsedUtc.Value, now)) throw Race();
                    await RevokeForReuseAsync(stored, now, token).ConfigureAwait(false);
                    throw Invalid("reused");
                }
                if (stored.ExpiresUtc <= now) throw Invalid("expired");

                AuthSession? session = await _Database.Sessions.ReadAsync(stored.SessionId, token).ConfigureAwait(false);
                if (session == null || !session.IsActiveAt(now)) throw Invalid("session_inactive");
                User? user = await _Database.Users.ReadAsync(session.UserId, token).ConfigureAwait(false);
                if (user == null || user.IsBanned || user.IsDeleted)
                {
                    await _Database.Sessions.RevokeAsync(session.Id, "account unavailable", now, token).ConfigureAwait(false);
                    _Tokens.InvalidateSession(session.Id);
                    throw Invalid("account_unavailable");
                }

                string newId = IdGenerator.GenerateRefreshTokenId();
                if (!await _Database.RefreshTokens.TryMarkUsedAsync(stored.Id, newId, now, token).ConfigureAwait(false))
                {
                    // Another request claimed this token between our read and the conditional update: a race, not a replay.
                    throw Race();
                }

                string raw = await NewRefreshTokenAsync(session, now, newId, token).ConfigureAwait(false);
                await _Database.Sessions.TouchAsync(session.Id, now, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.TokenRefreshes, "outcome", "success");
                activity?.SetTag("outcome", "success");
                return await IssueAsync(user, session, raw, false, now, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sign out the session of a refresh token. Unknown tokens are ignored.
        /// </summary>
        /// <param name="rawRefreshToken">Refresh token, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task LogoutAsync(string? rawRefreshToken, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(rawRefreshToken)) return;
            RefreshToken? stored = await _Database.RefreshTokens.ReadByHashAsync(TokenService.Hash(rawRefreshToken.Trim()), token).ConfigureAwait(false);
            if (stored == null) return;
            await _Database.Sessions.RevokeAsync(stored.SessionId, "signed out", DateTime.UtcNow, token).ConfigureAwait(false);
            _Tokens.InvalidateSession(stored.SessionId);
        }

        /// <summary>
        /// Revoke a member's sessions.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="exceptSessionId">Session to keep, or null.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sessions revoked.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        public async Task<int> RevokeAllAsync(string userId, string? exceptSessionId, string reason, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            List<string> revoked = await _Database.Sessions.RevokeAllForUserAsync(userId, exceptSessionId, reason, DateTime.UtcNow, token).ConfigureAwait(false);
            foreach (string id in revoked) _Tokens.InvalidateSession(id);
            _Tokens.InvalidateUser(userId);
            if (exceptSessionId != null) _Tokens.InvalidateSession(exceptSessionId);
            return revoked.Count;
        }

        #endregion

        #region Private-Methods

        private async Task<string> NewRefreshTokenAsync(AuthSession session, DateTime now, string? id, CancellationToken token)
        {
            string raw = TokenService.NewOpaqueToken();
            await _Database.RefreshTokens.CreateAsync(new RefreshToken
            {
                Id = id ?? IdGenerator.GenerateRefreshTokenId(),
                SessionId = session.Id,
                UserId = session.UserId,
                TokenHash = TokenService.Hash(raw),
                CreatedUtc = now,
                ExpiresUtc = session.ExpiresUtc
            }, token).ConfigureAwait(false);
            return raw;
        }

        private async Task<IssuedSession> IssueAsync(User user, AuthSession session, string rawRefresh, bool isNewAccount, DateTime now, CancellationToken token)
        {
            IssuedAccessToken access = _Tokens.IssueAccessToken(session, now);
            MeResponse me = await _Views.BuildMeAsync(user, token).ConfigureAwait(false);
            return new IssuedSession
            {
                Response = new AuthResponse
                {
                    AccessToken = access.Token,
                    AccessTokenExpiresUtc = access.ExpiresUtc,
                    RefreshToken = rawRefresh,
                    IsNewAccount = isNewAccount,
                    User = me
                },
                RefreshToken = rawRefresh,
                RefreshExpiresUtc = session.ExpiresUtc,
                ClientType = session.ClientType
            };
        }

        private async Task RevokeForReuseAsync(RefreshToken stored, DateTime now, CancellationToken token)
        {
            if (await _Database.Sessions.RevokeAsync(stored.SessionId, "refresh token reuse", now, token).ConfigureAwait(false))
            {
                _Logger.Warn("[SessionService] refresh token reuse detected; revoked session " + stored.SessionId);
            }
            _Tokens.InvalidateSession(stored.SessionId);
        }

        private bool IsRace(DateTime usedUtc, DateTime now)
        {
            DateTime used = usedUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(usedUtc, DateTimeKind.Utc) : usedUtc.ToUniversalTime();
            return now - used < TimeSpan.FromSeconds(_Settings.RefreshReuseGraceSeconds);
        }

        private static RefreshRaceException Race()
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.TokenRefreshes, "outcome", "race");
            return new RefreshRaceException("The refresh token was just used by a concurrent request. Use the newest token.");
        }

        private static ApiErrorException Invalid(string outcome)
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.TokenRefreshes, "outcome", outcome);
            return new ApiErrorException(401, "InvalidRefreshToken", "The refresh token is invalid or expired. Sign in again.");
        }

        #endregion
    }
}
