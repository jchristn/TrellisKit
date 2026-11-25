namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The Up next card (upcoming published events the member RSVPed Going to, by start, at most training.upNextLimit)
    /// and the events part of the Schedule section: available when features.events is on and the member has an upcoming
    /// RSVP (Going, Interested, or Waitlisted). Any RSVP, past or upcoming, counts as activity for firstRun.
    /// </summary>
    public sealed class EventsTrainingProvider : ITrainingSectionProvider
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return "events"; }
        }

        /// <inheritdoc />
        public IReadOnlyList<TrainingSectionEnum> Owns
        {
            get { return _Owns; }
        }

        #endregion

        #region Private-Members

        private static readonly IReadOnlyList<TrainingSectionEnum> _Owns = new List<TrainingSectionEnum> { TrainingSectionEnum.Schedule };
        private static readonly List<string> _AnyRsvp = new List<string> { "Going", "Interested", "Waitlisted" };
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for one property.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public EventsTrainingProvider(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<TrainingContribution> ContributeAsync(TrainingContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            TrainingContribution contribution = new TrainingContribution();
            if (!_Services.Property.Property.Features.Events)
            {
                contribution.Sections.Add(new TrainingSectionView { Key = TrainingSectionEnum.Schedule, Reason = TrainingSectionReasonEnum.FeatureOff });
                return contribution;
            }
            int limit = _Services.Settings.Training.UpNextLimit;
            EnumerationResult<EventSummaryView> going = await _Services.Events.Catalog.MyEventsAsync(context.Actor, Query(new List<string> { "Going" }, true, limit), token).ConfigureAwait(false);
            List<TrainingUpNextItem> items = going.Objects
                .Where(e => e.Status == EventStatusEnum.Published && e.MyRsvp != null && e.MyRsvp.Status == EventRsvpStatusEnum.Going && e.EndUtc > context.NowUtc)
                .Take(limit)
                .Select(e => new TrainingUpNextItem
                {
                    Kind = "Event",
                    Id = e.Id,
                    Title = e.Title,
                    StartUtc = e.StartUtc,
                    EndUtc = e.EndUtc,
                    TimeZone = e.TimeZone,
                    LocationSummary = Location(e),
                    Path = "/events/" + e.Id,
                    Labels = e.Labels,
                    Tags = e.Tags
                }).ToList();
            contribution.UpNext = going.With(items);
            bool upcoming = items.Count > 0;
            if (!upcoming)
            {
                EnumerationResult<EventSummaryView> any = await _Services.Events.Catalog.MyEventsAsync(context.Actor, Query(_AnyRsvp, true, 1), token).ConfigureAwait(false);
                upcoming = any.Objects.Any(e => e.MyRsvp != null && e.Status == EventStatusEnum.Published);
            }
            bool activity = upcoming;
            if (!activity)
            {
                EnumerationResult<EventSummaryView> ever = await _Services.Events.Catalog.MyEventsAsync(context.Actor, Query(_AnyRsvp, false, 1), token).ConfigureAwait(false);
                activity = ever.Objects.Any(e => e.MyRsvp != null);
            }
            contribution.HasActivity = activity;
            contribution.Sections.Add(new TrainingSectionView { Key = TrainingSectionEnum.Schedule, Available = upcoming, Reason = upcoming ? null : TrainingSectionReasonEnum.NoData });
            return contribution;
        }

        #endregion

        #region Private-Methods

        private static EnumerationQuery Query(List<string> statuses, bool upcoming, int limit)
        {
            EnumerationQuery query = new EnumerationQuery { MaxResults = limit };
            query.Filters["rsvpStatus"] = JsonSerializer.SerializeToElement(statuses);
            if (upcoming) query.SetFilter("upcoming", "true");
            return query;
        }

        private static string? Location(EventSummaryView e)
        {
            if (e.Format == EventFormatEnum.Online) return "Online";
            string? place = e.Location?.Venue?.Name ?? e.Location?.Name ?? e.Location?.Address?.Locality;
            return String.IsNullOrWhiteSpace(place) ? null : place;
        }

        #endregion
    }
}
