namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/ai/assistants/{id}/test.
    /// </summary>
    public class AssistantTestRequest
    {
        #region Public-Members

        /// <summary>
        /// 1 to 20 messages; the last is the user turn answered.
        /// </summary>
        public List<AssistantTestMessage>? Messages { get; set; } = null;

        /// <summary>
        /// Unsaved assistant fields to test with.
        /// </summary>
        public AssistantRequest? UseDraft { get; set; } = null;

        #endregion
    }
}
