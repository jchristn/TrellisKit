namespace TrellisKit.Server.Settings
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings registry fields of the content changelog and Terms of Service review (docs/INTEGRATION.md owner
    /// requirement R3).
    /// </summary>
    public static class ContentReviewSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// Build the fields.
        /// </summary>
        /// <returns>Fields.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            const string section = "Content review";

            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Integer("contentReview.batchSize", section, "Rows per review run",
                    "Pending content changelog rows one content.review run sends to the Moderation role, oldest first.", live, null, 1, 5000,
                    s => s.ContentReview.BatchSize, (s, v) => s.ContentReview.BatchSize = v),
                SettingFieldDefinition.Integer("contentReview.maxReviewCharacters", section, "Characters reviewed per row",
                    "Most characters of a contribution's text sent to the reviewer; longer text is cut.", live, null, 200, 20000,
                    s => s.ContentReview.MaxReviewCharacters, (s, v) => s.ContentReview.MaxReviewCharacters = v),
                SettingFieldDefinition.Integer("contentReview.maxOutputTokens", section, "Reviewer output tokens",
                    "Output token cap of one review.", live, null, 50, 4000,
                    s => s.ContentReview.MaxOutputTokens, (s, v) => s.ContentReview.MaxOutputTokens = v),
                SettingFieldDefinition.Integer("contentReview.maxAttempts", section, "Failed reviews before flagging",
                    "Failed reviews of one row (model errors or unreadable verdicts) before it is flagged for an administrator instead, so nothing escapes review.", live, null, 1, 50,
                    s => s.ContentReview.MaxAttempts, (s, v) => s.ContentReview.MaxAttempts = v),
                SettingFieldDefinition.Integer("contentReview.retentionDays", section, "Reviewed rows kept (days)",
                    "Days Clean, Dismissed, and Actioned rows are kept. Pending and Flagged rows are kept until resolved.", live, null, 30, 3650,
                    s => s.ContentReview.RetentionDays, (s, v) => s.ContentReview.RetentionDays = v),
                SettingFieldDefinition.Integer("contentReview.maxNotifiedAdmins", section, "Administrators notified per flag",
                    "Most administrators who get the in-app and push notification of one flagged row.", live, null, 1, 100,
                    s => s.ContentReview.MaxNotifiedAdmins, (s, v) => s.ContentReview.MaxNotifiedAdmins = v)
            };
        }

        #endregion
    }
}
