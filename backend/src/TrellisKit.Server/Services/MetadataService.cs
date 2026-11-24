namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Labels and tags for every object type (docs/ENUMERATION_AND_METADATA.md section 1): batch loading for payloads
    /// (staff entries stripped for non-staff), validation and replace semantics on create and update routes, the
    /// generic metadata routes, admin bulk changes, and discovery enumerations. Member text goes through
    /// <see cref="TextFilter"/> (the word filter) when one is set.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MetadataService
    {
        #region Public-Members

        /// <summary>
        /// Word filter hook for member-written labels and tag values: called with the author, the object type, and (field, text) pairs
        /// such as ("labels[0]", "guard") and ("tags.level", "beginner"); throws 400 ContentFiltered on a Block match.
        /// Null skips filtering. Never called for staff.
        /// </summary>
        public Func<ContentActor, TargetTypeEnum, List<KeyValuePair<string, string>>, CancellationToken, Task>? TextFilter { get; set; } = null;

        /// <summary>
        /// Discovery collection for labels and tag keys and values (offset-based continuation, most used first). The
        /// standard createdAfterUtc and createdBeforeUtc window counts only labels and tags applied in that window.
        /// </summary>
        public static readonly EnumerationCollection DiscoveryCollection = new EnumerationCollection("metadata-discovery", null, "m.id", "m.createdutc", false)
            .Order("CountDescending", "cnt", true, EnumerationKeyKindEnum.Number).DefaultTo("CountDescending");

        #endregion

        #region Private-Members

        private const int _MaxBulkTargets = 500;
        private readonly DatabaseDriverBase _Database;
        private readonly EnumerationService _Enumeration;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MetadataService(DatabaseDriverBase database, EnumerationService enumeration)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse an object type name.
        /// </summary>
        /// <param name="raw">Name, for example FeedPost.</param>
        /// <param name="field">Field name for the error.</param>
        /// <returns>Type.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed for an unknown type.</exception>
        public static TargetTypeEnum ParseType(string? raw, string field = "targetType")
        {
            if (!String.IsNullOrWhiteSpace(raw) && !Int32.TryParse(raw, out int _) && Enum.TryParse(raw.Trim(), true, out TargetTypeEnum type) && Enum.IsDefined(type)) return type;
            throw ApiErrorException.Field(field, "Use one of " + String.Join(", ", Enum.GetNames<TargetTypeEnum>()) + ".");
        }

        /// <summary>
        /// Labels and tags of many objects for a payload, staff entries stripped unless includeStaff.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetIds">Identifiers.</param>
        /// <param name="includeStaff">Whether the caller is staff for this type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata keyed by every distinct identifier.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        public async Task<Dictionary<string, ObjectMetadata>> LoadAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, bool includeStaff, CancellationToken token = default)
        {
            Dictionary<string, ObjectMetadata> stored = await _Database.Metadata.ReadManyAsync(targetType, targetIds, token).ConfigureAwait(false);
            return stored.ToDictionary(p => p.Key, p => MetadataRules.ForViewer(p.Value, includeStaff), StringComparer.Ordinal);
        }

        /// <summary>
        /// Labels and tags of one object for a payload.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Identifier.</param>
        /// <param name="includeStaff">Whether the caller is staff for this type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        public async Task<ObjectMetadata> LoadOneAsync(TargetTypeEnum targetType, string targetId, bool includeStaff, CancellationToken token = default)
        {
            return MetadataRules.ForViewer(await _Database.Metadata.ReadAsync(targetType, targetId, token).ConfigureAwait(false), includeStaff);
        }

        /// <summary>
        /// Fill labels and tags on a page of payloads with one metadata load (never one per item). Staff entries are
        /// stripped unless includeStaff.
        /// </summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <param name="targetType">Object type.</param>
        /// <param name="items">Payloads.</param>
        /// <param name="id">Identifier of each payload's object.</param>
        /// <param name="includeStaff">Whether the caller is staff for this type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The same payloads.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<T>> AttachAsync<T>(TargetTypeEnum targetType, List<T> items, Func<T, string> id, bool includeStaff, CancellationToken token = default) where T : IMetadataPayload
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(id);
            if (items.Count == 0) return items;
            Dictionary<string, ObjectMetadata> map = await LoadAsync(targetType, items.Select(id), includeStaff, token).ConfigureAwait(false);
            foreach (T item in items)
            {
                ObjectMetadata metadata = map.TryGetValue(id(item), out ObjectMetadata? found) ? found : new ObjectMetadata();
                item.Labels = new List<string>(metadata.Labels);
                item.Tags = new Dictionary<string, string>(metadata.Tags);
            }
            return items;
        }

        /// <summary>
        /// Fill labels and tags on one payload.
        /// </summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <param name="targetType">Object type.</param>
        /// <param name="item">Payload.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <param name="includeStaff">Whether the caller is staff for this type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The same payload.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<T> AttachOneAsync<T>(TargetTypeEnum targetType, T item, string targetId, bool includeStaff, CancellationToken token = default) where T : IMetadataPayload
        {
            ArgumentNullException.ThrowIfNull(item);
            List<T> one = new List<T> { item };
            await AttachAsync(targetType, one, _ => targetId, includeStaff, token).ConfigureAwait(false);
            return item;
        }

        /// <summary>
        /// Remove an object's public labels and tags but keep its staff: entries (account deletion: the member's own
        /// metadata goes with their profile; moderation notes stay).
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        public Task RemovePublicAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetId)) throw new ArgumentNullException(nameof(targetId));
            return _Database.Metadata.DeletePublicAsync(targetType, targetId, token);
        }

        /// <summary>
        /// Validate labels and tags and return the clean metadata to store on a new object created by this caller (staff
        /// entries dropped for non-staff). Call before inserting the object.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Object type.</param>
        /// <param name="labels">Labels, or null.</param>
        /// <param name="tags">Tags, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata for the new object.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 400 ContentFiltered.</exception>
        public async Task<ObjectMetadata> PrepareNewAsync(ContentActor actor, TargetTypeEnum targetType, List<string>? labels, Dictionary<string, string>? tags, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            bool isStaff = actor.IsStaffFor(targetType);
            await ValidateAsync(actor, labels, tags, isStaff, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            return MetadataRules.Replace(new ObjectMetadata(), MetadataRules.CleanLabels(labels, "labels", errors), MetadataRules.CleanTags(tags, "tags", errors), isStaff);
        }

        /// <summary>
        /// Validate labels and tags from a create or update payload before the object is written (limits, characters,
        /// and the word filter for non-staff). Call it before inserting so a bad label does not leave a half-created object.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="labels">Labels, or null.</param>
        /// <param name="tags">Tags, or null.</param>
        /// <param name="isStaff">Whether the caller is staff for this type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="targetType">Object type, passed to the word filter (default User).</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 400 ContentFiltered.</exception>
        public async Task ValidateAsync(ContentActor actor, List<string>? labels, Dictionary<string, string>? tags, bool isStaff, CancellationToken token = default, TargetTypeEnum? targetType = null)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<string> cleanLabels = MetadataRules.CleanLabels(labels, "labels", errors);
            Dictionary<string, string> cleanTags = MetadataRules.CleanTags(tags, "tags", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (isStaff || TextFilter == null) return;
            List<KeyValuePair<string, string>> fields = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < cleanLabels.Count; i++) if (!MetadataRules.IsStaff(cleanLabels[i])) fields.Add(new KeyValuePair<string, string>("labels[" + i + "]", cleanLabels[i]));
            foreach (KeyValuePair<string, string> pair in cleanTags) if (!MetadataRules.IsStaff(pair.Key)) fields.Add(new KeyValuePair<string, string>("tags." + pair.Key, pair.Key + " " + pair.Value));
            if (fields.Count > 0) await TextFilter(actor, targetType ?? TargetTypeEnum.User, fields, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply labels and tags from a create or update payload with replace semantics (null means unchanged; non-staff
        /// callers cannot add, change, or remove staff entries). Validates first.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Identifier.</param>
        /// <param name="labels">Replacement labels, or null.</param>
        /// <param name="tags">Replacement tags, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The metadata the caller sees afterwards.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 400 ContentFiltered.</exception>
        public async Task<ObjectMetadata> ApplyAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, List<string>? labels, Dictionary<string, string>? tags, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (String.IsNullOrEmpty(targetId)) throw new ArgumentNullException(nameof(targetId));
            bool isStaff = actor.IsStaffFor(targetType);
            ObjectMetadata existing = await _Database.Metadata.ReadAsync(targetType, targetId, token).ConfigureAwait(false);
            if (labels == null && tags == null) return MetadataRules.ForViewer(existing, isStaff);
            await ValidateAsync(actor, labels, tags, isStaff, token, targetType).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ObjectMetadata next = MetadataRules.Replace(existing,
                labels == null ? null : MetadataRules.CleanLabels(labels, "labels", errors),
                tags == null ? null : MetadataRules.CleanTags(tags, "tags", errors), isStaff);
            Limit(next);
            await _Database.Metadata.WriteAsync(targetType, targetId, next, token).ConfigureAwait(false);
            return MetadataRules.ForViewer(next, isStaff);
        }

        /// <summary>
        /// Copy every label and tag (staff entries included) of one object to another, for example a post's metadata to
        /// the revision that stores its previous version. Nothing is written when the source has none.
        /// </summary>
        /// <param name="sourceType">Source object type.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="targetType">Target object type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An identifier is null or empty.</exception>
        public async Task SnapshotAsync(TargetTypeEnum sourceType, string sourceId, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(sourceId)) throw new ArgumentNullException(nameof(sourceId));
            if (String.IsNullOrEmpty(targetId)) throw new ArgumentNullException(nameof(targetId));
            ObjectMetadata source = await _Database.Metadata.ReadAsync(sourceType, sourceId, token).ConfigureAwait(false);
            if (source.Labels.Count == 0 && source.Tags.Count == 0) return;
            await _Database.Metadata.WriteAsync(targetType, targetId, source, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/metadata/{targetType}/{targetId}: owner, staff, or any member for content members can see.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="rawType">Type name.</param>
        /// <param name="targetId">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<MetadataResponse> ReadAsync(ContentActor actor, string? rawType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            TargetTypeEnum type = ParseType(rawType);
            MetadataTarget target = await RequireTargetAsync(type, targetId, token).ConfigureAwait(false);
            bool isStaff = actor.IsStaffFor(type);
            bool owner = target.OwnerUserId != null && target.OwnerUserId == actor.UserId;
            bool publicRead = type == TargetTypeEnum.User || type == TargetTypeEnum.FeedPost || type == TargetTypeEnum.Comment || type == TargetTypeEnum.Media || type == TargetTypeEnum.Reaction;
            if (!isStaff && !owner && !publicRead) throw ApiErrorException.NotFound(type.ToString());
            return View(type, targetId, await LoadOneAsync(type, targetId, isStaff, token).ConfigureAwait(false));
        }

        /// <summary>
        /// PUT /api/v1/metadata/{targetType}/{targetId}: owner (public entries) or staff.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="rawType">Type name.</param>
        /// <param name="targetId">Identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata after the change.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 403 Forbidden, 404 NotFound.</exception>
        public async Task<MetadataResponse> ReplaceAsync(ContentActor actor, string? rawType, string targetId, MetadataReplaceRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            TargetTypeEnum type = ParseType(rawType);
            await RequireEditableAsync(actor, type, targetId, token).ConfigureAwait(false);
            ObjectMetadata result = await ApplyAsync(actor, type, targetId, request.Labels, request.Tags, token).ConfigureAwait(false);
            return View(type, targetId, result);
        }

        /// <summary>
        /// PATCH /api/v1/metadata/{targetType}/{targetId}: owner (public entries) or staff.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="rawType">Type name.</param>
        /// <param name="targetId">Identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata after the change.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 403 Forbidden, 404 NotFound.</exception>
        public async Task<MetadataResponse> PatchAsync(ContentActor actor, string? rawType, string targetId, MetadataPatchRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            TargetTypeEnum type = ParseType(rawType);
            await RequireEditableAsync(actor, type, targetId, token).ConfigureAwait(false);
            bool isStaff = actor.IsStaffFor(type);
            await ValidatePatchAsync(actor, type, request.AddLabels, request.SetTags, request.RemoveLabels, request.RemoveTagKeys, isStaff, token).ConfigureAwait(false);
            ObjectMetadata existing = await _Database.Metadata.ReadAsync(type, targetId, token).ConfigureAwait(false);
            ObjectMetadata next = MetadataRules.Patch(existing, request.AddLabels, request.RemoveLabels, request.SetTags, request.RemoveTagKeys, isStaff);
            Limit(next);
            await _Database.Metadata.WriteAsync(type, targetId, next, token).ConfigureAwait(false);
            return View(type, targetId, MetadataRules.ForViewer(next, isStaff));
        }

        /// <summary>
        /// POST /api/v1/admin/metadata/bulk (SystemAdmin or the admin token): one incremental change on up to 500 objects.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<MetadataBulkResponse> BulkAsync(ContentActor actor, MetadataBulkRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Bulk metadata changes need SystemAdmin.");
            TargetTypeEnum type = ParseType(request.TargetType);
            List<string> ids = (request.TargetIds ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0 || ids.Count > _MaxBulkTargets) throw ApiErrorException.Field("targetIds", "Send 1 to " + _MaxBulkTargets + " identifiers.");
            await ValidatePatchAsync(actor, type, request.AddLabels, request.SetTags, request.RemoveLabels, request.RemoveTagKeys, true, token).ConfigureAwait(false);
            MetadataBulkResponse response = new MetadataBulkResponse();
            Dictionary<string, ObjectMetadata> stored = await _Database.Metadata.ReadManyAsync(type, ids, token).ConfigureAwait(false);
            foreach (string id in ids)
            {
                token.ThrowIfCancellationRequested();
                if (await _Database.Metadata.ReadTargetAsync(type, id, token).ConfigureAwait(false) == null) { response.NotFound.Add(id); continue; }
                ObjectMetadata next = MetadataRules.Patch(stored[id], request.AddLabels, request.RemoveLabels, request.SetTags, request.RemoveTagKeys, true);
                if (MetadataRules.OverLabelLimit(next.Labels) || MetadataRules.OverTagLimit(next.Tags.Keys))
                    throw ApiErrorException.Field("targetIds", "The change would exceed " + MetadataRules.MaxLabels + " labels or " + MetadataRules.MaxTags + " tags on " + id + ".");
                await _Database.Metadata.WriteAsync(type, id, next, token).ConfigureAwait(false);
                response.Updated++;
            }
            return response;
        }

        /// <summary>
        /// POST /api/v1/labels/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Labels with counts.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public Task<EnumerationResult<LabelCount>> EnumerateLabelsAsync(ContentActor actor, MetadataDiscoveryQuery query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(query);
            TargetTypeEnum? type = String.IsNullOrWhiteSpace(query.TargetType) ? null : ParseType(query.TargetType);
            bool isStaff = type == null ? actor.IsSystemAdmin : actor.IsStaffFor(type.Value);
            EnumerationRequest request = _Enumeration.Prepare(query, DiscoveryCollection, isStaff, "labels|" + type + "|" + query.Prefix);
            return _Database.Metadata.EnumerateLabelsAsync(request, type, query.Prefix, actor.UserId, isStaff, token);
        }

        /// <summary>
        /// POST /api/v1/tags/keys/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Keys with counts.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public Task<EnumerationResult<TagKeyCount>> EnumerateTagKeysAsync(ContentActor actor, MetadataDiscoveryQuery query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(query);
            TargetTypeEnum? type = String.IsNullOrWhiteSpace(query.TargetType) ? null : ParseType(query.TargetType);
            bool isStaff = type == null ? actor.IsSystemAdmin : actor.IsStaffFor(type.Value);
            EnumerationRequest request = _Enumeration.Prepare(query, DiscoveryCollection, isStaff, "tagkeys|" + type + "|" + query.Prefix);
            return _Database.Metadata.EnumerateTagKeysAsync(request, type, query.Prefix, actor.UserId, isStaff, token);
        }

        /// <summary>
        /// POST /api/v1/tags/values/enumerate (key required).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Values with counts.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public Task<EnumerationResult<TagValueCount>> EnumerateTagValuesAsync(ContentActor actor, MetadataDiscoveryQuery query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(query);
            if (String.IsNullOrWhiteSpace(query.Key) || !MetadataRules.IsValidKey(query.Key)) throw ApiErrorException.Field("key", "A tag key is required.");
            TargetTypeEnum? type = String.IsNullOrWhiteSpace(query.TargetType) ? null : ParseType(query.TargetType);
            bool isStaff = type == null ? actor.IsSystemAdmin : actor.IsStaffFor(type.Value);
            if (!isStaff && MetadataRules.IsStaff(query.Key)) throw ApiErrorException.Field("key", "Staff tags cannot be used here.");
            EnumerationRequest request = _Enumeration.Prepare(query, DiscoveryCollection, isStaff, "tagvalues|" + type + "|" + query.Key + "|" + query.Prefix);
            return _Database.Metadata.EnumerateTagValuesAsync(request, type, query.Key, query.Prefix, actor.UserId, isStaff, token);
        }

        #endregion

        #region Private-Methods

        private static MetadataResponse View(TargetTypeEnum type, string id, ObjectMetadata metadata)
        {
            return new MetadataResponse { TargetType = type.ToString(), TargetId = id, Labels = metadata.Labels, Tags = new Dictionary<string, string>(metadata.Tags) };
        }

        private static void Limit(ObjectMetadata metadata)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            // Public and staff: entries have separate limits, so a member's limit error never reveals hidden staff metadata.
            if (MetadataRules.OverLabelLimit(metadata.Labels)) errors["labels"] = "At most " + MetadataRules.MaxLabels + " labels per object.";
            if (MetadataRules.OverTagLimit(metadata.Tags.Keys)) errors["tags"] = "At most " + MetadataRules.MaxTags + " tags per object.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        private async Task ValidatePatchAsync(ContentActor actor, TargetTypeEnum type, List<string>? addLabels, Dictionary<string, string>? setTags, List<string>? removeLabels, List<string>? removeTagKeys, bool isStaff, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            MetadataRules.CleanLabels(removeLabels, "removeLabels", errors);
            MetadataRules.CleanLabels(removeTagKeys, "removeTagKeys", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await ValidateAsync(actor, addLabels, setTags, isStaff, token, type).ConfigureAwait(false);
        }

        private async Task<MetadataTarget> RequireTargetAsync(TargetTypeEnum type, string targetId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(targetId)) throw ApiErrorException.NotFound(type.ToString());
            MetadataTarget? target = await _Database.Metadata.ReadTargetAsync(type, targetId, token).ConfigureAwait(false);
            if (target == null) throw ApiErrorException.NotFound(type.ToString());
            return target;
        }

        private async Task RequireEditableAsync(ContentActor actor, TargetTypeEnum type, string targetId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            MetadataTarget target = await RequireTargetAsync(type, targetId, token).ConfigureAwait(false);
            if (actor.IsStaffFor(type)) return;
            if (target.OwnerUserId != null && target.OwnerUserId == actor.UserId)
            {
                actor.RequireNotMuted();
                return;
            }
            throw ApiErrorException.Forbidden("Only the owner or staff can change this object's labels and tags.");
        }

        #endregion
    }
}
