namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Optional support section of property.json (docs/PHASE5_CONTRACT.md section 6). Absent or with no ticket types, the
    /// engine defaults Support, Bug, Enhancement, and Billing are seeded.
    /// </summary>
    public class PropertySupport
    {
        #region Public-Members

        /// <summary>
        /// Ticket types to seed (at most 50), or null for the engine defaults.
        /// </summary>
        public List<PropertyTicketTypeSeed>? TicketTypes { get; set; } = null;

        #endregion
    }
}
