namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The optional property.json events section (docs/PHASE4_CONTRACT.md section 0.2).
    /// </summary>
    public class PropertyEventsSection
    {
        #region Public-Members

        /// <summary>
        /// Event categories (0 to 50 options with an optional color). Default empty.
        /// </summary>
        public List<PropertyOptionSeed> Categories { get; set; } = new List<PropertyOptionSeed>();

        /// <summary>
        /// Whether members may propose events through the review queue (resolved decision 10). Default true.
        /// </summary>
        public bool Proposals { get; set; } = true;

        #endregion
    }
}
