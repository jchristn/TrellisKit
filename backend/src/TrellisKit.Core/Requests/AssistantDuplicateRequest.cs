namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/ai/assistants/{id}/duplicate.
    /// </summary>
    public class AssistantDuplicateRequest
    {
        #region Public-Members

        /// <summary>
        /// Name of the copy.
        /// </summary>
        public string? Name { get; set; } = null;

        #endregion
    }
}
