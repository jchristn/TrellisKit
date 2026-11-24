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
    /// Support ticket types (docs/PHASE5_CONTRACT.md sections 2.3 and 6.2): seeded once per property from property.json
    /// support.ticketTypes (engine defaults Support, Bug, Enhancement, Billing when absent) the first time any support route
    /// needs them; later property.json changes do not resync (admins edit types in the console). Members list the enabled
    /// types; SystemAdmin creates, edits, retires (DELETE retires; existing tickets keep their type), and reorders them.
    /// </summary>
    public class TicketTypeService
    {
        #region Public-Members

        /// <summary>
        /// Most ticket types per property.
        /// </summary>
        public const int MaxTypes = 50;

        /// <summary>
        /// Most staff recipients per type.
        /// </summary>
        public const int MaxRecipients = 10;

        /// <summary>
        /// Engine default type keys, seeded when property.json has no support.ticketTypes.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultKeys = new List<string> { "Support", "Bug", "Enhancement", "Billing" };

        #endregion

        #region Private-Members

        private static readonly Regex _Key = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Email = new Regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ApplicationServices _Services;
        private readonly SupportModule _Module;
        private int _Seeded = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Support module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public TicketTypeService(ApplicationServices services, SupportModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed the ticket types when the property has none (idempotent; concurrent callers produce one set).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Types created by this call.</returns>
        public async Task<int> EnsureSeededAsync(CancellationToken token = default)
        {
            if (Volatile.Read(ref _Seeded) != 0) return 0;
            if (await _Services.Database.Support.CountTypesAsync(token).ConfigureAwait(false) > 0)
            {
                Interlocked.Exchange(ref _Seeded, 1);
                return 0;
            }
            List<PropertyTicketTypeSeed> seeds = _Services.Property.Property.Support?.TicketTypes ?? new List<PropertyTicketTypeSeed>();
            if (seeds.Count == 0) seeds = DefaultKeys.Select(k => new PropertyTicketTypeSeed { Key = k, Label = k }).ToList();
            List<TicketType> types = new List<TicketType>();
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.UtcNow;
            foreach (PropertyTicketTypeSeed seed in seeds.Take(MaxTypes))
            {
                string key = (seed?.Key ?? String.Empty).Trim();
                if (seed == null || !_Key.IsMatch(key) || !keys.Add(key)) continue;
                string label = MarkdownNormalizer.NormalizeSingleLine(String.IsNullOrWhiteSpace(seed.Label) ? key : seed.Label);
                types.Add(new TicketType
                {
                    Id = IdGenerator.Generate(SupportIdPrefixes.TicketType),
                    Key = key,
                    Label = label.Length > 80 ? label.Substring(0, 80) : label,
                    Description = String.IsNullOrWhiteSpace(seed.Description) ? null : seed.Description.Trim(),
                    HelpText = String.IsNullOrWhiteSpace(seed.HelpText) ? null : seed.HelpText.Trim(),
                    StaffRecipients = (seed.StaffRecipients ?? new List<string>()).Where(e => !String.IsNullOrWhiteSpace(e) && _Email.IsMatch(e.Trim())).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecipients).ToList(),
                    SortOrder = (types.Count + 1) * 10,
                    CreatedUtc = now,
                    UpdatedUtc = now
                });
            }
            int created = await _Services.Database.Support.SeedTypesAsync(types, token).ConfigureAwait(false);
            Interlocked.Exchange(ref _Seeded, 1);
            return created;
        }

        /// <summary>
        /// A ticket type by key (seeding first).
        /// </summary>
        /// <param name="key">Key (case-insensitive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Type, or null.</returns>
        public async Task<TicketType?> FindAsync(string? key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key)) return null;
            await EnsureSeededAsync(token).ConfigureAwait(false);
            return await _Services.Database.Support.ReadTypeByKeyAsync(key.Trim(), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/support/ticket-types/enumerate: enabled types in order with description and help text.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<TicketTypeView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            await EnsureSeededAsync(token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = MaxTypes;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, SupportCollections.TicketTypes, false, "ticket-types");
            EnumerationResult<TicketType> page = await _Services.Database.Support.EnumerateTypesAsync(request, false, token).ConfigureAwait(false);
            return page.With(await _Module.Views.TypesAsync(page.Objects, actor, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/support/ticket-types/enumerate (SystemAdmin): every type; filters.includeRetired (default true).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<TicketTypeView>> ListAdminAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool includeRetired = q.FilterBool("includeRetired", errors) ?? true;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, SupportCollections.AdminTicketTypes, true, "admin-ticket-types");
            EnumerationResult<TicketType> page = await _Services.Database.Support.EnumerateTypesAsync(request, includeRetired, token).ConfigureAwait(false);
            return page.With(await _Module.Views.TypesAsync(page.Objects, actor, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/support/ticket-types/{id} (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Type.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound or NotAvailable.</exception>
        public async Task<TicketTypeView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            RequireAdmin(actor);
            TicketType type = await RequireAsync(id, token).ConfigureAwait(false);
            return (await _Module.Views.TypesAsync(new List<TicketType> { type }, actor, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// POST /api/v1/admin/support/ticket-types (SystemAdmin). Audited ticket_type.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Type.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable, 409 KeyInUse or TicketTypeLimitReached.</exception>
        public async Task<TicketTypeView> CreateAsync(ContentActor actor, RequestInfo info, TicketTypeRequest request, CancellationToken token = default)
        {
            RequireAdmin(actor);
            ArgumentNullException.ThrowIfNull(request);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            TicketType type = new TicketType { Id = IdGenerator.Generate(SupportIdPrefixes.TicketType), CreatedUtc = now, UpdatedUtc = now };
            Apply(type, request, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "key", "label", "description", "helpText", "staffRecipients", "isRetired" }, true);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.TicketType).ConfigureAwait(false);
            if (await _Services.Database.Support.CountTypesAsync(token).ConfigureAwait(false) >= MaxTypes) throw new ApiErrorException(409, "TicketTypeLimitReached", "A community can have at most " + MaxTypes + " ticket types.");
            List<TicketType> existing = await _Services.Database.Support.ListTypesAsync(true, MaxTypes, token).ConfigureAwait(false);
            type.SortOrder = existing.Count == 0 ? 10 : existing.Max(t => t.SortOrder) + 10;
            try
            {
                await _Services.Database.Support.CreateTypeAsync(type, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "KeyInUse", "Another ticket type uses the key " + type.Key + ".", new Dictionary<string, string> { { "key", "In use." } });
            }
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.TicketType, type.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "ticket_type.create", TargetTypeEnum.TicketType.ToString(), type.Id, type.Key, token).ConfigureAwait(false);
            return await GetAsync(actor, type.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/support/ticket-types/{id} (SystemAdmin): omitted fields stay. Changing the key keeps existing
        /// tickets on the old key (they show the old key as the label). Audited ticket_type.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="id">Identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Present fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Type.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound or NotAvailable, 409 KeyInUse.</exception>
        public async Task<TicketTypeView> UpdateAsync(ContentActor actor, RequestInfo info, string id, TicketTypeRequest request, HashSet<string> present, CancellationToken token = default)
        {
            RequireAdmin(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            TicketType type = await RequireAsync(id, token).ConfigureAwait(false);
            Apply(type, request, present, false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.TicketType).ConfigureAwait(false);
            type.UpdatedUtc = DateTime.UtcNow;
            try
            {
                await _Services.Database.Support.UpdateTypeAsync(type, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "KeyInUse", "Another ticket type uses the key " + type.Key + ".", new Dictionary<string, string> { { "key", "In use." } });
            }
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.TicketType, type.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "ticket_type.update", TargetTypeEnum.TicketType.ToString(), type.Id, String.Join(",", present.OrderBy(p => p, StringComparer.Ordinal)), token).ConfigureAwait(false);
            return await GetAsync(actor, type.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/support/ticket-types/{id} (SystemAdmin): retires the type (hidden from the new ticket form;
        /// existing tickets keep it; restore with PUT isRetired false). Idempotent. Audited ticket_type.retire.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound or NotAvailable.</exception>
        public async Task RetireAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            RequireAdmin(actor);
            TicketType type = await RequireAsync(id, token).ConfigureAwait(false);
            if (type.IsRetired) return;
            type.IsRetired = true;
            type.UpdatedUtc = DateTime.UtcNow;
            await _Services.Database.Support.UpdateTypeAsync(type, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "ticket_type.retire", TargetTypeEnum.TicketType.ToString(), type.Id, type.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/support/ticket-types/order (SystemAdmin): the listed types first in that order, the others after
        /// them in their current order. Audited ticket_type.order.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Every type in the new order.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<TicketTypeOrderResponse> OrderAsync(ContentActor actor, RequestInfo info, TicketTypeOrderRequest request, CancellationToken token = default)
        {
            RequireAdmin(actor);
            ArgumentNullException.ThrowIfNull(request);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            List<string> ids = (request.TicketTypeIds ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList();
            if (ids.Count == 0) throw ApiErrorException.Field("ticketTypeIds", "Required.");
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw ApiErrorException.Field("ticketTypeIds", "Each type at most once.");
            List<TicketType> all = await _Services.Database.Support.ListTypesAsync(true, MaxTypes, token).ConfigureAwait(false);
            HashSet<string> known = new HashSet<string>(all.Select(t => t.Id), StringComparer.Ordinal);
            List<string> unknown = ids.Where(i => !known.Contains(i)).ToList();
            if (unknown.Count > 0) throw ApiErrorException.Field("ticketTypeIds", "Unknown ticket types: " + String.Join(", ", unknown.Take(5)) + ".");
            List<string> ordered = ids.Concat(all.Where(t => !ids.Contains(t.Id)).Select(t => t.Id)).ToList();
            await _Services.Database.Support.OrderTypesAsync(ordered, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "ticket_type.order", TargetTypeEnum.TicketType.ToString(), null, ids.Count + " types", token).ConfigureAwait(false);
            List<TicketType> after = await _Services.Database.Support.ListTypesAsync(true, MaxTypes, token).ConfigureAwait(false);
            return new TicketTypeOrderResponse { Updated = ids.Count, Objects = await _Module.Views.TypesAsync(after, actor, token).ConfigureAwait(false) };
        }

        #endregion

        #region Private-Methods

        private void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin manages ticket types.");
        }

        private async Task<TicketType> RequireAsync(string id, CancellationToken token)
        {
            await EnsureSeededAsync(token).ConfigureAwait(false);
            TicketType? type = await _Services.Database.Support.ReadTypeAsync((id ?? String.Empty).Trim(), token).ConfigureAwait(false);
            if (type == null) throw ApiErrorException.NotFound("TicketType");
            return type;
        }

        private static void Apply(TicketType type, TicketTypeRequest request, HashSet<string> present, bool create)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (create || present.Contains("key"))
            {
                string key = (request.Key ?? String.Empty).Trim();
                if (!_Key.IsMatch(key)) errors["key"] = "1 to 64 letters, digits, underscores, or hyphens.";
                else type.Key = key;
            }
            if (create || present.Contains("label"))
            {
                string label = MarkdownNormalizer.NormalizeSingleLine(request.Label ?? String.Empty);
                if (label.Length == 0 && create && !String.IsNullOrEmpty(type.Key)) label = type.Key;
                if (label.Length < 1 || MarkdownNormalizer.Length(label) > 80) errors["label"] = "1 to 80 characters.";
                else type.Label = label;
            }
            if (present.Contains("description"))
            {
                string? description = String.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                if (description != null && MarkdownNormalizer.Length(description) > 500) errors["description"] = "At most 500 characters.";
                else type.Description = description;
            }
            if (present.Contains("helpText"))
            {
                string? help = String.IsNullOrWhiteSpace(request.HelpText) ? null : MarkdownNormalizer.Normalize(request.HelpText);
                if (help != null && MarkdownNormalizer.Length(help) > 2000) errors["helpText"] = "At most 2000 characters.";
                else type.HelpText = help;
            }
            if (present.Contains("staffRecipients"))
            {
                List<string> emails = (request.StaffRecipients ?? new List<string>()).Where(e => !String.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (emails.Count > MaxRecipients) errors["staffRecipients"] = "At most " + MaxRecipients + " addresses.";
                for (int i = 0; i < emails.Count; i++) if (emails[i].Length > 254 || !_Email.IsMatch(emails[i])) errors["staffRecipients[" + i + "]"] = "Not an email address.";
                type.StaffRecipients = emails;
            }
            if (present.Contains("isRetired") && request.IsRetired != null) type.IsRetired = request.IsRetired.Value;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        #endregion
    }
}
