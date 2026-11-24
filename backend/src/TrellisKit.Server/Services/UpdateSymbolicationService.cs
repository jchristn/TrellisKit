namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services.Symbolication;

    /// <summary>
    /// Reported launch failures of an update and server-side symbolication of stack traces with the update's private
    /// source maps (docs/LIVE_UPDATES_CONTRACT.md resolved decision 11 and the as-built notes). Source maps are read from
    /// private storage on the server only; they are never served. The most recently used decoded map is cached.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class UpdateSymbolicationService
    {
        #region Public-Members

        /// <summary>
        /// Longest stack trace the symbolication route accepts (characters).
        /// </summary>
        public const int MaxStackTraceCharacters = 65536;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly UpdateObjectStore _Store;
        private readonly EnumerationService _Enumeration;
        private readonly string _PropertyId;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private string? _CachedKey = null;
        private SourceMapDecoder? _Cached = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="store">Update storage (private source maps).</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <param name="propertyId">Property id.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UpdateSymbolicationService(DatabaseDriverBase database, AppSettings settings, UpdateObjectStore store, EnumerationService enumeration, string propertyId)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/admin/updates/{id}/failures/enumerate: reported failures with a message or stack trace (filter type).
        /// </summary>
        /// <param name="updateId">Update id (upd_).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of failures, newest first.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EnumerationResult<UpdateFailureView>> EnumerateFailuresAsync(string updateId, EnumerationQuery? query, CancellationToken token = default)
        {
            UpdateRecord update = await RequireUpdateAsync(updateId, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.UpdateFailures, true, update.Id);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            UpdateEventTypeEnum? type = request.Query.FilterEnum<UpdateEventTypeEnum>("type", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<UpdateEventDetail> page = await _Database.LiveUpdates.EnumerateEventDetailsAsync(request, update.Uuid, type, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(d => new UpdateFailureView
            {
                Id = d.EventId,
                Type = d.Type,
                ErrorCode = d.ErrorCode,
                Message = d.Message,
                HasStackTrace = !String.IsNullOrEmpty(d.StackTrace),
                OccurredUtc = d.OccurredUtc,
                ReceivedUtc = d.ReceivedUtc
            }).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/updates/{id}/symbolicate: map a pasted stack trace, or a reported failure's stack trace, through
        /// the update's private source map.
        /// </summary>
        /// <param name="updateId">Update id (upd_).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Frames and the symbolicated text.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 413 TooLarge (source map above updates.maxSourceMapMegabytes).</exception>
        public async Task<SymbolicationView> SymbolicateAsync(string updateId, SymbolicateRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            UpdateRecord update = await RequireUpdateAsync(updateId, token).ConfigureAwait(false);
            string? stack = request.StackTrace;
            if (!String.IsNullOrWhiteSpace(request.EventId))
            {
                UpdateEventDetail? detail = await _Database.LiveUpdates.ReadEventDetailAsync(request.EventId.Trim(), token).ConfigureAwait(false);
                if (detail == null || !String.Equals(detail.UpdateUuid, update.Uuid, StringComparison.OrdinalIgnoreCase)) throw ApiErrorException.NotFound("Reported failure");
                if (String.IsNullOrEmpty(detail.StackTrace)) throw ApiErrorException.Field("eventId", "That failure has no stack trace.");
                stack = detail.StackTrace;
            }
            if (String.IsNullOrWhiteSpace(stack)) throw ApiErrorException.Field("stackTrace", "Send stackTrace or eventId.");
            if (stack.Length > MaxStackTraceCharacters) throw ApiErrorException.Field("stackTrace", "At most " + MaxStackTraceCharacters + " characters.");
            List<SymbolicatedFrameView> frames = StackTraceParser.Parse(stack, out bool truncated);
            SymbolicationView view = new SymbolicationView { UpdateId = update.Id, Frames = frames, Truncated = truncated };
            UpdateSourceMap? map = await SelectMapAsync(update, token).ConfigureAwait(false);
            if (map != null)
            {
                SourceMapDecoder decoder = await LoadAsync(map, token).ConfigureAwait(false);
                List<SymbolicatedFrameView> positioned = frames.Where(f => f.Line != null && f.Column != null).ToList();
                List<OriginalPositionView?> originals = decoder.Lookup(positioned.Select(f => new[] { f.Line!.Value - 1, Math.Max(0, f.Column!.Value - 1) }).ToList());
                for (int i = 0; i < positioned.Count; i++) positioned[i].Original = originals[i];
                view.SourceMapAvailable = true;
                view.SourceMap = new SourceMapInfoView { Key = map.Key, BundleKey = map.BundleKey, SizeBytes = map.SizeBytes };
            }
            view.Text = Render(frames);
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Symbolications, 1, "outcome", map == null ? "no_source_map" : "symbolicated", "trelliskit.property", _PropertyId);
            return view;
        }

        #endregion

        #region Private-Methods

        private async Task<UpdateRecord> RequireUpdateAsync(string updateId, CancellationToken token)
        {
            return await _Database.LiveUpdates.ReadUpdateAsync(updateId ?? String.Empty, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Update");
        }

        private async Task<UpdateSourceMap?> SelectMapAsync(UpdateRecord update, CancellationToken token)
        {
            List<UpdateSourceMap> maps = await _Database.LiveUpdates.ListSourceMapsAsync(update.Id, 50, token).ConfigureAwait(false);
            if (maps.Count <= 1) return maps.FirstOrDefault();
            List<UpdateAssetRef> refs = await _Database.LiveUpdates.ListAssetRefsAsync(update.Id, _Settings.Updates.MaxAssetsPerUpdate, token).ConfigureAwait(false);
            string? launchKey = refs.FirstOrDefault(r => r.IsLaunchAsset)?.Key;
            return maps.FirstOrDefault(m => launchKey != null && String.Equals(m.BundleKey, launchKey, StringComparison.OrdinalIgnoreCase)) ?? maps[0];
        }

        private async Task<SourceMapDecoder> LoadAsync(UpdateSourceMap map, CancellationToken token)
        {
            string key = String.IsNullOrEmpty(map.StorageKey) ? UpdateObjectStore.SourceMapKey(_PropertyId, map.Sha256Hex) : map.StorageKey;
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Cached != null && _CachedKey == key) return _Cached;
                ObjectHead head = await _Store.HeadAsync(key, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Source map object");
                long max = (long)_Settings.Updates.MaxSourceMapMegabytes * 1024 * 1024;
                if (head.SizeBytes > max) throw new ApiErrorException(413, "TooLarge", "The source map is larger than " + _Settings.Updates.MaxSourceMapMegabytes + " MB (updates.maxSourceMapMegabytes).");
                byte[] bytes = head.SizeBytes == 0 ? Array.Empty<byte>() : await _Store.ReadRangeAsync(key, 0, (int)head.SizeBytes, token).ConfigureAwait(false);
                SourceMapDecoder decoder;
                try
                {
                    decoder = SourceMapDecoder.Parse(bytes);
                }
                catch (FormatException e)
                {
                    throw new ApiErrorException(422, "SourceMapInvalid", "The stored source map cannot be read: " + e.Message);
                }
                _Cached = decoder;
                _CachedKey = key;
                return decoder;
            }
            finally
            {
                _Lock.Release();
            }
        }

        private static string Render(List<SymbolicatedFrameView> frames)
        {
            StringBuilder builder = new StringBuilder();
            foreach (SymbolicatedFrameView f in frames)
            {
                if (f.Original == null) builder.Append(f.Raw);
                else builder.Append("    at ").Append(f.Original.Name ?? f.Function ?? "<anonymous>").Append(" (").Append(f.Original.Source).Append(':').Append(f.Original.Line).Append(':').Append(f.Original.Column).Append(')');
                builder.Append('\n');
            }
            return builder.ToString().TrimEnd('\n');
        }

        #endregion
    }
}
