namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The verified rank awarding rules of a Rank profile field (rank.awarding; docs/TRELLISKIT.md, Ranks, curricula, and
    /// attendance). Options are ordered lowest to highest by their position in the field. SystemAdmin is exempt; every
    /// other awarder needs a Verified rank in the field and:
    /// <list type="bullet">
    /// <item>when optionAwarderMinimum is set, an option at or above it;</item>
    /// <item>an awarded option strictly below their own; or their own option when allowEqualOption is true, never above
    /// their own level; or, when allowEqualOptionLevels is true, a level within their own option for a member who
    /// already holds it verified, never above their own level.</item>
    /// </list>
    /// Thread safety: stateless.
    /// </summary>
    public static class RankAwardRules
    {
        #region Public-Methods

        /// <summary>
        /// The position of an option in a field (case-insensitive), or -1.
        /// </summary>
        /// <param name="field">Rank field.</param>
        /// <param name="optionKey">Option key.</param>
        /// <returns>Zero-based position, or -1 when the key is not an option.</returns>
        /// <exception cref="ArgumentNullException">The field is null.</exception>
        public static int IndexOf(ProfileField field, string? optionKey)
        {
            ArgumentNullException.ThrowIfNull(field);
            if (String.IsNullOrWhiteSpace(optionKey)) return -1;
            string key = optionKey.Trim();
            for (int i = 0; i < field.Options.Count; i++)
            {
                if (field.Options[i] != null && String.Equals(field.Options[i].Key, key, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>
        /// The level maximum of an option: its maxLevel, else the field's rank.maxLevel, clamped to 0 to 20.
        /// </summary>
        /// <param name="field">Rank field.</param>
        /// <param name="option">Option.</param>
        /// <returns>Maximum level.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static int MaxLevelOf(ProfileField field, ProfileFieldOption option)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(option);
            return Math.Clamp(option.MaxLevel ?? field.Rank?.MaxLevel ?? 0, 0, 20);
        }

        /// <summary>
        /// Whether an awarder may award an option and level to a member.
        /// </summary>
        /// <param name="field">Rank field.</param>
        /// <param name="isSystemAdmin">Whether the awarder is SystemAdmin (exempt).</param>
        /// <param name="awarder">The awarder's rank row in the field, or null.</param>
        /// <param name="target">The member's rank row in the field, or null.</param>
        /// <param name="optionKey">Awarded option.</param>
        /// <param name="level">Awarded level.</param>
        /// <returns>True when allowed.</returns>
        /// <exception cref="ArgumentNullException">The field is null.</exception>
        public static bool Allows(ProfileField field, bool isSystemAdmin, MemberRank? awarder, MemberRank? target, string optionKey, int level)
        {
            ArgumentNullException.ThrowIfNull(field);
            if (isSystemAdmin) return true;
            if (awarder == null || awarder.Status != TrellisKit.Core.Enums.RankStatusEnum.Verified) return false;
            int own = IndexOf(field, awarder.OptionKey);
            int awarded = IndexOf(field, optionKey);
            if (own < 0 || awarded < 0) return false;
            ProfileRankAwarding rules = field.Rank?.Awarding ?? new ProfileRankAwarding();
            if (!String.IsNullOrWhiteSpace(rules.OptionAwarderMinimum))
            {
                int minimum = IndexOf(field, rules.OptionAwarderMinimum);
                if (minimum >= 0 && own < minimum) return false;
            }
            if (awarded < own) return true;
            if (awarded > own) return false;
            if (level > awarder.Level) return false;
            if (rules.AllowEqualOption) return true;
            bool holdsOption = target != null && target.Status == TrellisKit.Core.Enums.RankStatusEnum.Verified && IndexOf(field, target.OptionKey) == awarded;
            return rules.AllowEqualOptionLevels && holdsOption;
        }

        /// <summary>
        /// The rank after an option and level: the next level of the option while below its maximum, else level 0 of the
        /// next option, else null at the highest rank.
        /// </summary>
        /// <param name="field">Rank field.</param>
        /// <param name="optionKey">Current option.</param>
        /// <param name="level">Current level.</param>
        /// <param name="nextLevel">Next level.</param>
        /// <returns>Next option, or null.</returns>
        /// <exception cref="ArgumentNullException">The field is null.</exception>
        public static ProfileFieldOption? Next(ProfileField field, string optionKey, int level, out int nextLevel)
        {
            ArgumentNullException.ThrowIfNull(field);
            nextLevel = 0;
            int index = IndexOf(field, optionKey);
            if (index < 0) return null;
            ProfileFieldOption option = field.Options[index];
            if (level < MaxLevelOf(field, option))
            {
                nextLevel = level + 1;
                return option;
            }
            List<ProfileFieldOption> later = field.Options.Skip(index + 1).Where(o => o != null && !o.Retired).ToList();
            return later.Count == 0 ? null : later[0];
        }

        /// <summary>
        /// Display text of a rank, for example "Gold, 2 Stars" (the level part is omitted at level 0 or without a level
        /// label).
        /// </summary>
        /// <param name="field">Rank field.</param>
        /// <param name="optionKey">Option key.</param>
        /// <param name="level">Level.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentNullException">The field is null.</exception>
        public static string Label(ProfileField field, string optionKey, int level)
        {
            ArgumentNullException.ThrowIfNull(field);
            int index = IndexOf(field, optionKey);
            string text = index < 0 ? optionKey : field.Options[index].Label;
            string levelLabel = (field.Rank?.LevelLabel ?? String.Empty).Trim();
            if (level > 0 && levelLabel.Length > 0) text += ", " + level + " " + levelLabel;
            return text;
        }

        #endregion
    }
}
