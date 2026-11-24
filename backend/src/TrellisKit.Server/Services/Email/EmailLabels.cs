namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Text;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Human labels for enum values used as email tokens (subjects and bodies read by people, never enum names).
    /// </summary>
    public static class EmailLabels
    {
        #region Public-Methods

        /// <summary>
        /// Lower-case label of a target type for staff email, for example "feed post" or "AI assistant reply".
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>Label.</returns>
        public static string TargetType(TargetTypeEnum targetType)
        {
            switch (targetType)
            {
                case TargetTypeEnum.FeedPost: return "feed post";
                case TargetTypeEnum.Comment: return "comment";
                case TargetTypeEnum.User: return "member profile";
                case TargetTypeEnum.Thread: return "forum thread";
                case TargetTypeEnum.AssistantMessage: return "AI assistant reply";
                case TargetTypeEnum.Message: return "message";
                default: return Words(targetType.ToString());
            }
        }

        /// <summary>
        /// Label of a report reason for staff email, for example "Hate speech".
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <returns>Label.</returns>
        public static string ReportReason(ReportReasonEnum reason)
        {
            switch (reason)
            {
                case ReportReasonEnum.Filter: return "Word filter match";
                default:
                    string words = Words(reason.ToString());
                    return Char.ToUpperInvariant(words[0]) + words.Substring(1);
            }
        }

        #endregion

        #region Private-Methods

        private static string Words(string pascal)
        {
            StringBuilder builder = new StringBuilder(pascal.Length + 8);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && Char.IsUpper(c)) builder.Append(' ');
                builder.Append(Char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        #endregion
    }
}
