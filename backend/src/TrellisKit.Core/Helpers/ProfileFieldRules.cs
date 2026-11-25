namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Definition rules of configurable profile fields (docs/MULTIPROPERTY_CONTRACT.md sections 9.1 to 9.4), the four
    /// built-in fields (9.3), and the conversion of a schema version 1 profile section into seeded fields (9.13).
    /// Engine-neutral: every label, option, and rank comes from the property.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class ProfileFieldRules
    {
        #region Public-Members

        /// <summary>
        /// Hard maximum of non-built-in fields per property.
        /// </summary>
        public const int HardMaxFields = 100;

        /// <summary>
        /// Keys of the built-in fields.
        /// </summary>
        public static readonly IReadOnlyList<string> BuiltInKeys = new[] { "displayName", "handle", "avatar", "bio" };

        /// <summary>
        /// Keys no profile field may use (besides the built-ins).
        /// </summary>
        public static readonly IReadOnlyList<string> ReservedKeys = new[]
        {
            "id", "email", "displayName", "handle", "avatar", "avatarUrl", "bio", "roles", "flags", "membership", "profile", "labels", "tags",
            "createdUtc", "updatedUtc", "isStaff", "isDeleted"
        };

        #endregion

        #region Private-Members

        private static readonly Regex _Key = new Regex("^[a-z][A-Za-z0-9]{0,39}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _OptionKey = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Color = new Regex("^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<ProfileFieldTypeEnum> _SelectTypes = new HashSet<ProfileFieldTypeEnum> { ProfileFieldTypeEnum.SingleSelect, ProfileFieldTypeEnum.MultiSelect, ProfileFieldTypeEnum.Rank };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a type takes options.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>True for SingleSelect, MultiSelect, and Rank.</returns>
        public static bool HasOptions(ProfileFieldTypeEnum type)
        {
            return _SelectTypes.Contains(type);
        }

        /// <summary>
        /// Lowercased, trimmed text used for equality and prefix filters.
        /// </summary>
        /// <param name="value">Text, or null.</param>
        /// <returns>Normalized text, or null.</returns>
        public static string? Normalize(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// The four built-in fields with their engine defaults (labels are engine-neutral words; a property relabels them).
        /// </summary>
        /// <param name="nowUtc">Creation time.</param>
        /// <returns>Built-in definitions (ids assigned).</returns>
        public static List<ProfileField> BuiltIns(DateTime nowUtc)
        {
            ProfileField Make(string key, string label, ProfileFieldTypeEnum type, ProfileCollectAtEnum collectAt, bool required, int order, bool card, int? maxLength)
            {
                return new ProfileField
                {
                    Id = IdGenerator.Generate(Constants.ProfileFieldIdPrefix),
                    Key = key,
                    BuiltIn = true,
                    Label = label,
                    Type = type,
                    CollectAt = collectAt,
                    EditableBy = ProfileEditableByEnum.Member,
                    Visibility = ProfileVisibilityEnum.Public,
                    ShowOnProfile = true,
                    ShowOnCard = card,
                    ProfileOrder = order,
                    Enabled = true,
                    Validation = new ProfileFieldValidation { Required = required, MaxLength = maxLength },
                    CreatedUtc = nowUtc,
                    UpdatedUtc = nowUtc
                };
            }

            return new List<ProfileField>
            {
                Make("displayName", "Name", ProfileFieldTypeEnum.Text, ProfileCollectAtEnum.SignUp, true, 0, true, 50),
                Make("handle", "Handle", ProfileFieldTypeEnum.Text, ProfileCollectAtEnum.SignUp, true, 1, true, 30),
                Make("avatar", "Photo", ProfileFieldTypeEnum.Image, ProfileCollectAtEnum.Profile, false, 2, true, null),
                Make("bio", "About", ProfileFieldTypeEnum.LongText, ProfileCollectAtEnum.Profile, false, 5, false, 1000)
            };
        }

        /// <summary>
        /// The fields a property document seeds: its profileFields list when present, otherwise the conversion of its
        /// schema version 1 profile section (section 9.13). Built-ins are not included.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <param name="nowUtc">Creation time.</param>
        /// <returns>Definitions (ids assigned), in profile order.</returns>
        /// <exception cref="ArgumentNullException">The property is null.</exception>
        public static List<ProfileField> Seed(PropertyConfiguration property, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(property);
            List<ProfileField> fields = property.ProfileFields != null && property.ProfileFields.Count > 0
                ? property.ProfileFields.Where(f => f != null && !BuiltInKeys.Contains(f.Key, StringComparer.OrdinalIgnoreCase)).Select(Copy).ToList()
                : FromProfile(property.Profile ?? new PropertyProfile());
            for (int i = 0; i < fields.Count; i++)
            {
                ProfileField f = fields[i];
                f.Id = IdGenerator.Generate(Constants.ProfileFieldIdPrefix);
                f.BuiltIn = false;
                f.ValueCount = 0;
                f.CreatedUtc = nowUtc;
                f.UpdatedUtc = nowUtc;
                if (property.ProfileFields != null && property.ProfileFields.Count > 0 && f.ProfileOrder == 0) f.ProfileOrder = (i + 1) * 10;
                foreach (ProfileFieldOption o in f.Options) o.Id = String.Empty;
                foreach (ProfileSubField s in f.SubFields)
                {
                    s.Id = String.Empty;
                    foreach (ProfileFieldOption o in s.Options) o.Id = String.Empty;
                }
            }
            return fields.OrderBy(f => f.ProfileOrder).ToList();
        }

        /// <summary>
        /// Convert a schema version 1 profile section (docs/MULTIPROPERTY_CONTRACT.md section 9.13): city, region, and
        /// country always; rank, affiliation, goals, and rankHistory when the section is enabled or has values.
        /// </summary>
        /// <param name="profile">Profile section.</param>
        /// <returns>Definitions without ids.</returns>
        /// <exception cref="ArgumentNullException">The section is null.</exception>
        public static List<ProfileField> FromProfile(PropertyProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            List<ProfileField> fields = new List<ProfileField>();
            string affiliationLabel = String.IsNullOrWhiteSpace(profile.Affiliation?.Label) ? "Affiliation" : profile.Affiliation.Label;
            PropertyRankField? rank = profile.Rank;
            bool hasRank = rank != null && rank.Values != null && rank.Values.Count > 0;
            if (hasRank)
            {
                fields.Add(new ProfileField
                {
                    Key = "rank",
                    Label = Label(rank!.Label, "Rank"),
                    Type = ProfileFieldTypeEnum.Rank,
                    Options = rank.Values!.Where(v => v != null).Select(v => new ProfileFieldOption { Key = v.Key, Label = Label(v.Label, v.Key), Color = String.IsNullOrWhiteSpace(v.Color) ? "#9CA3AF" : v.Color }).ToList(),
                    Rank = new ProfileRankSettings { LevelLabel = rank.LevelLabel ?? String.Empty, MaxLevel = Math.Clamp(rank.MaxLevel, 0, 20) },
                    Validation = new ProfileFieldValidation { Required = true },
                    CollectAt = ProfileCollectAtEnum.SignUp,
                    Visibility = ProfileVisibilityEnum.Public,
                    ShowOnProfile = true,
                    ShowOnCard = true,
                    Filterable = true,
                    Enabled = rank.Enabled,
                    ProfileOrder = 10
                });
            }
            if (profile.Affiliation != null && (profile.Affiliation.Enabled || !String.IsNullOrWhiteSpace(profile.Affiliation.Label)))
            {
                fields.Add(new ProfileField
                {
                    Key = "affiliation",
                    Label = affiliationLabel,
                    Type = ProfileFieldTypeEnum.Text,
                    Validation = new ProfileFieldValidation { MaxLength = 100 },
                    CollectAt = ProfileCollectAtEnum.Profile,
                    ShowOnProfile = true,
                    Searchable = true,
                    Filterable = true,
                    Enabled = profile.Affiliation.Enabled,
                    ProfileOrder = 20
                });
            }
            fields.Add(Place("city", "City", ProfileFieldTypeEnum.Text, 30));
            fields.Add(Place("region", "Region", ProfileFieldTypeEnum.Text, 40));
            fields.Add(Place("country", "Country", ProfileFieldTypeEnum.Country, 50));
            if (profile.Goals != null && profile.Goals.Values != null && profile.Goals.Values.Count > 0)
            {
                fields.Add(new ProfileField
                {
                    Key = "goals",
                    Label = Label(profile.Goals.Label, "Goals"),
                    Type = ProfileFieldTypeEnum.MultiSelect,
                    Options = profile.Goals.Values.Where(v => v != null).Select(v => new ProfileFieldOption { Key = v.Key, Label = Label(v.Label, v.Key), FreeText = v.FreeText }).ToList(),
                    CollectAt = ProfileCollectAtEnum.Profile,
                    Visibility = ProfileVisibilityEnum.Private,
                    ShowOnProfile = true,
                    Filterable = true,
                    Enabled = profile.Goals.Enabled,
                    ProfileOrder = 60
                });
            }
            if (profile.RankHistory != null && hasRank && (profile.RankHistory.Enabled || !String.IsNullOrWhiteSpace(profile.RankHistory.Label)))
            {
                fields.Add(new ProfileField
                {
                    Key = "rankHistory",
                    Label = Label(profile.RankHistory.Label, "History"),
                    Type = ProfileFieldTypeEnum.History,
                    Validation = new ProfileFieldValidation { MaxEntries = Math.Clamp(profile.RankHistory.MaxEntries <= 0 ? 12 : profile.RankHistory.MaxEntries, 1, 50) },
                    SubFields = new List<ProfileSubField>
                    {
                        new ProfileSubField { Key = "rank", Label = Label(rank!.Label, "Rank"), Type = ProfileSubFieldTypeEnum.RankValue, Required = true, RankFieldKey = "rank", WithLevel = false },
                        new ProfileSubField { Key = "awardedBy", Label = Label(profile.RankHistory.AwardedByLabel, "Awarded by"), Type = ProfileSubFieldTypeEnum.Member },
                        new ProfileSubField { Key = "affiliation", Label = affiliationLabel, Type = ProfileSubFieldTypeEnum.Text, MaxLength = 100 },
                        new ProfileSubField { Key = "dateAwarded", Label = "Date", Type = ProfileSubFieldTypeEnum.Date, NotInFuture = true }
                    },
                    CollectAt = ProfileCollectAtEnum.Profile,
                    ShowOnProfile = true,
                    Enabled = profile.RankHistory.Enabled,
                    ProfileOrder = 70
                });
            }
            return fields;
        }

        /// <summary>
        /// Validate one non-built-in definition against the attribute rules and its siblings (docs/MULTIPROPERTY_CONTRACT.md
        /// section 9.4). The card limit and the field count limit are checked by the caller.
        /// </summary>
        /// <param name="field">Definition.</param>
        /// <param name="siblings">Every other definition of the property (for key uniqueness and RankValue references).</param>
        /// <param name="prefix">Field error prefix, for example "" or "profileFields[2].".</param>
        /// <returns>Field errors (empty when valid).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static Dictionary<string, string> Validate(ProfileField field, IEnumerable<ProfileField> siblings, string prefix)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(siblings);
            prefix ??= String.Empty;
            Dictionary<string, string> e = new Dictionary<string, string>(StringComparer.Ordinal);
            List<ProfileField> others = siblings.Where(s => s != null && !ReferenceEquals(s, field) && (String.IsNullOrEmpty(field.Id) || s.Id != field.Id)).ToList();
            ProfileFieldValidation v = field.Validation ??= new ProfileFieldValidation();

            if (field.Key == null || !_Key.IsMatch(field.Key)) e[prefix + "key"] = "Use a camelCase key: a lowercase letter, then up to 39 letters or digits.";
            else if (ReservedKeys.Contains(field.Key, StringComparer.OrdinalIgnoreCase)) e[prefix + "key"] = "That key is reserved.";
            else if (others.Any(o => String.Equals(o.Key, field.Key, StringComparison.OrdinalIgnoreCase))) e[prefix + "key"] = "Another field uses that key.";
            Length(e, prefix + "label", field.Label, 1, 60);
            Length(e, prefix + "helpText", field.HelpText, 0, 300);
            Length(e, prefix + "placeholder", field.Placeholder, 0, 100);
            if (field.Type == ProfileFieldTypeEnum.Image) e[prefix + "type"] = "Image is only used by the built-in avatar field.";

            if (HasOptions(field.Type))
            {
                if (field.Options.Count < 1 || field.Options.Count > 200) e[prefix + "options"] = "Use 1 to 200 options.";
                Options(e, prefix + "options", field.Options, field.Type == ProfileFieldTypeEnum.Rank, field.Type != ProfileFieldTypeEnum.Rank);
            }
            else if (field.Options.Count > 0) e[prefix + "options"] = "Only select and rank fields have options.";

            if (field.Type == ProfileFieldTypeEnum.Rank)
            {
                if (field.Rank == null) field.Rank = new ProfileRankSettings();
                Length(e, prefix + "rank.levelLabel", field.Rank.LevelLabel, 0, 30);
                if (field.Rank.MaxLevel < 0 || field.Rank.MaxLevel > 20) e[prefix + "rank.maxLevel"] = "Use 0 to 20.";
                RankAwardingRules(e, prefix, field);
            }
            else if (field.Rank != null) e[prefix + "rank"] = "Only rank fields have rank settings.";

            if (field.Type == ProfileFieldTypeEnum.History)
            {
                if (field.SubFields.Count < 1 || field.SubFields.Count > 10) e[prefix + "subFields"] = "Use 1 to 10 sub-fields.";
                HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < field.SubFields.Count; i++)
                {
                    ProfileSubField s = field.SubFields[i];
                    string p = prefix + "subFields[" + i + "].";
                    if (s == null)
                    {
                        e[p.TrimEnd('.')] = "Sub-field is required.";
                        continue;
                    }
                    if (s.Key == null || !_Key.IsMatch(s.Key)) e[p + "key"] = "Use a camelCase key.";
                    else if (!keys.Add(s.Key)) e[p + "key"] = "Sub-field keys must be unique.";
                    Length(e, p + "label", s.Label, 1, 60);
                    if (s.Type == ProfileSubFieldTypeEnum.Text && s.MaxLength != null && (s.MaxLength < 1 || s.MaxLength > 500)) e[p + "maxLength"] = "Use 1 to 500.";
                    if (s.MinValue != null && s.MaxValue != null && s.MinValue > s.MaxValue) e[p + "minValue"] = "Must not exceed maxValue.";
                    if (s.Type == ProfileSubFieldTypeEnum.SingleSelect)
                    {
                        if (s.Options.Count < 1 || s.Options.Count > 50) e[p + "options"] = "Use 1 to 50 options.";
                        Options(e, p + "options", s.Options, false, false);
                    }
                    else if (s.Options.Count > 0) e[p + "options"] = "Only SingleSelect sub-fields have options.";
                    if (s.Type == ProfileSubFieldTypeEnum.RankValue)
                    {
                        ProfileField? target = others.FirstOrDefault(o => String.Equals(o.Key, s.RankFieldKey, StringComparison.OrdinalIgnoreCase));
                        if (target == null || target.Type != ProfileFieldTypeEnum.Rank) e[p + "rankFieldKey"] = "Name a rank field of this community.";
                    }
                }
                int max = v.MaxEntries ?? 12;
                if (max < 1 || max > 50) e[prefix + "validation.maxEntries"] = "Use 1 to 50.";
            }
            else if (field.SubFields.Count > 0) e[prefix + "subFields"] = "Only history fields have sub-fields.";

            if (v.Required && field.CollectAt == ProfileCollectAtEnum.AdminOnly) e[prefix + "validation.required"] = "Only fields collected at sign-up or on the profile can be required.";
            if (field.CollectAt == ProfileCollectAtEnum.AdminOnly && field.EditableBy != ProfileEditableByEnum.Staff) e[prefix + "editableBy"] = "Fields set by staff must be editable by staff.";
            if (field.CollectAt != ProfileCollectAtEnum.AdminOnly && field.EditableBy != ProfileEditableByEnum.Member) e[prefix + "editableBy"] = "Fields collected from members must be editable by members.";
            if (field.Visibility == ProfileVisibilityEnum.Staff && field.CollectAt != ProfileCollectAtEnum.AdminOnly) e[prefix + "visibility"] = "Staff-only fields must be set by staff (collectAt AdminOnly).";
            if (field.ProfileOrder < 0 || field.ProfileOrder > 100000) e[prefix + "profileOrder"] = "Use 0 to 100000.";
            if (field.Searchable && field.Type != ProfileFieldTypeEnum.Text && field.Type != ProfileFieldTypeEnum.LongText) e[prefix + "searchable"] = "Only text fields can be searchable.";
            if (field.Filterable && (field.Type == ProfileFieldTypeEnum.LongText || field.Type == ProfileFieldTypeEnum.Url || field.Type == ProfileFieldTypeEnum.Phone))
                e[prefix + "filterable"] = "This type cannot be filtered.";

            bool text = field.Type == ProfileFieldTypeEnum.Text || field.Type == ProfileFieldTypeEnum.LongText;
            int cap = field.Type == ProfileFieldTypeEnum.LongText ? 10000 : 1000;
            if (v.MinLength != null && (!text || v.MinLength < 0 || v.MinLength > cap)) e[prefix + "validation.minLength"] = text ? "Use 0 to " + cap + "." : "Only text fields have a length.";
            if (v.MaxLength != null && (!text || v.MaxLength < 1 || v.MaxLength > cap)) e[prefix + "validation.maxLength"] = text ? "Use 1 to " + cap + "." : "Only text fields have a length.";
            if (v.MinLength != null && v.MaxLength != null && v.MinLength > v.MaxLength) e[prefix + "validation.minLength"] = "Must not exceed maxLength.";
            if ((v.MinValue != null || v.MaxValue != null || v.IntegerOnly) && field.Type != ProfileFieldTypeEnum.Number) e[prefix + "validation.minValue"] = "Only number fields have value bounds.";
            if (v.MinValue != null && v.MaxValue != null && v.MinValue > v.MaxValue) e[prefix + "validation.minValue"] = "Must not exceed maxValue.";
            if (v.Pattern != null)
            {
                if (field.Type != ProfileFieldTypeEnum.Text) e[prefix + "validation.pattern"] = "Only text fields have a pattern.";
                else if (v.Pattern.Length > 200) e[prefix + "validation.pattern"] = "At most 200 characters.";
                else
                {
                    try
                    {
                        _ = new Regex(v.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                    }
                    catch (ArgumentException)
                    {
                        e[prefix + "validation.pattern"] = "Not a valid regular expression.";
                    }
                }
            }
            Length(e, prefix + "validation.patternMessage", v.PatternMessage, 0, 200);
            bool entries = field.Type == ProfileFieldTypeEnum.MultiSelect || field.Type == ProfileFieldTypeEnum.History;
            if ((v.MinEntries != null || (v.MaxEntries != null && field.Type != ProfileFieldTypeEnum.History)) && !entries) e[prefix + "validation.maxEntries"] = "Only multi-select and history fields have entry limits.";
            if (v.MinEntries != null && v.MinEntries < 0) e[prefix + "validation.minEntries"] = "Must be 0 or more.";
            if (v.MinEntries != null && v.MaxEntries != null && v.MinEntries > v.MaxEntries) e[prefix + "validation.minEntries"] = "Must not exceed maxEntries.";
            if (field.Type == ProfileFieldTypeEnum.MultiSelect && v.MaxEntries != null && v.MaxEntries > field.Options.Count(o => o != null && !o.Retired))
                e[prefix + "validation.maxEntries"] = "Must not exceed the number of options.";
            bool date = field.Type == ProfileFieldTypeEnum.Date;
            if ((v.MinDate != null || v.MaxDate != null || v.NotInFuture) && !date) e[prefix + "validation.minDate"] = "Only date fields have date bounds.";
            if (v.MinDate != null && !IsDate(v.MinDate)) e[prefix + "validation.minDate"] = "Use YYYY-MM-DD.";
            if (v.MaxDate != null && !IsDate(v.MaxDate)) e[prefix + "validation.maxDate"] = "Use YYYY-MM-DD.";
            if (v.MinDate != null && v.MaxDate != null && IsDate(v.MinDate) && IsDate(v.MaxDate) && String.CompareOrdinal(v.MinDate, v.MaxDate) > 0) e[prefix + "validation.minDate"] = "Must not be after maxDate.";
            return e;
        }

        /// <summary>
        /// Whether text is a valid YYYY-MM-DD calendar date.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <returns>True when valid.</returns>
        public static bool IsDate(string? value)
        {
            return value != null && value.Length == 10 && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime _);
        }

        /// <summary>
        /// A deep copy of a definition (options and sub-fields included).
        /// </summary>
        /// <param name="source">Definition.</param>
        /// <returns>Copy.</returns>
        /// <exception cref="ArgumentNullException">The definition is null.</exception>
        public static ProfileField Copy(ProfileField source)
        {
            ArgumentNullException.ThrowIfNull(source);
            ProfileFieldValidation v = source.Validation ?? new ProfileFieldValidation();
            return new ProfileField
            {
                Id = source.Id,
                Key = source.Key,
                BuiltIn = source.BuiltIn,
                Label = source.Label,
                HelpText = source.HelpText ?? String.Empty,
                Placeholder = source.Placeholder ?? String.Empty,
                Type = source.Type,
                Options = source.Options.Where(o => o != null).Select(CopyOption).ToList(),
                Rank = source.Rank == null ? null : new ProfileRankSettings { LevelLabel = source.Rank.LevelLabel ?? String.Empty, MaxLevel = source.Rank.MaxLevel, Awarding = CopyAwarding(source.Rank.Awarding) },
                SubFields = source.SubFields.Where(s => s != null).Select(s => new ProfileSubField
                {
                    Id = s.Id, Key = s.Key, Label = s.Label, Type = s.Type, Required = s.Required, MaxLength = s.MaxLength, MinValue = s.MinValue, MaxValue = s.MaxValue,
                    NotInFuture = s.NotInFuture, Options = s.Options.Where(o => o != null).Select(CopyOption).ToList(), RankFieldKey = s.RankFieldKey, WithLevel = s.WithLevel, Position = s.Position
                }).ToList(),
                Validation = new ProfileFieldValidation
                {
                    Required = v.Required, MinLength = v.MinLength, MaxLength = v.MaxLength, MinValue = v.MinValue, MaxValue = v.MaxValue, IntegerOnly = v.IntegerOnly,
                    Pattern = v.Pattern, PatternMessage = v.PatternMessage, MinEntries = v.MinEntries, MaxEntries = v.MaxEntries, MinDate = v.MinDate, MaxDate = v.MaxDate, NotInFuture = v.NotInFuture
                },
                CollectAt = source.CollectAt,
                EditableBy = source.EditableBy,
                Visibility = source.Visibility,
                ShowOnProfile = source.ShowOnProfile,
                ProfileOrder = source.ProfileOrder,
                ShowOnCard = source.ShowOnCard,
                Searchable = source.Searchable,
                Filterable = source.Filterable,
                Enabled = source.Enabled,
                ValueCount = source.ValueCount,
                Revision = source.Revision,
                Labels = new List<string>(source.Labels),
                Tags = new Dictionary<string, string>(source.Tags, StringComparer.Ordinal),
                CreatedUtc = source.CreatedUtc,
                UpdatedUtc = source.UpdatedUtc
            };
        }

        #endregion

        #region Private-Methods

        private static ProfileRankAwarding? CopyAwarding(ProfileRankAwarding? a)
        {
            if (a == null) return null;
            return new ProfileRankAwarding { OptionAwarderMinimum = a.OptionAwarderMinimum, AllowEqualOption = a.AllowEqualOption, AllowEqualOptionLevels = a.AllowEqualOptionLevels };
        }

        private static void RankAwardingRules(Dictionary<string, string> e, string prefix, ProfileField field)
        {
            // options[].maxLevel may not exceed rank.maxLevel (missing means rank.maxLevel); rank.awarding keys name options.
            int max = field.Rank?.MaxLevel ?? 0;
            for (int i = 0; i < field.Options.Count; i++)
            {
                ProfileFieldOption o = field.Options[i];
                if (o == null || o.MaxLevel == null) continue;
                string name = prefix + "options[" + i + "].maxLevel";
                if (!e.ContainsKey(name) && (o.MaxLevel < 0 || o.MaxLevel > max)) e[name] = "Use 0 to rank.maxLevel (" + max + ").";
            }
            ProfileRankAwarding? awarding = field.Rank?.Awarding;
            if (awarding == null || String.IsNullOrWhiteSpace(awarding.OptionAwarderMinimum)) return;
            if (!field.Options.Any(o => o != null && String.Equals(o.Key, awarding.OptionAwarderMinimum.Trim(), StringComparison.OrdinalIgnoreCase)))
                e[prefix + "rank.awarding.optionAwarderMinimum"] = "Use the key of one of the field's options.";
        }

        private static ProfileFieldOption CopyOption(ProfileFieldOption o)
        {
            return new ProfileFieldOption { Id = o.Id, Key = o.Key, Label = o.Label, Color = o.Color, MaxLevel = o.MaxLevel, FreeText = o.FreeText, Retired = o.Retired, SubFieldId = o.SubFieldId };
        }

        private static ProfileField Place(string key, string label, ProfileFieldTypeEnum type, int order)
        {
            return new ProfileField
            {
                Key = key,
                Label = label,
                Type = type,
                Validation = new ProfileFieldValidation { MaxLength = type == ProfileFieldTypeEnum.Text ? 100 : null },
                CollectAt = ProfileCollectAtEnum.Profile,
                Visibility = ProfileVisibilityEnum.Public,
                ShowOnProfile = true,
                ShowOnCard = false,
                Filterable = type == ProfileFieldTypeEnum.Country,
                Enabled = true,
                ProfileOrder = order
            };
        }

        private static string Label(string? value, string fallback)
        {
            string text = String.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return text.Length > 60 ? text.Substring(0, 60) : text;
        }

        private static void Length(Dictionary<string, string> e, string name, string? value, int min, int max)
        {
            int length = (value ?? String.Empty).Trim().Length;
            if (length < min || length > max) e[name] = min > 0 ? "Use " + min + " to " + max + " characters." : "At most " + max + " characters.";
        }

        private static void Options(Dictionary<string, string> e, string prefix, List<ProfileFieldOption> options, bool rank, bool freeTextAllowed)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < options.Count; i++)
            {
                ProfileFieldOption o = options[i];
                string p = prefix + "[" + i + "].";
                if (o == null)
                {
                    e[prefix + "[" + i + "]"] = "Option is required.";
                    continue;
                }
                if (o.Key == null || !_OptionKey.IsMatch(o.Key)) e[p + "key"] = "Use 1 to 64 letters, digits, underscores, or hyphens.";
                else if (!keys.Add(o.Key)) e[p + "key"] = "Option keys must be unique.";
                Length(e, p + "label", o.Label, 1, 60);
                if (rank && String.IsNullOrWhiteSpace(o.Color)) e[p + "color"] = "Rank options need a color.";
                if (!String.IsNullOrWhiteSpace(o.Color) && !_Color.IsMatch(o.Color)) e[p + "color"] = "Use #RGB, #RRGGBB, or #RRGGBBAA.";
                if (o.MaxLevel != null && (!rank || o.MaxLevel < 0 || o.MaxLevel > 20)) e[p + "maxLevel"] = rank ? "Use 0 to 20." : "Only rank options have a level.";
                if (o.FreeText && !freeTextAllowed) e[p + "freeText"] = "Only select options can take free text.";
            }
        }

        #endregion
    }
}
