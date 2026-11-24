namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Broadcast segments (docs/PHASE5_CONTRACT.md section 8.1): filter validation and compilation, saved segment CRUD,
    /// and the audience preview. Filters are stored one row per value (never JSON) and combined with AND. Profile
    /// filters reuse the member directory operators (ProfileValueService.CompileFiltersAsync, staff visibility).
    /// Recipients are always limited to accounts that are age attested, not banned, and not deleted, and in FullyGated to
    /// members and staff (docs/ACCESS_MODELS_CONTRACT.md section 7).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class BroadcastSegmentService
    {
        #region Public-Members

        /// <summary>
        /// The error code of an invalid segment.
        /// </summary>
        public const string SegmentInvalid = "SegmentInvalid";

        /// <summary>
        /// Most filters per segment. Default 20. Minimum 1, maximum 50.
        /// </summary>
        public int MaxFilters
        {
            get { return _MaxFilters; }
            set { _MaxFilters = Math.Clamp(value, 1, 50); }
        }

        /// <summary>
        /// Most values in one list filter. Default 20. Minimum 1, maximum 100.
        /// </summary>
        public int MaxValues
        {
            get { return _MaxValues; }
            set { _MaxValues = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Members in a preview sample. Default 10. Minimum 1, maximum 10.
        /// </summary>
        public int SampleSize
        {
            get { return _SampleSize; }
            set { _SampleSize = Math.Clamp(value, 1, 10); }
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

        private static readonly string[] _Fields =
        {
            "membership", "membershipSource", "store", "labels", "signedUpAfterUtc", "signedUpBeforeUtc", "activeWithinDays", "inactiveForDays",
            "hasPushToken", "hasVerifiedEmail", "role", "eventRsvp", "seriesFollowers"
        };

        private static readonly string[] _RsvpStatuses = { "Going", "Interested", "Waitlisted" };

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private int _MaxFilters = 20;
        private int _MaxValues = 20;
        private int _SampleSize = 10;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public BroadcastSegmentService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate filters as sent and turn them into rows (one per value).
        /// </summary>
        /// <param name="filters">Filters, or null for none.</param>
        /// <param name="prefix">Field error prefix, for example segment.filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ApiErrorException">400 SegmentInvalid with fieldErrors.</exception>
        public async Task<List<BroadcastSegmentFilter>> ValidateAsync(List<BroadcastFilterSpec>? filters, string prefix, CancellationToken token = default)
        {
            List<BroadcastFilterSpec> list = filters ?? new List<BroadcastFilterSpec>();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (list.Count > _MaxFilters)
                throw new ApiErrorException(400, SegmentInvalid, "A segment has at most " + _MaxFilters + " filters.", new Dictionary<string, string> { { prefix, "Use at most " + _MaxFilters + " filters." } });
            List<BroadcastSegmentFilter> rows = new List<BroadcastSegmentFilter>();
            for (int i = 0; i < list.Count; i++)
            {
                string at = prefix + "[" + i + "]";
                BroadcastFilterSpec? spec = list[i];
                if (spec == null || String.IsNullOrWhiteSpace(spec.Field))
                {
                    errors[at + ".field"] = "Required.";
                    continue;
                }
                if (String.IsNullOrWhiteSpace(spec.Operator))
                {
                    errors[at + ".operator"] = "Required.";
                    continue;
                }
                JsonElement? value = spec.Value;
                if (value == null || value.Value.ValueKind == JsonValueKind.Null || value.Value.ValueKind == JsonValueKind.Undefined)
                {
                    errors[at + ".value"] = "Required.";
                    continue;
                }
                string field = spec.Field.Trim();
                string op = spec.Operator.Trim();
                JsonElement v = value.Value;
                switch (v.ValueKind)
                {
                    case JsonValueKind.String:
                        rows.Add(Row(field, op, v.GetString() ?? String.Empty, "String", i));
                        break;
                    case JsonValueKind.Number:
                        rows.Add(Row(field, op, v.GetRawText(), "Number", i));
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        rows.Add(Row(field, op, v.GetBoolean() ? "true" : "false", "Boolean", i));
                        break;
                    case JsonValueKind.Array:
                        List<JsonElement> items = v.EnumerateArray().ToList();
                        if (items.Count == 0 || items.Count > _MaxValues || items.Any(x => x.ValueKind != JsonValueKind.String))
                        {
                            errors[at + ".value"] = "Use 1 to " + _MaxValues + " strings.";
                            break;
                        }
                        foreach (JsonElement item in items) rows.Add(Row(field, op, item.GetString() ?? String.Empty, "List", i));
                        break;
                    default:
                        errors[at + ".value"] = "Use a string, number, boolean, or array of strings.";
                        break;
                }
            }
            if (errors.Count == 0) await CompileCoreAsync(rows, prefix, errors, token).ConfigureAwait(false);
            if (errors.Count > 0) throw new ApiErrorException(400, SegmentInvalid, "One or more segment filters are invalid.", errors);
            return rows;
        }

        /// <summary>
        /// Compile stored rows into an audience (the access model's base rules included).
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Audience.</returns>
        /// <exception cref="ApiErrorException">400 SegmentInvalid when a stored filter no longer applies (for example a deleted profile field).</exception>
        public async Task<BroadcastAudience> CompileAsync(List<BroadcastSegmentFilter> rows, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(rows);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            BroadcastAudience audience = await CompileCoreAsync(rows, "segment.filters", errors, token).ConfigureAwait(false);
            if (errors.Count > 0) throw new ApiErrorException(400, SegmentInvalid, "One or more segment filters no longer apply.", errors);
            return audience;
        }

        /// <summary>
        /// Count an audience.
        /// </summary>
        /// <param name="audience">Audience.</param>
        /// <param name="essential">Whether the Announcements preferences are ignored.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        public Task<BroadcastAudienceCounts> CountAsync(BroadcastAudience audience, bool essential, CancellationToken token = default)
        {
            return _Services.Database.Broadcasts.CountAudienceAsync(audience, essential, _Clock(), token);
        }

        /// <summary>
        /// The filters of stored rows as sent (grouped by position; list values as arrays).
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <returns>Filters.</returns>
        public static List<BroadcastFilterSpec> Specs(List<BroadcastSegmentFilter> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            List<BroadcastFilterSpec> specs = new List<BroadcastFilterSpec>();
            foreach (IGrouping<int, BroadcastSegmentFilter> group in rows.GroupBy(r => r.Position).OrderBy(g => g.Key))
            {
                BroadcastSegmentFilter first = group.First();
                JsonElement value;
                switch (first.Kind)
                {
                    case "List":
                        value = JsonSerializer.SerializeToElement(group.Select(r => r.Value).ToList());
                        break;
                    case "Number":
                        value = Double.TryParse(first.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? JsonSerializer.SerializeToElement(number) : JsonSerializer.SerializeToElement(first.Value);
                        break;
                    case "Boolean":
                        value = JsonSerializer.SerializeToElement(first.Value == "true");
                        break;
                    default:
                        value = JsonSerializer.SerializeToElement(first.Value);
                        break;
                }
                specs.Add(new BroadcastFilterSpec { Field = first.Field, Operator = first.Operator, Value = value });
            }
            return specs;
        }

        /// <summary>
        /// Copy rows with new identifiers (a saved segment copied into a broadcast).
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <returns>Copies.</returns>
        public static List<BroadcastSegmentFilter> Copy(List<BroadcastSegmentFilter> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            return rows.Select(r => Row(r.Field, r.Operator, r.Value, r.Kind, r.Position)).ToList();
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/segments.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or Communicator).</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The segment.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or SegmentInvalid, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<BroadcastSegmentView> CreateAsync(ContentActor actor, RequestInfo info, BroadcastSegmentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            BroadcastService.RequireBroadcaster(_Services, actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string name = Text(request.Name, "name", 1, 100, errors) ?? String.Empty;
            string? description = Text(request.Description, "description", 0, 500, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<BroadcastSegmentFilter> rows = await ValidateAsync(request.Filters, "filters", token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.BroadcastSegment), token, TargetTypeEnum.BroadcastSegment).ConfigureAwait(false);
            DateTime now = _Clock();
            BroadcastSegment segment = new BroadcastSegment
            {
                Id = IdGenerator.Generate(BroadcastIdPrefixes.Segment),
                Name = name,
                Description = description,
                CreatedByUserId = actor.UserId,
                Filters = rows,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            await _Services.Database.Broadcasts.CreateSegmentAsync(segment, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.BroadcastSegment, segment.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "broadcast.segment.create", TargetTypeEnum.BroadcastSegment.ToString(), segment.Id, name + " (" + rows.Select(r => r.Position).Distinct().Count() + " filters)", token).ConfigureAwait(false);
            return await ViewAsync(actor, segment, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/admin/broadcasts/segments/{id}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Segment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The segment.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound or NotAvailable.</exception>
        public async Task<BroadcastSegmentView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            BroadcastService.RequireBroadcaster(_Services, actor);
            BroadcastSegment segment = await RequireAsync(id, token).ConfigureAwait(false);
            return await ViewAsync(actor, segment, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/broadcasts/segments/{id}: omitted fields keep their values; filters, when sent, replace them.
        /// Broadcasts that copied the segment keep their copies.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Segment.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The segment.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or SegmentInvalid, 403 Forbidden, 404 NotFound.</exception>
        public async Task<BroadcastSegmentView> UpdateAsync(ContentActor actor, RequestInfo info, string id, BroadcastSegmentRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            BroadcastService.RequireBroadcaster(_Services, actor);
            BroadcastSegment segment = await RequireAsync(id, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("name")) segment.Name = Text(request.Name, "name", 1, 100, errors) ?? String.Empty;
            if (present.Contains("description")) segment.Description = Text(request.Description, "description", 0, 500, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (present.Contains("filters")) segment.Filters = await ValidateAsync(request.Filters, "filters", token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.BroadcastSegment), token, TargetTypeEnum.BroadcastSegment).ConfigureAwait(false);
            segment.UpdatedUtc = _Clock();
            if (!await _Services.Database.Broadcasts.UpdateSegmentAsync(segment, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Segment");
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.BroadcastSegment, segment.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "broadcast.segment.update", TargetTypeEnum.BroadcastSegment.ToString(), segment.Id, segment.Name, token).ConfigureAwait(false);
            return await GetAsync(actor, segment.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/broadcasts/segments/{id}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Segment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            BroadcastService.RequireBroadcaster(_Services, actor);
            BroadcastSegment segment = await RequireAsync(id, token).ConfigureAwait(false);
            if (!await _Services.Database.Broadcasts.DeleteSegmentAsync(segment.Id, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Segment");
            await _Services.Database.Metadata.WriteAsync(TargetTypeEnum.BroadcastSegment, segment.Id, new ObjectMetadata(), token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "broadcast.segment.delete", TargetTypeEnum.BroadcastSegment.ToString(), segment.Id, segment.Name, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/segments/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden.</exception>
        public async Task<EnumerationResult<BroadcastSegmentView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            BroadcastService.RequireBroadcaster(_Services, actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, BroadcastCollections.Segments, true);
            EnumerationResult<BroadcastSegment> page = await _Services.Database.Broadcasts.EnumerateSegmentsAsync(request, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> creators = await _Services.Summaries.BuildAsync(page.Objects.Select(s => s.CreatedByUserId), ProfileViewerEnum.Staff, token).ConfigureAwait(false);
            List<BroadcastSegmentView> views = page.Objects.Select(s => View(s, creators)).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.BroadcastSegment, views, v => v.Id ?? String.Empty, actor.IsStaffFor(TargetTypeEnum.BroadcastSegment), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/segments/preview: count, channel counts, and a sample of at most 10.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">A saved segment id or an inline segment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Preview.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or SegmentInvalid, 403 Forbidden, 404 NotFound.</exception>
        public async Task<BroadcastPreviewResponse> PreviewAsync(ContentActor actor, BroadcastPreviewRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            BroadcastService.RequireBroadcaster(_Services, actor);
            if (!String.IsNullOrWhiteSpace(request.SegmentId) && request.Segment != null) throw ApiErrorException.Field("segmentId", "Send segmentId or segment, not both.");
            List<BroadcastSegmentFilter> rows = !String.IsNullOrWhiteSpace(request.SegmentId)
                ? (await RequireAsync(request.SegmentId, token).ConfigureAwait(false)).Filters
                : await ValidateAsync(request.Segment?.Filters, "segment.filters", token).ConfigureAwait(false);
            BroadcastAudience audience = await CompileAsync(rows, token).ConfigureAwait(false);
            return await PreviewAudienceAsync(audience, request.Category == BroadcastCategoryEnum.Essential, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The preview of a compiled audience.
        /// </summary>
        /// <param name="audience">Audience.</param>
        /// <param name="essential">Whether the Announcements preferences are ignored.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Preview.</returns>
        public async Task<BroadcastPreviewResponse> PreviewAudienceAsync(BroadcastAudience audience, bool essential, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(audience);
            DateTime now = _Clock();
            BroadcastAudienceCounts counts = await _Services.Database.Broadcasts.CountAudienceAsync(audience, essential, now, token).ConfigureAwait(false);
            List<string> sample = await _Services.Database.Broadcasts.SampleAudienceAsync(audience, _SampleSize, now, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(sample.Cast<string?>(), ProfileViewerEnum.Staff, token).ConfigureAwait(false);
            return new BroadcastPreviewResponse
            {
                Count = counts.Count,
                ByChannel = new BroadcastChannelCounts { InApp = counts.Count, Push = counts.Push, Email = counts.Email },
                Sample = new BroadcastSample { Objects = sample.Where(summaries.ContainsKey).Select(s => summaries[s]).ToList(), TotalRecords = counts.Count }
            };
        }

        /// <summary>
        /// Read a saved segment or throw 404.
        /// </summary>
        /// <param name="id">Segment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The segment.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<BroadcastSegment> RequireAsync(string? id, CancellationToken token = default)
        {
            BroadcastSegment? segment = String.IsNullOrWhiteSpace(id) ? null : await _Services.Database.Broadcasts.ReadSegmentAsync(id.Trim(), token).ConfigureAwait(false);
            return segment ?? throw ApiErrorException.NotFound("Segment");
        }

        #endregion

        #region Private-Methods

        private async Task<BroadcastSegmentView> ViewAsync(ContentActor actor, BroadcastSegment segment, CancellationToken token)
        {
            Dictionary<string, UserSummary> creators = await _Services.Summaries.BuildAsync(new[] { segment.CreatedByUserId }, ProfileViewerEnum.Staff, token).ConfigureAwait(false);
            BroadcastSegmentView view = View(segment, creators);
            return await _Services.Metadata.AttachOneAsync(TargetTypeEnum.BroadcastSegment, view, segment.Id, actor.IsStaffFor(TargetTypeEnum.BroadcastSegment), token).ConfigureAwait(false);
        }

        private static BroadcastSegmentView View(BroadcastSegment segment, Dictionary<string, UserSummary> creators)
        {
            return new BroadcastSegmentView
            {
                Id = segment.Id,
                Name = segment.Name,
                Description = segment.Description,
                Filters = Specs(segment.Filters),
                CreatedBy = segment.CreatedByUserId != null && creators.TryGetValue(segment.CreatedByUserId, out UserSummary? creator) ? creator : null,
                CreatedUtc = segment.CreatedUtc,
                UpdatedUtc = segment.UpdatedUtc
            };
        }

        private static BroadcastSegmentFilter Row(string field, string op, string value, string kind, int position)
        {
            return new BroadcastSegmentFilter { Id = IdGenerator.Generate(BroadcastIdPrefixes.Filter), Field = field, Operator = op, Value = value, Kind = kind, Position = position };
        }

        private static string? Text(string? value, string field, int min, int max, Dictionary<string, string> errors)
        {
            string text = (value ?? String.Empty).Trim();
            if (text.Length < min || text.Length > max)
            {
                errors[field] = min > 0 ? "Required, " + min + " to " + max + " characters." : "At most " + max + " characters.";
                return null;
            }
            return text.Length == 0 ? null : text;
        }

        private async Task<BroadcastAudience> CompileCoreAsync(List<BroadcastSegmentFilter> rows, string prefix, Dictionary<string, string> errors, CancellationToken token)
        {
            BroadcastAudience audience = new BroadcastAudience { RequireMembershipOrStaff = _Services.Access.Current.Model == AccessModelEnum.FullyGated };
            foreach (IGrouping<int, BroadcastSegmentFilter> group in rows.GroupBy(r => r.Position).OrderBy(g => g.Key))
            {
                string at = prefix + "[" + group.Key + "]";
                BroadcastSegmentFilter first = group.First();
                string field = first.Field;
                string op = first.Operator;
                List<string> values = group.Select(r => r.Value.Trim()).ToList();
                bool isList = first.Kind == "List";
                string? single = !isList && values.Count == 1 ? values[0] : null;
                string? problem = null;
                if (field.StartsWith("profile.", StringComparison.Ordinal))
                {
                    problem = await ProfileAsync(audience, field.Substring("profile.".Length), op, first.Kind, group.Select(r => r.Value).ToList(), token).ConfigureAwait(false);
                }
                else if (field.StartsWith("tags.", StringComparison.Ordinal))
                {
                    string key = MetadataRules.Normalize(field.Substring("tags.".Length));
                    if (key.Length == 0 || key.Length > 100) problem = "Use tags.<key> with a tag key.";
                    else if (op == "eq" && single != null && first.Kind == "String") audience.Tags.Add(new BroadcastTagFilter { Key = key, Value = single });
                    else if (op == "exists" && single == "true" && first.Kind == "Boolean") audience.Tags.Add(new BroadcastTagFilter { Key = key });
                    else problem = "Use operator eq with a string value, or exists with true.";
                }
                else if (!_Fields.Contains(field, StringComparer.Ordinal))
                {
                    problem = "Unknown field " + field + ".";
                }
                else
                {
                    problem = await FieldAsync(audience, field, op, first.Kind, values, single, token).ConfigureAwait(false);
                }
                if (problem != null) errors[at] = problem;
            }
            return audience;
        }

        private async Task<string?> FieldAsync(BroadcastAudience a, string field, string op, string kind, List<string> values, string? single, CancellationToken token)
        {
            switch (field)
            {
                case "membership":
                    if (op != "eq" || single == null || kind != "String") return "Use operator eq with Active, NotActive, or Any.";
                    if (String.Equals(single, "Any", StringComparison.OrdinalIgnoreCase)) return null;
                    bool active;
                    if (String.Equals(single, "Active", StringComparison.OrdinalIgnoreCase)) active = true;
                    else if (String.Equals(single, "NotActive", StringComparison.OrdinalIgnoreCase)) active = false;
                    else return "Unknown option " + single + "; use Active, NotActive, or Any.";
                    if (a.MembershipActive != null && a.MembershipActive != active) a.MatchesNothing = true;
                    a.MembershipActive = active;
                    return null;
                case "membershipSource":
                {
                    if (op != "in" && op != "eq") return "Use operator in or eq.";
                    List<string>? names = EnumNames<MembershipSourceEnum>(values);
                    if (names == null) return "Unknown option; use Subscription, SignupCode, or AdminComp.";
                    a.MembershipSourceGroups.Add(names);
                    return null;
                }
                case "store":
                {
                    if (op != "in" && op != "eq") return "Use operator in or eq.";
                    List<string>? names = EnumNames<StoreEnum>(values);
                    if (names == null) return "Unknown option; use Apple, Google, or Stripe.";
                    a.StoreGroups.Add(names);
                    return null;
                }
                case "labels":
                {
                    if (op != "any" && op != "all") return "Use operator any or all.";
                    List<string> labels = values.Select(MetadataRules.Normalize).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                    if (labels.Count == 0 || labels.Any(l => l.Length > 100)) return "Use 1 to " + _MaxValues + " labels.";
                    if (op == "any") a.LabelAnyGroups.Add(labels);
                    else a.LabelsAll.AddRange(labels);
                    return null;
                }
                case "signedUpAfterUtc":
                case "signedUpBeforeUtc":
                {
                    if (op != "eq" || single == null || kind != "String") return "Use operator eq with an ISO 8601 UTC time.";
                    if (!DateTime.TryParse(single, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime time)) return "Use an ISO 8601 UTC time.";
                    time = DateTime.SpecifyKind(time, DateTimeKind.Utc);
                    if (field == "signedUpAfterUtc") a.SignedUpAfterUtc = a.SignedUpAfterUtc == null || time > a.SignedUpAfterUtc ? time : a.SignedUpAfterUtc;
                    else a.SignedUpBeforeUtc = a.SignedUpBeforeUtc == null || time < a.SignedUpBeforeUtc ? time : a.SignedUpBeforeUtc;
                    return null;
                }
                case "activeWithinDays":
                case "inactiveForDays":
                {
                    if (op != "eq" || single == null || kind != "Number" || !Int32.TryParse(single, NumberStyles.Integer, CultureInfo.InvariantCulture, out int days) || days < 1 || days > 3650)
                        return "Use operator eq with a whole number of days from 1 to 3650.";
                    if (field == "activeWithinDays") a.ActiveWithinDays = a.ActiveWithinDays == null ? days : Math.Min(a.ActiveWithinDays.Value, days);
                    else a.InactiveForDays = a.InactiveForDays == null ? days : Math.Max(a.InactiveForDays.Value, days);
                    return null;
                }
                case "hasPushToken":
                case "hasVerifiedEmail":
                {
                    if (op != "eq" || single == null || kind != "Boolean") return "Use operator eq with true or false.";
                    bool flag = single == "true";
                    bool? current = field == "hasPushToken" ? a.HasPushToken : a.HasVerifiedEmail;
                    if (current != null && current != flag) a.MatchesNothing = true;
                    if (field == "hasPushToken") a.HasPushToken = flag;
                    else a.HasVerifiedEmail = flag;
                    return null;
                }
                case "role":
                {
                    if (op != "in" && op != "eq") return "Use operator in or eq.";
                    List<string>? roles = EnumNames<RoleEnum>(values);
                    if (roles == null || roles.Contains(RoleEnum.User.ToString())) return "Unknown role; use staff roles such as SystemAdmin, Moderator, SupportAgent, or Communicator.";
                    a.RoleGroups.Add(roles);
                    return null;
                }
                case "eventRsvp":
                {
                    if (!_Services.Property.Property.Features.Events) return "eventRsvp needs the events feature (features.events).";
                    if (single == null || kind != "String" || single.Length == 0 || single.Length > 64) return "Use an event identifier as the value.";
                    EventRecord? ev = await _Services.Database.Events.Records.ReadAsync(single, token).ConfigureAwait(false);
                    if (ev == null || ev.DeletedUtc != null) return "Unknown event " + single + ".";
                    string? status = _RsvpStatuses.FirstOrDefault(s => String.Equals(s, op, StringComparison.OrdinalIgnoreCase));
                    if (status == null && !String.Equals(op, "any", StringComparison.OrdinalIgnoreCase)) return "Use operator going, interested, waitlisted, or any.";
                    a.EventRsvps.Add(new BroadcastEventRsvpFilter { EventId = single, Status = status });
                    return null;
                }
                case "seriesFollowers":
                {
                    if (!_Services.Property.Property.Features.Library) return "seriesFollowers needs the library feature (features.library).";
                    if (op != "eq" || single == null || kind != "String" || single.Length == 0 || single.Length > 64) return "Use operator eq with a series identifier.";
                    LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(single, token).ConfigureAwait(false);
                    if (series == null || series.DeletedUtc != null) return "Unknown series " + single + ".";
                    a.SeriesIds.Add(single);
                    return null;
                }
                default:
                    return "Unknown field " + field + ".";
            }
        }

        private async Task<string?> ProfileAsync(BroadcastAudience a, string key, string op, string kind, List<string> values, CancellationToken token)
        {
            if (key.Length == 0) return "Use profile.<fieldKey>.";
            object value;
            if (kind == "List") value = values;
            else if (kind == "Number" && Double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) value = number;
            else if (kind == "Boolean") value = values[0] == "true";
            else value = values[0];
            Dictionary<string, Dictionary<string, object>> filter = new Dictionary<string, Dictionary<string, object>> { { key, new Dictionary<string, object> { { op, value } } } };
            JsonElement element = JsonSerializer.SerializeToElement(filter);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<ProfileFilterClause> clauses = await _Services.ProfileValues.CompileFiltersAsync(element, ProfileViewerEnum.Staff, errors, token).ConfigureAwait(false);
            if (errors.Count > 0 || clauses.Count == 0) return "Unknown profile field, or an operator or value it does not support.";
            a.Profile.AddRange(clauses);
            return null;
        }

        private static List<string>? EnumNames<T>(List<string> values) where T : struct, Enum
        {
            List<string> names = new List<string>();
            foreach (string value in values)
            {
                if (Int32.TryParse(value, out int _) || !Enum.TryParse(value, true, out T parsed) || !Enum.IsDefined(parsed)) return null;
                names.Add(parsed.ToString());
            }
            return names.Count == 0 ? null : names.Distinct(StringComparer.Ordinal).ToList();
        }

        #endregion
    }
}
