namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Configurable profile values of one property (docs/MULTIPROPERTY_CONTRACT.md sections 9.6 to 9.12): visibility
    /// filtering and the profile map of each payload, validation per type, writes (with dual writes of the Phase 1 to 2
    /// columns while profileFields.legacyFieldsMode is not Off), profileComplete and profileMissing, and the compilation
    /// of member search filters.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class ProfileValueService
    {
        #region Public-Members

        /// <summary>
        /// Word filter that screens text values (Block is 400 ContentFiltered). Set by the composition root; null skips
        /// screening.
        /// </summary>
        public WordFilterService? WordFilter { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly Regex _Phone = new Regex("^\\+[1-9][0-9]{6,14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly ProfileFieldService _Fields;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="fields">Definitions.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ProfileValueService(DatabaseDriverBase database, AppSettings settings, ProfileFieldService fields)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a viewer may see a field's values (section 9.8).
        /// </summary>
        /// <param name="visibility">Field visibility.</param>
        /// <param name="viewer">Viewer.</param>
        /// <returns>True when visible.</returns>
        public static bool IsVisible(ProfileVisibilityEnum visibility, ProfileViewerEnum viewer)
        {
            switch (visibility)
            {
                case ProfileVisibilityEnum.Public:
                    return true;
                case ProfileVisibilityEnum.Members:
                    return viewer == ProfileViewerEnum.Member || viewer == ProfileViewerEnum.Self || viewer == ProfileViewerEnum.Staff;
                case ProfileVisibilityEnum.Private:
                    return viewer == ProfileViewerEnum.Self || viewer == ProfileViewerEnum.Staff;
                default:
                    return viewer == ProfileViewerEnum.Staff;
            }
        }

        /// <summary>
        /// The viewer tier of a caller looking at a member.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="subjectUserId">Member being viewed, or null.</param>
        /// <returns>Viewer.</returns>
        public static ProfileViewerEnum ViewerOf(ContentActor? actor, string? subjectUserId)
        {
            if (actor == null || (actor.UserId == null && !actor.IsStaff)) return ProfileViewerEnum.Anonymous;
            if (actor.IsStaff || actor.IsSystemAdmin || actor.IsFeedModerator) return ProfileViewerEnum.Staff;
            if (subjectUserId != null && String.Equals(actor.UserId, subjectUserId, StringComparison.Ordinal)) return ProfileViewerEnum.Self;
            return actor.CountsAsMember ? ProfileViewerEnum.Member : ProfileViewerEnum.SignedIn;
        }

        /// <summary>
        /// The non-built-in fields a payload may carry, in profile order.
        /// </summary>
        /// <param name="kind">Payload.</param>
        /// <param name="viewer">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fields.</returns>
        public async Task<List<ProfileField>> FieldsForAsync(ProfileMapKindEnum kind, ProfileViewerEnum viewer, CancellationToken token = default)
        {
            IReadOnlyList<ProfileField> all = await _Fields.AllAsync(token).ConfigureAwait(false);
            IEnumerable<ProfileField> fields = all.Where(f => !f.BuiltIn);
            switch (kind)
            {
                case ProfileMapKindEnum.Export:
                    return fields.Where(f => f.Visibility != ProfileVisibilityEnum.Staff).ToList();
                case ProfileMapKindEnum.Staff:
                    return fields.Where(f => f.Enabled).ToList();
                case ProfileMapKindEnum.Me:
                    return fields.Where(f => f.Enabled && IsVisible(f.Visibility, ProfileViewerEnum.Self)).ToList();
                case ProfileMapKindEnum.Profile:
                    return fields.Where(f => f.Enabled && f.ShowOnProfile && IsVisible(f.Visibility, viewer)).ToList();
                default:
                    return fields.Where(f => f.Enabled && f.ShowOnCard && IsVisible(f.Visibility, viewer)).Take(_Settings.ProfileFields.MaxCardFields).ToList();
            }
        }

        /// <summary>
        /// Read one member's values.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Values.</returns>
        public Task<ProfileValueSet> ReadAsync(string userId, CancellationToken token = default)
        {
            return _Database.ProfileValues.ReadForUserAsync(userId, token);
        }

        /// <summary>
        /// Build the profile map of a payload.
        /// </summary>
        /// <param name="set">Member's values.</param>
        /// <param name="kind">Payload.</param>
        /// <param name="viewer">Viewer (Profile and Card).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Map of field key to value.</returns>
        /// <exception cref="ArgumentNullException">The values are null.</exception>
        public async Task<Dictionary<string, object?>> MapAsync(ProfileValueSet set, ProfileMapKindEnum kind, ProfileViewerEnum viewer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(set);
            List<ProfileField> fields = await FieldsForAsync(kind, viewer, token).ConfigureAwait(false);
            return Map(set, fields, kind == ProfileMapKindEnum.Me || kind == ProfileMapKindEnum.Staff);
        }

        /// <summary>
        /// Card maps of several members (UserSummary.profile).
        /// </summary>
        /// <param name="userIds">Members.</param>
        /// <param name="viewer">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Maps by member id (every requested id present).</returns>
        public async Task<Dictionary<string, Dictionary<string, object?>>> CardsAsync(List<string> userIds, ProfileViewerEnum viewer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            Dictionary<string, Dictionary<string, object?>> result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
            List<ProfileField> fields = await FieldsForAsync(ProfileMapKindEnum.Card, viewer, token).ConfigureAwait(false);
            Dictionary<string, ProfileValueSet> sets = fields.Count == 0 || userIds.Count == 0
                ? new Dictionary<string, ProfileValueSet>(StringComparer.Ordinal)
                : await _Database.ProfileValues.ReadForUsersAsync(userIds, fields.Select(f => f.Id).ToList(), token).ConfigureAwait(false);
            foreach (string id in userIds.Distinct(StringComparer.Ordinal))
                result[id] = sets.TryGetValue(id, out ProfileValueSet? set) ? Map(set, fields, false) : new Dictionary<string, object?>(StringComparer.Ordinal);
            return result;
        }

        /// <summary>
        /// The keys that keep profileComplete false (section 9.10), built-ins first, then profile order.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="set">Member's values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Missing keys (empty when complete, except for age attestation which is reported separately).</returns>
        public async Task<List<string>> MissingAsync(User user, ProfileValueSet set, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(set);
            List<string> missing = new List<string>();
            if (String.IsNullOrWhiteSpace(user.DisplayName)) missing.Add("displayName");
            if (String.IsNullOrWhiteSpace(user.Handle)) missing.Add("handle");
            IReadOnlyList<ProfileField> all = await _Fields.AllAsync(token).ConfigureAwait(false);
            foreach (ProfileField field in all.Where(f => !f.BuiltIn && f.Enabled && f.CollectAt == ProfileCollectAtEnum.SignUp && f.Validation.Required))
                if (!HasValue(set, field)) missing.Add(field.Key);
            return missing;
        }

        /// <summary>
        /// Validate a profile map and build the writes (section 9.9). Errors are added as profile.&lt;key&gt; or
        /// profile.&lt;key&gt;[i].&lt;subKey&gt;.
        /// </summary>
        /// <param name="user">Member whose profile is written.</param>
        /// <param name="profile">Keys and values (JSON null clears).</param>
        /// <param name="staff">Whether staff write it (editableBy and Staff visibility checks are skipped).</param>
        /// <param name="current">The member's current values.</param>
        /// <param name="errors">Field errors.</param>
        /// <param name="texts">Text values to screen with the word filter (field error name to text).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Writes, one per key.</returns>
        public async Task<List<ProfileFieldWrite>> BuildWritesAsync(User user, Dictionary<string, JsonElement> profile, bool staff, ProfileValueSet current,
            Dictionary<string, string> errors, Dictionary<string, string?> texts, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(errors);
            ArgumentNullException.ThrowIfNull(texts);
            IReadOnlyList<ProfileField> all = await _Fields.AllAsync(token).ConfigureAwait(false);
            List<ProfileFieldWrite> writes = new List<ProfileFieldWrite>();
            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, JsonElement> pair in profile)
            {
                string name = "profile." + pair.Key;
                ProfileField? field = all.FirstOrDefault(f => !f.BuiltIn && String.Equals(f.Key, pair.Key, StringComparison.Ordinal));
                if (field == null || (!staff && field.Visibility == ProfileVisibilityEnum.Staff))
                {
                    errors[name] = "Unknown field.";
                    continue;
                }
                if (!field.Enabled)
                {
                    errors[name] = "This field is not available.";
                    continue;
                }
                if (!staff && field.EditableBy == ProfileEditableByEnum.Staff)
                {
                    errors[name] = "This field can only be changed by staff.";
                    continue;
                }
                ProfileFieldWrite write = new ProfileFieldWrite { FieldId = field.Id };
                JsonElement value = pair.Value;
                bool empty = value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined ||
                    (value.ValueKind == JsonValueKind.String && String.IsNullOrWhiteSpace(value.GetString())) ||
                    (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0 && field.Type == ProfileFieldTypeEnum.History);
                if (empty)
                {
                    if (field.Validation.Required && field.CollectAt != ProfileCollectAtEnum.AdminOnly && !staff)
                    {
                        Fail(errors, name, "This field is required.", field.Type, "required");
                        continue;
                    }
                    writes.Add(write);
                    continue;
                }
                if (field.Type == ProfileFieldTypeEnum.History)
                {
                    List<ProfileHistoryEntry>? entries = await HistoryAsync(user, field, all, value, current, name, errors, texts, now, token).ConfigureAwait(false);
                    if (entries == null) continue;
                    write.History = entries;
                }
                else
                {
                    ProfileValue? parsed = Parse(field, value, current, name, errors, texts);
                    if (parsed == null) continue;
                    if (field.Type == ProfileFieldTypeEnum.MultiSelect && parsed.OptionKeys.Count == 0 && parsed.OtherText == null)
                    {
                        if (field.Validation.Required && !staff)
                        {
                            Fail(errors, name, "This field is required.", field.Type, "required");
                            continue;
                        }
                        writes.Add(write);
                        continue;
                    }
                    parsed.UserId = user.Id;
                    parsed.FieldId = field.Id;
                    write.Value = parsed;
                    if (field.Searchable) write.SearchWords = DisplayNameWords.Split(parsed.ValueText);
                }
                writes.Add(write);
            }
            return writes;
        }

        /// <summary>
        /// Write values and mirror them into the Phase 1 to 2 columns while the compatibility mode is not Off. The member
        /// row's legacy columns are updated on the passed object (the caller saves it).
        /// </summary>
        /// <param name="user">Member (legacy columns are set on it).</param>
        /// <param name="writes">Writes.</param>
        /// <param name="updatedByUserId">Writer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a legacy column of the member row changed.</returns>
        public async Task<bool> WriteAsync(User user, List<ProfileFieldWrite> writes, string? updatedByUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(writes);
            if (writes.Count == 0) return false;
            DateTime now = DateTime.UtcNow;
            await _Database.ProfileValues.WriteAsync(user.Id, writes, updatedByUserId, now, token).ConfigureAwait(false);
            if (_Settings.ProfileFields.LegacyFieldsMode == LegacyFieldsModeEnum.Off) return false;
            IReadOnlyList<ProfileField> all = await _Fields.AllAsync(token).ConfigureAwait(false);
            ProfileLegacyFieldIds legacy = ProfileFieldService.LegacyIds(all);
            bool changed = false;
            foreach (ProfileFieldWrite write in writes)
            {
                ProfileValue? v = write.Value;
                if (write.FieldId == legacy.Rank)
                {
                    user.Rank = v?.ValueText;
                    user.RankLevel = v?.ValueLevel ?? 0;
                    changed = true;
                }
                else if (write.FieldId == legacy.Affiliation) { user.Affiliation = v?.ValueText; changed = true; }
                else if (write.FieldId == legacy.City) { user.City = v?.ValueText; changed = true; }
                else if (write.FieldId == legacy.Region) { user.Region = v?.ValueText; changed = true; }
                else if (write.FieldId == legacy.Country) { user.Country = v?.ValueText; changed = true; }
                else if (write.FieldId == legacy.Goals)
                {
                    user.GoalsOther = v?.OtherText;
                    changed = true;
                    await _Database.Goals.ReplaceForUserAsync(user.Id, v?.OptionKeys.ToList() ?? new List<string>(), token).ConfigureAwait(false);
                }
                else if (write.FieldId == legacy.RankHistory)
                {
                    List<RankHistoryEntry> entries = (write.History ?? new List<ProfileHistoryEntry>()).Select((e, i) => new RankHistoryEntry
                    {
                        Id = e.Id,
                        UserId = user.Id,
                        Position = i,
                        Rank = Sub(e, "rank")?.ValueText ?? String.Empty,
                        AwardedByName = NullIfEmpty(Sub(e, "awardedBy")?.ValueText),
                        AwardedByUserId = Sub(e, "awardedBy")?.ValueUserId,
                        Affiliation = Sub(e, "affiliation")?.ValueText,
                        DateAwarded = Sub(e, "dateAwarded")?.ValueDate,
                        CreatedUtc = e.CreatedUtc
                    }).ToList();
                    await _Database.RankHistory.ReplaceForUserAsync(user.Id, entries, token).ConfigureAwait(false);
                }
            }
            return changed;
        }

        /// <summary>
        /// Compile filters.profile (section 9.12). Every unknown, disabled, non-filterable, or invisible field and every
        /// operator not allowed for the type gets the same message so hidden fields are not revealed.
        /// </summary>
        /// <param name="filter">filters.profile JSON object, or null.</param>
        /// <param name="viewer">Viewer.</param>
        /// <param name="errors">Field errors.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Clauses.</returns>
        public async Task<List<ProfileFilterClause>> CompileFiltersAsync(JsonElement? filter, ProfileViewerEnum viewer, Dictionary<string, string> errors, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(errors);
            List<ProfileFilterClause> clauses = new List<ProfileFilterClause>();
            if (filter == null || filter.Value.ValueKind == JsonValueKind.Null || filter.Value.ValueKind == JsonValueKind.Undefined) return clauses;
            if (filter.Value.ValueKind != JsonValueKind.Object)
            {
                errors["filters.profile"] = "Use an object of field keys to operators.";
                return clauses;
            }
            IReadOnlyList<ProfileField> all = await _Fields.AllAsync(token).ConfigureAwait(false);
            int count = 0;
            foreach (JsonProperty property in filter.Value.EnumerateObject())
            {
                string name = "filters.profile." + property.Name;
                ProfileField? field = all.FirstOrDefault(f => !f.BuiltIn && String.Equals(f.Key, property.Name, StringComparison.Ordinal));
                if (field == null || !field.Enabled || !field.Filterable || !IsVisible(field.Visibility, viewer) || property.Value.ValueKind != JsonValueKind.Object)
                {
                    errors[name] = "Unknown filter.";
                    continue;
                }
                foreach (JsonProperty op in property.Value.EnumerateObject())
                {
                    count++;
                    ProfileFilterClause? clause = Clause(field, op.Name, op.Value);
                    if (clause == null)
                    {
                        errors[name] = "Unknown filter.";
                        continue;
                    }
                    clauses.Add(clause);
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileFilters, "type", field.Type.ToString(), "operator", clause.Operator.ToString());
                }
            }
            if (count > _Settings.ProfileFields.MaxFiltersPerQuery) errors["filters.profile"] = "Use at most " + _Settings.ProfileFields.MaxFiltersPerQuery + " profile filters.";
            return clauses;
        }

        /// <summary>
        /// POST /api/v1/admin/profile-fields/preview: what a surface shows for a viewer, in the server's order and
        /// visibility (docs/MULTIPROPERTY_CONTRACT.md section 9.11), with unsaved drafts when given: drafts merge into the
        /// saved fields by key (a draft replaces the saved field with its key; new keys are added; the rest stay).
        /// </summary>
        /// <param name="request">Target, viewer, optional drafts, optional sample values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rendered items (bounded by the field count).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed for invalid drafts.</exception>
        public async Task<TrellisKit.Core.Responses.ProfilePreviewResponse> PreviewAsync(TrellisKit.Core.Requests.ProfilePreviewRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            IReadOnlyList<ProfileField> saved = await _Fields.AllAsync(token).ConfigureAwait(false);
            List<ProfileField> fields = saved.Where(f => f.BuiltIn).ToList();
            if (request.Fields != null)
            {
                if (request.Fields.Count > ProfileFieldRules.HardMaxFields) throw ApiErrorException.Field("fields", "Preview at most " + ProfileFieldRules.HardMaxFields + " fields.");
                Dictionary<string, string> errors = new Dictionary<string, string>();
                List<ProfileField> drafts = request.Fields.Where(f => f != null && !ProfileFieldRules.BuiltInKeys.Contains(f.Key, StringComparer.OrdinalIgnoreCase)).Select(ProfileFieldRules.Copy).ToList();
                // Drafts merge into the saved fields by key: a draft replaces the saved field with the same key (keeping
                // its id), and drafts with new keys are added; saved fields without a draft stay as they are.
                List<ProfileField> merged = saved.Where(f => !f.BuiltIn && !drafts.Any(d => String.Equals(d.Key, f.Key, StringComparison.OrdinalIgnoreCase))).ToList();
                for (int i = 0; i < drafts.Count; i++)
                {
                    ProfileField? replaced = saved.FirstOrDefault(f => !f.BuiltIn && String.Equals(f.Key, drafts[i].Key, StringComparison.OrdinalIgnoreCase));
                    if (String.IsNullOrEmpty(drafts[i].Id)) drafts[i].Id = replaced?.Id ?? "draft_" + i.ToString(CultureInfo.InvariantCulture);
                }
                merged.AddRange(drafts);
                for (int i = 0; i < drafts.Count; i++)
                {
                    foreach (KeyValuePair<string, string> e in ProfileFieldRules.Validate(drafts[i], merged, "fields[" + i + "].")) errors[e.Key] = e.Value;
                }
                if (errors.Count > 0) throw ApiErrorException.Validation(errors);
                fields.AddRange(merged);
            }
            else fields.AddRange(saved.Where(f => !f.BuiltIn));
            fields = fields.Where(f => f.Enabled).OrderBy(f => f.ProfileOrder).ThenBy(f => f.CreatedUtc).ToList();
            Dictionary<string, JsonElement> values = request.Values ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            ProfileViewerEnum viewer = request.Viewer;
            IEnumerable<ProfileField> selected;
            switch (request.Target)
            {
                case ProfilePreviewTargetEnum.Onboarding:
                    selected = fields.Where(f => f.CollectAt == ProfileCollectAtEnum.SignUp).OrderBy(f => f.BuiltIn ? 0 : 1).ThenBy(f => f.ProfileOrder);
                    break;
                case ProfilePreviewTargetEnum.Editor:
                    selected = fields.Where(f => f.BuiltIn || f.EditableBy == ProfileEditableByEnum.Member || IsVisible(f.Visibility, ProfileViewerEnum.Self)).OrderBy(f => f.BuiltIn ? 0 : 1).ThenBy(f => f.ProfileOrder);
                    break;
                case ProfilePreviewTargetEnum.Card:
                    selected = fields.Where(f => f.BuiltIn ? f.Key != "bio" : f.ShowOnCard && IsVisible(f.Visibility, viewer)).Take(3 + _Settings.ProfileFields.MaxCardFields);
                    break;
                default:
                    selected = fields.Where(f => f.ShowOnProfile && IsVisible(f.Visibility, viewer) && (values.Count == 0 || values.ContainsKey(f.Key)));
                    break;
            }
            TrellisKit.Core.Responses.ProfilePreviewResponse response = new TrellisKit.Core.Responses.ProfilePreviewResponse { Target = request.Target, Viewer = viewer };
            foreach (ProfileField f in selected.Take(104))
            {
                object? value = values.TryGetValue(f.Key, out JsonElement raw) && raw.ValueKind != JsonValueKind.Null ? raw : null;
                bool readOnly = request.Target == ProfilePreviewTargetEnum.Editor && !f.BuiltIn && f.EditableBy == ProfileEditableByEnum.Staff;
                response.Items.Add(new TrellisKit.Core.Responses.ProfilePreviewItem
                {
                    Key = f.Key,
                    BuiltIn = f.BuiltIn,
                    Label = f.Label,
                    Type = f.Type,
                    Required = f.Validation.Required,
                    HelpText = f.HelpText,
                    Placeholder = f.Placeholder,
                    Control = readOnly ? "readOnly" : Control(f),
                    Display = Display(f.Type),
                    Value = value,
                    FormattedValue = value == null ? null : Format(f, raw),
                    Options = ProfileFieldRules.HasOptions(f.Type) ? f.Options.Where(o => !o.Retired).ToList() : null
                });
            }
            return response;
        }

        /// <summary>
        /// Display text of a Rank value: the option label for level 0, otherwise the label, the level, and the level label
        /// (all from configuration).
        /// </summary>
        /// <param name="field">Rank field.</param>
        /// <param name="key">Option key.</param>
        /// <param name="level">Level.</param>
        /// <returns>Text.</returns>
        public static string FormatRank(ProfileField field, string? key, int level)
        {
            ArgumentNullException.ThrowIfNull(field);
            ProfileFieldOption? option = field.Options.FirstOrDefault(o => String.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));
            string label = option?.Label ?? key ?? String.Empty;
            string levelLabel = field.Rank?.LevelLabel ?? String.Empty;
            if (level <= 0 || levelLabel.Length == 0) return label;
            return label + ", " + level.ToString(CultureInfo.InvariantCulture) + " " + levelLabel;
        }

        /// <summary>
        /// Display text of a profile map value (as built by <see cref="MapAsync"/>), or null when there is nothing to show.
        /// </summary>
        /// <param name="field">Field definition.</param>
        /// <param name="value">Map value.</param>
        /// <returns>Text, or null.</returns>
        /// <exception cref="ArgumentNullException">The field is null.</exception>
        public static string? FormatValue(ProfileField field, object? value)
        {
            ArgumentNullException.ThrowIfNull(field);
            if (value == null) return null;
            JsonElement raw = JsonSerializer.SerializeToElement(value, value.GetType());
            if (raw.ValueKind == JsonValueKind.Null) return null;
            string? text = Format(field, raw);
            return String.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>
        /// Searchable fields a viewer may search.
        /// </summary>
        /// <param name="viewer">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Field ids.</returns>
        public async Task<List<string>> SearchableFieldIdsAsync(ProfileViewerEnum viewer, CancellationToken token = default)
        {
            IReadOnlyList<ProfileField> all = await _Fields.AllAsync(token).ConfigureAwait(false);
            return all.Where(f => !f.BuiltIn && f.Enabled && f.Searchable && IsVisible(f.Visibility, viewer)).Select(f => f.Id).ToList();
        }

        /// <summary>
        /// Whether a member has a value for a field.
        /// </summary>
        /// <param name="set">Values.</param>
        /// <param name="field">Field.</param>
        /// <returns>True when set.</returns>
        public static bool HasValue(ProfileValueSet set, ProfileField field)
        {
            if (field.Type == ProfileFieldTypeEnum.History) return set.History.TryGetValue(field.Id, out List<ProfileHistoryEntry>? entries) && entries.Count > 0;
            if (!set.Values.TryGetValue(field.Id, out ProfileValue? v)) return false;
            if (field.Type == ProfileFieldTypeEnum.MultiSelect) return v.OptionKeys.Count > 0 || v.OtherText != null;
            return v.ValueText != null || v.ValueNumber != null || v.ValueDate != null || v.ValueBool != null;
        }

        /// <summary>
        /// The JSON value of one stored field (section 9.6 shapes), or null.
        /// </summary>
        /// <param name="set">Values.</param>
        /// <param name="field">Field.</param>
        /// <returns>Value.</returns>
        public static object? ValueOf(ProfileValueSet set, ProfileField field)
        {
            if (!HasValue(set, field)) return null;
            if (field.Type == ProfileFieldTypeEnum.History)
            {
                return set.History[field.Id].OrderBy(e => e.Position).Select(e => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = e.Id,
                    ["values"] = field.SubFields.Where(s => e.Values.Any(v => v.SubFieldKey == s.Key)).ToDictionary(s => s.Key, s => SubValue(s, e.Values.First(v => v.SubFieldKey == s.Key)), StringComparer.Ordinal)
                }).ToList();
            }
            ProfileValue value = set.Values[field.Id];
            switch (field.Type)
            {
                case ProfileFieldTypeEnum.Number:
                    return value.ValueNumber;
                case ProfileFieldTypeEnum.Date:
                    return value.ValueDate;
                case ProfileFieldTypeEnum.Boolean:
                    return value.ValueBool;
                case ProfileFieldTypeEnum.Rank:
                    return new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = value.ValueText, ["level"] = value.ValueLevel ?? 0 };
                case ProfileFieldTypeEnum.SingleSelect:
                    return new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = value.ValueText, ["otherText"] = value.OtherText };
                case ProfileFieldTypeEnum.MultiSelect:
                    return new Dictionary<string, object?>(StringComparer.Ordinal) { ["keys"] = value.OptionKeys.ToList(), ["otherText"] = value.OtherText };
                default:
                    return value.ValueText;
            }
        }

        #endregion

        #region Private-Methods

        private static string Control(ProfileField f)
        {
            switch (f.Type)
            {
                case ProfileFieldTypeEnum.LongText: return "textarea";
                case ProfileFieldTypeEnum.Number: return "number";
                case ProfileFieldTypeEnum.Date: return "date";
                case ProfileFieldTypeEnum.Boolean: return "switch";
                case ProfileFieldTypeEnum.Url: return "url";
                case ProfileFieldTypeEnum.Email: return "email";
                case ProfileFieldTypeEnum.Phone: return "phone";
                case ProfileFieldTypeEnum.Country: return "countryPicker";
                case ProfileFieldTypeEnum.SingleSelect: return f.Options.Count(o => !o.Retired) <= 5 ? "radio" : "dropdown";
                case ProfileFieldTypeEnum.MultiSelect: return "checkboxes";
                case ProfileFieldTypeEnum.Rank: return "rankPicker";
                case ProfileFieldTypeEnum.History: return "historyEditor";
                case ProfileFieldTypeEnum.Image: return "image";
                default: return "input";
            }
        }

        private static string Display(ProfileFieldTypeEnum type)
        {
            switch (type)
            {
                case ProfileFieldTypeEnum.LongText: return "multilineText";
                case ProfileFieldTypeEnum.Number: return "number";
                case ProfileFieldTypeEnum.Date: return "date";
                case ProfileFieldTypeEnum.Boolean: return "yesNo";
                case ProfileFieldTypeEnum.Url: return "link";
                case ProfileFieldTypeEnum.Email: return "mailto";
                case ProfileFieldTypeEnum.Phone: return "tel";
                case ProfileFieldTypeEnum.Country: return "countryName";
                case ProfileFieldTypeEnum.SingleSelect: return "optionLabel";
                case ProfileFieldTypeEnum.MultiSelect: return "optionLabels";
                case ProfileFieldTypeEnum.Rank: return "rank";
                case ProfileFieldTypeEnum.History: return "historyList";
                case ProfileFieldTypeEnum.Image: return "image";
                default: return "text";
            }
        }

        private static string? Format(ProfileField f, JsonElement raw)
        {
            switch (f.Type)
            {
                case ProfileFieldTypeEnum.Rank:
                    if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("key", out JsonElement k)) return null;
                    int level = raw.TryGetProperty("level", out JsonElement l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out int lv) ? lv : 0;
                    return FormatRank(f, k.GetString(), level);
                case ProfileFieldTypeEnum.Boolean:
                    return raw.ValueKind == JsonValueKind.True ? "Yes" : raw.ValueKind == JsonValueKind.False ? "No" : null;
                case ProfileFieldTypeEnum.SingleSelect:
                {
                    string? key = raw.ValueKind == JsonValueKind.String ? raw.GetString() : raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("key", out JsonElement sk) ? sk.GetString() : null;
                    string? other = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("otherText", out JsonElement so) && so.ValueKind == JsonValueKind.String ? so.GetString() : null;
                    return other ?? f.Options.FirstOrDefault(o => String.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase))?.Label ?? key;
                }
                case ProfileFieldTypeEnum.MultiSelect:
                    if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("keys", out JsonElement keys) || keys.ValueKind != JsonValueKind.Array) return null;
                    return String.Join(", ", keys.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => f.Options.FirstOrDefault(o => String.Equals(o.Key, e.GetString(), StringComparison.OrdinalIgnoreCase))?.Label ?? e.GetString()));
                case ProfileFieldTypeEnum.History:
                    return raw.ValueKind == JsonValueKind.Array ? raw.GetArrayLength().ToString(CultureInfo.InvariantCulture) + " entries" : null;
                default:
                    return raw.ValueKind == JsonValueKind.String ? raw.GetString() : raw.GetRawText();
            }
        }

        private static Dictionary<string, object?> Map(ProfileValueSet set, List<ProfileField> fields, bool everyKey)
        {
            Dictionary<string, object?> map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (ProfileField field in fields)
            {
                object? value = ValueOf(set, field);
                if (value != null || everyKey) map[field.Key] = value;
            }
            return map;
        }

        private static object? SubValue(ProfileSubField sub, ProfileHistoryValue v)
        {
            switch (sub.Type)
            {
                case ProfileSubFieldTypeEnum.Number:
                    return v.ValueNumber;
                case ProfileSubFieldTypeEnum.Date:
                    return v.ValueDate;
                case ProfileSubFieldTypeEnum.Boolean:
                    return v.ValueBool;
                case ProfileSubFieldTypeEnum.RankValue:
                    Dictionary<string, object?> rank = new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = v.ValueText };
                    if (sub.WithLevel) rank["level"] = v.ValueLevel ?? 0;
                    return rank;
                case ProfileSubFieldTypeEnum.Member:
                    return new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = v.ValueText, ["userId"] = v.ValueUserId };
                default:
                    return v.ValueText;
            }
        }

        private static ProfileHistoryValue? Sub(ProfileHistoryEntry entry, string key)
        {
            return entry.Values.FirstOrDefault(v => v.SubFieldKey == key);
        }

        private static string? NullIfEmpty(string? value)
        {
            return String.IsNullOrEmpty(value) ? null : value;
        }

        private static void Fail(Dictionary<string, string> errors, string name, string message, ProfileFieldTypeEnum type, string rule)
        {
            errors[name] = message;
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileValidationFailures, "type", type.ToString(), "rule", rule);
        }

        private static ProfileFieldOption? Option(List<ProfileFieldOption> options, string? key, string? currentKey)
        {
            if (String.IsNullOrWhiteSpace(key)) return null;
            ProfileFieldOption? option = options.FirstOrDefault(o => String.Equals(o.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
            if (option == null) return null;
            if (option.Retired && !String.Equals(option.Key, currentKey, StringComparison.Ordinal)) return null;
            return option;
        }

        private ProfileValue? Parse(ProfileField field, JsonElement value, ProfileValueSet current, string name, Dictionary<string, string> errors, Dictionary<string, string?> texts)
        {
            ProfileFieldValidation v = field.Validation;
            current.Values.TryGetValue(field.Id, out ProfileValue? existing);
            switch (field.Type)
            {
                case ProfileFieldTypeEnum.Text:
                case ProfileFieldTypeEnum.LongText:
                {
                    if (value.ValueKind != JsonValueKind.String) { Fail(errors, name, "Must be text.", field.Type, "format"); return null; }
                    string text = field.Type == ProfileFieldTypeEnum.Text ? value.GetString()!.Trim() : value.GetString()!.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
                    int max = v.MaxLength ?? (field.Type == ProfileFieldTypeEnum.Text ? 200 : 2000);
                    if (text.Length > max) { Fail(errors, name, "Must be " + max + " characters or fewer.", field.Type, "length"); return null; }
                    if (v.MinLength != null && text.Length < v.MinLength) { Fail(errors, name, "Must be at least " + v.MinLength + " characters.", field.Type, "length"); return null; }
                    if (field.Type == ProfileFieldTypeEnum.Text && !String.IsNullOrEmpty(v.Pattern))
                    {
                        bool matches;
                        try
                        {
                            matches = Regex.IsMatch(text, v.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                        }
                        catch (RegexMatchTimeoutException)
                        {
                            matches = false;
                        }
                        if (!matches) { Fail(errors, name, String.IsNullOrWhiteSpace(v.PatternMessage) ? "That value is not in the expected format." : v.PatternMessage!, field.Type, "pattern"); return null; }
                    }
                    texts[name] = text;
                    return new ProfileValue { ValueText = text, ValueNormalized = field.Type == ProfileFieldTypeEnum.Text ? ProfileFieldRules.Normalize(text) : null };
                }
                case ProfileFieldTypeEnum.Number:
                {
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || Double.IsNaN(number) || Double.IsInfinity(number))
                    { Fail(errors, name, "Must be a number.", field.Type, "format"); return null; }
                    if (v.IntegerOnly && Math.Floor(number) != number) { Fail(errors, name, "Must be a whole number.", field.Type, "format"); return null; }
                    if (Math.Round(number, 4) != number) { Fail(errors, name, "Use at most 4 decimal places.", field.Type, "format"); return null; }
                    if (v.MinValue != null && number < v.MinValue) { Fail(errors, name, "Must be at least " + v.MinValue.Value.ToString(CultureInfo.InvariantCulture) + ".", field.Type, "range"); return null; }
                    if (v.MaxValue != null && number > v.MaxValue) { Fail(errors, name, "Must be at most " + v.MaxValue.Value.ToString(CultureInfo.InvariantCulture) + ".", field.Type, "range"); return null; }
                    return new ProfileValue { ValueNumber = number };
                }
                case ProfileFieldTypeEnum.Date:
                {
                    string? date = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : null;
                    if (!ProfileFieldRules.IsDate(date)) { Fail(errors, name, "Use a date in the form YYYY-MM-DD.", field.Type, "date"); return null; }
                    if (v.NotInFuture && String.CompareOrdinal(date, DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) > 0) { Fail(errors, name, "That date is in the future.", field.Type, "date"); return null; }
                    if (v.MinDate != null && String.CompareOrdinal(date, v.MinDate) < 0) { Fail(errors, name, "Use " + v.MinDate + " or later.", field.Type, "date"); return null; }
                    if (v.MaxDate != null && String.CompareOrdinal(date, v.MaxDate) > 0) { Fail(errors, name, "Use " + v.MaxDate + " or earlier.", field.Type, "date"); return null; }
                    return new ProfileValue { ValueDate = date };
                }
                case ProfileFieldTypeEnum.Boolean:
                    if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) { Fail(errors, name, "Must be true or false.", field.Type, "format"); return null; }
                    return new ProfileValue { ValueBool = value.GetBoolean() };
                case ProfileFieldTypeEnum.Url:
                {
                    string? text = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : null;
                    if (text == null || text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || String.IsNullOrEmpty(uri.Host))
                    { Fail(errors, name, "Use a full web address starting with https://.", field.Type, "format"); return null; }
                    return new ProfileValue { ValueText = text };
                }
                case ProfileFieldTypeEnum.Email:
                {
                    string? text = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : null;
                    int at = text?.IndexOf('@') ?? -1;
                    if (text == null || text.Length > 254 || at < 1 || text.IndexOf('@', at + 1) >= 0 || !text.Substring(at + 1).Contains('.', StringComparison.Ordinal) || text.Contains(' ', StringComparison.Ordinal))
                    { Fail(errors, name, "Use a valid email address.", field.Type, "format"); return null; }
                    return new ProfileValue { ValueText = text, ValueNormalized = text.ToLowerInvariant() };
                }
                case ProfileFieldTypeEnum.Phone:
                {
                    string? text = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : null;
                    if (text == null || !_Phone.IsMatch(text)) { Fail(errors, name, "Use an international number such as +15551234567.", field.Type, "format"); return null; }
                    return new ProfileValue { ValueText = text };
                }
                case ProfileFieldTypeEnum.Country:
                {
                    string? text = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim().ToUpperInvariant() : null;
                    if (text == null || Validators.Country(text) != null) { Fail(errors, name, "Use a two-letter ISO 3166-1 country code, for example US.", field.Type, "option"); return null; }
                    return new ProfileValue { ValueText = text };
                }
                case ProfileFieldTypeEnum.SingleSelect:
                {
                    string? key = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Object && value.TryGetProperty("key", out JsonElement k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                    ProfileFieldOption? option = Option(field.Options, key, existing?.ValueText);
                    if (option == null) { Fail(errors, name, "Choose one of the listed options.", field.Type, "option"); return null; }
                    string? other = OtherText(value, option, name, errors, texts, field.Type);
                    if (other == "\0") return null;
                    return new ProfileValue { ValueText = option.Key, ValueNormalized = option.Key.ToLowerInvariant(), OtherText = other };
                }
                case ProfileFieldTypeEnum.MultiSelect:
                {
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("keys", out JsonElement keys) || keys.ValueKind != JsonValueKind.Array)
                    { Fail(errors, name, "Send { \"keys\": [...] }.", field.Type, "format"); return null; }
                    List<string> selected = new List<string>();
                    ProfileFieldOption? freeText = null;
                    foreach (JsonElement item in keys.EnumerateArray())
                    {
                        string? key = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                        ProfileFieldOption? option = Option(field.Options, key, existing != null && existing.OptionKeys.Any(o => String.Equals(o, key, StringComparison.OrdinalIgnoreCase)) ? field.Options.FirstOrDefault(o => String.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase))?.Key : null);
                        if (option == null) { Fail(errors, name, "Choose from the listed options.", field.Type, "option"); return null; }
                        if (!selected.Contains(option.Key)) selected.Add(option.Key);
                        if (option.FreeText) freeText = option;
                    }
                    if (v.MinEntries != null && selected.Count < v.MinEntries) { Fail(errors, name, "Choose at least " + v.MinEntries + ".", field.Type, "entries"); return null; }
                    if (v.MaxEntries != null && selected.Count > v.MaxEntries) { Fail(errors, name, "Choose at most " + v.MaxEntries + ".", field.Type, "entries"); return null; }
                    string? other = freeText == null ? null : OtherText(value, freeText, name, errors, texts, field.Type);
                    if (other == "\0") return null;
                    if (freeText == null && value.TryGetProperty("otherText", out JsonElement o) && o.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(o.GetString()))
                    { Fail(errors, name, "Choose the option that takes your own text first.", field.Type, "option"); return null; }
                    ProfileValue result = new ProfileValue { OtherText = other };
                    result.OptionKeys.AddRange(selected);
                    return result;
                }
                case ProfileFieldTypeEnum.Rank:
                {
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("key", out JsonElement k) || k.ValueKind != JsonValueKind.String)
                    { Fail(errors, name, "Send { \"key\": \"...\", \"level\": 0 }.", field.Type, "format"); return null; }
                    ProfileFieldOption? option = Option(field.Options, k.GetString(), existing?.ValueText);
                    if (option == null) { Fail(errors, name, "Choose one of the listed options.", field.Type, "option"); return null; }
                    int level = 0;
                    if (value.TryGetProperty("level", out JsonElement l) && l.ValueKind != JsonValueKind.Null)
                    {
                        if (l.ValueKind != JsonValueKind.Number || !l.TryGetInt32(out level)) { Fail(errors, name, "The level must be a whole number.", field.Type, "range"); return null; }
                    }
                    int maxLevel = option.MaxLevel ?? field.Rank?.MaxLevel ?? 0;
                    if (level < 0 || level > maxLevel) { Fail(errors, name, "Use a level from 0 to " + maxLevel + ".", field.Type, "range"); return null; }
                    return new ProfileValue { ValueText = option.Key, ValueLevel = level };
                }
                default:
                    Fail(errors, name, "Unknown field.", field.Type, "format");
                    return null;
            }
        }

        private static string? OtherText(JsonElement value, ProfileFieldOption option, string name, Dictionary<string, string> errors, Dictionary<string, string?> texts, ProfileFieldTypeEnum type)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("otherText", out JsonElement o) || o.ValueKind == JsonValueKind.Null) return null;
            if (!option.FreeText) { Fail(errors, name, "Only the option that takes your own text accepts it.", type, "option"); return "\0"; }
            string text = o.ValueKind == JsonValueKind.String ? o.GetString()!.Trim() : String.Empty;
            if (text.Length == 0) return null;
            if (text.Length > 100) { Fail(errors, name, "Must be 100 characters or fewer.", type, "length"); return "\0"; }
            texts[name + ".otherText"] = text;
            return text;
        }

        private async Task<List<ProfileHistoryEntry>?> HistoryAsync(User user, ProfileField field, IReadOnlyList<ProfileField> all, JsonElement value, ProfileValueSet current,
            string name, Dictionary<string, string> errors, Dictionary<string, string?> texts, DateTime now, CancellationToken token)
        {
            if (value.ValueKind != JsonValueKind.Array) { Fail(errors, name, "Send a list of entries.", field.Type, "format"); return null; }
            int max = field.Validation.MaxEntries ?? 12;
            if (value.GetArrayLength() > max) { Fail(errors, name, "Use at most " + max + " entries.", field.Type, "entries"); return null; }
            if (field.Validation.MinEntries != null && value.GetArrayLength() < field.Validation.MinEntries) { Fail(errors, name, "Add at least " + field.Validation.MinEntries + " entries.", field.Type, "entries"); return null; }
            Dictionary<string, ProfileHistoryEntry> existing = (current.History.TryGetValue(field.Id, out List<ProfileHistoryEntry>? list) ? list : new List<ProfileHistoryEntry>()).ToDictionary(e => e.Id, StringComparer.Ordinal);
            List<ProfileHistoryEntry> result = new List<ProfileHistoryEntry>();
            int before = errors.Count;
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                string prefix = name + "[" + index + "]";
                index++;
                if (item.ValueKind != JsonValueKind.Object) { Fail(errors, prefix, "Entry is required.", field.Type, "format"); continue; }
                string? id = item.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
                if (id != null && !existing.ContainsKey(id)) { Fail(errors, prefix + ".id", "Unknown entry.", field.Type, "format"); continue; }
                ProfileHistoryEntry entry = new ProfileHistoryEntry
                {
                    Id = id ?? IdGenerator.Generate(Constants.ProfileHistoryEntryIdPrefix),
                    UserId = user.Id,
                    FieldId = field.Id,
                    CreatedUtc = id != null ? existing[id].CreatedUtc : now,
                    UpdatedUtc = now
                };
                JsonElement values = item.TryGetProperty("values", out JsonElement vs) && vs.ValueKind == JsonValueKind.Object ? vs : default;
                foreach (ProfileSubField sub in field.SubFields)
                {
                    string subName = prefix + "." + sub.Key;
                    JsonElement raw = values.ValueKind == JsonValueKind.Object && values.TryGetProperty(sub.Key, out JsonElement r) ? r : default;
                    bool empty = raw.ValueKind == JsonValueKind.Undefined || raw.ValueKind == JsonValueKind.Null || (raw.ValueKind == JsonValueKind.String && String.IsNullOrWhiteSpace(raw.GetString()));
                    if (empty)
                    {
                        if (sub.Required) Fail(errors, subName, "This field is required.", field.Type, "required");
                        continue;
                    }
                    ProfileHistoryValue? hv = await SubAsync(field, sub, all, raw, existing.TryGetValue(entry.Id, out ProfileHistoryEntry? old) ? old : null, subName, errors, texts, token).ConfigureAwait(false);
                    if (hv != null) entry.Values.Add(hv);
                }
                result.Add(entry);
            }
            if (result.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != result.Count) Fail(errors, name, "Each entry id can appear once.", field.Type, "format");
            return errors.Count > before ? null : result;
        }

        private async Task<ProfileHistoryValue?> SubAsync(ProfileField field, ProfileSubField sub, IReadOnlyList<ProfileField> all, JsonElement raw, ProfileHistoryEntry? old,
            string name, Dictionary<string, string> errors, Dictionary<string, string?> texts, CancellationToken token)
        {
            ProfileHistoryValue? previous = old?.Values.FirstOrDefault(v => v.SubFieldKey == sub.Key);
            ProfileHistoryValue result = new ProfileHistoryValue { SubFieldKey = sub.Key };
            switch (sub.Type)
            {
                case ProfileSubFieldTypeEnum.Text:
                case ProfileSubFieldTypeEnum.Url:
                {
                    if (raw.ValueKind != JsonValueKind.String) { Fail(errors, name, "Must be text.", field.Type, "format"); return null; }
                    string text = raw.GetString()!.Trim();
                    int max = sub.Type == ProfileSubFieldTypeEnum.Url ? 2048 : sub.MaxLength ?? 100;
                    if (text.Length > max) { Fail(errors, name, "Must be " + max + " characters or fewer.", field.Type, "length"); return null; }
                    if (sub.Type == ProfileSubFieldTypeEnum.Url && (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
                    { Fail(errors, name, "Use a full web address starting with https://.", field.Type, "format"); return null; }
                    if (sub.Type == ProfileSubFieldTypeEnum.Text) texts[name] = text;
                    result.ValueText = text;
                    result.ValueNormalized = ProfileFieldRules.Normalize(text);
                    return result;
                }
                case ProfileSubFieldTypeEnum.Number:
                    if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetDouble(out double number)) { Fail(errors, name, "Must be a number.", field.Type, "format"); return null; }
                    if ((sub.MinValue != null && number < sub.MinValue) || (sub.MaxValue != null && number > sub.MaxValue)) { Fail(errors, name, "That number is out of range.", field.Type, "range"); return null; }
                    result.ValueNumber = number;
                    return result;
                case ProfileSubFieldTypeEnum.Date:
                {
                    string? date = raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : null;
                    if (!ProfileFieldRules.IsDate(date)) { Fail(errors, name, "Use a date in the form YYYY-MM-DD.", field.Type, "date"); return null; }
                    if (sub.NotInFuture && String.CompareOrdinal(date, DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) > 0) { Fail(errors, name, "That date is in the future.", field.Type, "date"); return null; }
                    result.ValueDate = date;
                    return result;
                }
                case ProfileSubFieldTypeEnum.Boolean:
                    if (raw.ValueKind != JsonValueKind.True && raw.ValueKind != JsonValueKind.False) { Fail(errors, name, "Must be true or false.", field.Type, "format"); return null; }
                    result.ValueBool = raw.GetBoolean();
                    return result;
                case ProfileSubFieldTypeEnum.SingleSelect:
                {
                    string? key = raw.ValueKind == JsonValueKind.String ? raw.GetString() : null;
                    ProfileFieldOption? option = Option(sub.Options, key, previous?.ValueText);
                    if (option == null) { Fail(errors, name, "Choose one of the listed options.", field.Type, "option"); return null; }
                    result.ValueText = option.Key;
                    return result;
                }
                case ProfileSubFieldTypeEnum.RankValue:
                {
                    ProfileField? rank = all.FirstOrDefault(f => String.Equals(f.Key, sub.RankFieldKey, StringComparison.OrdinalIgnoreCase) && f.Type == ProfileFieldTypeEnum.Rank);
                    string? key = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("key", out JsonElement k) && k.ValueKind == JsonValueKind.String ? k.GetString() : raw.ValueKind == JsonValueKind.String ? raw.GetString() : null;
                    ProfileFieldOption? option = rank == null ? null : Option(rank.Options, key, previous?.ValueText);
                    if (option == null) { Fail(errors, name, "Choose one of the listed options.", field.Type, "option"); return null; }
                    result.ValueText = option.Key;
                    if (sub.WithLevel && raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("level", out JsonElement l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out int level))
                    {
                        int maxLevel = option.MaxLevel ?? rank!.Rank?.MaxLevel ?? 0;
                        if (level < 0 || level > maxLevel) { Fail(errors, name, "Use a level from 0 to " + maxLevel + ".", field.Type, "range"); return null; }
                        result.ValueLevel = level;
                    }
                    return result;
                }
                case ProfileSubFieldTypeEnum.Member:
                {
                    string? memberName = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : null;
                    string? userId = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("userId", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString()!.Trim() : null;
                    if (String.IsNullOrEmpty(userId)) userId = null;
                    if (String.IsNullOrEmpty(memberName) && userId == null) { Fail(errors, name, "Enter a name.", field.Type, "required"); return null; }
                    if ((memberName ?? String.Empty).Length > 100) { Fail(errors, name, "Must be 100 characters or fewer.", field.Type, "length"); return null; }
                    if (userId != null)
                    {
                        User? referenced = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
                        if (referenced == null || referenced.IsDeleted) { Fail(errors, name, "Unknown member.", field.Type, "option"); return null; }
                        if (String.IsNullOrEmpty(memberName)) memberName = referenced.DisplayName;
                    }
                    if (!String.IsNullOrEmpty(memberName)) texts[name] = memberName;
                    result.ValueText = memberName ?? String.Empty;
                    result.ValueUserId = userId;
                    return result;
                }
                default:
                    return null;
            }
        }

        private static ProfileFilterClause? Clause(ProfileField field, string op, JsonElement value)
        {
            if (!Enum.TryParse(op, true, out ProfileFilterOperatorEnum parsed) || !Char.IsLower(op[0]) || Int32.TryParse(op, out int _)) return null;
            ProfileFilterClause clause = new ProfileFilterClause { FieldId = field.Id, FieldType = field.Type, Operator = parsed };
            List<string>? Keys(int max)
            {
                List<string> keys = new List<string>();
                if (value.ValueKind == JsonValueKind.String) keys.Add(value.GetString()!);
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) return null;
                        keys.Add(item.GetString()!);
                    }
                }
                else return null;
                return keys.Count == 0 || keys.Count > max ? null : keys;
            }

            ProfileFieldTypeEnum t = field.Type;
            bool textEq = t == ProfileFieldTypeEnum.Text || t == ProfileFieldTypeEnum.Email;
            switch (parsed)
            {
                case ProfileFilterOperatorEnum.HasValue:
                    if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) return null;
                    clause.Flag = value.GetBoolean();
                    return clause;
                case ProfileFilterOperatorEnum.Eq:
                    if (textEq && (value.ValueKind == JsonValueKind.String || value.ValueKind == JsonValueKind.Number))
                    {
                        clause.Values.Add((value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()).Trim().ToLowerInvariant());
                        return clause;
                    }
                    if (t == ProfileFieldTypeEnum.Number && value.ValueKind == JsonValueKind.Number) { clause.Number = value.GetDouble(); return clause; }
                    if (t == ProfileFieldTypeEnum.Boolean && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)) { clause.Flag = value.GetBoolean(); return clause; }
                    // Single-value types: eq is the one-value form of in.
                    if ((t == ProfileFieldTypeEnum.SingleSelect || t == ProfileFieldTypeEnum.Country) && value.ValueKind == JsonValueKind.String) return Clause(field, "in", value);
                    return null;
                case ProfileFilterOperatorEnum.Prefix:
                    if (t != ProfileFieldTypeEnum.Text || value.ValueKind != JsonValueKind.String) return null;
                    string prefix = value.GetString()!.Trim().ToLowerInvariant();
                    if (prefix.Length < 1 || prefix.Length > 100) return null;
                    clause.Values.Add(prefix);
                    return clause;
                case ProfileFilterOperatorEnum.Gte:
                case ProfileFilterOperatorEnum.Lte:
                    if (t == ProfileFieldTypeEnum.Number && value.ValueKind == JsonValueKind.Number) { clause.Number = value.GetDouble(); return clause; }
                    if (t == ProfileFieldTypeEnum.Date && value.ValueKind == JsonValueKind.String && ProfileFieldRules.IsDate(value.GetString())) { clause.Values.Add(value.GetString()!); return clause; }
                    return null;
                case ProfileFilterOperatorEnum.In:
                {
                    if (t != ProfileFieldTypeEnum.Country && t != ProfileFieldTypeEnum.SingleSelect && t != ProfileFieldTypeEnum.Rank) return null;
                    List<string>? keys = Keys(20);
                    if (keys == null) return null;
                    if (t == ProfileFieldTypeEnum.Country) clause.Values.AddRange(keys.Select(k => k.Trim().ToUpperInvariant()));
                    else
                    {
                        foreach (string key in keys)
                        {
                            ProfileFieldOption? option = field.Options.FirstOrDefault(o => String.Equals(o.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
                            if (option == null) return null;
                            clause.Values.Add(option.Key);
                        }
                    }
                    return clause;
                }
                case ProfileFilterOperatorEnum.Any:
                case ProfileFilterOperatorEnum.All:
                {
                    if (t != ProfileFieldTypeEnum.MultiSelect) return null;
                    List<string>? keys = Keys(20);
                    if (keys == null) return null;
                    foreach (string key in keys)
                    {
                        ProfileFieldOption? option = field.Options.FirstOrDefault(o => String.Equals(o.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (option == null) return null;
                        clause.Values.Add(option.Key);
                    }
                    return clause;
                }
                case ProfileFilterOperatorEnum.MinRank:
                case ProfileFilterOperatorEnum.MaxRank:
                {
                    if (t != ProfileFieldTypeEnum.Rank || value.ValueKind != JsonValueKind.String) return null;
                    int position = field.Options.FindIndex(o => String.Equals(o.Key, value.GetString()!.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (position < 0) return null;
                    IEnumerable<ProfileFieldOption> range = parsed == ProfileFilterOperatorEnum.MinRank ? field.Options.Skip(position) : field.Options.Take(position + 1);
                    clause.Values.AddRange(range.Select(o => o.Key).Take(200));
                    return clause;
                }
                case ProfileFilterOperatorEnum.MinLevel:
                    if (t != ProfileFieldTypeEnum.Rank || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int level)) return null;
                    clause.Number = level;
                    return clause;
                default:
                    return null;
            }
        }

        #endregion
    }
}
