namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A map feature: a point (one venue or event) or a cluster (docs/PHASE4_CONTRACT.md sections 3.3 and 4.2).
    /// </summary>
    public class MapFeatureView
    {
        #region Public-Members

        /// <summary>Stable feature identifier within the viewport, visibility and feature budget.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Point creation timestamp; generated grid cells use the Unix epoch.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UnixEpoch;

        /// <summary>Map features carry empty metadata; filters select the underlying source records.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Map features carry empty metadata; filters select the underlying source records.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Point or Cluster.
        /// </summary>
        public MapFeatureKindEnum Kind { get; set; } = MapFeatureKindEnum.Point;

        /// <summary>
        /// The venue of a point.
        /// </summary>
        public MapVenueView? Venue { get; set; } = null;

        /// <summary>
        /// The event of a point (events map; omitted on directory features).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MapEventView? Event { get; set; } = null;

        /// <summary>
        /// Venues or events in a cluster.
        /// </summary>
        public int? Count { get; set; } = null;

        /// <summary>
        /// Cluster center latitude.
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Cluster center longitude.
        /// </summary>
        public double? Lng { get; set; } = null;

        /// <summary>
        /// Bounding box of a cluster's members (tap to zoom).
        /// </summary>
        public GeoBox? Bbox { get; set; } = null;

        #endregion
    }
}
