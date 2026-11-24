namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A configurable profile field definition of a property (docs/MULTIPROPERTY_CONTRACT.md section 9.1). The same shape is the
    /// admin API object, the create and update body (ids, builtIn, valueCount, and timestamps are ignored on input), and
    /// the property.json profileFields entry.
    /// </summary>
    public class ProfileField : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pfd_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key: camelCase, ^[a-z][A-Za-z0-9]{0,39}$, unique per property case-insensitively. Immutable once values exist.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Whether this is one of the four built-in fields (displayName, handle, avatar, bio).
        /// </summary>
        public bool BuiltIn { get; set; } = false;

        /// <summary>
        /// Label, 1 to 60 characters.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Help text, 0 to 300 characters.
        /// </summary>
        public string HelpText { get; set; } = String.Empty;

        /// <summary>
        /// Placeholder, 0 to 100 characters.
        /// </summary>
        public string Placeholder { get; set; } = String.Empty;

        /// <summary>
        /// Type. Changeable only while the field has no values.
        /// </summary>
        public ProfileFieldTypeEnum Type { get; set; } = ProfileFieldTypeEnum.Text;

        /// <summary>
        /// Options of select and rank types, in order (1 to 200). Empty for other types.
        /// </summary>
        public List<ProfileFieldOption> Options
        {
            get { return _Options; }
            set { _Options = value ?? new List<ProfileFieldOption>(); }
        }

        /// <summary>
        /// Rank settings (Rank type only), or null.
        /// </summary>
        public ProfileRankSettings? Rank { get; set; } = null;

        /// <summary>
        /// Sub-fields (History type only, 1 to 10).
        /// </summary>
        public List<ProfileSubField> SubFields
        {
            get { return _SubFields; }
            set { _SubFields = value ?? new List<ProfileSubField>(); }
        }

        /// <summary>
        /// Validation rules (per type).
        /// </summary>
        public ProfileFieldValidation Validation { get; set; } = new ProfileFieldValidation();

        /// <summary>
        /// When the field is collected.
        /// </summary>
        public ProfileCollectAtEnum CollectAt { get; set; } = ProfileCollectAtEnum.Profile;

        /// <summary>
        /// Who may change the value.
        /// </summary>
        public ProfileEditableByEnum EditableBy { get; set; } = ProfileEditableByEnum.Member;

        /// <summary>
        /// Who may see the value.
        /// </summary>
        public ProfileVisibilityEnum Visibility { get; set; } = ProfileVisibilityEnum.Public;

        /// <summary>
        /// Shown on the profile page.
        /// </summary>
        public bool ShowOnProfile { get; set; } = true;

        /// <summary>
        /// Order on the profile page, onboarding, and the editor (0 to 100000; ties by createdUtc).
        /// </summary>
        public int ProfileOrder { get; set; } = 0;

        /// <summary>
        /// Shown on compact member cards (at most profileFields.maxCardFields enabled fields).
        /// </summary>
        public bool ShowOnCard { get; set; } = false;

        /// <summary>
        /// Text and LongText only: words are indexed for member search text.
        /// </summary>
        public bool Searchable { get; set; } = false;

        /// <summary>
        /// Offered as a member search filter (not LongText).
        /// </summary>
        public bool Filterable { get; set; } = false;

        /// <summary>
        /// Disabling hides the field everywhere without deleting values.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Members with a value (read-only).
        /// </summary>
        public long ValueCount { get; set; } = 0;

        /// <summary>
        /// Revision, bumped on every change (feeds the property ETag). Not serialized.
        /// </summary>
        [JsonIgnore]
        public long Revision { get; set; } = 0;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<ProfileFieldOption> _Options = new List<ProfileFieldOption>();
        private List<ProfileSubField> _SubFields = new List<ProfileSubField>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
