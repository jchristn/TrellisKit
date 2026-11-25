namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's effective rank in one Rank profile field: the verified rank when one stands, otherwise the self-reported profile value.
    /// </summary>
    public class EffectiveRankView
    {
        #region Public-Members

        /// <summary>
        /// Rank profile field key.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Field label.
        /// </summary>
        public string FieldLabel { get; set; } = String.Empty;

        /// <summary>
        /// Option key, or null when the member has no value.
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Option label.
        /// </summary>
        public string? OptionLabel { get; set; } = null;

        /// <summary>
        /// Option color.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Level within the option.
        /// </summary>
        public int Level { get; set; } = 0;

        /// <summary>
        /// The option's level maximum (option maxLevel, else rank.maxLevel).
        /// </summary>
        public int MaxLevel { get; set; } = 0;

        /// <summary>
        /// The field's level label (for example Stars).
        /// </summary>
        public string LevelLabel { get; set; } = String.Empty;

        /// <summary>
        /// Display text, for example Gold, 2 Stars.
        /// </summary>
        public string? RankLabel { get; set; } = null;

        /// <summary>
        /// Whether a verified rank stands.
        /// </summary>
        public bool Verified { get; set; } = false;

        /// <summary>
        /// Verified, SelfReported, or None.
        /// </summary>
        public string Source { get; set; } = "None";

        /// <summary>
        /// Status of the member's memberranks row, or null when there is none.
        /// </summary>
        public RankStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Awarder of the verified rank.
        /// </summary>
        public string? AwardedBy { get; set; } = null;

        /// <summary>
        /// Awarder summary.
        /// </summary>
        public UserSummary? Awarder { get; set; } = null;

        /// <summary>
        /// Award time of the verified rank.
        /// </summary>
        public DateTime? AwardedUtc { get; set; } = null;

        #endregion
    }
}
