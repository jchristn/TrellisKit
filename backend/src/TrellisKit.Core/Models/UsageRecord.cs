namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One daily usage row of one meter (usagedaily; docs/USAGE_METERING_CONTRACT.md section 7.1).
    /// </summary>
    public class UsageRecord
    {
        #region Public-Members

        /// <summary>
        /// Deterministic id (usd_yyyymmdd_meter).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Owning property.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Meter id.
        /// </summary>
        public string Meter { get; set; } = String.Empty;

        /// <summary>
        /// Meter group.
        /// </summary>
        public string Group { get; set; } = String.Empty;

        /// <summary>
        /// Meter kind.
        /// </summary>
        public UsageMeterKindEnum Kind { get; set; } = UsageMeterKindEnum.Count;

        /// <summary>
        /// Unit.
        /// </summary>
        public string Unit { get; set; } = "count";

        /// <summary>
        /// Local day (yyyy-MM-dd) in <see cref="TimeZone"/>.
        /// </summary>
        public string Day { get; set; } = String.Empty;

        /// <summary>
        /// Time zone the row was computed in.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Local midnight starting the day (UTC).
        /// </summary>
        public DateTime PeriodStartUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Local midnight ending the day (UTC).
        /// </summary>
        public DateTime PeriodEndUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Value (count, bytes, tokens, or usdMicros).
        /// </summary>
        public long Value { get; set; } = 0;

        /// <summary>
        /// Whether the row is closed (immutable).
        /// </summary>
        public bool Closed { get; set; } = false;

        /// <summary>
        /// When the value was last computed.
        /// </summary>
        public DateTime ComputedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// When the row was first written.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Labels visible to the caller. Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags visible to the caller. Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
