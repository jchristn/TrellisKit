namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of loading a property's email template files.
    /// </summary>
    public class PropertyEmailTemplateLoadResult
    {
        #region Public-Members

        /// <summary>
        /// Valid templates.
        /// </summary>
        public List<PropertyEmailTemplate> Templates { get; set; } = new List<PropertyEmailTemplate>();

        /// <summary>
        /// Problems, one per line, naming the file (files with problems are skipped).
        /// </summary>
        public List<string> Problems { get; set; } = new List<string>();

        #endregion
    }
}
