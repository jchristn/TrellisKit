namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One timeseries point.
    /// </summary>
    public class AiTimeseriesPoint
    {
        /// <summary>Aggregate observations have empty inline labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Aggregate observations have empty inline tags.</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
        #region Public-Members

        /// <summary>
        /// Period start.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Value.
        /// </summary>
        public double Value { get; set; } = 0;

        #endregion
    }
}
