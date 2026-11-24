namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A profile field definition as clients read it from GET /api/v1/property (docs/MULTIPROPERTY_CONTRACT.md section
    /// 10.8): enabled fields whose visibility is not Staff, built-ins included, without counts or staff metadata.
    /// </summary>
    public class PublicProfileField
    {
        #region Public-Members

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Built-in field (value is top-level in payloads, never in profile).
        /// </summary>
        public bool BuiltIn { get; set; } = false;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Help text.
        /// </summary>
        public string HelpText { get; set; } = String.Empty;

        /// <summary>
        /// Placeholder.
        /// </summary>
        public string Placeholder { get; set; } = String.Empty;

        /// <summary>
        /// Type. Clients skip types they do not know.
        /// </summary>
        public ProfileFieldTypeEnum Type { get; set; } = ProfileFieldTypeEnum.Text;

        /// <summary>
        /// Options (select and rank types), retired ones included (pickers hide them).
        /// </summary>
        public List<ProfileFieldOption> Options
        {
            get { return _Options; }
            set { _Options = value ?? new List<ProfileFieldOption>(); }
        }

        /// <summary>
        /// Rank settings, or null.
        /// </summary>
        public ProfileRankSettings? Rank { get; set; } = null;

        /// <summary>
        /// Sub-fields (History).
        /// </summary>
        public List<ProfileSubField> SubFields
        {
            get { return _SubFields; }
            set { _SubFields = value ?? new List<ProfileSubField>(); }
        }

        /// <summary>
        /// Validation rules.
        /// </summary>
        public ProfileFieldValidation Validation { get; set; } = new ProfileFieldValidation();

        /// <summary>
        /// Same as validation.required.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// When collected.
        /// </summary>
        public ProfileCollectAtEnum CollectAt { get; set; } = ProfileCollectAtEnum.Profile;

        /// <summary>
        /// Who may change the value.
        /// </summary>
        public ProfileEditableByEnum EditableBy { get; set; } = ProfileEditableByEnum.Member;

        /// <summary>
        /// Visibility.
        /// </summary>
        public ProfileVisibilityEnum Visibility { get; set; } = ProfileVisibilityEnum.Public;

        /// <summary>
        /// Shown on the profile page.
        /// </summary>
        public bool ShowOnProfile { get; set; } = true;

        /// <summary>
        /// Profile order.
        /// </summary>
        public int ProfileOrder { get; set; } = 0;

        /// <summary>
        /// Shown on member cards.
        /// </summary>
        public bool ShowOnCard { get; set; } = false;

        /// <summary>
        /// Included in member search text.
        /// </summary>
        public bool Searchable { get; set; } = false;

        /// <summary>
        /// Offered as a member search filter.
        /// </summary>
        public bool Filterable { get; set; } = false;

        #endregion

        #region Private-Members

        private List<ProfileFieldOption> _Options = new List<ProfileFieldOption>();
        private List<ProfileSubField> _SubFields = new List<ProfileSubField>();

        #endregion
    }
}
