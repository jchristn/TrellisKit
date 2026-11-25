namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Awarding rules of a Rank profile field (rank.awarding). SystemAdmin is exempt from every rule; neutral defaults allow awarding any option strictly below the awarder's verified option and levels within it.
    /// </summary>
    public class ProfileRankAwarding
    {
        #region Public-Members

        /// <summary>
        /// Option key: an awarder's verified option must be at or above it to award anything. Default none.
        /// </summary>
        public string? OptionAwarderMinimum { get; set; } = null;

        /// <summary>
        /// Whether an awarder may award their own option (otherwise the awarded option must be strictly below theirs, except levels within it). Default false.
        /// </summary>
        public bool AllowEqualOption { get; set; } = false;

        /// <summary>
        /// Whether an awarder may award levels within their own option to a member who already holds it (never above the awarder's own level). Default true.
        /// </summary>
        public bool AllowEqualOptionLevels { get; set; } = true;

        #endregion
    }
}
