namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Create or edit a program option.
    /// </summary>
    public class DirectoryProgramRequest
    {
        #region Public-Members

        /// <summary>
        /// Stable key (1 to 40 of a to z, 0 to 9, hyphen, underscore; create only).
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Label (1 to 60).
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
