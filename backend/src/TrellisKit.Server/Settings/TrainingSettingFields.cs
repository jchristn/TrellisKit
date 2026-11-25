namespace TrellisKit.Server.Settings
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Settings registry fields of the Training hub overview (docs/TRELLISKIT.md section 15).
    /// </summary>
    public static class TrainingSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// Build the fields.
        /// </summary>
        /// <returns>Fields.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            const string section = "Training";

            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Integer("training.upNextLimit", section, "Up next items",
                    "Most upcoming events you RSVPed Going to on the Training overview. Default 3.", live, null, TrainingSettings.MinLimit, TrainingSettings.MaxUpNextLimit,
                    s => s.Training.UpNextLimit, (s, v) => s.Training.UpNextLimit = v),
                SettingFieldDefinition.Integer("training.continueLimit", section, "Continue training items",
                    "Most lessons in progress on the Training overview. Default 10.", live, null, TrainingSettings.MinLimit, TrainingSettings.MaxContinueLimit,
                    s => s.Training.ContinueLimit, (s, v) => s.Training.ContinueLimit = v),
                SettingFieldDefinition.Integer("training.recentSavesLimit", section, "Recently saved items",
                    "Most recent saves on the Training overview. Default 6.", live, null, TrainingSettings.MinLimit, TrainingSettings.MaxRecentSavesLimit,
                    s => s.Training.RecentSavesLimit, (s, v) => s.Training.RecentSavesLimit = v),
                SettingFieldDefinition.Integer("training.newForYouLimit", section, "New for you items",
                    "Most new lessons (followed series, not started) on the Training overview. Default 6.", live, null, TrainingSettings.MinLimit, TrainingSettings.MaxNewForYouLimit,
                    s => s.Training.NewForYouLimit, (s, v) => s.Training.NewForYouLimit = v),
                SettingFieldDefinition.Integer("training.newForYouDays", section, "New for you window (days)",
                    "Lessons published within this many days count as new. Default 14.", live, null, TrainingSettings.MinNewForYouDays, TrainingSettings.MaxNewForYouDays,
                    s => s.Training.NewForYouDays, (s, v) => s.Training.NewForYouDays = v)
            };
        }

        #endregion
    }
}
