namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/email/suppressions.
    /// </summary>
    public class EmailSuppressionRequest
    {
        #region Public-Members

        /// <summary>
        /// Address.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Staff note, at most 500 characters.
        /// </summary>
        public string? Note { get; set; } = null;

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
