namespace TrellisKit.Server.Services.Usage
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Platform usage (docs/USAGE_METERING_CONTRACT.md section 7.2): any property's usage service, and the bounded
    /// cross-property comparison. Thread safety: stateless apart from shared services; safe to call concurrently.
    /// </summary>
    public class PlatformUsageService
    {
        #region Public-Members

        /// <summary>
        /// Most registry rows a computed comparison ordering reads. 500.
        /// </summary>
        public const int ScanLimit = 500;

        #endregion

        #region Private-Members

        private static readonly string[] _ComparisonMeters = new[]
        {
            "users.total", "users.new", "users.active", "posts.new", "comments.new", "interactions.new", "images.new", "images.bytes", "videos.new", "videos.bytes",
            "storage.total", "api.requests", "push.sent", "email.sent", "ai.promptTokens", "ai.completionTokens", "ai.costMicros"
        };

        private readonly PlatformService _Platform;
        private readonly AppSettings _Settings;
        private readonly AppLogger _Logger;
        private readonly EnumerationService _Enumeration;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="platform">Platform service (registry, directory, realm metadata).</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PlatformUsageService(PlatformService platform, AppSettings settings, AppLogger logger)
        {
            _Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _Enumeration = new EnumerationService(settings) { PropertyId = PropertyIds.Platform };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The usage service of a registered property (its running one, or a read-only one for a property without a
        /// runtime).
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record and service.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<PlatformUsageTarget> ForAsync(string propertyId, CancellationToken token = default)
        {
            PropertyRecord? record = PropertyIds.IsValid(propertyId) ? await _Platform.Directory.Database.Registry.ReadAsync(propertyId, token).ConfigureAwait(false) : null;
            if (record == null || record.Status == PropertyStatusEnum.Deleted) throw ApiErrorException.NotFound("Property");
            return new PlatformUsageTarget { Record = record, Usage = Service(record.Id) };
        }

        /// <summary>
        /// POST /api/v1/platform/usage/properties/enumerate.
        /// </summary>
        /// <param name="query">Query; filters period, from, to, status.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<PropertyUsageRow>> EnumerateAsync(EnumerationQuery? query, DateTime nowUtc, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, UsageCollections.PropertyUsage, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            PropertyStatusEnum? status = request.Query.FilterEnum<PropertyStatusEnum>("status", errors);
            UsageQuery usageQuery = new UsageQuery { Period = request.Query.FilterString("period"), From = request.Query.FilterString("from"), To = request.Query.FilterString("to"), Bucket = "Month" };
            UsagePeriodCalculator.ParsePeriod(usageQuery.Period, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors.ToDictionary(e => "filters." + e.Key, e => e.Value), "One or more filters are invalid.");
            string ordering = request.Ordering.Name;
            bool computed = UsageCollections.ComparisonOrderings.Contains(ordering);
            EnumerationRequest source = request;
            if (computed)
            {
                source = new EnumerationRequest
                {
                    Collection = request.Collection,
                    Query = request.Query,
                    MaxResults = ScanLimit,
                    Skip = 0,
                    Ordering = new EnumerationOrdering { Name = "CreatedDescending", Expression = "p.createdutc", Descending = true, KeyKind = EnumerationKeyKindEnum.Time },
                    Fingerprint = request.Fingerprint,
                    CountTotals = true,
                    IncludeStaffMetadata = request.IncludeStaffMetadata
                };
            }
            EnumerationResult<PropertyRecord> page = await _Platform.Directory.Database.Registry.EnumerateAsync(source, status, null, token).ConfigureAwait(false);
            if (computed && !page.EndOfResults) throw ApiErrorException.Field("search", "Narrow the property selection to at most " + ScanLimit + " matching properties.");
            Dictionary<string, ObjectMetadata> metadata = await _Platform.RealmMetadata.LoadAsync(TargetTypeEnum.Property, page.Objects.Select(p => p.Id), true, token).ConfigureAwait(false);
            List<UsageMeter> meters = _ComparisonMeters.Select(id => UsageMeters.Find(id)!).ToList();
            List<PropertyUsageRow> rows = new List<PropertyUsageRow>();
            foreach (PropertyRecord record in page.Objects)
            {
                PropertyUsageRow row = new PropertyUsageRow { PropertyId = record.Id, Slug = record.Slug, Name = record.Name, Status = record.Status };
                if (record.Status != PropertyStatusEnum.Deleted)
                {
                    UsageService usage = Service(record.Id);
                    UsageZone zone = await usage.ZoneAsync(token).ConfigureAwait(false);
                    UsageWindow window = usage.ResolveWindow(usageQuery, nowUtc, zone, out List<UsageMeter> _);
                    Dictionary<string, UsageTotal> totals = await usage.TotalsAsync(window.FromUtc, window.ToUtc, meters, zone, nowUtc, token).ConfigureAwait(false);
                    UsageState? state = await _Platform.Directory.Database.ForProperty(record.Id).Usage.ReadStateAsync(token).ConfigureAwait(false);
                    row.Period = window.Period;
                    row.FromUtc = window.FromUtc;
                    row.ToUtc = window.ToUtc;
                    row.UsersTotal = totals["users.total"].Value;
                    row.UsersNew = totals["users.new"].Value;
                    row.ActiveUsers = totals["users.active"].Value;
                    row.Posts = totals["posts.new"].Value;
                    row.Comments = totals["comments.new"].Value;
                    row.Interactions = totals["interactions.new"].Value;
                    row.Images = totals["images.new"].Value;
                    row.ImageBytes = totals["images.bytes"].Value;
                    row.Videos = totals["videos.new"].Value;
                    row.VideoBytes = totals["videos.bytes"].Value;
                    row.StorageBytes = totals["storage.total"].Value;
                    row.ApiRequests = totals["api.requests"].Value;
                    row.PushSent = totals["push.sent"].Value;
                    row.EmailSent = totals["email.sent"].Value;
                    row.AiTokens = totals["ai.promptTokens"].Value + totals["ai.completionTokens"].Value;
                    row.AiCostMicros = totals["ai.costMicros"].Value;
                    row.DataThroughUtc = state?.LastRollupUtc;
                }
                if (metadata.TryGetValue(record.Id, out ObjectMetadata? meta))
                {
                    row.Labels = meta.Labels;
                    row.Tags = meta.Tags;
                }
                rows.Add(row);
            }
            if (!computed) return page.With(rows);
            Func<PropertyUsageRow, long> key = KeyOf(ordering);
            Dictionary<string, DateTime> created = page.Objects.ToDictionary(p => p.Id, p => p.CreatedUtc, StringComparer.Ordinal);
            return CatalogEnumeration.Page(request, rows, row => row.PropertyId, row => created[row.PropertyId],
                row => row.PropertyId + " " + row.Slug + " " + row.Name, row => row.Labels, row => row.Tags,
                row => key(row).ToString(CultureInfo.InvariantCulture));
        }

        #endregion

        #region Private-Methods

        private UsageService Service(string propertyId)
        {
            PropertyRuntime? runtime = _Platform.Directory.Find(propertyId);
            if (runtime != null) return runtime.Services.Usage;
            DatabaseDriverBase bound = _Platform.Directory.Database.ForProperty(propertyId);
            EnumerationService enumeration = new EnumerationService(_Settings) { PropertyId = propertyId };
            return new UsageService(bound, _Settings, null, new MetadataService(bound, enumeration), enumeration, _Logger);
        }

        private static Func<PropertyUsageRow, long> KeyOf(string ordering)
        {
            switch (ordering)
            {
                case "UsersDescending": return r => r.UsersTotal;
                case "ActiveUsersDescending": return r => r.ActiveUsers;
                case "PostsDescending": return r => r.Posts;
                case "CommentsDescending": return r => r.Comments;
                case "InteractionsDescending": return r => r.Interactions;
                case "ImagesDescending": return r => r.Images;
                case "VideosDescending": return r => r.Videos;
                case "StorageDescending": return r => r.StorageBytes;
                case "ApiRequestsDescending": return r => r.ApiRequests;
                case "PushDescending": return r => r.PushSent;
                case "EmailDescending": return r => r.EmailSent;
                case "AiTokensDescending": return r => r.AiTokens;
                default: return r => r.AiCostMicros;
            }
        }

        #endregion
    }
}
