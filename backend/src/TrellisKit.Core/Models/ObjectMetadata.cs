namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Labels and tags of one object (docs/ENUMERATION_AND_METADATA.md section 1). Never null collections.
    /// </summary>
    public class ObjectMetadata
    {
        #region Public-Members

        /// <summary>
        /// Labels, stored with the casing first used. Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags (key to value). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}
