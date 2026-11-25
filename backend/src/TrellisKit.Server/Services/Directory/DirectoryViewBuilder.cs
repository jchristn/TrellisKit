namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Builds venue and instructor payloads (docs/PHASE4_CONTRACT.md sections 1.3, 2.1 to 2.3): address visibility
    /// (staff see exact values), coordinate rounding, locked stubs, program labels, rank labels, photos, bounded
    /// previews, viewer capabilities, and the public route omissions. Thread safe.
    /// </summary>
    public class DirectoryViewBuilder
    {
        #region Public-Members

        /// <summary>
        /// Most instructors (or venues) in a detail preview.
        /// </summary>
        public const int PreviewSize = 10;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public DirectoryViewBuilder(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Venue list items (locked stubs where the caller may not open them).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venues">Venues.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in input order (hidden venues are left out).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<VenueSummaryView>> VenueSummariesAsync(ContentActor actor, IReadOnlyList<Venue> venues, DirectoryViewOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(venues);
            ArgumentNullException.ThrowIfNull(options);
            List<VenueSummaryView> result = new List<VenueSummaryView>();
            if (venues.Count == 0) return result;
            DirectoryConfig config = await _Services.Directory.Config.CurrentAsync(token).ConfigureAwait(false);
            List<DirectoryProgram> programs = await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false);
            bool reviews = await _Services.Directory.Contributions.ReviewsEnabledAsync(token).ConfigureAwait(false);
            bool staffMeta = actor.IsDirectoryStaff && !options.PublicRoute;
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Venue, venues.Select(v => v.Id), staffMeta, token).ConfigureAwait(false);
            List<string> openIds = venues.Where(v => Level(actor, v, options) == 2).Select(v => v.Id).ToList();
            Dictionary<string, List<MediaView>> photos = openIds.Count == 0 ? new Dictionary<string, List<MediaView>>() :
                await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Venue, openIds, options.PublicRoute || actor.IsAnonymous, staffMeta, token).ConfigureAwait(false);
            foreach (Venue venue in venues)
            {
                int level = Level(actor, venue, options);
                if (level == 0) continue;
                VenueSummaryView view = new VenueSummaryView();
                Fill(view, venue, actor, config, programs, metadata, level == 1, options, reviews);
                if (level == 2 && photos.TryGetValue(venue.Id, out List<MediaView>? list) && list.Count > 0) view.CoverPhoto = list[0];
                result.Add(view);
            }
            return result;
        }

        /// <summary>
        /// A venue detail (an AdminVenueView when options.Admin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venue">Venue.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<VenueView> VenueAsync(ContentActor actor, Venue venue, DirectoryViewOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(venue);
            ArgumentNullException.ThrowIfNull(options);
            DirectoryConfig config = await _Services.Directory.Config.CurrentAsync(token).ConfigureAwait(false);
            List<DirectoryProgram> programs = await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false);
            bool reviews = await _Services.Directory.Contributions.ReviewsEnabledAsync(token).ConfigureAwait(false);
            bool staffMeta = actor.IsDirectoryStaff && !options.PublicRoute;
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Venue, new[] { venue.Id }, staffMeta, token).ConfigureAwait(false);
            int level = Math.Max(1, Level(actor, venue, options));
            VenueView view = options.Admin ? new AdminVenueView() : new VenueView();
            Fill(view, venue, actor, config, programs, metadata, level == 1, options, reviews);
            if (level == 1) return view;
            view.Description = venue.Description;
            view.ScheduleText = venue.ScheduleText;
            view.Schedule = venue.Schedule;
            view.SocialLinks = venue.SocialLinks.OrderBy(l => l.Position).Select(l => new SocialLinkView { Kind = l.Kind, Url = l.Url }).ToList();
            Dictionary<string, List<MediaView>> photos = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Venue, new[] { venue.Id }, options.PublicRoute || actor.IsAnonymous, staffMeta, token).ConfigureAwait(false);
            if (photos.TryGetValue(venue.Id, out List<MediaView>? list))
            {
                view.Photos = list.Take(Math.Max(0, _Services.Settings.Directory.MaxPhotos)).ToList();
                view.CoverPhoto = view.Photos.FirstOrDefault();
            }
            view.Instructors = await VenueInstructorsPreviewAsync(actor, venue, options, token).ConfigureAwait(false);
            if (!options.PublicRoute) view.Reviews = await _Services.Directory.Contributions.SummaryAsync(actor, venue, view.Viewer, token).ConfigureAwait(false);
            if (view is AdminVenueView admin)
            {
                Dictionary<string, int> pending = await _Services.Database.DirectoryListings.Submissions.PendingCountsAsync(new[] { venue.Id }, token).ConfigureAwait(false);
                FillAdmin(admin, venue, pending);
            }
            return view;
        }

        /// <summary>
        /// Admin venue list items.
        /// </summary>
        /// <param name="actor">Caller (directory staff).</param>
        /// <param name="venues">Venues.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<AdminVenueView>> AdminVenuesAsync(ContentActor actor, IReadOnlyList<Venue> venues, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(venues);
            List<AdminVenueView> result = new List<AdminVenueView>();
            if (venues.Count == 0) return result;
            DirectoryViewOptions options = new DirectoryViewOptions { Admin = true };
            DirectoryConfig config = await _Services.Directory.Config.CurrentAsync(token).ConfigureAwait(false);
            List<DirectoryProgram> programs = await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false);
            bool reviews = await _Services.Directory.Contributions.ReviewsEnabledAsync(token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Venue, venues.Select(v => v.Id), true, token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> photos = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Venue, venues.Select(v => v.Id), false, true, token).ConfigureAwait(false);
            Dictionary<string, int> pending = await _Services.Database.DirectoryListings.Submissions.PendingCountsAsync(venues.Select(v => v.Id), token).ConfigureAwait(false);
            foreach (Venue venue in venues)
            {
                AdminVenueView view = new AdminVenueView();
                Fill(view, venue, actor, config, programs, metadata, false, options, reviews);
                view.Description = venue.Description;
                view.ScheduleText = venue.ScheduleText;
                view.Schedule = venue.Schedule;
                view.SocialLinks = venue.SocialLinks.OrderBy(l => l.Position).Select(l => new SocialLinkView { Kind = l.Kind, Url = l.Url }).ToList();
                if (photos.TryGetValue(venue.Id, out List<MediaView>? list))
                {
                    view.Photos = list;
                    view.CoverPhoto = list.FirstOrDefault();
                }
                FillAdmin(view, venue, pending);
                result.Add(view);
            }
            return result;
        }

        /// <summary>
        /// Instructor list items.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="instructors">Instructors.</param>
        /// <param name="options">Options (RoleVenueId adds each role).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in input order (hidden ones are left out).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<InstructorSummaryView>> InstructorSummariesAsync(ContentActor actor, IReadOnlyList<Instructor> instructors, DirectoryViewOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(instructors);
            ArgumentNullException.ThrowIfNull(options);
            List<InstructorSummaryView> result = new List<InstructorSummaryView>();
            if (instructors.Count == 0) return result;
            InstructorContext context = await ContextAsync(actor, instructors, options, token).ConfigureAwait(false);
            foreach (Instructor instructor in instructors)
            {
                int level = Level(actor, instructor, options);
                if (level == 0) continue;
                InstructorSummaryView view = options.Admin ? new AdminInstructorView() : new InstructorSummaryView();
                Fill(view, instructor, actor, context, level == 1, options);
                if (view is AdminInstructorView admin) FillAdmin(admin, instructor, context.Pending);
                result.Add(view);
            }
            return result;
        }

        /// <summary>
        /// An instructor detail (an AdminInstructorView when options.Admin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="instructor">Instructor.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<InstructorView> InstructorAsync(ContentActor actor, Instructor instructor, DirectoryViewOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(instructor);
            ArgumentNullException.ThrowIfNull(options);
            InstructorContext context = await ContextAsync(actor, new[] { instructor }, options, token).ConfigureAwait(false);
            int level = Math.Max(1, Level(actor, instructor, options));
            InstructorView view = options.Admin ? new AdminInstructorView() : new InstructorView();
            Fill(view, instructor, actor, context, level == 1, options);
            if (level == 1) return view;
            view.Bio = instructor.Bio;
            view.SocialLinks = instructor.SocialLinks.OrderBy(l => l.Position).Select(l => new SocialLinkView { Kind = l.Kind, Url = l.Url }).ToList();
            view.Venues = await InstructorVenuesPreviewAsync(actor, instructor, options, token).ConfigureAwait(false);
            if (actor.IsDirectoryStaff && !options.PublicRoute && view.LinkedUser != null && instructor.RankKey != null && context.RankField != null)
                view.RankDiffersFromProfile = !String.Equals(view.LinkedUser.Rank, instructor.RankKey, StringComparison.OrdinalIgnoreCase) || view.LinkedUser.RankLevel != (instructor.RankLevel ?? 0);
            if (view is AdminInstructorView admin) FillAdmin(admin, instructor, context.Pending);
            return view;
        }

        /// <summary>
        /// How a caller may see a venue: 2 full, 1 stub, 0 hidden (public routes: full only for Public, Active venues).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venue">Venue.</param>
        /// <param name="options">Options.</param>
        /// <returns>Level.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static int Level(ContentActor actor, Venue venue, DirectoryViewOptions options)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(venue);
            ArgumentNullException.ThrowIfNull(options);
            if (options.PublicRoute) return venue.Audience == ContentAudienceEnum.Public && venue.Status == VenueStatusEnum.Active && DirectoryAccess.IsLive(venue) ? 2 : 0;
            if (actor.IsDirectoryStaff) return 2;
            if (!DirectoryAccess.IsLive(venue)) return 0;
            return DirectoryAccess.ReadLevel(actor, venue.Audience);
        }

        /// <summary>
        /// How a caller may see an instructor.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="instructor">Instructor.</param>
        /// <param name="options">Options.</param>
        /// <returns>Level.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static int Level(ContentActor actor, Instructor instructor, DirectoryViewOptions options)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(instructor);
            ArgumentNullException.ThrowIfNull(options);
            if (options.PublicRoute) return instructor.Audience == ContentAudienceEnum.Public && DirectoryAccess.IsLive(instructor) ? 2 : 0;
            if (actor.IsDirectoryStaff) return 2;
            if (!DirectoryAccess.IsLive(instructor)) return 0;
            return DirectoryAccess.ReadLevel(actor, instructor.Audience);
        }

        /// <summary>
        /// The address a caller sees.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <param name="exact">Whether the full address is shown.</param>
        /// <returns>Address.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        public static AddressView Address(Venue venue, bool exact)
        {
            ArgumentNullException.ThrowIfNull(venue);
            AddressView view = new AddressView
            {
                Line1 = exact ? venue.Line1 : null,
                Line2 = exact ? venue.Line2 : null,
                Locality = venue.Locality,
                Region = venue.Region,
                PostalCode = exact ? venue.PostalCode : null,
                CountryCode = venue.CountryCode
            };
            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(view.Line1)) parts.Add(view.Line1!.Trim());
            if (!String.IsNullOrWhiteSpace(view.Line2)) parts.Add(view.Line2!.Trim());
            if (!String.IsNullOrWhiteSpace(view.Locality)) parts.Add(view.Locality!.Trim());
            string tail = String.Join(" ", new[] { view.Region, view.PostalCode }.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
            if (tail.Length > 0) parts.Add(tail);
            if (parts.Count == 0 && !String.IsNullOrWhiteSpace(view.CountryCode)) parts.Add(view.CountryCode!.Trim());
            view.Formatted = parts.Count == 0 ? null : String.Join(", ", parts);
            return view;
        }

        /// <summary>
        /// The position a caller sees (5 decimals Exact, 2 Approximate, none LocalityOnly; staff see the stored value
        /// with its source).
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <param name="staff">Staff view.</param>
        /// <returns>Position.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        public static GeoView Geo(Venue venue, bool staff)
        {
            ArgumentNullException.ThrowIfNull(venue);
            if (venue.Lat == null || venue.Lng == null) return new GeoView { Precision = GeoPrecisionEnum.None, Source = staff ? venue.GeoSource : null, GeocodedUtc = staff ? venue.GeocodedUtc : null };
            if (staff) return new GeoView { Lat = GeoMath.Round(venue.Lat.Value, 6), Lng = GeoMath.Round(venue.Lng.Value, 6), Precision = GeoPrecisionEnum.Exact, Source = venue.GeoSource, GeocodedUtc = venue.GeocodedUtc };
            switch (venue.AddressVisibility)
            {
                case AddressVisibilityEnum.Exact:
                    return new GeoView { Lat = GeoMath.Round(venue.Lat.Value, 5), Lng = GeoMath.Round(venue.Lng.Value, 5), Precision = GeoPrecisionEnum.Exact };
                case AddressVisibilityEnum.Approximate:
                    return new GeoView { Lat = GeoMath.Round(venue.Lat.Value, 2), Lng = GeoMath.Round(venue.Lng.Value, 2), Precision = GeoPrecisionEnum.Approximate };
                default:
                    return new GeoView { Precision = GeoPrecisionEnum.None };
            }
        }

        /// <summary>
        /// The distance a caller sees (one decimal; whole kilometers for LocalityOnly venues), or null.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <param name="staff">Staff view.</param>
        /// <returns>Distance, or null.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        public static double? Distance(Venue venue, bool staff)
        {
            ArgumentNullException.ThrowIfNull(venue);
            if (venue.DistanceKm == null) return null;
            if (!staff && venue.AddressVisibility == AddressVisibilityEnum.LocalityOnly) return Math.Round(venue.DistanceKm.Value, 0, MidpointRounding.AwayFromZero);
            return Math.Round(venue.DistanceKm.Value, 1, MidpointRounding.AwayFromZero);
        }

        #endregion

        #region Private-Methods

        private void Fill(VenueSummaryView view, Venue venue, ContentActor actor, DirectoryConfig config, List<DirectoryProgram> programs, Dictionary<string, ObjectMetadata> metadata, bool stub, DirectoryViewOptions options, bool reviews)
        {
            bool staff = actor.IsDirectoryStaff && !options.PublicRoute;
            bool exact = staff || venue.AddressVisibility == AddressVisibilityEnum.Exact;
            metadata.TryGetValue(venue.Id, out ObjectMetadata? m);
            view.Id = venue.Id;
            view.Name = venue.Name;
            view.Slug = venue.Slug;
            view.PublicUrl = _Services.PublicPages?.VenueUrl(venue);
            view.AddressVisibility = venue.AddressVisibility;
            view.Programs = venue.Programs.Select(k => DirectoryConfigService.Option(programs, k)).ToList();
            view.Status = venue.Status;
            view.Audience = venue.Audience;
            view.Labels = m?.Labels ?? new List<string>();
            view.Tags = m?.Tags ?? new Dictionary<string, string>();
            view.CreatedUtc = venue.CreatedUtc;
            view.UpdatedUtc = venue.UpdatedUtc;
            if (stub)
            {
                view.Locked = true;
                view.Address = Address(venue, false);
                view.Address.Formatted = String.Join(", ", new[] { venue.Locality, venue.Region }.Where(p => !String.IsNullOrWhiteSpace(p)));
                view.Geo = new GeoView { Precision = GeoPrecisionEnum.None };
                view.Viewer = new VenueViewer { CanOpen = false, Requires = DirectoryAccess.Requires(actor) };
                return;
            }
            view.Affiliation = venue.Affiliation;
            view.Summary = venue.Summary;
            view.Address = Address(venue, exact);
            view.Geo = Geo(venue, staff);
            view.DistanceKm = Distance(venue, staff);
            view.TimeZone = venue.TimeZone;
            view.Phone = options.PublicRoute && !options.ShowContact ? null : venue.Phone;
            view.Email = options.PublicRoute ? null : venue.Email;
            view.WebsiteUrl = venue.WebsiteUrl;
            view.VisitorPolicy = venue.VisitorPolicy;
            view.VisitorPolicyNote = venue.VisitorPolicyNote;
            view.InstructorCount = venue.InstructorCount;
            view.RatingCount = reviews ? venue.RatingCount : 0;
            view.RatingAverage = reviews ? VenueContributionService.Average(venue) : null;
            view.MediaCount = venue.MediaCount;
            view.IsVerified = venue.IsVerified;
            view.VerifiedUtc = venue.VerifiedUtc;
            view.Viewer = options.PublicRoute ? null : VenueViewer(actor, venue, config);
        }

        private static VenueViewer VenueViewer(ContentActor actor, Venue venue, DirectoryConfig config)
        {
            bool signedIn = !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId);
            bool submit = config.SubmissionsEnabled && DirectoryAccess.MaySubmit(actor);
            return new VenueViewer
            {
                CanEdit = actor.IsDirectoryStaff,
                CanModerate = actor.IsDirectoryStaff,
                CanSuggestEdit = submit && venue.Status != VenueStatusEnum.Archived,
                CanReportClosed = submit && venue.Status == VenueStatusEnum.Active,
                CanReport = signedIn,
                CanOpen = true,
                Requires = null
            };
        }

        private static void FillAdmin(AdminVenueView view, Venue venue, Dictionary<string, int> pending)
        {
            view.OpenReportCount = venue.OpenReportCount;
            view.PendingSubmissionCount = pending.TryGetValue(venue.Id, out int count) ? count : 0;
            view.GeoSource = venue.GeoSource;
            view.GeocodedUtc = venue.GeocodedUtc;
            view.NeedsPin = venue.Lat == null || venue.Lng == null;
            view.ModerationState = venue.ModerationState;
            view.ClosedReason = venue.ClosedReason;
            view.ClosedUtc = venue.ClosedUtc;
        }

        private async Task<InstructorContext> ContextAsync(ContentActor actor, IReadOnlyList<Instructor> instructors, DirectoryViewOptions options, CancellationToken token)
        {
            bool staffMeta = actor.IsDirectoryStaff && !options.PublicRoute;
            InstructorContext context = new InstructorContext
            {
                Config = await _Services.Directory.Config.CurrentAsync(token).ConfigureAwait(false),
                RankField = await _Services.Directory.Config.RankFieldAsync(token).ConfigureAwait(false),
                Metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Instructor, instructors.Select(i => i.Id), staffMeta, token).ConfigureAwait(false),
                Photos = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Instructor, instructors.Select(i => i.Id), options.PublicRoute || actor.IsAnonymous, staffMeta, token).ConfigureAwait(false),
                Users = options.PublicRoute ? new Dictionary<string, UserSummary>() : await _Services.Summaries.BuildAsync(instructors.Select(i => i.LinkedUserId), token).ConfigureAwait(false),
                Roles = options.RoleVenueId == null ? new Dictionary<string, VenueInstructorRoleEnum>() :
                    await _Services.Database.DirectoryListings.Venues.RolesAsync(options.RoleVenueId, instructors.Select(i => i.Id), token).ConfigureAwait(false),
                Pending = options.Admin ? await _Services.Database.DirectoryListings.Submissions.PendingCountsAsync(instructors.Select(i => i.Id), token).ConfigureAwait(false) : new Dictionary<string, int>()
            };
            if (actor.UserId != null && !actor.IsAnonymous)
                context.CallerLinked = await _Services.Database.DirectoryListings.Instructors.ReadByLinkedUserAsync(actor.UserId, token).ConfigureAwait(false) != null;
            return context;
        }

        private void Fill(InstructorSummaryView view, Instructor instructor, ContentActor actor, InstructorContext context, bool stub, DirectoryViewOptions options)
        {
            context.Metadata.TryGetValue(instructor.Id, out ObjectMetadata? m);
            view.Id = instructor.Id;
            view.DisplayName = instructor.DisplayName;
            view.Slug = instructor.Slug;
            view.Status = instructor.Status;
            view.Audience = instructor.Audience;
            view.Labels = m?.Labels ?? new List<string>();
            view.Tags = m?.Tags ?? new Dictionary<string, string>();
            view.CreatedUtc = instructor.CreatedUtc;
            view.UpdatedUtc = instructor.UpdatedUtc;
            if (context.Roles.TryGetValue(instructor.Id, out VenueInstructorRoleEnum role)) view.Role = role;
            if (stub)
            {
                view.Locked = true;
                view.Viewer = new InstructorViewer { CanOpen = false, Requires = DirectoryAccess.Requires(actor) };
                return;
            }
            if (context.Photos.TryGetValue(instructor.Id, out List<MediaView>? photos)) view.Photo = photos.FirstOrDefault();
            if (context.RankField != null && instructor.RankKey != null)
            {
                ProfileFieldOption? option = context.RankField.Options.FirstOrDefault(o => String.Equals(o.Key, instructor.RankKey, StringComparison.OrdinalIgnoreCase));
                view.Rank = instructor.RankKey;
                view.RankLabel = option?.Label ?? instructor.RankKey;
                view.RankColor = option?.Color;
                view.RankLevel = instructor.RankLevel;
            }
            view.Affiliation = instructor.Affiliation;
            view.WebsiteUrl = instructor.WebsiteUrl;
            view.VenueCount = instructor.VenueCount;
            if (!options.PublicRoute && instructor.LinkedUserId != null && context.Users.TryGetValue(instructor.LinkedUserId, out UserSummary? user)) view.LinkedUser = user;
            if (options.PublicRoute)
            {
                view.Viewer = null;
                return;
            }
            bool signedIn = !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId);
            bool submit = context.Config.SubmissionsEnabled && DirectoryAccess.MaySubmit(actor);
            view.Viewer = new InstructorViewer
            {
                CanEdit = actor.IsDirectoryStaff,
                CanModerate = actor.IsDirectoryStaff,
                CanSuggestEdit = submit,
                CanClaim = context.Config.ClaimsEnabled && DirectoryAccess.MaySubmit(actor) && instructor.LinkedUserId == null && !context.CallerLinked,
                CanReport = signedIn,
                CanOpen = true,
                Requires = null
            };
        }

        private static void FillAdmin(AdminInstructorView view, Instructor instructor, Dictionary<string, int> pending)
        {
            view.OpenReportCount = instructor.OpenReportCount;
            view.PendingSubmissionCount = pending.TryGetValue(instructor.Id, out int count) ? count : 0;
            view.ModerationState = instructor.ModerationState;
            view.LinkedUtc = instructor.LinkedUtc;
        }

        private async Task<BoundedPreview<InstructorSummaryView>> VenueInstructorsPreviewAsync(ContentActor actor, Venue venue, DirectoryViewOptions options, CancellationToken token)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = PreviewSize }, DirectoryCollections.VenueInstructors, actor.IsDirectoryStaff, venue.Id);
            InstructorFilter filter = new InstructorFilter
            {
                VenueId = venue.Id,
                Audiences = options.PublicRoute ? new List<ContentAudienceEnum> { ContentAudienceEnum.Public } : DirectoryAccess.VisibleAudiences(actor),
                LiveOnly = !actor.IsDirectoryStaff || options.PublicRoute
            };
            EnumerationResult<Instructor> page = await _Services.Database.DirectoryListings.Instructors.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            DirectoryViewOptions child = new DirectoryViewOptions { PublicRoute = options.PublicRoute, ShowContact = options.ShowContact, RoleVenueId = venue.Id };
            List<InstructorSummaryView> views = await InstructorSummariesAsync(actor, page.Objects, child, token).ConfigureAwait(false);
            return new BoundedPreview<InstructorSummaryView>
            {
                Objects = views,
                TotalRecords = page.TotalRecords ?? views.Count,
                Enumerate = (options.PublicRoute ? Constants.ApiPrefix + "/public/venues/" : Constants.ApiPrefix + "/venues/") + venue.Id + "/instructors/enumerate"
            };
        }

        private async Task<BoundedPreview<VenueSummaryView>> InstructorVenuesPreviewAsync(ContentActor actor, Instructor instructor, DirectoryViewOptions options, CancellationToken token)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = PreviewSize }, DirectoryCollections.InstructorVenues, actor.IsDirectoryStaff, instructor.Id);
            VenueFilter filter = new VenueFilter
            {
                InstructorId = instructor.Id,
                Audiences = options.PublicRoute ? new List<ContentAudienceEnum> { ContentAudienceEnum.Public } : DirectoryAccess.VisibleAudiences(actor),
                Staff = actor.IsDirectoryStaff && !options.PublicRoute,
                LiveOnly = !actor.IsDirectoryStaff || options.PublicRoute,
                IncludeClosed = true
            };
            EnumerationResult<Venue> page = await _Services.Database.DirectoryListings.Venues.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<VenueSummaryView> views = await VenueSummariesAsync(actor, page.Objects, new DirectoryViewOptions { PublicRoute = options.PublicRoute, ShowContact = options.ShowContact }, token).ConfigureAwait(false);
            return new BoundedPreview<VenueSummaryView> { Objects = views, TotalRecords = page.TotalRecords ?? views.Count, Enumerate = Constants.ApiPrefix + "/instructors/" + instructor.Id + "/venues/enumerate" };
        }

        #endregion
    }
}
