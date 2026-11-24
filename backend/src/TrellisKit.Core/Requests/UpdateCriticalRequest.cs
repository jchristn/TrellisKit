namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/update-groups/{id}/critical.
    /// </summary>
    public class UpdateCriticalRequest
    {
        /// <summary>
        /// Critical flag.
        /// </summary>
        public bool Critical { get; set; } = false;

        /// <summary>
        /// Prompt text up to 200 characters, or null.
        /// </summary>
        public string? CriticalMessage { get; set; } = null;
    }
}
