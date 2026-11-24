namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/ai/endpoints/{id}/test (fields by kind; defaults are built in).
    /// </summary>
    public class EndpointTestRequest
    {
        #region Public-Members

        /// <summary>
        /// Completion prompt.
        /// </summary>
        public string? Prompt { get; set; } = null;

        /// <summary>
        /// Completion token cap: default 1024, minimum 1, maximum 8192 (values outside are clamped).
        /// </summary>
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Embedding inputs (at most 10).
        /// </summary>
        public List<string>? Input { get; set; } = null;

        /// <summary>
        /// Rerank query.
        /// </summary>
        public string? Query { get; set; } = null;

        /// <summary>
        /// Rerank documents (at most 10).
        /// </summary>
        public List<string>? Documents { get; set; } = null;

        #endregion
    }
}
