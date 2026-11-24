namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Public domains of the property.
    /// </summary>
    public class PropertyDomains
    {
        #region Public-Members

        /// <summary>
        /// Website domain without scheme, for example example.test. Required.
        /// </summary>
        public string Web { get; set; } = String.Empty;

        /// <summary>
        /// API domain without scheme. Required.
        /// </summary>
        public string Api { get; set; } = String.Empty;

        /// <summary>
        /// Media domain without scheme, or empty.
        /// </summary>
        public string Media { get; set; } = String.Empty;

        /// <summary>
        /// Additional domains that redirect to the website. Never null after validation.
        /// </summary>
        public List<string> Redirects { get; set; } = new List<string>();

        #endregion
    }
}
