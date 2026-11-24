namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The usage meter catalog (docs/USAGE_METERING_CONTRACT.md section 2), fixed by the engine version. Meter ids are
    /// stable; a changed definition gets a new id.
    /// </summary>
    public static class UsageMeters
    {
        #region Public-Members

        /// <summary>
        /// Meter groups in display order.
        /// </summary>
        public static readonly IReadOnlyList<string> Groups = new List<string> { "users", "content", "interactions", "media", "storage", "platform", "ai" };

        /// <summary>
        /// Every meter, in display order.
        /// </summary>
        public static readonly IReadOnlyList<UsageMeter> All = Build();

        /// <summary>
        /// The storage classes summed by storage.total.
        /// </summary>
        public static readonly IReadOnlyList<string> StorageClasses = new List<string> { "storage.originals", "storage.imageVariants", "storage.videoRenditions", "storage.updateAssets", "storage.exports" };

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, UsageMeter> _ById = All.ToDictionary(m => m.Id, StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a meter by id (case-sensitive).
        /// </summary>
        /// <param name="id">Meter id.</param>
        /// <returns>Meter, or null.</returns>
        public static UsageMeter? Find(string? id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            return _ById.TryGetValue(id, out UsageMeter? meter) ? meter : null;
        }

        /// <summary>
        /// Resolve a comma-separated list of meter ids and group names into meters (catalog order, no duplicates).
        /// </summary>
        /// <param name="text">Ids and group names, or null or empty for every meter.</param>
        /// <param name="unknown">The first unknown entry, or null.</param>
        /// <returns>Meters.</returns>
        public static List<UsageMeter> Resolve(string? text, out string? unknown)
        {
            unknown = null;
            if (String.IsNullOrWhiteSpace(text)) return All.ToList();
            HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Groups.Contains(raw))
                {
                    foreach (UsageMeter m in All.Where(m => m.Group == raw)) wanted.Add(m.Id);
                }
                else if (_ById.ContainsKey(raw)) wanted.Add(raw);
                else if (unknown == null) unknown = raw;
            }
            return All.Where(m => wanted.Contains(m.Id)).ToList();
        }

        /// <summary>
        /// The deterministic id of a daily usage record.
        /// </summary>
        /// <param name="meter">Meter id.</param>
        /// <param name="day">Local day (yyyy-MM-dd).</param>
        /// <returns>Record id (usd_yyyymmdd_meter-with-dashes).</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        public static string RecordId(string meter, string day)
        {
            if (String.IsNullOrEmpty(meter)) throw new ArgumentNullException(nameof(meter));
            if (String.IsNullOrEmpty(day)) throw new ArgumentNullException(nameof(day));
            return "usd_" + day.Replace("-", String.Empty, StringComparison.Ordinal) + "_" + meter.Replace('.', '-');
        }

        #endregion

        #region Private-Methods

        private static List<UsageMeter> Build()
        {
            const UsageMeterKindEnum count = UsageMeterKindEnum.Count;
            const UsageMeterKindEnum gauge = UsageMeterKindEnum.Gauge;
            const UsageMeterKindEnum distinct = UsageMeterKindEnum.Distinct;
            const UsageMeterSourceEnum derived = UsageMeterSourceEnum.Derived;
            const UsageAggregateEnum sum = UsageAggregateEnum.Sum;
            const UsageAggregateEnum last = UsageAggregateEnum.Last;
            return new List<UsageMeter>
            {
                new UsageMeter("users.total", "users", gauge, "count", derived, last, false),
                new UsageMeter("users.new", "users", count, "count", derived, sum, false),
                new UsageMeter("users.deleted", "users", count, "count", derived, sum, false),
                new UsageMeter("users.active", "users", distinct, "count", derived, UsageAggregateEnum.Distinct, false),
                new UsageMeter("users.activeWeekly", "users", distinct, "count", derived, last, true),
                new UsageMeter("users.activeMonthly", "users", distinct, "count", derived, last, true),
                new UsageMeter("posts.new", "content", count, "count", derived, sum, false),
                new UsageMeter("posts.total", "content", gauge, "count", derived, last, false),
                new UsageMeter("comments.new", "content", count, "count", derived, sum, false),
                new UsageMeter("comments.total", "content", gauge, "count", derived, last, false),
                new UsageMeter("interactions.new", "interactions", count, "count", derived, sum, false),
                new UsageMeter("reactions.new", "interactions", count, "count", derived, sum, false),
                new UsageMeter("pollVotes.new", "interactions", count, "count", derived, sum, false),
                new UsageMeter("follows.new", "interactions", count, "count", derived, sum, false),
                new UsageMeter("assistantMessages.new", "interactions", count, "count", derived, sum, false),
                new UsageMeter("images.new", "media", count, "count", derived, sum, false),
                new UsageMeter("images.bytes", "media", count, "bytes", derived, sum, false),
                new UsageMeter("images.total", "media", gauge, "count", derived, last, false),
                new UsageMeter("videos.new", "media", count, "count", derived, sum, false),
                new UsageMeter("videos.bytes", "media", count, "bytes", derived, sum, false),
                new UsageMeter("videos.total", "media", gauge, "count", derived, last, false),
                new UsageMeter("storage.total", "storage", gauge, "bytes", derived, last, false),
                new UsageMeter("storage.originals", "storage", gauge, "bytes", derived, last, false),
                new UsageMeter("storage.imageVariants", "storage", gauge, "bytes", derived, last, false),
                new UsageMeter("storage.videoRenditions", "storage", gauge, "bytes", UsageMeterSourceEnum.Measured, last, false),
                new UsageMeter("storage.updateAssets", "storage", gauge, "bytes", derived, last, false),
                new UsageMeter("storage.exports", "storage", gauge, "bytes", derived, last, false),
                new UsageMeter("api.requests", "platform", count, "count", UsageMeterSourceEnum.Recorded, sum, false),
                new UsageMeter("api.errors", "platform", count, "count", UsageMeterSourceEnum.Recorded, sum, false),
                new UsageMeter("push.sent", "platform", count, "count", derived, sum, false),
                new UsageMeter("email.sent", "platform", count, "count", derived, sum, false),
                new UsageMeter("ai.requests", "ai", count, "count", derived, sum, false),
                new UsageMeter("ai.promptTokens", "ai", count, "tokens", derived, sum, false),
                new UsageMeter("ai.completionTokens", "ai", count, "tokens", derived, sum, false),
                new UsageMeter("ai.costMicros", "ai", count, "usdMicros", derived, sum, false)
            };
        }

        #endregion
    }
}
