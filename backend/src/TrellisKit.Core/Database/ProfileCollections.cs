namespace TrellisKit.Core.Database
{
    using System;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of configurable profile fields and member enumeration
    /// (docs/MULTIPROPERTY_CONTRACT.md sections 9.5 and 9.12). Table aliases: profilefields f, users u.
    /// </summary>
    public static class ProfileCollections
    {
        #region Public-Members

        /// <summary>
        /// POST /api/v1/admin/profile-fields/enumerate: search over key and label; filters type, collectAt, visibility,
        /// enabled, builtIn, filterable, showOnCard.
        /// </summary>
        public static readonly EnumerationCollection Fields = new EnumerationCollection("admin-profile-fields", TargetTypeEnum.ProfileField, "f.id", "f.createdutc", true)
            .Order("ProfileOrderAscending", "f.profileorder", false, EnumerationKeyKindEnum.Number)
            .Order("LabelAscending", "lower(f.label)", false, EnumerationKeyKindEnum.Text)
            .Search("f.key", "f.label")
            .Filters("type", "collectAt", "visibility", "enabled", "builtIn", "filterable", "showOnCard");

        /// <summary>
        /// POST /api/v1/members/enumerate: active (not deleted, not banned) members; search over display name, handle,
        /// and searchable fields the caller can see (applied by the service); filter profile.
        /// </summary>
        public static readonly EnumerationCollection Members = new EnumerationCollection("members", TargetTypeEnum.User, "u.id", "u.createdutc", false)
            .Order("DisplayNameAscending", "lower(u.displayname)", false, EnumerationKeyKindEnum.Text)
            .Filters("profile");

        #endregion
    }
}
