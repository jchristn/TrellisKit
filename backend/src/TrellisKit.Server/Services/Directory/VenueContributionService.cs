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
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// Member contributions to a venue (docs/PHASE4_CONTRACT.md sections 3.12 and 3.13): star reviews with one public
    /// reply by a linked instructor, and photos and videos members add to the venue's Media section through the
    /// existing media pipeline. Word filter, mutes, blocks, rate limits, the per-property switches, notifications,
    /// audits, and telemetry. Thread safe.
    /// </summary>
    public class VenueContributionService
    {
        #region Public-Members

        /// <summary>
        /// Most media ids in one add request (fixed bound).
        /// </summary>
        public const int MaxMediaPerAdd = 10;

        /// <summary>
        /// Longest removal reason.
        /// </summary>
        public const int MaxReasonLength = 500;

        /// <summary>
        /// Rate limit bucket of review creates.
        /// </summary>
        public const string ReviewsBucket = "directory.reviews.day";

        #endregion

        #region Private-Members

        private const int _ErasePassLimit = 1000;
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public VenueContributionService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether venue reviews are on for the property (directory.reviewsEnabled).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when on.</returns>
        public Task<bool> ReviewsEnabledAsync(CancellationToken token = default)
        {
            return _Services.Directory.Config.FlagAsync(DirectoryPropertySettings.ReviewsEnabled, token);
        }

        /// <summary>
        /// Whether member media is on for the property (directory.memberMediaEnabled).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when on.</returns>
        public Task<bool> MemberMediaEnabledAsync(CancellationToken token = default)
        {
            return _Services.Directory.Config.FlagAsync(DirectoryPropertySettings.MemberMediaEnabled, token);
        }

        /// <summary>
        /// The caller's instructor entry when it is live and linked to the venue, else null.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructor or null.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public async Task<Instructor?> LinkedInstructorAsync(ContentActor actor, string venueId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId) || String.IsNullOrEmpty(venueId)) return null;
            Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadByLinkedUserAsync(actor.UserId, token).ConfigureAwait(false);
            if (instructor == null || instructor.Status != InstructorStatusEnum.Active || !DirectoryAccess.IsLive(instructor)) return null;
            Dictionary<string, VenueInstructorRoleEnum> roles = await _Services.Database.DirectoryListings.Venues.RolesAsync(venueId, new[] { instructor.Id }, token).ConfigureAwait(false);
            return roles.ContainsKey(instructor.Id) ? instructor : null;
        }

        /// <summary>
        /// The review summary and the contribution capabilities of a venue detail for a caller.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venue">Venue (the caller may read it).</param>
        /// <param name="viewer">Venue viewer to complete (canReview, canAddMedia, canManageMedia, canReplyToReviews).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<VenueReviewSummaryView> SummaryAsync(ContentActor actor, Venue venue, VenueViewer? viewer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(venue);
            bool reviews = await ReviewsEnabledAsync(token).ConfigureAwait(false);
            bool media = await MemberMediaEnabledAsync(token).ConfigureAwait(false);
            VenueReviewSummaryView summary = new VenueReviewSummaryView
            {
                Enabled = reviews,
                Enumerate = Constants.ApiPrefix + "/venues/" + venue.Id + "/reviews/enumerate"
            };
            Dictionary<int, int> distribution = reviews ? await _Services.Database.DirectoryListings.Reviews.DistributionAsync(venue.Id, token).ConfigureAwait(false) : new Dictionary<int, int>();
            for (int stars = 1; stars <= 5; stars++) summary.Distribution[stars.ToString(System.Globalization.CultureInfo.InvariantCulture)] = distribution.TryGetValue(stars, out int count) ? count : 0;
            if (reviews)
            {
                summary.Count = venue.RatingCount;
                summary.AverageRating = Average(venue);
            }
            bool signedIn = !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId);
            Instructor? linked = signedIn ? await LinkedInstructorAsync(actor, venue.Id, token).ConfigureAwait(false) : null;
            VenueReview? mine = null;
            if (reviews && signedIn)
            {
                mine = await _Services.Database.DirectoryListings.Reviews.ReadByAuthorAsync(venue.Id, actor.UserId!, token).ConfigureAwait(false);
                if (mine != null && mine.ModerationState == ModerationStateEnum.Removed) mine = null;
                if (mine != null) summary.Mine = (await ReviewViewsAsync(actor, new[] { mine }, linked, token).ConfigureAwait(false)).FirstOrDefault();
            }
            if (viewer != null)
            {
                bool writer = signedIn && DirectoryAccess.MaySubmit(actor);
                bool hasReview = mine != null || (signedIn && reviews && await _Services.Database.DirectoryListings.Reviews.ReadByAuthorAsync(venue.Id, actor.UserId!, token).ConfigureAwait(false) != null);
                bool open = venue.Status != VenueStatusEnum.Archived;
                viewer.CanReview = reviews && writer && open && !hasReview;
                viewer.CanAddMedia = media && writer && open && actor.CanAttachMedia;
                viewer.CanManageMedia = signedIn && (actor.IsDirectoryStaff || linked != null);
                viewer.CanReplyToReviews = reviews && linked != null && actor.CanWrite;
            }
            return summary;
        }

        /// <summary>
        /// The average rating of a venue with one decimal, or null without live reviews.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <returns>Average or null.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        public static double? Average(Venue venue)
        {
            ArgumentNullException.ThrowIfNull(venue);
            if (venue.RatingCount <= 0) return null;
            return Math.Round((double)venue.RatingTotal / venue.RatingCount, 1, MidpointRounding.AwayFromZero);
        }

        // ---------------- Reviews ----------------

        /// <summary>
        /// POST /api/v1/venues/{id}/reviews/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 ReviewsDisabled, 404 NotFound.</exception>
        public async Task<EnumerationResult<VenueReviewView>> ListReviewsAsync(ContentActor actor, string venueId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            await RequireReviewsAsync(token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, MediaService.ReadBucket, _Services.Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            VenueContributionFilter filter = new VenueContributionFilter
            {
                VenueId = venue.Id,
                IncludeModerated = actor.IsDirectoryStaff,
                HiddenAuthorIds = await HiddenAsync(actor, token).ConfigureAwait(false),
                AuthorUserId = q.FilterString("authorUserId"),
                HasReply = q.FilterBool("hasReply", errors)
            };
            foreach (string value in DirectoryFilters.Strings(q, "rating"))
            {
                if (Int32.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int rating) && rating >= 1 && rating <= 5) filter.Ratings.Add(rating);
                else errors["filters.rating"] = "Use whole numbers 1 to 5.";
            }
            DirectoryFilters.ThrowIfAny(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, DirectoryCollections.VenueReviews, actor.IsDirectoryStaff, venue.Id);
            EnumerationResult<VenueReview> page = await _Services.Database.DirectoryListings.Reviews.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            Instructor? linked = await LinkedInstructorAsync(actor, venue.Id, token).ConfigureAwait(false);
            List<VenueReviewView> views = await ReviewViewsAsync(actor, page.Objects, linked, token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/venues/{id}/reviews/{reviewId}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="reviewId">Review.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Review.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 ReviewsDisabled, 404 NotFound.</exception>
        public async Task<VenueReviewView> GetReviewAsync(ContentActor actor, string venueId, string reviewId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            await RequireReviewsAsync(token).ConfigureAwait(false);
            VenueReview review = await RequireReviewAsync(actor, venue, reviewId, token).ConfigureAwait(false);
            return await ReviewViewAsync(actor, review, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/venues/{id}/reviews: one live review per member per venue. Notifies the venue's linked
        /// instructors (VenueReview).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Review.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 403 ReviewsDisabled, Muted, PostingNotAllowed, or Forbidden, 404 NotFound, 409 AlreadyReviewed, 429 RateLimited.</exception>
        public async Task<VenueReviewView> CreateReviewAsync(ContentActor actor, string venueId, VenueReviewRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            await RequireReviewsAsync(token).ConfigureAwait(false);
            string userId = RequireWriter(actor);
            if (venue.Status == VenueStatusEnum.Archived) throw ApiErrorException.NotFound("Venue");
            DateTime now = DateTime.UtcNow;
            bool newAccount = actor.User != null && (now - actor.User.CreatedUtc).TotalHours < _Services.Settings.Comments.NewAccountHours;
            int perDay = newAccount ? _Services.Settings.Directory.NewAccountReviewsPerDay : _Services.Settings.Directory.ReviewsPerDay;
            try
            {
                _Services.Limits.CheckMember(actor, ReviewsBucket, perDay, RateLimitService.Day);
            }
            catch (ApiErrorException)
            {
                Record("create", "limited");
                throw;
            }
            Dictionary<string, string> errors = new Dictionary<string, string>();
            int rating = Rating(request.Rating, true, errors) ?? 0;
            string body = Body(request.Body, true, errors) ?? String.Empty;
            if (errors.Count > 0)
            {
                Record("create", "rejected");
                throw ApiErrorException.Validation(errors);
            }
            if (await _Services.Database.DirectoryListings.Reviews.ReadByAuthorAsync(venue.Id, userId, token).ConfigureAwait(false) != null)
            {
                Record("create", "conflict");
                throw AlreadyReviewed();
            }
            WordFilterResult filter = await CheckTextAsync(actor, "body", body, "create", token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.VenueReview), token, TargetTypeEnum.VenueReview).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, ReviewsBucket, perDay, RateLimitService.Day);
            VenueReview review = new VenueReview
            {
                Id = IdGenerator.Generate(DirectoryIdPrefixes.Review),
                VenueId = venue.Id,
                AuthorUserId = userId,
                Rating = rating,
                Body = body,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                review = await _Services.Database.DirectoryListings.Reviews.CreateAsync(review, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                Record("create", "conflict");
                throw AlreadyReviewed();
            }
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.VenueReview, review.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.VenueReview, review.Id, userId, DirectoryTargetProvider.Scope, "*", token).ConfigureAwait(false);
            await NotifyInstructorsAsync(venue, review, token).ConfigureAwait(false);
            Record("create", "ok");
            return await ReviewViewAsync(actor, review, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/venues/{id}/reviews/{reviewId} (the author): omitted fields stay.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="reviewId">Review.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Review.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400, 403, 404, 429.</exception>
        public async Task<VenueReviewView> UpdateReviewAsync(ContentActor actor, string venueId, string reviewId, VenueReviewRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            await RequireReviewsAsync(token).ConfigureAwait(false);
            VenueReview review = await RequireReviewAsync(actor, venue, reviewId, token).ConfigureAwait(false);
            if (!IsSelf(actor, review.AuthorUserId)) throw ApiErrorException.Forbidden("Only the author can edit this review.");
            if (review.ModerationState == ModerationStateEnum.Removed) throw ApiErrorException.NotFound("Review");
            RequireWriter(actor);
            _Services.Limits.EnforceMember(actor, FeedService.EditsBucket, _Services.Settings.RateLimits.EditsPerHour, RateLimitService.Hour);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            int rating = present.Contains("rating") ? Rating(request.Rating, true, errors) ?? review.Rating : review.Rating;
            string body = present.Contains("body") ? Body(request.Body, true, errors) ?? review.Body : review.Body;
            if (errors.Count > 0)
            {
                Record("update", "rejected");
                throw ApiErrorException.Validation(errors);
            }
            WordFilterResult filter = await CheckTextAsync(actor, "body", body, "update", token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.VenueReview), token, TargetTypeEnum.VenueReview).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            bool changed = rating != review.Rating || !String.Equals(body, review.Body, StringComparison.Ordinal);
            if (changed)
            {
                review.Rating = rating;
                review.Body = body;
                review.EditedUtc = now;
                review.UpdatedUtc = now;
                review = await _Services.Database.DirectoryListings.Reviews.UpdateAsync(review, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Review");
            }
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.VenueReview, review.Id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.VenueReview, review.Id, review.AuthorUserId, DirectoryTargetProvider.Scope, "*", token).ConfigureAwait(false);
            Record("update", "ok");
            return await ReviewViewAsync(actor, review, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/venues/{id}/reviews/{reviewId} (the author). The member may review the venue again.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="reviewId">Review.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteReviewAsync(ContentActor actor, string venueId, string reviewId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            await RequireReviewsAsync(token).ConfigureAwait(false);
            VenueReview review = await RequireReviewAsync(actor, venue, reviewId, token).ConfigureAwait(false);
            if (!IsSelf(actor, review.AuthorUserId)) throw ApiErrorException.Forbidden("Only the author can delete this review; staff use the moderation actions.");
            if (!await _Services.Database.DirectoryListings.Reviews.SoftDeleteAsync(review.Id, DateTime.UtcNow, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Review");
            Record("delete", "ok");
        }

        /// <summary>
        /// PUT /api/v1/venues/{id}/reviews/{reviewId}/reply: a linked instructor of the venue writes or edits the one
        /// public reply. The first reply notifies the reviewer (VenueReviewReply).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="reviewId">Review.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Review.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 403 Forbidden, InteractionBlocked, or Muted, 404 NotFound, 429.</exception>
        public async Task<VenueReviewView> ReplyAsync(ContentActor actor, string venueId, string reviewId, VenueReviewReplyRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            await RequireReviewsAsync(token).ConfigureAwait(false);
            VenueReview review = await RequireReviewAsync(actor, venue, reviewId, token).ConfigureAwait(false);
            Instructor? linked = await LinkedInstructorAsync(actor, venue.Id, token).ConfigureAwait(false);
            if (linked == null) throw ApiErrorException.Forbidden("Only a linked instructor of this venue can reply to its reviews.");
            if (IsSelf(actor, review.AuthorUserId)) throw ApiErrorException.Forbidden("You cannot reply to your own review.");
            if (review.ModerationState != ModerationStateEnum.None) throw ApiErrorException.NotFound("Review");
            actor.RequireCanPost();
            if (!String.IsNullOrEmpty(review.ReplyUserId) && !IsSelf(actor, review.ReplyUserId)) throw ApiErrorException.Forbidden("Another instructor already replied to this review.");
            if (await _Services.Blocks.IsEitherBlockedAsync(review.AuthorUserId, actor.UserId, token).ConfigureAwait(false))
                throw new ApiErrorException(403, "InteractionBlocked", "You cannot reply to this member.");
            _Services.Limits.EnforceMember(actor, FeedService.EditsBucket, _Services.Settings.RateLimits.EditsPerHour, RateLimitService.Hour);
            string text = (request.Body ?? String.Empty).Trim();
            int max = _Services.Settings.Directory.ReviewReplyMaxLength;
            if (text.Length == 0) throw ApiErrorException.Field("body", "Required.");
            if (text.Length > max) throw ApiErrorException.Field("body", "At most " + max + " characters.");
            WordFilterResult filter = await CheckTextAsync(actor, "body", text, "reply", token).ConfigureAwait(false);
            bool first = String.IsNullOrEmpty(review.ReplyBody);
            if (!await _Services.Database.DirectoryListings.Reviews.SetReplyAsync(review.Id, text, actor.UserId!, linked.Id, DateTime.UtcNow, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Review");
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.VenueReview, review.Id, review.AuthorUserId, DirectoryTargetProvider.Scope, "*", token).ConfigureAwait(false);
            if (first)
            {
                await _Services.Notifications.CreateAsync(new NotificationDraft
                {
                    RecipientUserId = review.AuthorUserId,
                    Type = NotificationTypeEnum.VenueReviewReply,
                    ActorUserId = actor.UserId,
                    TargetType = TargetTypeEnum.VenueReview,
                    TargetId = review.Id,
                    TitleText = linked.DisplayName + " replied to your review of " + venue.Name,
                    Body = MarkdownNormalizer.Excerpt(text),
                    Path = ReviewPath(venue.Id, review.Id)
                }, token).ConfigureAwait(false);
            }
            Record("reply", "ok");
            VenueReview fresh = await _Services.Database.DirectoryListings.Reviews.ReadAsync(review.Id, token).ConfigureAwait(false) ?? review;
            return await ReviewViewAsync(actor, fresh, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/venues/{id}/reviews/{reviewId}/reply: the reply's author, or directory staff (audited as
        /// venue.review.reply.remove).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="reviewId">Review.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Review.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<VenueReviewView> DeleteReplyAsync(ContentActor actor, RequestInfo info, string venueId, string reviewId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            await RequireReviewsAsync(token).ConfigureAwait(false);
            VenueReview review = await RequireReviewAsync(actor, venue, reviewId, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(review.ReplyBody)) throw ApiErrorException.NotFound("Reply");
            bool own = IsSelf(actor, review.ReplyUserId);
            if (!own && !actor.IsDirectoryStaff) throw ApiErrorException.Forbidden("Only the reply's author or directory staff can delete it.");
            await _Services.Database.DirectoryListings.Reviews.ClearReplyAsync(review.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            if (!own) await _Services.Audit.WriteAsync(info, "venue.review.reply.remove", TargetTypeEnum.VenueReview.ToString(), review.Id, venue.Name, token).ConfigureAwait(false);
            Record("reply_delete", "ok");
            VenueReview fresh = await _Services.Database.DirectoryListings.Reviews.ReadAsync(review.Id, token).ConfigureAwait(false) ?? review;
            return await ReviewViewAsync(actor, fresh, token).ConfigureAwait(false);
        }

        // ---------------- Media ----------------

        /// <summary>
        /// POST /api/v1/venues/{id}/media/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EnumerationResult<VenueMediaItemView>> ListMediaAsync(ContentActor actor, string venueId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, MediaService.ReadBucket, _Services.Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            VenueContributionFilter filter = new VenueContributionFilter
            {
                VenueId = venue.Id,
                IncludeModerated = actor.IsDirectoryStaff,
                HiddenAuthorIds = await HiddenAsync(actor, token).ConfigureAwait(false),
                AuthorUserId = q.FilterString("uploaderUserId")
            };
            string? kind = q.FilterString("kind");
            if (!String.IsNullOrEmpty(kind))
            {
                if (String.Equals(kind, "Image", StringComparison.OrdinalIgnoreCase)) filter.Kind = MediaKindEnum.Image;
                else if (String.Equals(kind, "Video", StringComparison.OrdinalIgnoreCase)) filter.Kind = MediaKindEnum.Video;
                else errors["filters.kind"] = "Use Image or Video.";
            }
            DirectoryFilters.ThrowIfAny(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, DirectoryCollections.VenueMedia, actor.IsDirectoryStaff, venue.Id);
            EnumerationResult<VenueMediaItem> page = await _Services.Database.DirectoryListings.Media.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            Instructor? linked = await LinkedInstructorAsync(actor, venue.Id, token).ConfigureAwait(false);
            return page.With(await MediaViewsAsync(actor, page.Objects, linked, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/venues/{id}/media: attach 1 to 10 of the caller's uploads to the venue as member media items.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new items.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 MemberMediaDisabled, MediaNotAllowed, VideoUploadNotAllowed, Muted, or Forbidden, 404 NotFound, 409 MediaNotAttachable.</exception>
        public async Task<VenueMediaAddResponse> AddMediaAsync(ContentActor actor, string venueId, VenueMediaAddRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            if (!await MemberMediaEnabledAsync(token).ConfigureAwait(false)) throw new ApiErrorException(403, "MemberMediaDisabled", "Member photos and videos are turned off for this property.");
            string userId = RequireWriter(actor);
            if (venue.Status == VenueStatusEnum.Archived) throw ApiErrorException.NotFound("Venue");
            actor.RequireCanPostMedia();
            List<string> ids = (request.MediaIds ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList();
            if (ids.Count == 0) throw ApiErrorException.Field("mediaIds", "Add 1 to " + MaxMediaPerAdd + " uploaded media ids.");
            if (ids.Count > MaxMediaPerAdd) throw ApiErrorException.Field("mediaIds", "At most " + MaxMediaPerAdd + " per request.");
            List<MediaAttachment> media = await _Services.Media.ValidateAttachableAsync(actor, ids, MaxMediaPerAdd, MaxMediaPerAdd, null, null, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            for (int i = 0; i < media.Count; i++)
                if (media[i].Kind != MediaKindEnum.Image && media[i].Kind != MediaKindEnum.Video) errors["mediaIds[" + i + "]"] = "Only photos and videos can be added.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (media.Any(m => m.Kind == MediaKindEnum.Video)) actor.RequireCanUploadVideo();
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.VenueMedia), token, TargetTypeEnum.VenueMedia).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            List<VenueMediaItem> items = media.Select(m => new VenueMediaItem
            {
                Id = IdGenerator.Generate(DirectoryIdPrefixes.VenueMedia),
                VenueId = venue.Id,
                MediaId = m.Id,
                UploaderUserId = userId,
                Kind = m.Kind,
                CreatedUtc = now,
                UpdatedUtc = now
            }).ToList();
            try
            {
                await _Services.Database.DirectoryListings.Media.CreateManyAsync(items, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                TrellisKitTelemetry.Add(DirectoryTelemetry.VenueMedia, 1, "action", "add", "kind", "none", "outcome", "conflict");
                throw new ApiErrorException(409, "MediaNotAttachable", "This media is already attached to other content.");
            }
            foreach (VenueMediaItem item in items)
            {
                await _Services.Media.AttachAsync(TargetTypeEnum.VenueMedia, item.Id, new List<string> { item.MediaId }, token).ConfigureAwait(false);
                if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.VenueMedia, item.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(DirectoryTelemetry.VenueMedia, 1, "action", "add", "kind", KindLabel(item.Kind), "outcome", "ok");
            }
            Instructor? linked = await LinkedInstructorAsync(actor, venue.Id, token).ConfigureAwait(false);
            return new VenueMediaAddResponse { Objects = await MediaViewsAsync(actor, items, linked, token).ConfigureAwait(false) };
        }

        /// <summary>
        /// DELETE /api/v1/venues/{id}/media/{itemId}: the uploader deletes (media purged); directory staff and the
        /// venue's linked instructors remove (served objects retained, audited as venue.media.remove). A non-admin never
        /// removes a SystemAdmin's upload.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="venueId">Venue identifier or slug.</param>
        /// <param name="itemId">Item.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task RemoveMediaAsync(ContentActor actor, RequestInfo info, string venueId, string itemId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            Venue venue = await RequireVenueAsync(actor, venueId, token).ConfigureAwait(false);
            VenueMediaItem? item = await _Services.Database.DirectoryListings.Media.ReadAsync(itemId, token).ConfigureAwait(false);
            if (item == null || item.DeletedUtc != null || !String.Equals(item.VenueId, venue.Id, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Media item");
            string? why = String.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (why != null && why.Length > MaxReasonLength) throw ApiErrorException.Field("reason", "At most " + MaxReasonLength + " characters.");
            DateTime now = DateTime.UtcNow;
            if (IsSelf(actor, item.UploaderUserId))
            {
                if (!await _Services.Database.DirectoryListings.Media.SoftDeleteAsync(item.Id, now, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Media item");
                await _Services.Media.PurgeTargetAsync(TargetTypeEnum.VenueMedia, item.Id, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(DirectoryTelemetry.VenueMedia, 1, "action", "delete", "kind", KindLabel(item.Kind), "outcome", "ok");
                return;
            }
            bool manager = actor.IsDirectoryStaff || await LinkedInstructorAsync(actor, venue.Id, token).ConfigureAwait(false) != null;
            if (!manager)
            {
                if (item.IsLive) throw ApiErrorException.Forbidden("Only the uploader, directory staff, and the venue's linked instructors can remove this.");
                throw ApiErrorException.NotFound("Media item");
            }
            if (item.ModerationState == ModerationStateEnum.Removed) throw ApiErrorException.NotFound("Media item");
            if (!actor.IsSystemAdmin && (await _Services.Summaries.SystemAdminsAsync(new[] { item.UploaderUserId }, token).ConfigureAwait(false)).Contains(item.UploaderUserId))
                throw ApiErrorException.Forbidden("Content by a SystemAdmin can only be removed by a SystemAdmin.");
            await _Services.Database.DirectoryListings.Media.SetModerationAsync(item.Id, ModerationStateEnum.Removed, actor.UserId, why, now, token).ConfigureAwait(false);
            await _Services.Media.RetainTargetAsync(TargetTypeEnum.VenueMedia, item.Id, token).ConfigureAwait(false);
            await _Services.Database.Reports.ResolveOpenAsync(TargetTypeEnum.VenueMedia, item.Id, ReportStatusEnum.ActionTaken, actor.UserId, why ?? "Removed", null, now,
                new List<string> { DirectoryTargetProvider.Scope }, token).ConfigureAwait(false);
            await _Services.Database.DirectoryListings.Media.SetOpenReportCountAsync(item.Id, 0, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "venue.media.remove", TargetTypeEnum.VenueMedia.ToString(), item.Id, why ?? venue.Name, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.VenueMedia, 1, "action", "remove", "kind", KindLabel(item.Kind), "outcome", "ok");
        }

        // ---------------- Provider support ----------------

        /// <summary>
        /// Whether a caller may read a review (its venue is readable; hidden and removed reviews only for directory
        /// staff and, while hidden, its author).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="reviewId">Review.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when readable.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public async Task<bool> ReviewVisibleAsync(ContentActor actor, string reviewId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId) && !actor.IsSystemAdmin) return false;
            VenueReview? review = await _Services.Database.DirectoryListings.Reviews.ReadAsync(reviewId, token).ConfigureAwait(false);
            if (review == null || review.DeletedUtc != null) return false;
            Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(review.VenueId, token).ConfigureAwait(false);
            if (venue == null || DirectoryViewBuilder.Level(actor, venue, new DirectoryViewOptions()) != 2) return false;
            return CanSeeReview(actor, review);
        }

        /// <summary>
        /// Whether a caller may read a media item and its media (its venue is readable; moderated items only for
        /// directory staff and the uploader).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="itemId">Item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when readable.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public async Task<bool> MediaVisibleAsync(ContentActor actor, string itemId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId) && !actor.IsSystemAdmin) return false;
            VenueMediaItem? item = await _Services.Database.DirectoryListings.Media.ReadAsync(itemId, token).ConfigureAwait(false);
            if (item == null || item.DeletedUtc != null) return false;
            Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(item.VenueId, token).ConfigureAwait(false);
            if (venue == null || DirectoryViewBuilder.Level(actor, venue, new DirectoryViewOptions()) != 2) return false;
            return item.IsLive || actor.IsDirectoryStaff || IsSelf(actor, item.UploaderUserId);
        }

        /// <summary>
        /// The view of a review for a caller (moderation detail and single reads).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="review">Review.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<VenueReviewView> ReviewViewAsync(ContentActor actor, VenueReview review, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(review);
            Instructor? linked = await LinkedInstructorAsync(actor, review.VenueId, token).ConfigureAwait(false);
            return (await ReviewViewsAsync(actor, new[] { review }, linked, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// The view of a media item for a caller (moderation detail).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="item">Item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<VenueMediaItemView> MediaViewAsync(ContentActor actor, VenueMediaItem item, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(item);
            Instructor? linked = await LinkedInstructorAsync(actor, item.VenueId, token).ConfigureAwait(false);
            return (await MediaViewsAsync(actor, new[] { item }, linked, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// App path of a review: /venues/{venueId}?review={reviewId}.
        /// </summary>
        /// <param name="venueId">Venue.</param>
        /// <param name="reviewId">Review.</param>
        /// <returns>Path.</returns>
        public static string ReviewPath(string venueId, string reviewId)
        {
            return "/venues/" + venueId + "?review=" + reviewId;
        }

        /// <summary>
        /// Account deletion (docs/PHASE4_CONTRACT.md sections 3.12 and 3.13): the member's reviews and replies are
        /// deleted and their media items deleted with the media purged.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            for (int pass = 0; pass < 100; pass++)
            {
                if (await _Services.Database.DirectoryListings.Reviews.EraseForUserAsync(userId, DateTime.UtcNow, _ErasePassLimit, token).ConfigureAwait(false) < _ErasePassLimit) break;
            }
            for (int pass = 0; pass < 100; pass++)
            {
                List<VenueMediaItem> items = await _Services.Database.DirectoryListings.Media.EraseForUserAsync(userId, DateTime.UtcNow, _ErasePassLimit, token).ConfigureAwait(false);
                foreach (VenueMediaItem item in items) await _Services.Media.PurgeTargetAsync(TargetTypeEnum.VenueMedia, item.Id, token).ConfigureAwait(false);
                if (items.Count < _ErasePassLimit) break;
            }
        }

        #endregion

        #region Private-Methods

        private async Task<Venue> RequireVenueAsync(ContentActor actor, string idOrSlug, CancellationToken token)
        {
            if (actor.IsAnonymous || (String.IsNullOrEmpty(actor.UserId) && !actor.IsSystemAdmin)) throw new ApiErrorException(401, "Unauthorized", "Sign in to see reviews and member photos.");
            if (String.IsNullOrWhiteSpace(idOrSlug)) throw ApiErrorException.NotFound("Venue");
            Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(idOrSlug.Trim(), token).ConfigureAwait(false)
                ?? await _Services.Database.DirectoryListings.Venues.ReadBySlugAsync(idOrSlug.Trim(), token).ConfigureAwait(false);
            if (venue == null || DirectoryViewBuilder.Level(actor, venue, new DirectoryViewOptions()) != 2) throw ApiErrorException.NotFound("Venue");
            return venue;
        }

        private async Task RequireReviewsAsync(CancellationToken token)
        {
            if (!await ReviewsEnabledAsync(token).ConfigureAwait(false)) throw new ApiErrorException(403, "ReviewsDisabled", "Reviews are turned off for this property.");
        }

        private async Task<VenueReview> RequireReviewAsync(ContentActor actor, Venue venue, string reviewId, CancellationToken token)
        {
            VenueReview? review = String.IsNullOrWhiteSpace(reviewId) ? null : await _Services.Database.DirectoryListings.Reviews.ReadAsync(reviewId.Trim(), token).ConfigureAwait(false);
            if (review == null || review.DeletedUtc != null || !String.Equals(review.VenueId, venue.Id, StringComparison.Ordinal) || !CanSeeReview(actor, review)) throw ApiErrorException.NotFound("Review");
            if (!IsSelf(actor, review.AuthorUserId) && !actor.IsDirectoryStaff && !String.IsNullOrEmpty(actor.UserId)
                && await _Services.Blocks.IsEitherBlockedAsync(actor.UserId, review.AuthorUserId, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Review");
            return review;
        }

        private static bool CanSeeReview(ContentActor actor, VenueReview review)
        {
            if (review.ModerationState == ModerationStateEnum.None || actor.IsDirectoryStaff) return true;
            return review.ModerationState == ModerationStateEnum.Hidden && IsSelf(actor, review.AuthorUserId);
        }

        private static bool IsSelf(ContentActor actor, string? userId)
        {
            return !String.IsNullOrEmpty(userId) && String.Equals(actor.UserId, userId, StringComparison.Ordinal);
        }

        private static string RequireWriter(ContentActor actor)
        {
            string userId = actor.RequireUserId();
            actor.RequireCanPost();
            if (!DirectoryAccess.MaySubmit(actor)) throw ApiErrorException.Forbidden("Your account cannot contribute to the directory.");
            return userId;
        }

        private int? Rating(int? value, bool required, Dictionary<string, string> errors)
        {
            if (value == null)
            {
                if (required) errors["rating"] = "Required: 1 to 5 stars.";
                return null;
            }
            if (value < 1 || value > 5)
            {
                errors["rating"] = "Use a whole number 1 to 5.";
                return null;
            }
            return value;
        }

        private string? Body(string? value, bool required, Dictionary<string, string> errors)
        {
            string text = (value ?? String.Empty).Trim();
            int min = _Services.Settings.Directory.ReviewMinLength;
            int max = _Services.Settings.Directory.ReviewMaxLength;
            if (text.Length == 0 && (required && min > 0))
            {
                errors["body"] = "Required.";
                return null;
            }
            if (text.Length < min)
            {
                errors["body"] = "At least " + min + " characters.";
                return null;
            }
            if (text.Length > max)
            {
                errors["body"] = "At most " + max + " characters.";
                return null;
            }
            return text;
        }

        private async Task<WordFilterResult> CheckTextAsync(ContentActor actor, string field, string text, string action, CancellationToken token)
        {
            try
            {
                return await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.VenueReview, new Dictionary<string, string?> { { field, text } }, token).ConfigureAwait(false);
            }
            catch (ApiErrorException)
            {
                Record(action, "filtered");
                throw;
            }
        }

        private static ApiErrorException AlreadyReviewed()
        {
            return new ApiErrorException(409, "AlreadyReviewed", "You already reviewed this venue; edit your review instead.");
        }

        private static void Record(string action, string outcome)
        {
            TrellisKitTelemetry.Add(DirectoryTelemetry.Reviews, 1, "action", action, "outcome", outcome);
        }

        private static string KindLabel(MediaKindEnum kind)
        {
            return kind == MediaKindEnum.Video ? "video" : kind == MediaKindEnum.Image ? "image" : "none";
        }

        private async Task<List<string>> HiddenAsync(ContentActor actor, CancellationToken token)
        {
            if (String.IsNullOrEmpty(actor.UserId)) return new List<string>();
            return (await _Services.Blocks.RelatedAsync(actor.UserId, token).ConfigureAwait(false)).ToList();
        }

        private async Task NotifyInstructorsAsync(Venue venue, VenueReview review, CancellationToken token)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 100 }, DirectoryCollections.VenueInstructors, true, venue.Id);
            EnumerationResult<Instructor> page = await _Services.Database.DirectoryListings.Instructors.EnumerateAsync(request, new InstructorFilter { VenueId = venue.Id, LiveOnly = true }, token).ConfigureAwait(false);
            HashSet<string> sent = new HashSet<string>(StringComparer.Ordinal);
            foreach (Instructor instructor in page.Objects)
            {
                if (String.IsNullOrEmpty(instructor.LinkedUserId) || instructor.Status != InstructorStatusEnum.Active || !sent.Add(instructor.LinkedUserId)) continue;
                if (String.Equals(instructor.LinkedUserId, review.AuthorUserId, StringComparison.Ordinal)) continue;
                await _Services.Notifications.CreateAsync(new NotificationDraft
                {
                    RecipientUserId = instructor.LinkedUserId,
                    Type = NotificationTypeEnum.VenueReview,
                    ActorUserId = review.AuthorUserId,
                    TargetType = TargetTypeEnum.VenueReview,
                    TargetId = review.Id,
                    TitleText = "New " + review.Rating + "-star review of " + venue.Name,
                    Body = MarkdownNormalizer.Excerpt(review.Body),
                    Path = ReviewPath(venue.Id, review.Id)
                }, token).ConfigureAwait(false);
            }
        }

        private async Task<List<VenueReviewView>> ReviewViewsAsync(ContentActor actor, IReadOnlyList<VenueReview> reviews, Instructor? linked, CancellationToken token)
        {
            List<VenueReviewView> result = new List<VenueReviewView>();
            if (reviews.Count == 0) return result;
            bool staff = actor.IsDirectoryStaff;
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(reviews.Select(r => (string?)r.AuthorUserId).Concat(reviews.Select(r => r.ReplyUserId)), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.VenueReview, reviews.Select(r => r.Id), actor.IsStaffFor(TargetTypeEnum.VenueReview), token).ConfigureAwait(false);
            HashSet<string> hidden = String.IsNullOrEmpty(actor.UserId) ? new HashSet<string>() : await _Services.Blocks.RelatedAsync(actor.UserId, token).ConfigureAwait(false);
            List<string> instructorIds = reviews.Where(r => !String.IsNullOrEmpty(r.ReplyInstructorId)).Select(r => r.ReplyInstructorId!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string id in instructorIds)
            {
                Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(id, token).ConfigureAwait(false);
                if (instructor != null) names[id] = instructor.DisplayName;
            }
            bool signedIn = !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId);
            foreach (VenueReview review in reviews)
            {
                metadata.TryGetValue(review.Id, out ObjectMetadata? m);
                bool author = IsSelf(actor, review.AuthorUserId);
                VenueReviewView view = new VenueReviewView
                {
                    Id = review.Id,
                    VenueId = review.VenueId,
                    Author = users.TryGetValue(review.AuthorUserId, out UserSummary? a) ? a : UserSummary.Deleted(),
                    Rating = review.Rating,
                    Body = review.Body,
                    EditedUtc = review.EditedUtc,
                    ModerationState = review.ModerationState,
                    Labels = m?.Labels ?? new List<string>(),
                    Tags = m?.Tags ?? new Dictionary<string, string>(),
                    CreatedUtc = review.CreatedUtc,
                    UpdatedUtc = review.UpdatedUtc
                };
                bool replyShown = !String.IsNullOrEmpty(review.ReplyBody) && (String.IsNullOrEmpty(review.ReplyUserId) || !hidden.Contains(review.ReplyUserId) || staff);
                if (replyShown)
                {
                    view.Reply = new VenueReviewReplyView
                    {
                        Body = review.ReplyBody!,
                        Author = review.ReplyUserId != null && users.TryGetValue(review.ReplyUserId, out UserSummary? r) ? r : null,
                        InstructorId = review.ReplyInstructorId,
                        InstructorName = review.ReplyInstructorId != null && names.TryGetValue(review.ReplyInstructorId, out string? name) ? name : null,
                        CreatedUtc = review.ReplyUtc ?? review.UpdatedUtc,
                        EditedUtc = review.ReplyEditedUtc
                    };
                }
                bool otherReplied = !String.IsNullOrEmpty(review.ReplyUserId) && !IsSelf(actor, review.ReplyUserId);
                view.Viewer = new VenueReviewViewer
                {
                    IsAuthor = author,
                    CanEdit = author && review.ModerationState != ModerationStateEnum.Removed && actor.CanWrite,
                    CanDelete = author,
                    CanReply = linked != null && !author && review.ModerationState == ModerationStateEnum.None && actor.CanWrite && !otherReplied,
                    CanDeleteReply = view.Reply != null && (IsSelf(actor, review.ReplyUserId) || staff),
                    CanReport = signedIn && !author,
                    CanModerate = staff
                };
                result.Add(view);
            }
            return result;
        }

        private async Task<List<VenueMediaItemView>> MediaViewsAsync(ContentActor actor, IReadOnlyList<VenueMediaItem> items, Instructor? linked, CancellationToken token)
        {
            List<VenueMediaItemView> result = new List<VenueMediaItemView>();
            if (items.Count == 0) return result;
            bool staff = actor.IsDirectoryStaff;
            Dictionary<string, List<MediaView>> media = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.VenueMedia, items.Select(i => i.Id), false, actor.IsStaffFor(TargetTypeEnum.Media), token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(items.Select(i => (string?)i.UploaderUserId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.VenueMedia, items.Select(i => i.Id), actor.IsStaffFor(TargetTypeEnum.VenueMedia), token).ConfigureAwait(false);
            HashSet<string> admins = linked != null && !actor.IsSystemAdmin ? await _Services.Summaries.SystemAdminsAsync(items.Select(i => i.UploaderUserId), token).ConfigureAwait(false) : new HashSet<string>();
            bool signedIn = !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId);
            foreach (VenueMediaItem item in items)
            {
                metadata.TryGetValue(item.Id, out ObjectMetadata? m);
                bool uploader = IsSelf(actor, item.UploaderUserId);
                bool removable = uploader || (item.ModerationState != ModerationStateEnum.Removed && (staff || (linked != null && !admins.Contains(item.UploaderUserId))));
                result.Add(new VenueMediaItemView
                {
                    Id = item.Id,
                    VenueId = item.VenueId,
                    Media = media.TryGetValue(item.Id, out List<MediaView>? list) ? list.FirstOrDefault() : null,
                    Uploader = users.TryGetValue(item.UploaderUserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                    ModerationState = item.ModerationState,
                    Labels = m?.Labels ?? new List<string>(),
                    Tags = m?.Tags ?? new Dictionary<string, string>(),
                    Viewer = new VenueMediaItemViewer { IsUploader = uploader, CanRemove = removable, CanReport = signedIn && !uploader },
                    CreatedUtc = item.CreatedUtc,
                    UpdatedUtc = item.UpdatedUtc
                });
            }
            return result;
        }

        #endregion
    }
}
