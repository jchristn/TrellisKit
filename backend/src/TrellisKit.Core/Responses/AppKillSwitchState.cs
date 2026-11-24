namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One kill switch resolved for the caller.
    /// </summary>
    public class AppKillSwitchState
    {
        /// <summary>
        /// Killed for this caller.
        /// </summary>
        public bool Killed { get; set; } = false;

        /// <summary>
        /// Message, or null.
        /// </summary>
        public string? Message { get; set; } = null;
    }
}
