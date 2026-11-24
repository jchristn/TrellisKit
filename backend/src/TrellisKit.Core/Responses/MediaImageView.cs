namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Image fields of the Media payload.
    /// </summary>
    public class MediaImageView
    {
        #region Public-Members

        /// <summary>
        /// Variants keyed thumb, medium, large (fixed keys).
        /// </summary>
        public Dictionary<string, MediaVariantView> Variants { get; set; } = new Dictionary<string, MediaVariantView>();

        #endregion
    }
}
