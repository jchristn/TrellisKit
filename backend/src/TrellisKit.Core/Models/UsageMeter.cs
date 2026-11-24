namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One entry of the usage meter catalog (docs/USAGE_METERING_CONTRACT.md section 2). Immutable.
    /// </summary>
    public sealed class UsageMeter
    {
        #region Public-Members

        /// <summary>
        /// Stable meter id, for example posts.new.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Group: users, content, interactions, media, storage, platform, or ai.
        /// </summary>
        public string Group { get; }

        /// <summary>
        /// Kind.
        /// </summary>
        public UsageMeterKindEnum Kind { get; }

        /// <summary>
        /// Unit: count, bytes, tokens, or usdMicros.
        /// </summary>
        public string Unit { get; }

        /// <summary>
        /// Source.
        /// </summary>
        public UsageMeterSourceEnum Source { get; }

        /// <summary>
        /// How values combine into larger buckets and periods.
        /// </summary>
        public UsageAggregateEnum Aggregate { get; }

        /// <summary>
        /// Whether the meter exists only in daily rows (trailing distinct counts).
        /// </summary>
        public bool DailyOnly { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a catalog entry.
        /// </summary>
        /// <param name="id">Meter id.</param>
        /// <param name="group">Group.</param>
        /// <param name="kind">Kind.</param>
        /// <param name="unit">Unit.</param>
        /// <param name="source">Source.</param>
        /// <param name="aggregate">Aggregate.</param>
        /// <param name="dailyOnly">Whether only daily rows exist.</param>
        /// <exception cref="ArgumentNullException">The id, group, or unit is null or empty.</exception>
        public UsageMeter(string id, string group, UsageMeterKindEnum kind, string unit, UsageMeterSourceEnum source, UsageAggregateEnum aggregate, bool dailyOnly)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrEmpty(group)) throw new ArgumentNullException(nameof(group));
            if (String.IsNullOrEmpty(unit)) throw new ArgumentNullException(nameof(unit));
            Id = id;
            Group = group;
            Kind = kind;
            Unit = unit;
            Source = source;
            Aggregate = aggregate;
            DailyOnly = dailyOnly;
        }

        #endregion
    }
}
