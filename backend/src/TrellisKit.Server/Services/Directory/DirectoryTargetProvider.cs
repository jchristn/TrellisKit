namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The content target provider of venues and instructors (docs/PHASE4_CONTRACT.md sections 3.10 and 7): scope
    /// Directory moderated by directory staff, no comments or reactions, reports only queue (no auto-hide: a competitor
    /// could hide a listing), Hide/Unhide/Remove/Restore through the moderation state, and search documents over name,
    /// affiliation, summary, locality, region, and linked instructor names. Thread safe.
    /// </summary>
    public class DirectoryTargetProvider : ContentTargetProvider
    {
        #region Public-Members

        /// <summary>
        /// Report and moderation scope of directory entries.
        /// </summary>
        public const string Scope = "Directory";

        /// <inheritdoc />
        public override IReadOnlyList<TargetTypeEnum> TargetTypes
        {
            get { return _Types; }
        }

        #endregion

        #region Private-Members

        private static readonly List<TargetTypeEnum> _Types = new List<TargetTypeEnum> { TargetTypeEnum.Venue, TargetTypeEnum.Instructor, TargetTypeEnum.VenueReview, TargetTypeEnum.VenueMedia };
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public DirectoryTargetProvider(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string ScopeType(TargetTypeEnum targetType)
        {
            return Scope;
        }

        /// <inheritdoc />
        public override bool Moderates(ContentActor actor, string scopeType)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return String.Equals(scopeType, Scope, StringComparison.OrdinalIgnoreCase) && actor.IsDirectoryStaff;
        }

        /// <inheritdoc />
        public override string PathOf(TargetTypeEnum targetType, string targetId)
        {
            if (targetType == TargetTypeEnum.VenueReview || targetType == TargetTypeEnum.VenueMedia) return "/directory";
            return (targetType == TargetTypeEnum.Instructor ? "/instructors/" : "/venues/") + targetId;
        }

        /// <inheritdoc />
        public override async Task<bool> IsVisibleAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (targetType == TargetTypeEnum.Venue)
            {
                Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(targetId, token).ConfigureAwait(false);
                return venue != null && DirectoryViewBuilder.Level(actor, venue, new DirectoryViewOptions()) == 2;
            }
            if (targetType == TargetTypeEnum.Instructor)
            {
                Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(targetId, token).ConfigureAwait(false);
                return instructor != null && DirectoryViewBuilder.Level(actor, instructor, new DirectoryViewOptions()) == 2;
            }
            if (targetType == TargetTypeEnum.VenueReview) return await _Services.Directory.Contributions.ReviewVisibleAsync(actor, targetId, token).ConfigureAwait(false);
            if (targetType == TargetTypeEnum.VenueMedia) return await _Services.Directory.Contributions.MediaVisibleAsync(actor, targetId, token).ConfigureAwait(false);
            return false;
        }

        /// <inheritdoc />
        public override async Task<ModerationTarget?> LoadModerationTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType == TargetTypeEnum.Venue)
            {
                Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(targetId, token).ConfigureAwait(false);
                return venue == null ? null : new ModerationTarget { TargetType = TargetTypeEnum.Venue, TargetId = venue.Id, AuthorUserId = venue.CreatedByUserId ?? String.Empty, State = venue.ModerationState, Deleted = venue.DeletedUtc != null };
            }
            if (targetType == TargetTypeEnum.Instructor)
            {
                Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(targetId, token).ConfigureAwait(false);
                return instructor == null ? null : new ModerationTarget { TargetType = TargetTypeEnum.Instructor, TargetId = instructor.Id, AuthorUserId = instructor.LinkedUserId ?? instructor.CreatedByUserId ?? String.Empty, State = instructor.ModerationState, Deleted = instructor.DeletedUtc != null };
            }
            if (targetType == TargetTypeEnum.VenueReview)
            {
                VenueReview? review = await _Services.Database.DirectoryListings.Reviews.ReadAsync(targetId, token).ConfigureAwait(false);
                if (review == null) return null;
                Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(review.VenueId, token).ConfigureAwait(false);
                string excerpt = review.Rating + "/5: " + MarkdownNormalizer.Excerpt(review.Body) + (String.IsNullOrEmpty(review.ReplyBody) ? String.Empty : " (reply: " + MarkdownNormalizer.Excerpt(review.ReplyBody) + ")");
                return new ModerationTarget
                {
                    TargetType = TargetTypeEnum.VenueReview,
                    TargetId = review.Id,
                    AuthorUserId = review.AuthorUserId,
                    State = review.ModerationState,
                    Deleted = review.DeletedUtc != null,
                    Title = venue?.Name,
                    Excerpt = excerpt,
                    ExtensionPath = VenueContributionService.ReviewPath(review.VenueId, review.Id),
                    ExtensionExcerpt = MarkdownNormalizer.Excerpt(review.Body)
                };
            }
            if (targetType == TargetTypeEnum.VenueMedia)
            {
                VenueMediaItem? item = await _Services.Database.DirectoryListings.Media.ReadAsync(targetId, token).ConfigureAwait(false);
                if (item == null) return null;
                Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(item.VenueId, token).ConfigureAwait(false);
                return new ModerationTarget
                {
                    TargetType = TargetTypeEnum.VenueMedia,
                    TargetId = item.Id,
                    AuthorUserId = item.UploaderUserId,
                    State = item.ModerationState,
                    Deleted = item.DeletedUtc != null,
                    Title = venue?.Name,
                    Excerpt = item.Kind == MediaKindEnum.Video ? "Video" : "Photo",
                    HasMedia = true,
                    ExtensionPath = "/venues/" + item.VenueId
                };
            }
            return null;
        }

        /// <inheritdoc />
        public override async Task SetModerationStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            DateTime now = DateTime.UtcNow;
            if (target.TargetType == TargetTypeEnum.Venue) await _Services.Database.DirectoryListings.Venues.SetModerationAsync(target.TargetId, state, moderatorUserId, reason, now, token).ConfigureAwait(false);
            else if (target.TargetType == TargetTypeEnum.Instructor) await _Services.Database.DirectoryListings.Instructors.SetModerationAsync(target.TargetId, state, moderatorUserId, reason, now, token).ConfigureAwait(false);
            else if (target.TargetType == TargetTypeEnum.VenueReview)
            {
                await _Services.Database.DirectoryListings.Reviews.SetModerationAsync(target.TargetId, state, moderatorUserId, reason, now, token).ConfigureAwait(false);
                return;
            }
            else if (target.TargetType == TargetTypeEnum.VenueMedia)
            {
                await _Services.Database.DirectoryListings.Media.SetModerationAsync(target.TargetId, state, moderatorUserId, reason, now, token).ConfigureAwait(false);
                return;
            }
            await _Services.Directory.ChangedAsync(target.TargetType, target.TargetId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            long open = await _Services.Database.Reports.CountOpenOnTargetAsync(target.TargetType, target.TargetId, null, token).ConfigureAwait(false);
            if (target.TargetType == TargetTypeEnum.Venue) await _Services.Database.DirectoryListings.Venues.SetOpenReportCountAsync(target.TargetId, (int)Math.Min(Int32.MaxValue, open), token).ConfigureAwait(false);
            else if (target.TargetType == TargetTypeEnum.Instructor) await _Services.Database.DirectoryListings.Instructors.SetOpenReportCountAsync(target.TargetId, (int)Math.Min(Int32.MaxValue, open), token).ConfigureAwait(false);
            else if (target.TargetType == TargetTypeEnum.VenueReview) await _Services.Database.DirectoryListings.Reviews.SetOpenReportCountAsync(target.TargetId, (int)Math.Min(Int32.MaxValue, open), token).ConfigureAwait(false);
            else if (target.TargetType == TargetTypeEnum.VenueMedia) await _Services.Database.DirectoryListings.Media.SetOpenReportCountAsync(target.TargetId, (int)Math.Min(Int32.MaxValue, open), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override bool AllowsAutoHide(TargetTypeEnum targetType)
        {
            return false;
        }

        /// <inheritdoc />
        public override async Task<object> ModerationViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(target);
            if (target.TargetType == TargetTypeEnum.Venue)
            {
                Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(target.TargetId, token).ConfigureAwait(false);
                if (venue != null) return await _Services.Directory.Views.VenueAsync(actor, venue, new DirectoryViewOptions { Admin = actor.IsDirectoryStaff }, token).ConfigureAwait(false);
            }
            else if (target.TargetType == TargetTypeEnum.Instructor)
            {
                Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(target.TargetId, token).ConfigureAwait(false);
                if (instructor != null) return await _Services.Directory.Views.InstructorAsync(actor, instructor, new DirectoryViewOptions { Admin = actor.IsDirectoryStaff }, token).ConfigureAwait(false);
            }
            else if (target.TargetType == TargetTypeEnum.VenueReview)
            {
                VenueReview? review = await _Services.Database.DirectoryListings.Reviews.ReadAsync(target.TargetId, token).ConfigureAwait(false);
                if (review != null) return await _Services.Directory.Contributions.ReviewViewAsync(actor, review, token).ConfigureAwait(false);
            }
            else if (target.TargetType == TargetTypeEnum.VenueMedia)
            {
                VenueMediaItem? item = await _Services.Database.DirectoryListings.Media.ReadAsync(target.TargetId, token).ConfigureAwait(false);
                if (item != null) return await _Services.Directory.Contributions.MediaViewAsync(actor, item, token).ConfigureAwait(false);
            }
            return await base.ModerationViewAsync(actor, target, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<SearchDocument?> SearchDocumentAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType == TargetTypeEnum.Venue)
            {
                Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(targetId, token).ConfigureAwait(false);
                if (venue == null || venue.Status == VenueStatusEnum.Archived || venue.ModerationState != ModerationStateEnum.None) return null;
                List<Instructor> instructors = await InstructorsOfAsync(venue.Id, token).ConfigureAwait(false);
                ObjectMetadata metadata = await _Services.Metadata.LoadOneAsync(TargetTypeEnum.Venue, venue.Id, false, token).ConfigureAwait(false);
                List<string> parts = new List<string> { venue.Name, venue.Affiliation ?? String.Empty, venue.Summary ?? String.Empty, venue.Locality ?? String.Empty, venue.Region ?? String.Empty };
                parts.AddRange(instructors.Select(i => i.DisplayName));
                return new SearchDocument { Id = "Venue:" + venue.Id, Content = String.Join(" ", parts.Where(p => !String.IsNullOrWhiteSpace(p))), Labels = metadata.Labels, Tags = metadata.Tags };
            }
            if (targetType == TargetTypeEnum.Instructor)
            {
                Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(targetId, token).ConfigureAwait(false);
                if (instructor == null || instructor.Status != InstructorStatusEnum.Active || instructor.ModerationState != ModerationStateEnum.None) return null;
                ObjectMetadata metadata = await _Services.Metadata.LoadOneAsync(TargetTypeEnum.Instructor, instructor.Id, false, token).ConfigureAwait(false);
                return new SearchDocument { Id = "Instructor:" + instructor.Id, Content = String.Join(" ", new[] { instructor.DisplayName, instructor.Affiliation ?? String.Empty }.Where(p => p.Length > 0)), Labels = metadata.Labels, Tags = metadata.Tags };
            }
            return null;
        }

        /// <inheritdoc />
        public override async Task<List<string>> ChangedIdsAsync(TargetTypeEnum targetType, DateTime? sinceUtc, int limit, CancellationToken token = default)
        {
            List<string> ids = new List<string>();
            int bound = Math.Clamp(limit, 1, 1000);
            if (targetType == TargetTypeEnum.Venue)
            {
                foreach (Venue v in await _Services.Database.DirectoryListings.Venues.ListPageAsync(null, bound, token).ConfigureAwait(false))
                    if (sinceUtc == null || v.UpdatedUtc >= sinceUtc) ids.Add(v.Id);
            }
            else if (targetType == TargetTypeEnum.Instructor)
            {
                foreach (Instructor i in await _Services.Database.DirectoryListings.Instructors.ListPageAsync(null, bound, token).ConfigureAwait(false))
                    if (sinceUtc == null || i.UpdatedUtc >= sinceUtc) ids.Add(i.Id);
            }
            return ids;
        }

        /// <inheritdoc />
        public override bool AcceptsReportReason(TargetTypeEnum targetType, ReportReasonEnum reason)
        {
            if (reason == ReportReasonEnum.Filter) return false;
            if (reason == ReportReasonEnum.PermanentlyClosed) return targetType == TargetTypeEnum.Venue;
            return true;
        }

        /// <inheritdoc />
        public override async Task ReportedAsync(ModerationTarget target, Report report, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(report);
            await RecomputeReportsAsync(target, token).ConfigureAwait(false);
            if (target.TargetType != TargetTypeEnum.Venue || report.Reason != ReportReasonEnum.PermanentlyClosed || String.IsNullOrEmpty(report.ReporterUserId)) return;
            ContentActor reporter;
            try
            {
                reporter = await _Services.Actors.ForUserAsync(report.ReporterUserId, token).ConfigureAwait(false);
            }
            catch (TrellisKit.Core.Exceptions.ApiErrorException)
            {
                return;
            }
            await _Services.Directory.Submissions.FromClosedReportAsync(reporter, target.TargetId, report.Details, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<SearchResultItem?> SearchResultAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, IReadOnlyList<string> terms, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(terms);
            if (targetType == TargetTypeEnum.Venue)
            {
                Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(targetId, token).ConfigureAwait(false);
                if (venue == null) return null;
                string text = String.Join(" ", new[] { venue.Summary, venue.Affiliation, venue.Locality, venue.Region }.Where(p => !String.IsNullOrWhiteSpace(p)));
                return new SearchResultItem { Type = targetType.ToString(), Id = venue.Id, Title = venue.Name, Path = PathOf(targetType, venue.Id), CreatedUtc = venue.CreatedUtc, Snippet = SearchService.Snippet(text, terms, "summary", null) ?? new SearchSnippetView() };
            }
            if (targetType == TargetTypeEnum.Instructor)
            {
                Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(targetId, token).ConfigureAwait(false);
                if (instructor == null) return null;
                return new SearchResultItem { Type = targetType.ToString(), Id = instructor.Id, Title = instructor.DisplayName, Path = PathOf(targetType, instructor.Id), CreatedUtc = instructor.CreatedUtc, Snippet = SearchService.Snippet(instructor.Affiliation, terms, "affiliation", null) ?? new SearchSnippetView() };
            }
            return null;
        }

        /// <inheritdoc />
        public override bool ProvidesKnowledge(TargetTypeEnum targetType)
        {
            return targetType == TargetTypeEnum.Venue;
        }

        /// <inheritdoc />
        public override async Task<List<KnowledgeDocument>> KnowledgeDocumentsAsync(TargetTypeEnum targetType, AssistantKnowledgeSource source, int maxItems, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            List<KnowledgeDocument> result = new List<KnowledgeDocument>();
            if (targetType != TargetTypeEnum.Venue) return result;
            EnumerationQuery query = new EnumerationQuery { MaxResults = 100, Labels = new List<string>(source.Labels), LabelMatch = source.LabelMatch, Tags = new Dictionary<string, string>(source.Tags) };
            int bound = Math.Clamp(maxItems, 1, 1000);
            while (result.Count < bound)
            {
                EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.AdminVenues, true);
                EnumerationResult<Venue> page = await _Services.Database.DirectoryListings.Venues.EnumerateAsync(request, new VenueFilter { Statuses = new List<VenueStatusEnum> { VenueStatusEnum.Active }, LiveOnly = true, Staff = true }, token).ConfigureAwait(false);
                foreach (Venue venue in page.Objects)
                {
                    if (result.Count >= bound) break;
                    result.Add(await KnowledgeOfAsync(venue, token).ConfigureAwait(false));
                }
                if (page.EndOfResults || String.IsNullOrEmpty(page.ContinuationToken)) break;
                query.ContinuationToken = page.ContinuationToken;
            }
            return result;
        }

        /// <inheritdoc />
        public override async Task<List<KnowledgeDocument>> KnowledgeSearchAsync(ContentActor actor, TargetTypeEnum targetType, AssistantKnowledgeSource source, string keyword, int maxResults, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(source);
            List<KnowledgeDocument> result = new List<KnowledgeDocument>();
            if (targetType != TargetTypeEnum.Venue || String.IsNullOrWhiteSpace(keyword)) return result;
            EnumerationQuery query = new EnumerationQuery { MaxResults = Math.Clamp(maxResults, 1, 25), Search = keyword.Trim(), Labels = new List<string>(source.Labels), LabelMatch = source.LabelMatch, Tags = new Dictionary<string, string>(source.Tags) };
            EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.Venues, actor.IsDirectoryStaff);
            EnumerationResult<Venue> page = await _Services.Database.DirectoryListings.Venues.EnumerateAsync(request, new VenueFilter { Audiences = DirectoryAccess.VisibleAudiences(actor), LiveOnly = true }, token).ConfigureAwait(false);
            foreach (Venue venue in page.Objects) result.Add(await KnowledgeOfAsync(venue, token).ConfigureAwait(false));
            return result;
        }

        /// <summary>
        /// The knowledge text of a venue: name, summary, programs, place, schedule, and visitor policy; contact details
        /// (phone, email, website, street address) only for Public venues.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Document.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        public async Task<KnowledgeDocument> KnowledgeOfAsync(Venue venue, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(venue);
            List<DirectoryProgram> programs = await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false);
            DirectoryConfig config = await _Services.Directory.Config.CurrentAsync(token).ConfigureAwait(false);
            bool contact = venue.Audience == ContentAudienceEnum.Public && venue.AddressVisibility == AddressVisibilityEnum.Exact;
            List<string> lines = new List<string> { config.VenueLabel + ": " + venue.Name };
            if (!String.IsNullOrWhiteSpace(venue.Affiliation)) lines.Add("Affiliation: " + venue.Affiliation);
            if (!String.IsNullOrWhiteSpace(venue.Summary)) lines.Add(venue.Summary!);
            string place = DirectoryViewBuilder.Address(venue, contact).Formatted ?? String.Empty;
            if (place.Length > 0) lines.Add("Location: " + place);
            if (venue.Programs.Count > 0) lines.Add("Programs: " + String.Join(", ", venue.Programs.Select(k => DirectoryConfigService.Option(programs, k).Label)));
            if (!String.IsNullOrWhiteSpace(venue.ScheduleText)) lines.Add("Schedule: " + venue.ScheduleText);
            lines.Add("Visitors: " + venue.VisitorPolicy + (String.IsNullOrWhiteSpace(venue.VisitorPolicyNote) ? String.Empty : " (" + venue.VisitorPolicyNote + ")"));
            if (contact)
            {
                if (!String.IsNullOrWhiteSpace(venue.WebsiteUrl)) lines.Add("Website: " + venue.WebsiteUrl);
                if (!String.IsNullOrWhiteSpace(venue.Phone)) lines.Add("Phone: " + venue.Phone);
            }
            lines.Add("Link: /venues/" + venue.Id);
            return new KnowledgeDocument { TargetId = venue.Id, Title = venue.Name, Text = String.Join("\n", lines), Audience = venue.Audience.ToString() };
        }

        #endregion

        #region Private-Methods

        private async Task<List<Instructor>> InstructorsOfAsync(string venueId, CancellationToken token)
        {
            TrellisKit.Core.Enumeration.EnumerationRequest request = _Services.Enumeration.Prepare(new TrellisKit.Core.Requests.EnumerationQuery { MaxResults = 50 }, TrellisKit.Core.Enumeration.DirectoryCollections.VenueInstructors, true, venueId);
            TrellisKit.Core.Responses.EnumerationResult<Instructor> page = await _Services.Database.DirectoryListings.Instructors.EnumerateAsync(request, new InstructorFilter { VenueId = venueId }, token).ConfigureAwait(false);
            return page.Objects;
        }

        #endregion
    }
}
