namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Body of PUT /api/v1/me/profile. Only the properties present in the JSON are changed; null clears an optional field.
    /// </summary>
    public class ProfileUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Display name, 2 to 60 characters.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Handle, 3 to 30 of A-Z a-z 0-9 _ and period.
        /// </summary>
        public string? Handle { get; set; } = null;

        /// <summary>
        /// Biography, up to 1000 characters.
        /// </summary>
        public string? Bio { get; set; } = null;

        /// <summary>
        /// Rank key from the property profile.rank.values, for example Intermediate.
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Level within the rank, 0 to the property profile.rank.maxLevel.
        /// </summary>
        public int? RankLevel { get; set; } = null;

        /// <summary>
        /// Affiliation, up to 100 characters.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// City, up to 100 characters.
        /// </summary>
        public string? City { get; set; } = null;

        /// <summary>
        /// Region, up to 100 characters.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// ISO 3166-1 alpha-2 code.
        /// </summary>
        public string? Country { get; set; } = null;

        /// <summary>
        /// Goal keys from the property profile.goals.values.
        /// </summary>
        public List<string>? Goals { get; set; } = null;

        /// <summary>
        /// Text for the Other goal, up to 200 characters.
        /// </summary>
        public string? GoalsOther { get; set; } = null;

        /// <summary>
        /// Replacement rank history list, up to the property profile.rankHistory.maxEntries entries.
        /// </summary>
        public List<RankHistoryEntryRequest>? RankHistory { get; set; } = null;

        /// <summary>
        /// Labels (replace the stored list when present; omitted means unchanged). At most 50.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags (replace the stored dictionary when present; omitted means unchanged). At most 50 keys.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// Configurable profile values by field key (docs/MULTIPROPERTY_CONTRACT.md section 9.9): a provided key replaces
        /// that field's value (History and MultiSelect: the whole list), null clears it, an omitted key is unchanged.
        /// </summary>
        public Dictionary<string, JsonElement>? Profile { get; set; } = null;

        #endregion
    }
}
