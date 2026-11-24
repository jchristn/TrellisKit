namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
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
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Configurable profile field definitions of one property (docs/MULTIPROPERTY_CONTRACT.md section 9): admin
    /// create, update, delete, reorder, stats, seeding from property.json, and a short-lived definition cache.
    /// Thread safety: safe to call concurrently; the cache is replaced as a whole.
    /// </summary>
    public class ProfileFieldService
    {
        #region Public-Members

        /// <summary>
        /// Seconds definitions are cached before they are read again (other instances' changes arrive within this time).
        /// Default 15. Minimum 0, maximum 600.
        /// </summary>
        public int CacheSeconds
        {
            get { return _CacheSeconds; }
            set { _CacheSeconds = Math.Clamp(value, 0, 600); }
        }

        /// <summary>
        /// Called after definitions change (the value service and the property route reload).
        /// </summary>
        public Action? Changed { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly AuditService _Audit;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private List<ProfileField>? _Cache = null;
        private string _Revision = "0";
        private DateTime _CachedUtc = DateTime.MinValue;
        private int _CacheSeconds = 15;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ProfileFieldService(DatabaseDriverBase database, AppSettings settings, AuditService audit, EnumerationService enumeration, MetadataService metadata, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every definition of the property in profile order (built-ins included), from the cache.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definitions (do not modify).</returns>
        public async Task<IReadOnlyList<ProfileField>> AllAsync(CancellationToken token = default)
        {
            List<ProfileField>? cache = _Cache;
            if (cache != null && _CachedUtc != DateTime.MinValue && (DateTime.UtcNow - _CachedUtc).TotalSeconds < _CacheSeconds) return cache;
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Cache != null && _CachedUtc != DateTime.MinValue && (DateTime.UtcNow - _CachedUtc).TotalSeconds < _CacheSeconds) return _Cache;
                List<ProfileField> fields = await _Database.ProfileFields.ReadAllAsync(token).ConfigureAwait(false);
                _Revision = await _Database.ProfileFields.RevisionAsync(token).ConfigureAwait(false);
                _Cache = fields;
                _CachedUtc = DateTime.UtcNow;
                return fields;
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// The revision token of the cached definitions (for the property ETag).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Revision.</returns>
        public async Task<string> RevisionAsync(CancellationToken token = default)
        {
            await AllAsync(token).ConfigureAwait(false);
            return _Revision;
        }

        /// <summary>
        /// Drop the cache (after a change).
        /// </summary>
        public void Invalidate()
        {
            _CachedUtc = DateTime.MinValue;
            Changed?.Invoke();
        }

        /// <summary>
        /// The last loaded definitions for synchronous readers (may be up to the cache lifetime old), or an empty list
        /// before the first load.
        /// </summary>
        public IReadOnlyList<ProfileField> Snapshot
        {
            get { return _Cache ?? new List<ProfileField>(); }
        }

        /// <summary>
        /// The enabled non-built-in field mapping a legacy column (by key and type) that a viewer may see, or null.
        /// </summary>
        /// <param name="key">Legacy key: rank, affiliation, city, region, country, goals, or rankHistory.</param>
        /// <param name="viewer">Viewer.</param>
        /// <returns>Field, or null.</returns>
        public ProfileField? LegacyField(string key, ProfileViewerEnum viewer)
        {
            if (_Settings.ProfileFields.LegacyFieldsMode == LegacyFieldsModeEnum.Off) return null;
            ProfileFieldTypeEnum type = key == "rank" ? ProfileFieldTypeEnum.Rank : key == "country" ? ProfileFieldTypeEnum.Country :
                key == "goals" ? ProfileFieldTypeEnum.MultiSelect : key == "rankHistory" ? ProfileFieldTypeEnum.History : ProfileFieldTypeEnum.Text;
            ProfileField? field = Snapshot.FirstOrDefault(f => !f.BuiltIn && f.Key == key && f.Type == type);
            if (field == null || !field.Enabled || !ProfileValueService.IsVisible(field.Visibility, viewer)) return null;
            return field;
        }

        /// <summary>
        /// POST /api/v1/admin/profile-fields/enumerate.
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with labels and tags (staff view).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<ProfileField>> EnumerateAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, ProfileCollections.Fields, true);
            await _Database.ProfileValues.RecountAsync(null, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ProfileFieldFilter filter = new ProfileFieldFilter
            {
                Type = request.Query.FilterEnum<ProfileFieldTypeEnum>("type", errors),
                CollectAt = request.Query.FilterEnum<ProfileCollectAtEnum>("collectAt", errors),
                Visibility = request.Query.FilterEnum<ProfileVisibilityEnum>("visibility", errors),
                Enabled = request.Query.FilterBool("enabled", errors),
                BuiltIn = request.Query.FilterBool("builtIn", errors),
                Filterable = request.Query.FilterBool("filterable", errors),
                ShowOnCard = request.Query.FilterBool("showOnCard", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<ProfileField> page = await _Database.ProfileFields.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            await _Metadata.AttachAsync(TargetTypeEnum.ProfileField, page.Objects, f => f.Id, true, token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// GET /api/v1/admin/profile-fields/{id}.
        /// </summary>
        /// <param name="id">Field identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definition with labels and tags.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ProfileField> GetAsync(string id, CancellationToken token = default)
        {
            ProfileField field = await RequireAsync(id, token).ConfigureAwait(false);
            return await _Metadata.AttachOneAsync(TargetTypeEnum.ProfileField, field, field.Id, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/profile-fields.
        /// </summary>
        /// <param name="actor">Staff caller.</param>
        /// <param name="body">Definition (ids, builtIn, valueCount, and timestamps ignored).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created definition.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 409 FieldKeyInUse or FieldLimitReached.</exception>
        public async Task<ProfileField> CreateAsync(ContentActor actor, ProfileField body, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(body);
            ArgumentNullException.ThrowIfNull(info);
            IReadOnlyList<ProfileField> all = await FreshAsync(token).ConfigureAwait(false);
            ProfileField field = ProfileFieldRules.Copy(body);
            DateTime now = DateTime.UtcNow;
            field.Id = IdGenerator.Generate(Constants.ProfileFieldIdPrefix);
            field.BuiltIn = false;
            field.ValueCount = 0;
            field.CreatedUtc = now;
            field.UpdatedUtc = now;
            ClearChildIds(field);
            if (body.ProfileOrder == 0) field.ProfileOrder = all.Count == 0 ? 10 : Math.Min(100000, all.Max(f => f.ProfileOrder) + 10);
            if (all.Any(f => String.Equals(f.Key, field.Key, StringComparison.OrdinalIgnoreCase)))
                throw new ApiErrorException(409, "FieldKeyInUse", "A profile field with this key already exists.", new Dictionary<string, string> { ["key"] = "Another field uses that key." });
            if (all.Count(f => !f.BuiltIn) >= _Settings.ProfileFields.MaxFields)
                throw new ApiErrorException(409, "FieldLimitReached", "This community already has " + _Settings.ProfileFields.MaxFields + " profile fields.");
            Dictionary<string, string> errors = ProfileFieldRules.Validate(field, all, String.Empty);
            CardLimit(field, all, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Metadata.ValidateAsync(actor, body.Labels, body.Tags, true, token, TargetTypeEnum.ProfileField).ConfigureAwait(false);
            try
            {
                await _Database.ProfileFields.CreateAsync(field, actor.UserId, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "FieldKeyInUse", "A profile field with this key already exists.");
            }
            await _Metadata.ApplyAsync(actor, TargetTypeEnum.ProfileField, field.Id, body.Labels, body.Tags, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "profileField.create", "ProfileField", field.Id, "Created profile field " + field.Key, token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileDefinitionChanges, "action", "create");
            Invalidate();
            return await GetAsync(field.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/profile-fields/{id}: the present attributes replace the stored ones; options and subFields
        /// replace their lists (entries with an existing id are updated, others are new, missing ones are removed subject
        /// to the rules in docs/MULTIPROPERTY_CONTRACT.md section 9.4); validation and rank replace as whole objects.
        /// </summary>
        /// <param name="actor">Staff caller.</param>
        /// <param name="id">Field identifier.</param>
        /// <param name="body">Attributes.</param>
        /// <param name="present">camelCase keys present in the body.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated definition.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 409 FieldHasValues, OptionInUse, BuiltInField, FieldKeyInUse.</exception>
        public async Task<ProfileField> UpdateAsync(ContentActor actor, string id, ProfileField body, HashSet<string> present, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(body);
            ArgumentNullException.ThrowIfNull(present);
            ArgumentNullException.ThrowIfNull(info);
            ProfileField existing = await RequireAsync(id, token).ConfigureAwait(false);
            IReadOnlyList<ProfileField> all = await FreshAsync(token).ConfigureAwait(false);
            ProfileField field = ProfileFieldRules.Copy(existing);
            if (existing.BuiltIn)
            {
                ApplyBuiltIn(field, body, present);
            }
            else
            {
                if (present.Contains("key")) field.Key = body.Key;
                if (present.Contains("type")) field.Type = body.Type;
                if (existing.ValueCount > 0 && (!String.Equals(field.Key, existing.Key, StringComparison.Ordinal) || field.Type != existing.Type))
                    throw new ApiErrorException(409, "FieldHasValues", "The key and type of a field cannot change while members have values; create a new field instead.");
                if (present.Contains("label")) field.Label = body.Label;
                if (present.Contains("helpText")) field.HelpText = body.HelpText ?? String.Empty;
                if (present.Contains("placeholder")) field.Placeholder = body.Placeholder ?? String.Empty;
                if (present.Contains("rank")) field.Rank = body.Rank;
                if (present.Contains("validation")) field.Validation = body.Validation ?? new ProfileFieldValidation();
                if (present.Contains("collectAt")) field.CollectAt = body.CollectAt;
                if (present.Contains("editableBy")) field.EditableBy = body.EditableBy;
                if (present.Contains("visibility")) field.Visibility = body.Visibility;
                if (present.Contains("showOnProfile")) field.ShowOnProfile = body.ShowOnProfile;
                if (present.Contains("profileOrder")) field.ProfileOrder = body.ProfileOrder;
                if (present.Contains("showOnCard")) field.ShowOnCard = body.ShowOnCard;
                if (present.Contains("searchable")) field.Searchable = body.Searchable;
                if (present.Contains("filterable")) field.Filterable = body.Filterable;
                if (present.Contains("enabled")) field.Enabled = body.Enabled;
                if (present.Contains("options")) field.Options = MergeOptions(existing.Options, body.Options);
                if (present.Contains("subFields")) field.SubFields = MergeSubFields(existing.SubFields, body.SubFields);
                if (!ProfileFieldRules.HasOptions(field.Type)) field.Options = new List<ProfileFieldOption>();
                if (field.Type != ProfileFieldTypeEnum.Rank) field.Rank = null;
                else if (field.Rank == null) field.Rank = new ProfileRankSettings();
                if (field.Type != ProfileFieldTypeEnum.History) field.SubFields = new List<ProfileSubField>();
                Dictionary<string, string> errors = ProfileFieldRules.Validate(field, all, String.Empty);
                CardLimit(field, all, errors);
                if (errors.Count > 0) throw ApiErrorException.Validation(errors);
                List<string> removed = existing.Options.Where(o => !field.Options.Any(n => String.Equals(n.Key, o.Key, StringComparison.OrdinalIgnoreCase))).Select(o => o.Key).ToList();
                if (removed.Count > 0 && existing.ValueCount > 0)
                {
                    List<string> inUse = await _Database.ProfileValues.OptionKeysInUseAsync(existing.Id, removed, token).ConfigureAwait(false);
                    if (inUse.Count > 0)
                        throw new ApiErrorException(409, "OptionInUse", "Members have values for option " + String.Join(", ", inUse) + "; set retired instead of removing it.",
                            inUse.ToDictionary(k => "options." + k, k => "Members have values for " + k + "; set retired instead of removing it.", StringComparer.Ordinal));
                }
            }
            bool metadata = present.Contains("labels") || present.Contains("tags");
            if (metadata) await _Metadata.ValidateAsync(actor, present.Contains("labels") ? body.Labels : null, present.Contains("tags") ? body.Tags : null, true, token, TargetTypeEnum.ProfileField).ConfigureAwait(false);
            field.UpdatedUtc = DateTime.UtcNow;
            try
            {
                await _Database.ProfileFields.UpdateAsync(field, actor.UserId, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "FieldKeyInUse", "A profile field with this key already exists.");
            }
            if (metadata) await _Metadata.ApplyAsync(actor, TargetTypeEnum.ProfileField, field.Id, present.Contains("labels") ? body.Labels : null, present.Contains("tags") ? body.Tags : null, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "profileField.update", "ProfileField", field.Id, "Updated profile field " + field.Key + " (" + String.Join(", ", present.OrderBy(p => p, StringComparer.Ordinal)) + ")", token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileDefinitionChanges, "action", "update");
            Invalidate();
            return await GetAsync(field.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/profile-fields/{id}. A field with values needs deleteValues=true and confirm equal to its
        /// key; the field disappears at once and its values are deleted (in the same request; at most one property's
        /// members, bounded by the batch).
        /// </summary>
        /// <param name="actor">Staff caller.</param>
        /// <param name="id">Field identifier.</param>
        /// <param name="deleteValues">deleteValues query parameter.</param>
        /// <param name="confirm">Confirmation (the key), or null.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when values were deleted (202), false when the field had none (204).</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 409 BuiltInField or FieldHasValues, 400 ValidationFailed.</exception>
        public async Task<bool> DeleteAsync(ContentActor actor, string id, bool deleteValues, string? confirm, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ProfileField field = await RequireAsync(id, token).ConfigureAwait(false);
            if (field.BuiltIn) throw new ApiErrorException(409, "BuiltInField", "Built-in fields cannot be deleted; disable or relabel them instead.");
            IReadOnlyList<ProfileField> all = await FreshAsync(token).ConfigureAwait(false);
            ProfileField? referrer = all.FirstOrDefault(f => f.SubFields.Any(s => s.Type == ProfileSubFieldTypeEnum.RankValue && String.Equals(s.RankFieldKey, field.Key, StringComparison.OrdinalIgnoreCase)));
            if (referrer != null) throw ApiErrorException.Field("id", "The history field " + referrer.Key + " uses this rank field; change or delete it first.");
            bool hasValues = field.ValueCount > 0;
            if (hasValues)
            {
                if (!deleteValues) throw new ApiErrorException(409, "FieldHasValues", field.ValueCount + " members have values; send deleteValues=true and { \"confirm\": \"" + field.Key + "\" } to delete them.");
                if (!String.Equals(confirm, field.Key, StringComparison.Ordinal)) throw ApiErrorException.Field("confirm", "Type the field key to confirm.");
                await _Database.ProfileValues.DeleteForFieldAsync(field.Id, token).ConfigureAwait(false);
            }
            await _Database.ProfileFields.DeleteAsync(field.Id, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "profileField.delete", "ProfileField", field.Id, "Deleted profile field " + field.Key + (hasValues ? " and its values" : String.Empty), token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileDefinitionChanges, "action", "delete");
            Invalidate();
            return hasValues;
        }

        /// <summary>
        /// POST /api/v1/admin/profile-fields/reorder.
        /// </summary>
        /// <param name="actor">Staff caller.</param>
        /// <param name="order">Every field id of the property, in the new order.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The order.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed when the list is not exactly the property's fields.</exception>
        public async Task<List<string>> ReorderAsync(ContentActor actor, List<string>? order, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            IReadOnlyList<ProfileField> all = await FreshAsync(token).ConfigureAwait(false);
            List<string> ids = (order ?? new List<string>()).ToList();
            if (ids.Count > 104 || ids.Count != all.Count || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || !all.All(f => ids.Contains(f.Id)))
                throw ApiErrorException.Field("order", "List every profile field id of this community exactly once.");
            await _Database.ProfileFields.ReorderAsync(ids, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "profileField.reorder", "ProfileField", null, "Reordered " + ids.Count + " profile fields", token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileDefinitionChanges, "action", "reorder");
            Invalidate();
            return ids;
        }

        /// <summary>
        /// GET /api/v1/admin/profile-fields/{id}/stats.
        /// </summary>
        /// <param name="id">Field identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statistics.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ProfileFieldStats> StatsAsync(string id, CancellationToken token = default)
        {
            ProfileField field = await RequireAsync(id, token).ConfigureAwait(false);
            if (field.BuiltIn) return new ProfileFieldStats();
            return await _Database.ProfileValues.StatsAsync(field, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Seed the property's definitions (docs/MULTIPROPERTY_CONTRACT.md section 9.13): the built-ins when missing; on
        /// the first seed every field of the document (profileFields, or the version 1 profile section) followed by the
        /// value backfill from the Phase 1 to 2 columns; later, when the document's profile section changed (the folder
        /// wins on change in SingleProperty), merge it: new keys are created, existing keys take the document's allowed
        /// attributes, and fields missing from the document are left alone.
        /// </summary>
        /// <param name="record">Registry record.</param>
        /// <param name="property">Property document.</param>
        /// <param name="mergeOnChange">Whether a changed document is merged into existing definitions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task EnsureSeededAsync(PropertyRecord record, PropertyConfiguration property, bool mergeOnChange, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(property);
            DateTime now = DateTime.UtcNow;
            List<ProfileField> existing = await _Database.ProfileFields.ReadAllAsync(token).ConfigureAwait(false);
            foreach (ProfileField builtIn in ProfileFieldRules.BuiltIns(now))
            {
                if (existing.Any(f => String.Equals(f.Key, builtIn.Key, StringComparison.OrdinalIgnoreCase))) continue;
                ProfileField? fromDocument = property.ProfileFields?.FirstOrDefault(f => f != null && String.Equals(f.Key, builtIn.Key, StringComparison.OrdinalIgnoreCase));
                if (fromDocument != null) ApplyBuiltIn(builtIn, fromDocument, BuiltInPresent(builtIn.Key));
                if (builtIn.Key == "bio") builtIn.ProfileOrder = 5;
                await _Database.ProfileFields.CreateAsync(builtIn, null, token).ConfigureAwait(false);
                existing.Add(builtIn);
            }
            string hash = SectionHash(property);
            if (record.ProfileFieldsSeededUtc != null && (!mergeOnChange || record.ProfileFieldsHash == hash))
            {
                await _Database.ProfileValues.RecountAsync(null, token).ConfigureAwait(false);
                Invalidate();
                await FreshAsync(token).ConfigureAwait(false);
                return;
            }

            List<ProfileField> seeded = ProfileFieldRules.Seed(property, now);
            List<ProfileField> all = new List<ProfileField>(existing);
            int created = 0;
            int merged = 0;
            foreach (ProfileField field in seeded)
            {
                ProfileField? current = existing.FirstOrDefault(f => String.Equals(f.Key, field.Key, StringComparison.OrdinalIgnoreCase));
                if (current == null)
                {
                    Dictionary<string, string> errors = ProfileFieldRules.Validate(field, all, String.Empty);
                    if (errors.Count > 0)
                    {
                        _Logger.Warn("[ProfileFieldService] [p:" + record.Id + "] seeded field " + field.Key + " skipped: " + String.Join("; ", errors.Select(e => e.Key + " " + e.Value)));
                        continue;
                    }
                    await _Database.ProfileFields.CreateAsync(field, null, token).ConfigureAwait(false);
                    all.Add(field);
                    created++;
                    continue;
                }
                if (record.ProfileFieldsSeededUtc == null || current.BuiltIn) continue;
                ProfileField update = MergeFromDocument(current, field);
                Dictionary<string, string> mergeErrors = ProfileFieldRules.Validate(update, all, String.Empty);
                if (mergeErrors.Count > 0)
                {
                    _Logger.Warn("[ProfileFieldService] [p:" + record.Id + "] folder change to field " + field.Key + " not applied: " + String.Join("; ", mergeErrors.Select(e => e.Key + " " + e.Value)));
                    continue;
                }
                update.UpdatedUtc = now;
                await _Database.ProfileFields.UpdateAsync(update, null, token).ConfigureAwait(false);
                merged++;
            }
            if (record.ProfileFieldsSeededUtc == null)
            {
                List<ProfileField> fresh = await _Database.ProfileFields.ReadAllAsync(token).ConfigureAwait(false);
                ProfileLegacyFieldIds legacy = LegacyIds(fresh);
                await _Database.ProfileValues.BackfillLegacyAsync(legacy, now, token).ConfigureAwait(false);
                await _Database.ProfileValues.RecountAsync(null, token).ConfigureAwait(false);
                _Logger.Info("[ProfileFieldService] [p:" + record.Id + "] seeded " + created + " profile fields and backfilled values from the legacy columns");
            }
            else if (created > 0 || merged > 0)
            {
                _Logger.Info("[ProfileFieldService] [p:" + record.Id + "] merged the folder's profile fields: " + created + " created, " + merged + " updated");
            }
            await _Database.Registry.MarkProfileFieldsSeededAsync(record.Id, hash, now, token).ConfigureAwait(false);
            record.ProfileFieldsSeededUtc ??= now;
            record.ProfileFieldsHash = hash;
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileDefinitionChanges, "action", "seed");
            Invalidate();
            await FreshAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// The seeded fields that map the legacy columns: by key and type (rank: Rank, affiliation/city/region: Text,
        /// country: Country, goals: MultiSelect, rankHistory: History).
        /// </summary>
        /// <param name="fields">Definitions.</param>
        /// <returns>Field ids.</returns>
        public static ProfileLegacyFieldIds LegacyIds(IEnumerable<ProfileField> fields)
        {
            ArgumentNullException.ThrowIfNull(fields);
            List<ProfileField> list = fields.ToList();
            string? Id(string key, ProfileFieldTypeEnum type)
            {
                return list.FirstOrDefault(f => !f.BuiltIn && String.Equals(f.Key, key, StringComparison.Ordinal) && f.Type == type)?.Id;
            }

            ProfileField? goals = list.FirstOrDefault(f => !f.BuiltIn && f.Key == "goals" && f.Type == ProfileFieldTypeEnum.MultiSelect);
            ProfileField? history = list.FirstOrDefault(f => !f.BuiltIn && f.Key == "rankHistory" && f.Type == ProfileFieldTypeEnum.History);
            bool standardHistory = history != null && new[] { "rank", "awardedBy", "affiliation", "dateAwarded" }.All(k => history.SubFields.Any(s => s.Key == k));
            return new ProfileLegacyFieldIds
            {
                Rank = Id("rank", ProfileFieldTypeEnum.Rank),
                Affiliation = Id("affiliation", ProfileFieldTypeEnum.Text),
                City = Id("city", ProfileFieldTypeEnum.Text),
                Region = Id("region", ProfileFieldTypeEnum.Text),
                Country = Id("country", ProfileFieldTypeEnum.Country),
                Goals = goals?.Id,
                GoalsFreeTextKey = goals?.Options.FirstOrDefault(o => o.FreeText)?.Key,
                RankHistory = standardHistory ? history!.Id : null
            };
        }

        #endregion

        #region Private-Methods

        private async Task<IReadOnlyList<ProfileField>> FreshAsync(CancellationToken token)
        {
            _CachedUtc = DateTime.MinValue;
            return await AllAsync(token).ConfigureAwait(false);
        }

        private async Task<ProfileField> RequireAsync(string id, CancellationToken token)
        {
            ProfileField? field = String.IsNullOrWhiteSpace(id) ? null : await _Database.ProfileFields.ReadAsync(id, token).ConfigureAwait(false);
            if (field == null) throw ApiErrorException.NotFound("Profile field");
            // valueCount drives the key and type lock and the typed delete confirmation, so it is recomputed from the
            // stored values on every admin read (seeded and backfilled values included).
            await _Database.ProfileValues.RecountAsync(new List<string> { field.Id }, token).ConfigureAwait(false);
            return await _Database.ProfileFields.ReadAsync(id, token).ConfigureAwait(false) ?? field;
        }

        private void CardLimit(ProfileField field, IReadOnlyList<ProfileField> all, Dictionary<string, string> errors)
        {
            if (!field.ShowOnCard || !field.Enabled) return;
            int others = all.Count(f => !f.BuiltIn && f.Id != field.Id && f.Enabled && f.ShowOnCard);
            if (others + 1 > _Settings.ProfileFields.MaxCardFields)
                errors["showOnCard"] = "At most " + _Settings.ProfileFields.MaxCardFields + " fields can be shown on member cards.";
        }

        private static HashSet<string> BuiltInPresent(string key)
        {
            HashSet<string> present = new HashSet<string>(StringComparer.Ordinal) { "label", "helpText", "placeholder" };
            if (key == "avatar" || key == "bio") present.UnionWith(new[] { "collectAt", "enabled" });
            if (key == "bio") present.UnionWith(new[] { "visibility", "showOnProfile", "profileOrder" });
            return present;
        }

        private static void ApplyBuiltIn(ProfileField field, ProfileField body, HashSet<string> present)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            HashSet<string> allowed = BuiltInPresent(field.Key);
            allowed.UnionWith(new[] { "labels", "tags" });
            foreach (string key in present)
            {
                if (!allowed.Contains(key)) throw new ApiErrorException(409, "BuiltInField", "The " + key + " attribute of the built-in " + field.Key + " field cannot change.");
            }
            if (present.Contains("label"))
            {
                string label = (body.Label ?? String.Empty).Trim();
                if (label.Length < 1 || label.Length > 60) errors["label"] = "Use 1 to 60 characters.";
                else field.Label = label;
            }
            if (present.Contains("helpText"))
            {
                if ((body.HelpText ?? String.Empty).Length > 300) errors["helpText"] = "At most 300 characters.";
                else field.HelpText = body.HelpText ?? String.Empty;
            }
            if (present.Contains("placeholder"))
            {
                if ((body.Placeholder ?? String.Empty).Length > 100) errors["placeholder"] = "At most 100 characters.";
                else field.Placeholder = body.Placeholder ?? String.Empty;
            }
            if (present.Contains("collectAt"))
            {
                if (body.CollectAt == ProfileCollectAtEnum.AdminOnly) errors["collectAt"] = "Use SignUp or Profile.";
                else field.CollectAt = body.CollectAt;
            }
            if (present.Contains("enabled")) field.Enabled = body.Enabled;
            if (present.Contains("visibility"))
            {
                if (body.Visibility == ProfileVisibilityEnum.Staff) errors["visibility"] = "Use Public, Members, or Private.";
                else field.Visibility = body.Visibility;
            }
            if (present.Contains("showOnProfile")) field.ShowOnProfile = body.ShowOnProfile;
            if (present.Contains("profileOrder"))
            {
                if (body.ProfileOrder < 0 || body.ProfileOrder > 100000) errors["profileOrder"] = "Use 0 to 100000.";
                else field.ProfileOrder = body.ProfileOrder;
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        private static ProfileField MergeFromDocument(ProfileField current, ProfileField document)
        {
            ProfileField update = ProfileFieldRules.Copy(current);
            update.Label = document.Label;
            update.HelpText = document.HelpText ?? String.Empty;
            update.Placeholder = document.Placeholder ?? String.Empty;
            update.CollectAt = document.CollectAt;
            update.EditableBy = document.EditableBy;
            update.Visibility = document.Visibility;
            update.ShowOnProfile = document.ShowOnProfile;
            update.ProfileOrder = document.ProfileOrder;
            update.ShowOnCard = document.ShowOnCard;
            update.Searchable = document.Searchable;
            update.Filterable = document.Filterable;
            update.Enabled = document.Enabled;
            update.Validation = document.Validation ?? new ProfileFieldValidation();
            if (current.Type != document.Type) return update;
            update.Rank = document.Rank;
            // Options are merged by key: changed labels and colors apply, new options are added, and options missing from
            // the document are retired (never removed, so existing values stay valid).
            List<ProfileFieldOption> options = MergeOptions(current.Options, document.Options.Select(o =>
            {
                ProfileFieldOption copy = new ProfileFieldOption { Key = o.Key, Label = o.Label, Color = o.Color, MaxLevel = o.MaxLevel, FreeText = o.FreeText, Retired = o.Retired };
                ProfileFieldOption? match = current.Options.FirstOrDefault(c => String.Equals(c.Key, o.Key, StringComparison.OrdinalIgnoreCase));
                if (match != null) copy.Id = match.Id;
                return copy;
            }).ToList());
            foreach (ProfileFieldOption old in current.Options.Where(o => !options.Any(n => String.Equals(n.Key, o.Key, StringComparison.OrdinalIgnoreCase))))
            {
                ProfileFieldOption retired = new ProfileFieldOption { Id = old.Id, Key = old.Key, Label = old.Label, Color = old.Color, MaxLevel = old.MaxLevel, FreeText = old.FreeText, Retired = true };
                options.Add(retired);
            }
            update.Options = options;
            if (current.Type == ProfileFieldTypeEnum.History)
                update.SubFields = MergeSubFields(current.SubFields, document.SubFields.Select(s =>
                {
                    ProfileSubField copy = ProfileFieldRules.Copy(new ProfileField { SubFields = new List<ProfileSubField> { s } }).SubFields[0];
                    ProfileSubField? match = current.SubFields.FirstOrDefault(c => String.Equals(c.Key, s.Key, StringComparison.OrdinalIgnoreCase));
                    copy.Id = match?.Id ?? String.Empty;
                    return copy;
                }).Concat(current.SubFields.Where(c => !document.SubFields.Any(s => String.Equals(s.Key, c.Key, StringComparison.OrdinalIgnoreCase)))).ToList());
            return update;
        }

        private static List<ProfileFieldOption> MergeOptions(List<ProfileFieldOption> existing, List<ProfileFieldOption>? incoming)
        {
            List<ProfileFieldOption> result = new List<ProfileFieldOption>();
            foreach (ProfileFieldOption o in incoming ?? new List<ProfileFieldOption>())
            {
                if (o == null)
                {
                    result.Add(null!);
                    continue;
                }
                ProfileFieldOption copy = new ProfileFieldOption { Key = o.Key, Label = o.Label, Color = o.Color, MaxLevel = o.MaxLevel, FreeText = o.FreeText, Retired = o.Retired };
                copy.Id = !String.IsNullOrEmpty(o.Id) && existing.Any(e => e.Id == o.Id) ? o.Id : String.Empty;
                result.Add(copy);
            }
            return result;
        }

        private static List<ProfileSubField> MergeSubFields(List<ProfileSubField> existing, List<ProfileSubField>? incoming)
        {
            List<ProfileSubField> result = new List<ProfileSubField>();
            foreach (ProfileSubField s in incoming ?? new List<ProfileSubField>())
            {
                if (s == null)
                {
                    result.Add(null!);
                    continue;
                }
                ProfileSubField copy = ProfileFieldRules.Copy(new ProfileField { SubFields = new List<ProfileSubField> { s } }).SubFields[0];
                ProfileSubField? match = existing.FirstOrDefault(e => !String.IsNullOrEmpty(s.Id) && e.Id == s.Id);
                copy.Id = match?.Id ?? String.Empty;
                copy.Options = MergeOptions(match?.Options ?? new List<ProfileFieldOption>(), s.Options);
                result.Add(copy);
            }
            return result;
        }

        private static void ClearChildIds(ProfileField field)
        {
            foreach (ProfileFieldOption o in field.Options.Where(o => o != null)) o.Id = String.Empty;
            foreach (ProfileSubField s in field.SubFields.Where(s => s != null))
            {
                s.Id = String.Empty;
                foreach (ProfileFieldOption o in s.Options.Where(o => o != null)) o.Id = String.Empty;
            }
        }

        private static string SectionHash(PropertyConfiguration property)
        {
            object section = property.ProfileFields != null && property.ProfileFields.Count > 0 ? property.ProfileFields : property.Profile ?? new PropertyProfile();
            string json = JsonSerializer.Serialize(section);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant().Substring(0, 32);
        }

        #endregion
    }
}
