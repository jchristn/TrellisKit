namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The map enumeration body (docs/PHASE4_CONTRACT.md section 3.3).
    /// </summary>
    public class MapEnumerationQuery : EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Viewport (required).
        /// </summary>
        public GeoBox? Bbox { get; set; } = null;

        /// <summary>
        /// Map zoom 0 to 22 (default 2).
        /// </summary>
        public int? Zoom { get; set; } = null;

        #endregion
    }
}
