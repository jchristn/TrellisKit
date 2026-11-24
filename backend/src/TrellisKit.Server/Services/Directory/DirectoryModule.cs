namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// The Phase 4 services of the venue and instructor directory (venues, instructors, programs, submissions, geocoding) for one property (docs/PHASE4_CONTRACT.md section 3)
    /// and their wiring into the Phase 2 and Phase 3 services.
    /// </summary>
    public class DirectoryModule : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Configuration, programs, map settings, and the property decorator.
        /// </summary>
        public DirectoryConfigService Config { get; }

        /// <summary>
        /// Geocoding and place search.
        /// </summary>
        public GeocodingService Geocoding { get; }

        /// <summary>
        /// Venue and instructor payloads.
        /// </summary>
        public DirectoryViewBuilder Views { get; }

        /// <summary>
        /// Venues.
        /// </summary>
        public VenueService Venues { get; }

        /// <summary>
        /// Instructors.
        /// </summary>
        public InstructorService Instructors { get; }

        /// <summary>
        /// Map enumeration.
        /// </summary>
        public GeoQueryService Map { get; }

        /// <summary>
        /// Submissions and claims.
        /// </summary>
        public DirectorySubmissionService Submissions { get; }

        /// <summary>
        /// Import, export, regeocoding, metrics, and worker passes.
        /// </summary>
        public DirectoryAdminService Admin { get; }

        /// <summary>
        /// Map tile proxy.
        /// </summary>
        public MapTileProxyService Tiles { get; }

        /// <summary>
        /// The content target provider of venues and instructors.
        /// </summary>
        public DirectoryTargetProvider Targets { get; }

        /// <summary>
        /// Member reviews and media of venues (sections 3.12 and 3.13).
        /// </summary>
        public VenueContributionService Contributions { get; }

        /// <summary>
        /// Called after a venue or instructor changes (search indexing and knowledge refresh plug in here), or null.
        /// </summary>
        public Func<TargetTypeEnum, string, CancellationToken, Task>? Changed { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _Attached = 0;
        private Action? _GeocodeSignal = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module (call <see cref="Attach"/> once the Phase 2 and Phase 3 services exist).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public DirectoryModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Config = new DirectoryConfigService(services);
            Geocoding = new GeocodingService(services);
            Views = new DirectoryViewBuilder(services);
            Venues = new VenueService(services);
            Instructors = new InstructorService(services);
            Map = new GeoQueryService(services);
            Submissions = new DirectorySubmissionService(services);
            Admin = new DirectoryAdminService(services);
            Tiles = new MapTileProxyService(services);
            Targets = new DirectoryTargetProvider(services);
            Contributions = new VenueContributionService(services);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the module into the shared services (idempotent).
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            _Services.ContentTargets.Register(Targets);
            Changed ??= (type, id, token) => _Services.Forums.Search.EnqueueAsync(type, id, token);
            _Services.Ai.Tools.Register(FindVenuesTool.Build(_Services));
            _Services.PropertyDecorators.Add(Config.DecorateAsync);
            DirectoryExportSections.Register(_Services);
            // Photos processed after their listing was saved reach the public paths once they are Ready.
            _Services.Media.AddSettledHandler((media, token) => (media.AttachedTargetType == TargetTypeEnum.Venue || media.AttachedTargetType == TargetTypeEnum.Instructor) && !String.IsNullOrEmpty(media.AttachedTargetId)
                ? SyncPublicMediaAsync(media.AttachedTargetType.Value, media.AttachedTargetId!, token)
                : Task.CompletedTask);
            _Services.PropertySettings.Changed += () =>
            {
                Geocoding.Invalidate();
                Tiles.Invalidate();
                Config.Invalidate();
            };
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                await EraseForAccountAsync(userId, token).ConfigureAwait(false);
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Add the module's background workers for this property.
        /// </summary>
        /// <param name="workers">Worker list of the property runtime.</param>
        /// <param name="token">Lifetime token (for signaled passes).</param>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        public void AddWorkers(List<IntervalWorker> workers, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(workers);
            TimeSpan interval() => TimeSpan.FromSeconds(_Services.Settings.Directory.GeocodeIntervalSeconds);
            LeasedPassWorker geocode = new LeasedPassWorker(_Services.Database, "GeocodeWorker", interval, TimeSpan.FromSeconds(10), Admin.GeocodePassAsync, _Services.Logger);
            workers.Add(geocode);
            workers.Add(new LeasedPassWorker(_Services.Database, "SubmissionDueWorker", interval, TimeSpan.FromSeconds(20), Submissions.OverduePassAsync, _Services.Logger));
            workers.Add(new LeasedPassWorker(_Services.Database, "GeocodeCachePruneWorker", () => TimeSpan.FromHours(6), TimeSpan.FromMinutes(5), Admin.PrunePassAsync, _Services.Logger));
            _GeocodeSignal = () => geocode.Signal(token);
        }

        /// <summary>
        /// Ask GeocodeWorker for a pass now (imports and regeocode requests).
        /// </summary>
        public void SignalGeocode()
        {
            _GeocodeSignal?.Invoke();
        }

        /// <summary>
        /// Keep a venue's or instructor's photos on the public media paths exactly while signed-out visitors may see it
        /// (Public audience and live: not deleted, archived, or moderated), so public listings load them without a media
        /// grant; photos of members-only or withdrawn listings are removed from the public paths.
        /// </summary>
        /// <param name="targetType">Venue or Instructor (other types are ignored).</param>
        /// <param name="targetId">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task SyncPublicMediaAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetId)) return;
            if (targetType == TargetTypeEnum.Venue)
            {
                TrellisKit.Core.Models.Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(targetId, token).ConfigureAwait(false);
                // A venue with a public landing page (docs/PHASE4_CONTRACT.md section 18.4) shows its staff photos to
                // signed-out visitors whatever its audience; member media (VenueMedia) is a separate target and stays private.
                bool shouldBePublic = venue != null && ((venue.Audience == ContentAudienceEnum.Public && DirectoryAccess.IsLive(venue)) || (_Services.PublicPages?.VenueHasPage(venue) ?? false));
                await _Services.Media.SyncTargetPublicAsync(TargetTypeEnum.Venue, targetId, shouldBePublic, token).ConfigureAwait(false);
            }
            else if (targetType == TargetTypeEnum.Instructor)
            {
                TrellisKit.Core.Models.Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(targetId, token).ConfigureAwait(false);
                bool shouldBePublic = instructor != null && instructor.Audience == ContentAudienceEnum.Public && DirectoryAccess.IsLive(instructor);
                await _Services.Media.SyncTargetPublicAsync(TargetTypeEnum.Instructor, targetId, shouldBePublic, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Announce a change of a venue or instructor.
        /// </summary>
        /// <param name="targetType">Venue or Instructor.</param>
        /// <param name="targetId">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ChangedAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (!String.IsNullOrEmpty(targetId)) await SyncPublicMediaAsync(targetType, targetId, token).ConfigureAwait(false);
            Func<TargetTypeEnum, string, CancellationToken, Task>? handler = Changed;
            if (handler == null || String.IsNullOrEmpty(targetId)) return;
            try
            {
                await handler(targetType, targetId, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Services.Logger.Warn("[DirectoryModule] [p:" + _Services.PropertyId + "] change handler failed for " + targetType + " " + targetId + ": " + e.Message);
            }
        }

        /// <summary>
        /// Account deletion (docs/PHASE4_CONTRACT.md section 10): pending submissions withdrawn, submissions anonymized,
        /// instructor links removed (the entries stay).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            await Submissions.EraseForAccountAsync(userId, token).ConfigureAwait(false);
            await Contributions.EraseForAccountAsync(userId, token).ConfigureAwait(false);
            List<string> unlinked = await _Services.Database.DirectoryListings.Instructors.UnlinkUserAsync(userId, DateTime.UtcNow, token).ConfigureAwait(false);
            foreach (string id in unlinked) await ChangedAsync(TargetTypeEnum.Instructor, id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Release the geocoder and tile proxy clients.
        /// </summary>
        public void Dispose()
        {
            Geocoding.Dispose();
            Tiles.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
