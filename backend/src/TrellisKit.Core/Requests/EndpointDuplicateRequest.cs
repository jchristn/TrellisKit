namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/ai/endpoints/{id}/duplicate.
    /// </summary>
    public class EndpointDuplicateRequest
    {
        #region Public-Members

        /// <summary>
        /// Name of the copy.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Copy the stored secrets.
        /// </summary>
        public bool IncludeSecrets { get; set; } = false;

        #endregion
    }
}
