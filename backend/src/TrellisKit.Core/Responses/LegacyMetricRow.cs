namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    /// <summary>One actual daily observation of a legacy aggregate, independently pageable.</summary>
    public sealed class LegacyMetricRow
    {
        /// <summary>Stable series/date identity.</summary>
        public string Id { get; set; } = String.Empty;
        /// <summary>Original series name, for example postsByDay.</summary>
        public string Series { get; set; } = String.Empty;
        /// <summary>UTC day (yyyy-MM-dd).</summary>
        public string Date { get; set; } = String.Empty;
        /// <summary>Observed count.</summary>
        public long Value { get; set; }
        /// <summary>Empty inline labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Empty inline tags.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
