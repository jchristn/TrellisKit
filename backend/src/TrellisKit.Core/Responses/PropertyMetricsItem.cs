namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Usage of one property (POST /api/v1/platform/metrics/properties/enumerate).
    /// </summary>
    public class PropertyMetricsItem : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public PropertyStatusEnum Status { get; set; } = PropertyStatusEnum.Disabled;

        /// <summary>
        /// Non-deleted member accounts.
        /// </summary>
        public long Members { get; set; } = 0;

        /// <summary>
        /// Members seen in the last 30 days.
        /// </summary>
        public long ActiveMembers { get; set; } = 0;

        /// <summary>
        /// Captured requests in the last 24 hours (0 when request history is off).
        /// </summary>
        public long Requests24h { get; set; } = 0;

        /// <summary>
        /// Captured requests with status 500 or above in the last 24 hours.
        /// </summary>
        public long Errors24h { get; set; } = 0;

        /// <summary>
        /// Bytes of stored media that is not deleted.
        /// </summary>
        public long StorageBytes { get; set; } = 0;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
