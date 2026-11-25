namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings registry entries of verified ranks, curricula, and attendance (docs/TRELLISKIT.md, Ranks, curricula, and
    /// attendance).
    /// </summary>
    public static class RankSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// The definitions.
        /// </summary>
        /// <returns>Definitions.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Boolean("ranks.enabled", "Ranks", "Verified ranks",
                    "Turn on verified ranks, lineage, curricula, and attendance. While off every rank, curriculum, and attendance route answers 404 NotAvailable.", live, null,
                    s => s.Ranks.Enabled, (s, v) => s.Ranks.Enabled = v),
                SettingFieldDefinition.Boolean("ranks.showVerificationLabels", "Ranks", "Show verification labels",
                    "Profiles and member cards show verified or self-reported next to Rank fields. Turn off while verifying members in bulk.", live, null,
                    s => s.Ranks.ShowVerificationLabels, (s, v) => s.Ranks.ShowVerificationLabels = v),
                SettingFieldDefinition.Integer("attendance.checkInWindowBeforeMinutes", "Attendance", "Check-in opens (minutes before start)",
                    "Members can check in this many minutes before an event or session starts.", live, null, 0, 720,
                    s => s.Attendance.CheckInWindowBeforeMinutes, (s, v) => s.Attendance.CheckInWindowBeforeMinutes = v),
                SettingFieldDefinition.Integer("attendance.checkInWindowAfterMinutes", "Attendance", "Check-in closes (minutes after start)",
                    "Members can check in until this many minutes after an event or session starts.", live, null, 0, 720,
                    s => s.Attendance.CheckInWindowAfterMinutes, (s, v) => s.Attendance.CheckInWindowAfterMinutes = v),
                SettingFieldDefinition.Integer("attendance.codeRotationSeconds", "Attendance", "Code rotation (seconds)",
                    "How long each rotating check-in code is shown; the previous code is accepted for one more period.", live, null, 15, 600,
                    s => s.Attendance.CodeRotationSeconds, (s, v) => s.Attendance.CodeRotationSeconds = v)
            };
        }

        #endregion
    }
}
