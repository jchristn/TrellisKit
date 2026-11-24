namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The related rows of one page of events, loaded once by <see cref="EventViewBuilder"/>.
    /// </summary>
    internal sealed class EventPageData
    {
        internal Dictionary<string, EventCategory> Categories { get; } = new Dictionary<string, EventCategory>(StringComparer.Ordinal);

        internal Dictionary<string, EventVenueSnapshot> Venues { get; set; } = new Dictionary<string, EventVenueSnapshot>(StringComparer.Ordinal);

        internal Dictionary<string, EventRecurrence> Recurrences { get; set; } = new Dictionary<string, EventRecurrence>(StringComparer.Ordinal);

        internal Dictionary<string, EventRsvp> MyRsvps { get; set; } = new Dictionary<string, EventRsvp>(StringComparer.Ordinal);

        internal Dictionary<string, MediaView> Covers { get; } = new Dictionary<string, MediaView>(StringComparer.Ordinal);
    }
}
