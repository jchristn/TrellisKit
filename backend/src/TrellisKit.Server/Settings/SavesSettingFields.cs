namespace TrellisKit.Server.Settings
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Settings registry fields of saves and private collections (REST_API.md, Saves).
    /// </summary>
    public static class SavesSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// Build the fields.
        /// </summary>
        /// <returns>Fields.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            const string section = "Saves";

            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Integer("saves.maxPerMember", section, "Saves per member",
                    "Most saves one member may keep; saving more answers 409 LimitReached. Default 2000.", live, null, SavesSettings.MinMaxPerMember, SavesSettings.MaxMaxPerMember,
                    s => s.Saves.MaxPerMember, (s, v) => s.Saves.MaxPerMember = v),
                SettingFieldDefinition.Integer("saves.maxCollectionsPerMember", section, "Collections per member",
                    "Most save collections one member may keep; creating more answers 409 LimitReached. Default 50.", live, null, SavesSettings.MinMaxCollectionsPerMember, SavesSettings.MaxMaxCollectionsPerMember,
                    s => s.Saves.MaxCollectionsPerMember, (s, v) => s.Saves.MaxCollectionsPerMember = v)
            };
        }

        #endregion
    }
}
