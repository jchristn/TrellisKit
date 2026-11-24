namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
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
    /// Event categories (docs/PHASE4_CONTRACT.md section 0.2): seeded from property.json events.categories on first
    /// load (in SingleProperty the folder wins when the section changes on disk), edited by SystemAdmin afterwards;
    /// deleting a category in use retires it. Also replaces the events section of GET /api/v1/property with the
    /// database copy. Thread safety: stateless apart from the shared services.
    /// </summary>
    public class EventCategoryService
    {
        #region Private-Members

        private static readonly Regex _Key = new Regex("^[A-Za-z0-9][A-Za-z0-9-]{0,39}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Color = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ApplicationServices _Services;
        private int _Seeded = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public EventCategoryService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed categories from property.json when never seeded, or (SingleProperty) when the section changed.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a seed ran.</returns>
        public async Task<bool> EnsureSeededAsync(CancellationToken token = default)
        {
            if (Volatile.Read(ref _Seeded) == 1) return false;
            PropertyEventsSection section = _Services.Property.Property.EventsSection ?? new PropertyEventsSection();
            List<PropertyOptionSeed> seeds = (section.Categories ?? new List<PropertyOptionSeed>()).Where(c => c != null && !String.IsNullOrWhiteSpace(c.Key)).Take(_Services.Settings.Events.MaxCategories).ToList();
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(seeds.Select(s => new[] { s.Key, s.Label, s.Color ?? String.Empty }))))).Substring(0, 16);
            string? stored = await _Services.Database.Events.Categories.ReadSourceHashAsync(token).ConfigureAwait(false);
            bool single = _Services.Settings.Deployment.Mode == DeploymentModeEnum.SingleProperty;
            bool seed = stored == null || (single && !String.Equals(stored, hash, StringComparison.Ordinal));
            if (seed)
            {
                DateTime now = DateTime.UtcNow;
                List<EventCategory> categories = seeds.Select((s, i) => new EventCategory
                {
                    Id = IdGenerator.Generate(EventIdPrefixes.Category),
                    Key = s.Key.Trim(),
                    Label = String.IsNullOrWhiteSpace(s.Label) ? s.Key.Trim() : s.Label.Trim(),
                    Color = String.IsNullOrWhiteSpace(s.Color) ? null : s.Color.Trim(),
                    SortOrder = i
                }).ToList();
                await _Services.Database.Events.Categories.SeedAsync(categories, hash, now, token).ConfigureAwait(false);
            }
            Interlocked.Exchange(ref _Seeded, 1);
            return seed;
        }

        /// <summary>
        /// POST /api/v1/events/categories/enumerate (not retired).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<EventCategoryView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, EventCollections.Categories, actor.IsStaffFor(TargetTypeEnum.EventCategory));
            EnumerationResult<EventCategory> page = await _Services.Database.Events.Categories.EnumerateAsync(false, request, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/events/categories/enumerate (SystemAdmin and event staff read; filter includeRetired).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<EnumerationResult<EventCategoryView>> AdminListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            EventService.RequireStaff(actor);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool includeRetired = q.FilterBool("includeRetired", errors) ?? true;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, EventCollections.AdminCategories, actor.IsStaffFor(TargetTypeEnum.EventCategory));
            EnumerationResult<EventCategory> page = await _Services.Database.Events.Categories.EnumerateAsync(includeRetired, request, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/events/categories (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Category.</returns>
        /// <exception cref="ApiErrorException">400, 403, 409 KeyInUse or CategoryLimitReached.</exception>
        public async Task<EventCategoryView> CreateAsync(ContentActor actor, RequestInfo info, EventCategoryRequest request, CancellationToken token = default)
        {
            RequireAdmin(actor);
            ArgumentNullException.ThrowIfNull(request);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string key = (request.Key ?? String.Empty).Trim();
            if (!_Key.IsMatch(key)) errors["key"] = "Use 1 to 40 letters, digits, and hyphens.";
            string label = MarkdownNormalizer.NormalizeSingleLine(request.Label);
            if (label.Length < 1 || label.Length > 60) errors["label"] = "Use 1 to 60 characters.";
            string? color = Color(request.Color, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<EventCategory> all = await _Services.Database.Events.Categories.ListAllAsync(token).ConfigureAwait(false);
            if (all.Count >= _Services.Settings.Events.MaxCategories) throw new ApiErrorException(409, "CategoryLimitReached", "At most " + _Services.Settings.Events.MaxCategories + " categories.");
            if (all.Any(c => String.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))) throw new ApiErrorException(409, "KeyInUse", "A category with that key exists.");
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.EventCategory).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            EventCategory category = new EventCategory { Id = IdGenerator.Generate(EventIdPrefixes.Category), Key = key, Label = label, Color = color, SortOrder = all.Count, CreatedUtc = now, UpdatedUtc = now };
            try
            {
                await _Services.Database.Events.Categories.CreateAsync(category, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "KeyInUse", "A category with that key exists.");
            }
            if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.EventCategory, category.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.category.create", "EventCategory", category.Id, key, token).ConfigureAwait(false);
            return (await ViewsAsync(actor, new List<EventCategory> { category }, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// PUT /api/v1/admin/events/categories/{id} (SystemAdmin): label, color, labels, tags; retired false restores.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Category.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Present fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Category.</returns>
        /// <exception cref="ApiErrorException">400, 403, 404.</exception>
        public async Task<EventCategoryView> UpdateAsync(ContentActor actor, RequestInfo info, string id, EventCategoryRequest request, HashSet<string> present, CancellationToken token = default)
        {
            RequireAdmin(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            EventCategory category = await _Services.Database.Events.Categories.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Category");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("key") && !String.Equals((request.Key ?? String.Empty).Trim(), category.Key, StringComparison.Ordinal)) errors["key"] = "Keys cannot change.";
            if (present.Contains("label"))
            {
                string label = MarkdownNormalizer.NormalizeSingleLine(request.Label);
                if (label.Length < 1 || label.Length > 60) errors["label"] = "Use 1 to 60 characters.";
                category.Label = label;
            }
            if (present.Contains("color")) category.Color = Color(request.Color, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            category.IsRetired = false;
            category.UpdatedUtc = DateTime.UtcNow;
            await _Services.Database.Events.Categories.UpdateAsync(category, token).ConfigureAwait(false);
            if (present.Contains("labels") || present.Contains("tags")) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.EventCategory, category.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.category.update", "EventCategory", category.Id, category.Key, token).ConfigureAwait(false);
            return (await ViewsAsync(actor, new List<EventCategory> { category }, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// DELETE /api/v1/admin/events/categories/{id} (SystemAdmin): a category in use is retired, else deleted.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Category.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when retired (in use), false when deleted.</returns>
        /// <exception cref="ApiErrorException">403, 404.</exception>
        public async Task<bool> DeleteAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            RequireAdmin(actor);
            EventCategory category = await _Services.Database.Events.Categories.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Category");
            bool inUse = await _Services.Database.Events.Categories.InUseAsync(category.Key, token).ConfigureAwait(false);
            if (inUse)
            {
                category.IsRetired = true;
                category.UpdatedUtc = DateTime.UtcNow;
                await _Services.Database.Events.Categories.UpdateAsync(category, token).ConfigureAwait(false);
            }
            else await _Services.Database.Events.Categories.DeleteAsync(category.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, inUse ? "event.category.retire" : "event.category.delete", "EventCategory", category.Id, category.Key, token).ConfigureAwait(false);
            return inUse;
        }

        /// <summary>
        /// PUT /api/v1/admin/events/categories/order (SystemAdmin): every category id in the new order.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Categories updated.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403.</exception>
        public async Task<int> OrderAsync(ContentActor actor, RequestInfo info, EventCategoryOrderRequest request, CancellationToken token = default)
        {
            RequireAdmin(actor);
            ArgumentNullException.ThrowIfNull(request);
            List<string> ids = (request.CategoryIds ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList();
            List<EventCategory> all = await _Services.Database.Events.Categories.ListAllAsync(token).ConfigureAwait(false);
            if (ids.Count != all.Count || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || !all.All(c => ids.Contains(c.Id, StringComparer.Ordinal)))
                throw ApiErrorException.Field("categoryIds", "Send every category id exactly once.");
            int updated = await _Services.Database.Events.Categories.SetOrderAsync(ids, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.category.order", "EventCategory", null, ids.Count + " categories", token).ConfigureAwait(false);
            return updated;
        }

        /// <summary>
        /// Replace the events section of GET /api/v1/property with the database copy (a PropertyDecorators entry).
        /// </summary>
        /// <param name="response">Response.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DecorateAsync(PublicPropertyResponse response, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(response);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            List<EventCategory> categories = await _Services.Database.Events.Categories.ListAllAsync(token).ConfigureAwait(false);
            PropertyEventsSection section = _Services.Property.Property.EventsSection ?? new PropertyEventsSection();
            response.Events = new PublicEventsConfig
            {
                Categories = categories.Where(c => !c.IsRetired).Select(c => new PublicOption { Key = c.Key, Label = c.Label, Color = c.Color }).ToList(),
                Proposals = section.Proposals && _Services.Settings.Events.ProposalsEnabled,
                CalendarFeedEnabled = _Services.Settings.Events.CalendarFeedEnabled,
                DefaultReminderOffsetsMinutes = EventEditor.ParseOffsets(_Services.Settings.Events.DefaultReminderOffsetsMinutes),
                PublicListingsEnabled = _Services.Settings.Events.PublicListingsEnabled
            };
        }

        #endregion

        #region Private-Methods

        private static void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin manages event categories.");
        }

        private static string? Color(string? value, Dictionary<string, string> errors)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string c = value.Trim();
            if (!_Color.IsMatch(c)) errors["color"] = "Use #RRGGBB.";
            return c.ToUpperInvariant();
        }

        private async Task<List<EventCategoryView>> ViewsAsync(ContentActor actor, List<EventCategory> rows, CancellationToken token)
        {
            List<EventCategoryView> views = rows.Select(c => new EventCategoryView
            {
                Id = c.Id,
                Key = c.Key,
                Label = c.Label,
                Color = c.Color,
                SortOrder = c.SortOrder,
                IsRetired = c.IsRetired,
                CreatedUtc = c.CreatedUtc,
                UpdatedUtc = c.UpdatedUtc
            }).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.EventCategory, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.EventCategory), token).ConfigureAwait(false);
            return views;
        }

        #endregion
    }
}
