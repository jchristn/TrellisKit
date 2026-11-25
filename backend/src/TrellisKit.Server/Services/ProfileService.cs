namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
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
    /// Member profiles: partial profile updates with validation, handle availability, avatars, and public profiles.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class ProfileService
    {
        #region Public-Members

        /// <summary>Optional property-scoped product metrics; set by the composition root.</summary>
        public TrellisKit.Server.Services.Metrics.ProductMetricEventService? ProductEvents { get; set; } = null;

        /// <summary>
        /// Message when a handle is already used.
        /// </summary>
        public const string HandleTaken = "That handle is taken.";

        /// <summary>
        /// Word filter that screens display names and bios (Block is 400 ContentFiltered naming displayName or bio; Flag
        /// files a system report on the member). Set by the composition root; null skips screening.
        /// </summary>
        public WordFilterService? WordFilter { get; set; } = null;

        /// <summary>
        /// Profile field definitions. Set by the composition root.
        /// </summary>
        public ProfileFieldService? ProfileFields { get; set; } = null;

        /// <summary>
        /// Profile values. Set by the composition root; null keeps only the built-in fields.
        /// </summary>
        public ProfileValueService? ProfileValues { get; set; } = null;

        /// <summary>
        /// Audit service (every profile edit is audited). Set by the composition root.
        /// </summary>
        public AuditService? Audit { get; set; } = null;

        /// <summary>
        /// Running settings (profileFields.legacyFieldsMode). Set by the composition root.
        /// </summary>
        public AppSettings? Settings { get; set; } = null;

        /// <summary>
        /// Verified rank lock (docs/TRELLISKIT.md, Ranks, curricula, and attendance): called with the member and their
        /// profile writes before a member's own edit is stored; throws 409 RankVerified for a changed verified Rank field.
        /// Null (default) applies no lock.
        /// </summary>
        public Func<string, List<ProfileFieldWrite>, CancellationToken, Task>? RankLock { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly MemberViewBuilder _Views;
        private readonly AvatarStore _Avatars;
        private readonly StorageSettings _Storage;
        private readonly PropertyConfiguration _Property;
        private readonly MetadataService _Metadata;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="views">View builder.</param>
        /// <param name="avatars">Avatar store.</param>
        /// <param name="storage">Storage settings (live instance).</param>
        /// <param name="property">Active property (profile taxonomy: ranks, levels, goals, enabled sections).</param>
        /// <param name="metadata">Labels and tags (the member's own public metadata).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ProfileService(DatabaseDriverBase database, MemberViewBuilder views, AvatarStore avatars, StorageSettings storage, PropertyConfiguration property, MetadataService metadata)
        {
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Views = views ?? throw new ArgumentNullException(nameof(views));
            _Avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply a partial profile update (PUT /api/v1/me/profile). Only the keys present in the body change. Built-in
        /// fields (displayName, handle, bio) stay top-level; configurable fields arrive in profile (validated against the
        /// definitions, docs/MULTIPROPERTY_CONTRACT.md section 9.9) or, during the compatibility window, as the legacy
        /// fields rank, rankLevel, affiliation, city, region, country, goals, goalsOther, and rankHistory (section 9.14),
        /// which map onto the seeded fields with the same keys and keep their Phase 1 messages. Every edit is audited
        /// (key names only).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">camelCase keys present in the JSON body.</param>
        /// <param name="actor">The member (labels and tags: the member edits their own public metadata; staff: entries are ignored for non-staff; muted members cannot change them).</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="info">Request (audit and client telemetry), or null.</param>
        /// <returns>Me.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered (fieldErrors.handle is "That handle is taken." when not unique), 403 Muted.</exception>
        public async Task<MeResponse> UpdateAsync(string userId, ProfileUpdateRequest request, HashSet<string> present, ContentActor actor, CancellationToken token = default, RequestInfo? info = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            ArgumentNullException.ThrowIfNull(actor);
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string previousName = user.DisplayName;
            string? previousBio = user.Bio;

            if (present.Contains("displayName"))
            {
                string? error = Validators.DisplayName(request.DisplayName);
                if (error != null) errors["displayName"] = error;
                else user.DisplayName = request.DisplayName!.Trim();
            }
            if (present.Contains("handle"))
            {
                if (request.Handle == null) user.Handle = null;
                else
                {
                    string? error = Validators.Handle(request.Handle);
                    if (error != null) errors["handle"] = error;
                    else
                    {
                        string handle = request.Handle.Trim();
                        User? holder = await _Database.Users.ReadByHandleAsync(handle, token).ConfigureAwait(false);
                        if (holder != null && holder.Id != user.Id) errors["handle"] = HandleTaken;
                        else user.Handle = handle;
                    }
                }
            }
            if (present.Contains("bio"))
            {
                ProfileField? bio = ProfileFields?.Snapshot.FirstOrDefault(f => f.BuiltIn && f.Key == "bio");
                if (bio != null && !bio.Enabled && Clean(request.Bio) != null) errors["bio"] = "This field is not available.";
                else Text(request.Bio, 1000, "bio", errors, v => user.Bio = v);
            }

            ProfileValueSet current = ProfileValues == null ? new ProfileValueSet { UserId = user.Id } : await ProfileValues.ReadAsync(user.Id, token).ConfigureAwait(false);
            List<ProfileFieldWrite> writes = new List<ProfileFieldWrite>();
            Dictionary<string, string?> texts = new Dictionary<string, string?>(StringComparer.Ordinal);
            Dictionary<string, JsonElement> profile = present.Contains("profile") && request.Profile != null ? request.Profile : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            List<string> legacy = _LegacyKeys.Where(present.Contains).ToList();
            if (legacy.Count > 0)
            {
                TrellisKit.Core.Enums.LegacyFieldsModeEnum mode = Settings?.ProfileFields.LegacyFieldsMode ?? TrellisKit.Core.Enums.LegacyFieldsModeEnum.ReadWrite;
                foreach (string key in legacy)
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.ProfileLegacyFields, 1, "direction", "write", "field", key, "client", ClientLabel(info));
                if (mode != TrellisKit.Core.Enums.LegacyFieldsModeEnum.ReadWrite)
                {
                    foreach (string key in legacy) errors[key] = "This field moved to profile." + LegacyTarget(key) + ".";
                }
                else
                {
                    bool conflict = false;
                    foreach (string key in legacy)
                    {
                        if (!profile.ContainsKey(LegacyTarget(key))) continue;
                        errors[key] = "Send either " + key + " or profile." + LegacyTarget(key) + ", not both.";
                        conflict = true;
                    }
                    if (!conflict) writes.AddRange(await LegacyWritesAsync(user, request, present, current, errors, texts, token).ConfigureAwait(false));
                }
            }
            if (profile.Count > 0 && ProfileValues != null)
                writes.AddRange(await ProfileValues.BuildWritesAsync(user, profile, false, current, errors, texts, token).ConfigureAwait(false));

            if (errors.Count > 0)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileUpdates, "source", "member", "outcome", "invalid");
                throw ApiErrorException.Validation(errors);
            }
            if (RankLock != null && writes.Count > 0) await RankLock(user.Id, writes, token).ConfigureAwait(false);
            bool nameChanged = !String.Equals(previousName, user.DisplayName, StringComparison.Ordinal);
            bool bioChanged = !String.Equals(previousBio, user.Bio, StringComparison.Ordinal);
            bool self = String.Equals(actor.UserId, user.Id, StringComparison.Ordinal);
            // A muted member cannot change their display name or bio until the mute ends (unchanged values pass).
            if (self && (nameChanged || bioChanged)) actor.RequireNotMuted();
            WordFilterResult? flags = null;
            if (WordFilter != null && (nameChanged || bioChanged || texts.Count > 0))
            {
                // The member's words are screened even when staff make the edit: the subject is the profile's owner.
                ContentActor subject = self ? actor : new ContentActor { UserId = user.Id };
                Dictionary<string, string?> fields = new Dictionary<string, string?>(StringComparer.Ordinal);
                if (nameChanged) fields["displayName"] = user.DisplayName;
                if (bioChanged) fields["bio"] = user.Bio;
                foreach (KeyValuePair<string, string?> text in texts) fields[text.Key] = text.Value;
                flags = await WordFilter.CheckAsync(subject, TargetTypeEnum.User, fields, token).ConfigureAwait(false);
            }
            bool metadataChanged = request.Labels != null || request.Tags != null;
            if (metadataChanged)
            {
                actor.RequireNotMuted();
                await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.User), token).ConfigureAwait(false);
            }

            user.UpdatedUtc = DateTime.UtcNow;
            if (ProfileValues != null && writes.Count > 0) await ProfileValues.WriteAsync(user, writes, actor.UserId ?? user.Id, token).ConfigureAwait(false);
            try
            {
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw ApiErrorException.Field("handle", HandleTaken);
            }
            if (present.Contains("displayName")) await _Database.MemberSearch.ReplaceWordsAsync(user.Id, user.DisplayName, token).ConfigureAwait(false);
            if (metadataChanged) await _Metadata.ApplyAsync(actor, TargetTypeEnum.User, user.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            if (WordFilter != null) await WordFilter.RecordFlagsAsync(flags, TargetTypeEnum.User, user.Id, user.Id, token).ConfigureAwait(false);
            List<string> changedKeys = present.Where(k => k != "labels" && k != "tags" && k != "profile").Concat(profile.Keys.Select(k => "profile." + k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (Audit != null && changedKeys.Count > 0)
            {
                // Resolved decision 12: every profile edit is audited, members' own edits included (key names only).
                RequestInfo audit = info ?? new RequestInfo();
                audit.Context ??= new TrellisKit.Core.Security.RequestContext { UserId = actor.UserId, PrincipalName = actor.User?.DisplayName };
                await Audit.WriteAsync(audit, self ? "profile.update" : "member.profile", "User", user.Id, "Changed " + String.Join(", ", changedKeys), token).ConfigureAwait(false);
            }
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileUpdates, "source", self ? "member" : "staff", "outcome", "ok");
            MeResponse response = await _Views.BuildMeAsync(user, token).ConfigureAwait(false);
            if (response.ProfileComplete && ProductEvents != null) await ProductEvents.ObserveAsync("profile.completed", user.Id, token).ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// PUT /api/v1/admin/members/{id}/profile: staff write any configurable field (AdminOnly and Staff visibility
        /// included) with the member rules except editableBy and Staff visibility. Audited member.profile (key names only).
        /// </summary>
        /// <param name="actor">Staff caller.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="profile">Keys and values.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The member after the write.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 404 NotFound.</exception>
        public async Task<User> UpdateByStaffAsync(ContentActor actor, string userId, Dictionary<string, JsonElement>? profile, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            User? user = String.IsNullOrWhiteSpace(userId) ? null : await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            if (ProfileValues == null) return user;
            Dictionary<string, JsonElement> values = profile ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            Dictionary<string, string?> texts = new Dictionary<string, string?>(StringComparer.Ordinal);
            ProfileValueSet current = await ProfileValues.ReadAsync(user.Id, token).ConfigureAwait(false);
            List<ProfileFieldWrite> writes = await ProfileValues.BuildWritesAsync(user, values, true, current, errors, texts, token).ConfigureAwait(false);
            if (errors.Count > 0)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileUpdates, "source", "staff", "outcome", "invalid");
                throw ApiErrorException.Validation(errors);
            }
            WordFilterResult? flags = null;
            if (WordFilter != null && texts.Count > 0) flags = await WordFilter.CheckAsync(new ContentActor { UserId = user.Id }, TargetTypeEnum.User, texts, token).ConfigureAwait(false);
            if (writes.Count > 0 && await ProfileValues.WriteAsync(user, writes, actor.UserId, token).ConfigureAwait(false))
            {
                user.UpdatedUtc = DateTime.UtcNow;
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            }
            if (WordFilter != null) await WordFilter.RecordFlagsAsync(flags, TargetTypeEnum.User, user.Id, user.Id, token).ConfigureAwait(false);
            if (Audit != null && values.Count > 0)
                await Audit.WriteAsync(info, "member.profile", "User", user.Id, "Changed " + String.Join(", ", values.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => "profile." + k)), token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ProfileUpdates, "source", "staff", "outcome", "ok");
            return user;
        }

        /// <summary>
        /// Whether a handle is available to a member (their own handle counts as available).
        /// </summary>
        /// <param name="handle">Handle.</param>
        /// <param name="userId">Asking member, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Availability.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed for an invalid handle.</exception>
        public async Task<AvailabilityResponse> HandleAvailabilityAsync(string? handle, string? userId, CancellationToken token = default)
        {
            string? error = Validators.Handle(handle);
            if (error != null) throw ApiErrorException.Field("handle", error);
            User? holder = await _Database.Users.ReadByHandleAsync(handle!.Trim(), token).ConfigureAwait(false);
            return new AvailabilityResponse { Available = holder == null || holder.Id == userId };
        }

        /// <summary>
        /// Replace the member's avatar.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="contentType">Declared content type.</param>
        /// <param name="data">Image bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Me.</returns>
        /// <exception cref="ApiErrorException">400 InvalidImage or 413 TooLarge.</exception>
        public async Task<MeResponse> SetAvatarAsync(string userId, string? contentType, byte[] data, CancellationToken token = default)
        {
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            RequireNotMuted(user);
            if (data == null || data.Length == 0) throw new ApiErrorException(400, "InvalidImage", "Send a JPEG, PNG, or WebP image as the request body.");
            if (data.Length > _Storage.MaxAvatarBytes) throw TooLarge(_Storage.MaxAvatarBytes);
            string declared = (contentType ?? String.Empty).Split(';')[0].Trim().ToLowerInvariant();
            string? extension = AvatarStore.SniffExtension(data);
            if (extension == null || declared != AvatarStore.ContentTypeFor(extension))
                throw new ApiErrorException(400, "InvalidImage", "The image must be a JPEG, PNG, or WebP file sent with its matching Content-Type.");

            string previous = user.AvatarFile ?? String.Empty;
            user.AvatarFile = await _Avatars.SaveAsync(user.Id, data, extension, token).ConfigureAwait(false);
            user.UpdatedUtc = DateTime.UtcNow;
            await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            if (previous.Length > 0) _Avatars.Delete(previous);
            return await _Views.BuildMeAsync(user, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove the member's avatar.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Me.</returns>
        public async Task<MeResponse> DeleteAvatarAsync(string userId, CancellationToken token = default)
        {
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            RequireNotMuted(user);
            if (user.AvatarFile != null)
            {
                _Avatars.Delete(user.AvatarFile);
                user.AvatarFile = null;
                user.UpdatedUtc = DateTime.UtcNow;
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            }
            return await _Views.BuildMeAsync(user, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A member's public profile.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile.</returns>
        /// <exception cref="ApiErrorException">404 for unknown, banned, or deleted accounts.</exception>
        public async Task<PublicProfileResponse> GetPublicAsync(string id, CancellationToken token = default)
        {
            return await GetPublicAsync(id, ProfileViewerEnum.Member, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A member's public profile as seen by a viewer (profile map and legacy fields filtered by visibility).
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="viewer">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile.</returns>
        /// <exception cref="ApiErrorException">404 for unknown, banned, or deleted accounts.</exception>
        public async Task<PublicProfileResponse> GetPublicAsync(string id, ProfileViewerEnum viewer, CancellationToken token = default)
        {
            User? user = String.IsNullOrWhiteSpace(id) ? null : await _Database.Users.ReadAsync(id, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted || user.IsBanned) throw ApiErrorException.NotFound("Member");
            return await _Views.BuildPublicAsync(user, viewer, token).ConfigureAwait(false);
        }

        /// <summary>
        /// 413 TooLarge error.
        /// </summary>
        /// <param name="maxBytes">Limit.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException TooLarge(int maxBytes)
        {
            return new ApiErrorException(413, "TooLarge", "The image is larger than " + (maxBytes / (1024 * 1024.0)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB.");
        }

        #endregion

        #region Private-Methods

        private static readonly List<string> _LegacyKeys = new List<string> { "rank", "rankLevel", "affiliation", "city", "region", "country", "goals", "goalsOther", "rankHistory" };

        private static string LegacyTarget(string key)
        {
            if (key == "rankLevel") return "rank";
            if (key == "goalsOther") return "goals";
            return key;
        }

        private static string ClientLabel(RequestInfo? info)
        {
            if (info == null) return "other";
            if (info.ClientType == ClientTypeEnum.Web) return "web";
            if (info.ClientType == ClientTypeEnum.Mobile) return "mobile";
            return "other";
        }

        private ProfileField? Definition(string key, ProfileFieldTypeEnum type)
        {
            return ProfileFields?.Snapshot.FirstOrDefault(f => !f.BuiltIn && f.Key == key && f.Type == type);
        }

        private async Task<List<ProfileFieldWrite>> LegacyWritesAsync(User user, ProfileUpdateRequest request, HashSet<string> present, ProfileValueSet current,
            Dictionary<string, string> errors, Dictionary<string, string?> texts, CancellationToken token)
        {
            List<ProfileFieldWrite> writes = new List<ProfileFieldWrite>();
            DateTime now = DateTime.UtcNow;

            ProfileField? rankField = Definition("rank", ProfileFieldTypeEnum.Rank);
            bool rankEnabled = rankField != null && rankField.Enabled;
            if (present.Contains("rank") || present.Contains("rankLevel"))
            {
                current.Values.TryGetValue(rankField?.Id ?? String.Empty, out ProfileValue? existing);
                string? key = existing?.ValueText;
                int level = existing?.ValueLevel ?? 0;
                ProfileFieldOption? option = null;
                bool ok = true;
                if (present.Contains("rank"))
                {
                    if (String.IsNullOrWhiteSpace(request.Rank)) key = null;
                    else if (!rankEnabled) { errors["rank"] = Disabled("rank"); ok = false; }
                    else
                    {
                        option = rankField!.Options.FirstOrDefault(o => String.Equals(o.Key, request.Rank.Trim(), StringComparison.OrdinalIgnoreCase) && (!o.Retired || o.Key == existing?.ValueText));
                        if (option != null) key = option.Key;
                        else { errors["rank"] = "Use one of " + String.Join(", ", rankField.Options.Where(o => !o.Retired).Select(o => o.Key)) + "."; ok = false; }
                    }
                }
                if (present.Contains("rankLevel"))
                {
                    int requested = request.RankLevel ?? 0;
                    option ??= rankField?.Options.FirstOrDefault(o => o.Key == key);
                    int max = option?.MaxLevel ?? rankField?.Rank?.MaxLevel ?? 0;
                    if (requested != 0 && !rankEnabled) { errors["rankLevel"] = Disabled("rank"); ok = false; }
                    else if (requested < 0 || requested > max) { errors["rankLevel"] = "Use 0 to " + max + "."; ok = false; }
                    else level = requested;
                }
                if (ok && rankField != null)
                    writes.Add(new ProfileFieldWrite { FieldId = rankField.Id, Value = key == null ? null : new ProfileValue { ValueText = key, ValueLevel = level } });
            }

            void LegacyText(string key, ProfileFieldTypeEnum type, int fallbackMax, string? value)
            {
                ProfileField? field = Definition(key, type);
                string? cleaned = Clean(value);
                if (type == ProfileFieldTypeEnum.Country) cleaned = cleaned?.ToUpperInvariant();
                if ((field == null || !field.Enabled) && cleaned != null)
                {
                    errors[key] = Disabled(key);
                    return;
                }
                string? error = type == ProfileFieldTypeEnum.Country ? Validators.Country(cleaned) : Validators.MaxLength(cleaned, field?.Validation.MaxLength ?? fallbackMax);
                if (error != null)
                {
                    errors[key] = error;
                    return;
                }
                if (field == null) return;
                if (cleaned != null && type == ProfileFieldTypeEnum.Text) texts[key] = cleaned;
                writes.Add(new ProfileFieldWrite
                {
                    FieldId = field.Id,
                    Value = cleaned == null ? null : new ProfileValue { ValueText = cleaned, ValueNormalized = type == ProfileFieldTypeEnum.Text ? ProfileFieldRules.Normalize(cleaned) : null },
                    SearchWords = field.Searchable ? DisplayNameWords.Split(cleaned) : new List<string>()
                });
            }

            if (present.Contains("affiliation")) LegacyText("affiliation", ProfileFieldTypeEnum.Text, 100, request.Affiliation);
            if (present.Contains("city")) LegacyText("city", ProfileFieldTypeEnum.Text, 100, request.City);
            if (present.Contains("region")) LegacyText("region", ProfileFieldTypeEnum.Text, 100, request.Region);
            if (present.Contains("country")) LegacyText("country", ProfileFieldTypeEnum.Country, 2, request.Country);

            if (present.Contains("goals") || present.Contains("goalsOther"))
            {
                ProfileField? goalsField = Definition("goals", ProfileFieldTypeEnum.MultiSelect);
                bool enabled = goalsField != null && goalsField.Enabled;
                current.Values.TryGetValue(goalsField?.Id ?? String.Empty, out ProfileValue? existing);
                List<string> keys = existing?.OptionKeys.ToList() ?? new List<string>();
                string? other = existing?.OtherText;
                bool ok = true;
                if (present.Contains("goals"))
                {
                    List<string> requested = request.Goals ?? new List<string>();
                    keys = new List<string>();
                    if (!enabled && requested.Count > 0) { errors["goals"] = Disabled("goals"); ok = false; }
                    else
                    {
                        foreach (string name in requested)
                        {
                            ProfileFieldOption? goal = goalsField?.Options.FirstOrDefault(o => String.Equals(o.Key, name?.Trim(), StringComparison.OrdinalIgnoreCase) && (!o.Retired || existing?.OptionKeys.Contains(o.Key) == true));
                            if (goal == null) { errors["goals"] = "Use any of " + String.Join(", ", (goalsField?.Options ?? new List<ProfileFieldOption>()).Where(o => !o.Retired).Select(o => o.Key)) + "."; ok = false; }
                            else if (!keys.Contains(goal.Key)) keys.Add(goal.Key);
                        }
                    }
                }
                if (present.Contains("goalsOther"))
                {
                    string? cleaned = Clean(request.GoalsOther);
                    if (!enabled && cleaned != null) { errors["goalsOther"] = Disabled("goals"); ok = false; }
                    else if (Validators.MaxLength(cleaned, 200) != null) { errors["goalsOther"] = Validators.MaxLength(cleaned, 200)!; ok = false; }
                    else other = cleaned;
                }
                if (ok && goalsField != null)
                {
                    if (other != null) texts["goalsOther"] = other;
                    ProfileValue? value = keys.Count == 0 && other == null ? null : new ProfileValue { OtherText = other };
                    value?.OptionKeys.AddRange(keys);
                    writes.Add(new ProfileFieldWrite { FieldId = goalsField.Id, Value = value });
                }
            }

            if (present.Contains("rankHistory"))
            {
                ProfileField? historyField = Definition("rankHistory", ProfileFieldTypeEnum.History);
                List<RankHistoryEntryRequest> requested = request.RankHistory ?? new List<RankHistoryEntryRequest>();
                if ((historyField == null || !historyField.Enabled) && requested.Count > 0) errors["rankHistory"] = Disabled("rankHistory");
                else if (historyField != null)
                {
                    List<ProfileHistoryEntry>? entries = await LegacyHistoryAsync(user, historyField, rankField, requested, current, errors, texts, now, token).ConfigureAwait(false);
                    if (entries != null) writes.Add(new ProfileFieldWrite { FieldId = historyField.Id, History = entries });
                }
            }
            return writes;
        }

        private async Task<List<ProfileHistoryEntry>?> LegacyHistoryAsync(User user, ProfileField field, ProfileField? rankField, List<RankHistoryEntryRequest> entries,
            ProfileValueSet current, Dictionary<string, string> errors, Dictionary<string, string?> texts, DateTime now, CancellationToken token)
        {
            int maxEntries = field.Validation.MaxEntries ?? 12;
            if (entries.Count > maxEntries)
            {
                errors["rankHistory"] = "Use at most " + maxEntries + " entries.";
                return null;
            }
            int before = errors.Count;
            Dictionary<string, ProfileHistoryEntry> existing = (current.History.TryGetValue(field.Id, out List<ProfileHistoryEntry>? list) ? list : new List<ProfileHistoryEntry>()).ToDictionary(e => e.Id, StringComparer.Ordinal);
            List<ProfileHistoryEntry> result = new List<ProfileHistoryEntry>();
            ProfileField? affiliationField = Definition("affiliation", ProfileFieldTypeEnum.Text);
            List<ProfileFieldOption> ranks = rankField?.Options ?? new List<ProfileFieldOption>();
            for (int i = 0; i < entries.Count; i++)
            {
                RankHistoryEntryRequest entry = entries[i];
                string prefix = "rankHistory[" + i + "].";
                if (entry == null)
                {
                    errors["rankHistory[" + i + "]"] = "Entry is required.";
                    continue;
                }
                ProfileFieldOption? rank = String.IsNullOrWhiteSpace(entry.Rank) ? null : ranks.FirstOrDefault(o => String.Equals(o.Key, entry.Rank.Trim(), StringComparison.OrdinalIgnoreCase));
                if (rank == null) errors[prefix + "rank"] = "Use one of " + String.Join(", ", ranks.Where(o => !o.Retired).Select(o => o.Key)) + ".";
                string? name = Clean(entry.AwardedByName);
                string? affiliation = Clean(entry.Affiliation);
                if (affiliation != null && (affiliationField == null || !affiliationField.Enabled)) errors[prefix + "affiliation"] = Disabled("affiliation");
                if (Validators.MaxLength(name, 100) != null) errors[prefix + "awardedByName"] = "Use at most 100 characters.";
                if (Validators.MaxLength(affiliation, 100) != null) errors[prefix + "affiliation"] = "Use at most 100 characters.";
                string? date = Clean(entry.DateAwarded);
                if (date != null && (!Validators.TryParseDate(date, out DateTime awarded) || awarded.Date > now.Date)) errors[prefix + "dateAwarded"] = "Use YYYY-MM-DD, not in the future.";
                string? awardedBy = Clean(entry.AwardedByUserId);
                if (awardedBy != null && await _Database.Users.ReadAsync(awardedBy, token).ConfigureAwait(false) == null) errors[prefix + "awardedByUserId"] = "Unknown member.";
                string id = entry.Id != null && existing.ContainsKey(entry.Id) ? entry.Id : IdGenerator.Generate(Constants.ProfileHistoryEntryIdPrefix);
                ProfileHistoryEntry built = new ProfileHistoryEntry { Id = id, UserId = user.Id, FieldId = field.Id, Position = i, CreatedUtc = existing.TryGetValue(id, out ProfileHistoryEntry? old) ? old.CreatedUtc : now, UpdatedUtc = now };
                if (rank != null) built.Values.Add(new ProfileHistoryValue { SubFieldKey = "rank", ValueText = rank.Key });
                if (name != null || awardedBy != null)
                {
                    built.Values.Add(new ProfileHistoryValue { SubFieldKey = "awardedBy", ValueText = name ?? String.Empty, ValueUserId = awardedBy });
                    if (name != null) texts[prefix + "awardedByName"] = name;
                }
                if (affiliation != null)
                {
                    built.Values.Add(new ProfileHistoryValue { SubFieldKey = "affiliation", ValueText = affiliation, ValueNormalized = ProfileFieldRules.Normalize(affiliation) });
                    texts[prefix + "affiliation"] = affiliation;
                }
                if (date != null) built.Values.Add(new ProfileHistoryValue { SubFieldKey = "dateAwarded", ValueDate = date });
                result.Add(built);
            }
            if (result.Select(r => r.Id).Distinct().Count() != result.Count) errors["rankHistory"] = "Each entry id can appear once.";
            return errors.Count > before ? null : result;
        }

        private async Task<User> RequireUserAsync(string userId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(userId)) throw ApiErrorException.Forbidden("This route needs a member session.");
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw new ApiErrorException(401, "Unauthorized", "Sign in again.");
            return user;
        }

        private static void RequireNotMuted(User user)
        {
            if (user.IsMutedAt(DateTime.UtcNow)) throw ContentActor.MutedError(user.MutedUntilUtc);
        }

        private static void Text(string? value, int max, string field, Dictionary<string, string> errors, Action<string?> apply)
        {
            string? cleaned = Clean(value);
            string? error = Validators.MaxLength(cleaned, max);
            if (error != null) errors[field] = error;
            else apply(cleaned);
        }

        private static string? Clean(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Disabled(string section)
        {
            return "The " + section + " field is not enabled for this community.";
        }

        #endregion
    }
}
