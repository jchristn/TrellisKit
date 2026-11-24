namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/updates/events.
    /// </summary>
    public class UpdateEventsRequest
    {
        /// <summary>
        /// 1 to 20 events.
        /// </summary>
        public List<UpdateEventRequest> Events { get; set; } = new List<UpdateEventRequest>();
    }
}
