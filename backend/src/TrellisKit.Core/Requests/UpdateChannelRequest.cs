namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/updates/channels.
    /// </summary>
    public class UpdateChannelRequest
    {
        /// <summary>
        /// Channel name, ^[a-z][a-z0-9-]{1,31}$.
        /// </summary>
        public string Name { get; set; } = String.Empty;
    }
}
