namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// GET /api/v1/admin/ai/analytics/timeseries.
    /// </summary>
    public class AiTimeseriesView
    {
        #region Public-Members

        /// <summary>
        /// Metric.
        /// </summary>
        public string Metric { get; set; } = String.Empty;

        /// <summary>
        /// Granularity.
        /// </summary>
        public AiUsageGranularityEnum Granularity { get; set; } = AiUsageGranularityEnum.Day;

        /// <summary>
        /// At most 400 points.
        /// </summary>
        public List<AiTimeseriesPoint> Points { get; set; } = new List<AiTimeseriesPoint>();

        #endregion
    }
}
