namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    /// <summary>One meter observation, rather than a meter containing an unpaged values array.</summary>
    public sealed class UsageObservationRow
    {
        /// <summary>Stable meter/bucket identity.</summary>
        public string Id { get; set; } = String.Empty;
        /// <summary>Meter key.</summary>
        public string Meter { get; set; } = String.Empty;
        /// <summary>Meter group.</summary>
        public string Group { get; set; } = String.Empty;
        /// <summary>Meter kind.</summary>
        public UsageMeterKindEnum Kind { get; set; } = UsageMeterKindEnum.Count;
        /// <summary>Measurement unit.</summary>
        public string Unit { get; set; } = "count";
        /// <summary>Whole-period total.</summary>
        public long Total { get; set; } = 0;
        /// <summary>Previous equivalent period total.</summary>
        public long PreviousTotal { get; set; } = 0;
        /// <summary>Fractional period change.</summary>
        public double? Change { get; set; } = null;
        /// <summary>Whether totals are exact.</summary>
        public bool Exact { get; set; } = true;
        /// <summary>Bucket start.</summary>
        public DateTime StartUtc { get; set; } = default;
        /// <summary>Bucket end.</summary>
        public DateTime EndUtc { get; set; } = default;
        /// <summary>Local bucket label.</summary>
        public string Label { get; set; } = String.Empty;
        /// <summary>Bucket observation, null where unavailable.</summary>
        public long? Value { get; set; } = null;
        /// <summary>Empty aggregate labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Empty aggregate tags.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
