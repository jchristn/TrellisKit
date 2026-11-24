namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/ai/assistants/{id}/knowledge/preview.
    /// </summary>
    public class KnowledgePreviewRequest
    {
        #region Public-Members

        /// <summary>
        /// Query text.
        /// </summary>
        public string? Query { get; set; } = null;

        /// <summary>
        /// Retrieve as this member (default an anonymous Members caller).
        /// </summary>
        public string? AsUserId { get; set; } = null;

        #endregion
    }
}
