namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Phase 5 support enumeration collections (docs/PHASE5_CONTRACT.md section 6): the member's own tickets, ticket
    /// messages, ticket types, and the staff inbox. The ticket alias is t; the inbox joins the requester as ru.
    /// </summary>
    public static class SupportCollections
    {
        #region Public-Members

        /// <summary>
        /// The member's own tickets.
        /// </summary>
        public static readonly EnumerationCollection MemberTickets = new EnumerationCollection("tickets", TargetTypeEnum.Ticket, "t.id", "t.createdutc", false)
            .Order("LastActivityDescending", "t.lastactivityutc", true, EnumerationKeyKindEnum.Time)
            .Search("t.title")
            .Filters("status", "type")
            .DefaultTo("LastActivityDescending");

        /// <summary>
        /// Ticket messages (members: replies and status lines; staff: everything).
        /// </summary>
        public static readonly EnumerationCollection Messages = new EnumerationCollection("ticket-messages", TargetTypeEnum.TicketMessage, "m.id", "m.createdutc", false)
            .Filters("kind")
            .DefaultTo("CreatedAscending");

        /// <summary>
        /// Ticket types (members see enabled types; staff may include retired ones).
        /// </summary>
        public static readonly EnumerationCollection TicketTypes = new EnumerationCollection("ticket-types", TargetTypeEnum.TicketType, "tt.id", "tt.createdutc", false)
            .Order("SortOrderAscending", "tt.sortorder", false, EnumerationKeyKindEnum.Number)
            .Search("tt.key", "tt.label")
            .Filters("includeRetired")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// Admin ticket types.
        /// </summary>
        public static readonly EnumerationCollection AdminTicketTypes = new EnumerationCollection("admin-ticket-types", TargetTypeEnum.TicketType, "tt.id", "tt.createdutc", true)
            .Order("SortOrderAscending", "tt.sortorder", false, EnumerationKeyKindEnum.Number)
            .Search("tt.key", "tt.label")
            .Filters("includeRetired")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// The staff inbox. Search covers the number, title, body, and the requester's display name and email (PostgreSQL
        /// text matching on the property's own rows; tickets are never in global search).
        /// </summary>
        public static readonly EnumerationCollection Inbox = new EnumerationCollection("admin-tickets", TargetTypeEnum.Ticket, "t.id", "t.createdutc", true)
            .Order("LastActivityDescending", "t.lastactivityutc", true, EnumerationKeyKindEnum.Time)
            .Order("DueAscending", "COALESCE(t.firstresponsedueutc, TIMESTAMPTZ '9999-12-31T00:00:00Z')", false, EnumerationKeyKindEnum.Time)
            .Order("PriorityDescending", "(CASE t.priority WHEN 'Urgent' THEN 3 WHEN 'High' THEN 2 WHEN 'Normal' THEN 1 ELSE 0 END)", true, EnumerationKeyKindEnum.Number)
            .Search("CAST(t.number AS TEXT)", "t.title", "t.body", "ru.displayname", "ru.email")
            .Filters("status", "type", "priority", "assigneeUserId", "unassigned", "requesterUserId", "overdue", "hasIdea")
            .DefaultTo("LastActivityDescending");

        #endregion
    }
}
