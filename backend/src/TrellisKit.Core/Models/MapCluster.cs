namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One grid cell of the map clustering query of venues or events (docs/PHASE4_CONTRACT.md sections 3.3 and 4.2).
    /// </summary>
    public class MapCluster
    {
        #region Public-Members

        /// <summary>Stable latitude grid index within a query's cell size.</summary>
        public long GridLatitude { get; set; }

        /// <summary>Stable longitude grid index within a query's cell size.</summary>
        public long GridLongitude { get; set; }

        /// <summary>
        /// Items in the cell.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Mean latitude.
        /// </summary>
        public double Lat { get; set; } = 0;

        /// <summary>
        /// Mean longitude.
        /// </summary>
        public double Lng { get; set; } = 0;

        /// <summary>
        /// Southern edge of the members.
        /// </summary>
        public double South { get; set; } = 0;

        /// <summary>
        /// Western edge of the members.
        /// </summary>
        public double West { get; set; } = 0;

        /// <summary>
        /// Northern edge of the members.
        /// </summary>
        public double North { get; set; } = 0;

        /// <summary>
        /// Eastern edge of the members.
        /// </summary>
        public double East { get; set; } = 0;

        /// <summary>
        /// The only item (venue or event) when the cell holds one.
        /// </summary>
        public string? ItemId { get; set; } = null;

        #endregion
    }
}
