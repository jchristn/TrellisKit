namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Idea statuses (docs/PHASE5_CONTRACT.md sections 2.4 and 7.2): seeded once from property.json ideas.statuses (or the
    /// engine defaults), listed for members, and administered (create, edit, retire, order) by SystemAdmin only.
    /// Thread safety: safe to call concurrently; seeding is idempotent.
    /// </summary>
    public class IdeaStatusService
    {
        #region Public-Members

        /// <summary>
        /// The status key that sends the idea_shipped email when an idea moves into it (case-insensitive).
        /// </summary>
        public const string ShippedKey = "Shipped";

        /// <summary>
        /// Most statuses per property.
        /// </summary>
        public const int MaxStatuses = 50;

        #endregion

        #region Private-Members

        private static readonly Regex _Key = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ApplicationServices _Services;
        private int _Seeded = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public IdeaStatusService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed the statuses from property.json (or the engine defaults) when the property has none. Idempotent.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statuses created.</returns>
        public async Task<int> EnsureSeededAsync(CancellationToken token = default)
        {
            if (Volatile.Read(ref _Seeded) != 0) return 0;
            if (await _Services.Database.Ideas.CountStatusesAsync(token).ConfigureAwait(false) > 0)
            {
                Interlocked.Exchange(ref _Seeded, 1);
                return 0;
            }
            List<PropertyIdeaStatus> seeds = _Services.Property.Property.Ideas?.Statuses ?? PropertyIdeas.Defaults();
            int created = 0;
            int order = 10;
            foreach (PropertyIdeaStatus seed in seeds)
            {
                token.ThrowIfCancellationRequested();
                DateTime now = DateTime.UtcNow;
                try
                {
                    await _Services.Database.Ideas.CreateStatusAsync(new IdeaStatus
                    {
                        Id = IdGenerator.Generate(IdeaIdPrefixes.Status),
                        Key = seed.Key.Trim(),
                        Label = seed.Label.Trim(),
                        Color = String.IsNullOrWhiteSpace(seed.Color) ? null : seed.Color.Trim(),
                        IsClosed = seed.IsClosed,
                        IsInitial = seed.IsInitial,
                        SortOrder = order,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    }, token).ConfigureAwait(false);
                    created++;
                    order += 10;
                }
                catch (DuplicateRecordException)
                {
                    // Another instance seeded the same status concurrently.
                }
            }
            Interlocked.Exchange(ref _Seeded, 1);
            return created;
        }

        /// <summary>
        /// Every status, retired included (bounded), in display order.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statuses.</returns>
        public async Task<List<IdeaStatus>> AllAsync(CancellationToken token = default)
        {
            await EnsureSeededAsync(token).ConfigureAwait(false);
            return await _Services.Database.Ideas.ListStatusesAsync(true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The initial status (the first active status when none is marked).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status.</returns>
        /// <exception cref="InvalidOperationException">The property has no active status.</exception>
        public async Task<IdeaStatus> InitialAsync(CancellationToken token = default)
        {
            List<IdeaStatus> all = await AllAsync(token).ConfigureAwait(false);
            return all.FirstOrDefault(s => s.IsInitial && !s.IsRetired) ?? all.FirstOrDefault(s => !s.IsRetired)
                ?? throw new InvalidOperationException("The property has no active idea status.");
        }

        /// <summary>
        /// The status with a key (case-insensitive), or null.
        /// </summary>
        /// <param name="statuses">Statuses.</param>
        /// <param name="key">Key.</param>
        /// <returns>Status, or null.</returns>
        public static IdeaStatus? Find(IEnumerable<IdeaStatus> statuses, string? key)
        {
            ArgumentNullException.ThrowIfNull(statuses);
            if (String.IsNullOrWhiteSpace(key)) return null;
            return statuses.FirstOrDefault(s => String.Equals(s.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Whether a status key is the shipped status (sends idea_shipped).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when shipped.</returns>
        public static bool IsShipped(string? key)
        {
            return String.Equals(key, ShippedKey, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The status block of an idea payload.
        /// </summary>
        /// <param name="statuses">Statuses.</param>
        /// <param name="key">Stored key.</param>
        /// <returns>Block.</returns>
        public static IdeaStatusRefView Ref(IEnumerable<IdeaStatus> statuses, string key)
        {
            IdeaStatus? status = Find(statuses, key);
            if (status == null) return new IdeaStatusRefView { Key = key, Label = key };
            return new IdeaStatusRefView { Key = status.Key, Label = status.Label, Color = status.Color, IsClosed = status.IsClosed };
        }

        /// <summary>
        /// POST /api/v1/ideas/statuses/enumerate (active statuses) and POST /api/v1/admin/ideas/statuses/enumerate
        /// (staff; filter includeRetired).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="admin">Whether this is the staff route.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<IdeaStatusView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, bool admin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Ideas.Ideas.RequireEnabled();
            if (admin) RequireStaff(actor);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool includeRetired = admin && (q.FilterBool("includeRetired", errors) ?? false);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (q.MaxResults == null) q.MaxResults = MaxStatuses;
            bool staff = actor.IsStaffFor(TargetTypeEnum.IdeaStatus);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, admin ? IdeaCollections.AdminStatuses : IdeaCollections.Statuses, staff, "idea-statuses|" + includeRetired, MaxStatuses);
            EnumerationResult<IdeaStatus> page = await _Services.Database.Ideas.EnumerateStatusesAsync(request, includeRetired, token).ConfigureAwait(false);
            return page.With(await _Services.Metadata.AttachAsync(TargetTypeEnum.IdeaStatus, page.Objects.Select(ToView).ToList(), v => v.Id, staff, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/ideas/statuses/{id} (staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Status identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<IdeaStatusView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Ideas.Ideas.RequireEnabled();
            RequireStaff(actor);
            IdeaStatus status = await RequireAsync(id, token).ConfigureAwait(false);
            return await ViewAsync(actor, status, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ideas/statuses (SystemAdmin). Audited idea.status.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The status.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 409 StatusKeyInUse.</exception>
        public async Task<IdeaStatusView> CreateAsync(ContentActor actor, RequestInfo info, IdeaStatusRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Services.Ideas.Ideas.RequireEnabled();
            RequireAdmin(actor);
            List<IdeaStatus> all = await AllAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string key = (request.Key ?? String.Empty).Trim();
            string label = (request.Label ?? String.Empty).Trim();
            if (!_Key.IsMatch(key)) errors["key"] = "Use 1 to 64 letters, digits, underscores, or hyphens.";
            if (label.Length < 1 || label.Length > 60) errors["label"] = "Use 1 to 60 characters.";
            string? color = NormalizeColor(request.Color, errors);
            if (request.IsInitial == true && request.IsClosed == true) errors["isInitial"] = "The initial status cannot be closed.";
            if (all.Count(s => !s.IsRetired) >= MaxStatuses) errors["key"] = "At most " + MaxStatuses + " statuses.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (Find(all, key) != null) throw new ApiErrorException(409, "StatusKeyInUse", "An idea status with the key " + key + " already exists.");
            DateTime now = DateTime.UtcNow;
            IdeaStatus status = new IdeaStatus
            {
                Id = IdGenerator.Generate(IdeaIdPrefixes.Status),
                Key = key,
                Label = label,
                Color = color,
                IsClosed = request.IsClosed ?? false,
                IsInitial = request.IsInitial ?? false,
                SortOrder = (all.Count == 0 ? 0 : all.Max(s => s.SortOrder)) + 10,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                status = await _Services.Database.Ideas.CreateStatusAsync(status, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "StatusKeyInUse", "An idea status with the key " + key + " already exists.");
            }
            await _Services.Audit.WriteAsync(info, "idea.status.create", TargetTypeEnum.IdeaStatus.ToString(), status.Id, key, token).ConfigureAwait(false);
            return await ViewAsync(actor, status, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/ideas/statuses/{id} (SystemAdmin): label, color, isClosed, isInitial, key (renaming moves
        /// every idea to the new key). Audited idea.status.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="id">Status identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Top-level fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The status.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 StatusKeyInUse.</exception>
        public async Task<IdeaStatusView> UpdateAsync(ContentActor actor, RequestInfo info, string id, IdeaStatusRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            _Services.Ideas.Ideas.RequireEnabled();
            RequireAdmin(actor);
            IdeaStatus status = await RequireAsync(id, token).ConfigureAwait(false);
            List<IdeaStatus> all = await AllAsync(token).ConfigureAwait(false);
            string previousKey = status.Key;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("key"))
            {
                string key = (request.Key ?? String.Empty).Trim();
                if (!_Key.IsMatch(key)) errors["key"] = "Use 1 to 64 letters, digits, underscores, or hyphens.";
                else if (all.Any(s => s.Id != status.Id && String.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)))
                    throw new ApiErrorException(409, "StatusKeyInUse", "An idea status with the key " + key + " already exists.");
                status.Key = key;
            }
            if (present.Contains("label"))
            {
                string label = (request.Label ?? String.Empty).Trim();
                if (label.Length < 1 || label.Length > 60) errors["label"] = "Use 1 to 60 characters.";
                status.Label = label;
            }
            if (present.Contains("color")) status.Color = NormalizeColor(request.Color, errors);
            if (present.Contains("isClosed") && request.IsClosed != null) status.IsClosed = request.IsClosed.Value;
            if (present.Contains("isInitial") && request.IsInitial != null)
            {
                if (status.IsInitial && !request.IsInitial.Value) errors["isInitial"] = "Mark another status initial instead.";
                status.IsInitial = request.IsInitial.Value;
            }
            if (status.IsInitial && status.IsClosed) errors["isClosed"] = "The initial status cannot be closed.";
            if (status.IsInitial && status.IsRetired) errors["isInitial"] = "A retired status cannot be initial.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            status.UpdatedUtc = DateTime.UtcNow;
            IdeaStatus? stored;
            try
            {
                stored = await _Services.Database.Ideas.UpdateStatusAsync(status, previousKey, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "StatusKeyInUse", "An idea status with the key " + status.Key + " already exists.");
            }
            if (stored == null) throw ApiErrorException.NotFound("Idea status");
            await _Services.Audit.WriteAsync(info, "idea.status.update", TargetTypeEnum.IdeaStatus.ToString(), stored.Id, stored.Key, token).ConfigureAwait(false);
            return await ViewAsync(actor, stored, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/ideas/statuses/{id} (SystemAdmin): retire the status (ideas keep it; it is no longer offered).
        /// The initial status and the last active status cannot be retired. Audited idea.status.retire.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="id">Status identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 StatusInUse.</exception>
        public async Task RetireAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            _Services.Ideas.Ideas.RequireEnabled();
            RequireAdmin(actor);
            IdeaStatus status = await RequireAsync(id, token).ConfigureAwait(false);
            if (status.IsRetired) return;
            if (status.IsInitial) throw new ApiErrorException(409, "StatusInUse", "Mark another status initial before retiring this one.");
            status.IsRetired = true;
            status.UpdatedUtc = DateTime.UtcNow;
            await _Services.Database.Ideas.UpdateStatusAsync(status, status.Key, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "idea.status.retire", TargetTypeEnum.IdeaStatus.ToString(), status.Id, status.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/ideas/statuses/order (SystemAdmin): every active status exactly once. Audited idea.status.order.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statuses in the new order (bounded).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<IdeaStatusView>> OrderAsync(ContentActor actor, RequestInfo info, IdeaStatusOrderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Services.Ideas.Ideas.RequireEnabled();
            RequireAdmin(actor);
            List<IdeaStatus> active = (await AllAsync(token).ConfigureAwait(false)).Where(s => !s.IsRetired).ToList();
            List<string> ids = (request.IdeaStatusIds ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList();
            HashSet<string> wanted = new HashSet<string>(active.Select(s => s.Id), StringComparer.Ordinal);
            if (ids.Count != wanted.Count || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || !ids.All(wanted.Contains))
                throw ApiErrorException.Field("ideaStatusIds", "List every active status exactly once.");
            await _Services.Database.Ideas.ReorderStatusesAsync(ids, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "idea.status.order", TargetTypeEnum.IdeaStatus.ToString(), null, ids.Count + " statuses", token).ConfigureAwait(false);
            return await EnumerateAsync(actor, new EnumerationQuery { MaxResults = MaxStatuses }, true, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void RequireStaff(ContentActor actor)
        {
            if (!IdeaService.IsStaff(actor)) throw ApiErrorException.Forbidden("Ideas staff (SystemAdmin or SupportAgent) only.");
        }

        private static void RequireAdmin(ContentActor actor)
        {
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Idea statuses are managed by a SystemAdmin.");
        }

        private async Task<IdeaStatus> RequireAsync(string id, CancellationToken token)
        {
            IdeaStatus? status = String.IsNullOrWhiteSpace(id) ? null : await _Services.Database.Ideas.ReadStatusAsync(id.Trim(), token).ConfigureAwait(false);
            return status ?? throw ApiErrorException.NotFound("Idea status");
        }

        private static string? NormalizeColor(string? raw, Dictionary<string, string> errors)
        {
            if (String.IsNullOrWhiteSpace(raw)) return null;
            string color = raw.Trim();
            if (!PropertyValidator.IsColor(color)) errors["color"] = "Use a hex color such as #2E7D32.";
            return color;
        }

        private async Task<IdeaStatusView> ViewAsync(ContentActor actor, IdeaStatus status, CancellationToken token)
        {
            return await _Services.Metadata.AttachOneAsync(TargetTypeEnum.IdeaStatus, ToView(status), status.Id, actor.IsStaffFor(TargetTypeEnum.IdeaStatus), token).ConfigureAwait(false);
        }

        private static IdeaStatusView ToView(IdeaStatus status)
        {
            return new IdeaStatusView
            {
                Id = status.Id,
                Key = status.Key,
                Label = status.Label,
                Color = status.Color,
                IsClosed = status.IsClosed,
                IsInitial = status.IsInitial,
                SortOrder = status.SortOrder,
                IsRetired = status.IsRetired,
                CreatedUtc = status.CreatedUtc,
                UpdatedUtc = status.UpdatedUtc
            };
        }

        #endregion
    }
}
