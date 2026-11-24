namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Push token registration (Phase 2 contract 8.7). Tokens are bound to the registering session; revoking that
    /// session deletes them (in the session data access). Registering an existing token moves it to the caller and the
    /// caller's session. At most <see cref="MaxTokensPerMember"/> per member: registering one more deletes the least
    /// recently seen. Token values are never returned.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class PushTokenService
    {
        #region Public-Members

        /// <summary>
        /// Tokens kept per member. Default 10. Minimum 1, maximum 100.
        /// </summary>
        public int MaxTokensPerMember
        {
            get { return _MaxTokensPerMember; }
            set { _MaxTokensPerMember = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Clock. Default DateTime.UtcNow. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _TokenPattern = new Regex("^Expo(nent)?PushToken\\[[A-Za-z0-9_-]+\\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private int _MaxTokensPerMember = 10;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PushTokenService(DatabaseDriverBase database, EnumerationService enumeration, MetadataService metadata)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register or refresh a token for the caller's session.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="sessionId">The caller's session.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The token and whether it was new.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed; 403 Forbidden without a session.</exception>
        public async Task<PushTokenRegistration> RegisterAsync(string userId, string? sessionId, PushTokenRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            if (String.IsNullOrEmpty(sessionId)) throw ApiErrorException.Forbidden("Push tokens need a member session.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string value = (request.Token ?? String.Empty).Trim();
            if (value.Length == 0 || value.Length > 255 || !_TokenPattern.IsMatch(value)) errors["token"] = "Must be an Expo push token, for example ExponentPushToken[xxxx].";
            PushPlatformEnum platform = PushPlatformEnum.Ios;
            if (String.IsNullOrWhiteSpace(request.Platform) || Int32.TryParse(request.Platform, out int _) || !Enum.TryParse(request.Platform.Trim(), true, out platform) || !Enum.IsDefined(platform))
                errors["platform"] = "Use Ios or Android.";
            string? deviceId = Optional(request.DeviceId, 200, "deviceId", errors);
            string? deviceName = Optional(request.DeviceName, 100, "deviceName", errors);
            string? appVersion = Optional(request.AppVersion, 50, "appVersion", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            using (Activity? activity = TrellisKitTelemetry.StartSpan("push.tokens.register"))
            {
                DateTime now = _Clock();
                PushToken candidate = new PushToken
                {
                    Id = IdGenerator.Generate(Constants.PushTokenIdPrefix),
                    UserId = userId,
                    SessionId = sessionId,
                    Token = value,
                    Platform = platform,
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    AppVersion = appVersion,
                    CreatedUtc = now,
                    LastSeenUtc = now
                };
                PushToken stored = await _Database.PushTokens.UpsertAsync(candidate, token).ConfigureAwait(false);
                bool created = String.Equals(stored.Id, candidate.Id, StringComparison.Ordinal);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.PushTokens, 1, "action", created ? "registered" : "refreshed");
                int trimmed = await _Database.PushTokens.TrimAsync(userId, _MaxTokensPerMember, token).ConfigureAwait(false);
                if (trimmed > 0) TrellisKitTelemetry.Add(TrellisKitTelemetry.PushTokens, trimmed, "action", "deleted");
                activity?.SetTag("outcome", created ? "registered" : "refreshed");
                PushTokenView view = ToView(stored);
                if (!created)
                {
                    ObjectMetadata meta = await _Metadata.LoadOneAsync(TargetTypeEnum.PushToken, stored.Id, false, token).ConfigureAwait(false);
                    view.Labels = meta.Labels;
                    view.Tags = meta.Tags;
                }
                return new PushTokenRegistration { Token = view, Created = created };
            }
        }

        /// <summary>
        /// Delete one of the caller's tokens by value. Unknown values are ignored.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="value">Token value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a token was deleted.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<bool> DeleteAsync(string userId, string? value, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(value)) return false;
            bool deleted = await _Database.PushTokens.DeleteByValueAsync(userId, value.Trim(), token).ConfigureAwait(false);
            if (deleted) TrellisKitTelemetry.Add(TrellisKitTelemetry.PushTokens, 1, "action", "deleted");
            return deleted;
        }

        /// <summary>
        /// Enumerate the caller's tokens (POST /api/v1/me/push-tokens/enumerate and GET /api/v1/me/push-tokens):
        /// collection <see cref="NotificationCollections.PushTokens"/>, at most 10 exist. Token values are never returned.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query, or null for defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<PushTokenView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            bool staff = actor.IsStaffFor(TargetTypeEnum.PushToken);
            EnumerationRequest request = _Enumeration.Prepare(query, NotificationCollections.PushTokens, staff, "push-tokens|" + userId);
            EnumerationResult<PushToken> page = await _Database.PushTokens.EnumerateForUserAsync(userId, request, token).ConfigureAwait(false);
            List<PushTokenView> views = page.Objects.Select(ToView).ToList();
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.PushToken, views.Select(v => v.Id), staff, token).ConfigureAwait(false);
            foreach (PushTokenView view in views)
            {
                if (!metadata.TryGetValue(view.Id, out ObjectMetadata? meta)) continue;
                view.Labels = meta.Labels;
                view.Tags = meta.Tags;
            }
            return page.With(views);
        }

        /// <summary>
        /// The API view of a token (never the value).
        /// </summary>
        /// <param name="pushToken">Token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The token is null.</exception>
        public static PushTokenView ToView(PushToken pushToken)
        {
            ArgumentNullException.ThrowIfNull(pushToken);
            return new PushTokenView
            {
                Id = pushToken.Id,
                Platform = pushToken.Platform,
                DeviceName = pushToken.DeviceName,
                AppVersion = pushToken.AppVersion,
                CreatedUtc = pushToken.CreatedUtc,
                LastSeenUtc = pushToken.LastSeenUtc
            };
        }

        #endregion

        #region Private-Methods

        private static string? Optional(string? value, int max, string field, Dictionary<string, string> errors)
        {
            if (value == null) return null;
            string trimmed = value.Trim();
            if (trimmed.Length == 0) return null;
            if (trimmed.Length > max) errors[field] = "At most " + max + " characters.";
            return trimmed;
        }

        #endregion
    }
}
