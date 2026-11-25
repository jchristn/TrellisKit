namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings registry fields of the daily administrator summary email (docs/ADMIN_SUMMARY.md). The item list
    /// (adminSummary.items) is edited in the settings JSON file.
    /// </summary>
    public static class AdminSummarySettingFields
    {
        #region Public-Methods

        /// <summary>
        /// Build the fields.
        /// </summary>
        /// <returns>Fields.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            const string section = "Admin summary";

            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Boolean("adminSummary.enabled", section, "Daily summary enabled",
                    "Email the recipients a summary of the previous day (members, sign-ups, active members, activity by type, items needing attention, platform health). Default off.", live, "TK_ADMIN_SUMMARY_ENABLED",
                    s => s.AdminSummary.Enabled, (s, v) => s.AdminSummary.Enabled = v),
                SettingFieldDefinition.Text("adminSummary.recipients", section, "Summary recipients",
                    "Comma-separated email addresses (at most 20). Invalid addresses are ignored. In MultiProperty mode every property sends its own summary to these addresses.", live, "TK_ADMIN_SUMMARY_RECIPIENTS",
                    s => String.Join(",", s.AdminSummary.Recipients),
                    (s, v) => s.AdminSummary.Recipients = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()),
                SettingFieldDefinition.Integer("adminSummary.sendHour", section, "Send hour",
                    "Local hour (in the property's usage time zone) after which the previous day's summary is sent.", live, "TK_ADMIN_SUMMARY_SEND_HOUR", 0, 23,
                    s => s.AdminSummary.SendHour, (s, v) => s.AdminSummary.SendHour = v),
                SettingFieldDefinition.Boolean("adminSummary.includeDefaultItems", section, "Include default lines",
                    "Report the engine's default lines before the configured adminSummary.items.", live, null,
                    s => s.AdminSummary.IncludeDefaultItems, (s, v) => s.AdminSummary.IncludeDefaultItems = v),
                SettingFieldDefinition.Integer("adminSummary.maxGroups", section, "Groups per breakdown",
                    "Largest groups listed for a grouped line; the rest are folded into Other.", live, null, 1, 20,
                    s => s.AdminSummary.MaxGroups, (s, v) => s.AdminSummary.MaxGroups = v)
            };
        }

        #endregion
    }
}
