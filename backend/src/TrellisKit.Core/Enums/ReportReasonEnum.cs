namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Why content or a member was reported.
    /// </summary>
    public enum ReportReasonEnum
    {
        /// <summary>
        /// Spam or advertising.
        /// </summary>
        Spam,

        /// <summary>
        /// Harassment or bullying.
        /// </summary>
        Harassment,

        /// <summary>
        /// Hate speech.
        /// </summary>
        HateSpeech,

        /// <summary>
        /// Sexual content.
        /// </summary>
        SexualContent,

        /// <summary>
        /// Violence or threats.
        /// </summary>
        Violence,

        /// <summary>
        /// Self-harm.
        /// </summary>
        SelfHarm,

        /// <summary>
        /// Impersonation.
        /// </summary>
        Impersonation,

        /// <summary>
        /// Intellectual property violation.
        /// </summary>
        IntellectualProperty,

        /// <summary>
        /// Something else (details required).
        /// </summary>
        Other,

        /// <summary>
        /// System report from a Flag term of the word filter.
        /// </summary>
        Filter,

        /// <summary>
        /// Directory details are inaccurate.
        /// </summary>
        Inaccurate,

        /// <summary>
        /// The venue has permanently closed.
        /// </summary>
        PermanentlyClosed
    }
}
