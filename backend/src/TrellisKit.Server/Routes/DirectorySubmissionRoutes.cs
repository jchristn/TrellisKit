namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Member directory submission routes (docs/PHASE4_CONTRACT.md sections 3.1, 3.7, and 3.8): submit, list your own,
    /// withdraw, and remove an instructor link to your account.
    /// </summary>
    public class DirectorySubmissionRoutes
    {
        #region Private-Members

        private const string _Tag = "Directory submissions";
        private const string _Submission = "DirectorySubmission { id, type, status, target { type, id, name }, submitter, note, changes [{ field, current, proposed, isOverride }], proposedGeo, possibleDuplicates { objects, totalRecords }, isLinkedMember, isOverdue, dueUtc, reviewNote, decidedUtc, labels, tags, createdUtc, updatedUtc }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public DirectorySubmissionRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/directory/submissions", SubmitAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Submit a directory suggestion", _Tag, "Body { type: NewVenue | EditVenue | NewInstructor | EditInstructor | ClaimInstructor | ReportClosed, targetId (edits, claims, closures), fields (the editable fields of the target type; edits send only changed fields), note, confirmNotDuplicate, labels, tags }. " +
                    "Venue fields: name, affiliation, summary, description, address, addressVisibility (default directory.submissionDefaultAddressVisibility, Exact), phone, email, websiteUrl, socialLinks, scheduleText, programs, visitorPolicy, visitorPolicyNote, photoMediaIds (your own Ready images; they stay unattached until approval). " +
                    "Instructor fields: displayName, bio, photoMediaId, rank, rankLevel, affiliation, websiteUrl, socialLinks, venueIds. ClaimInstructor needs a note (10 to 1000). Nothing is published until directory staff approve it. " +
                    "When NewVenue or NewInstructor finds possible duplicates (name similarity of directory.duplicateNameSimilarity and, for venues, within directory.duplicateDistanceKm) and confirmNotDuplicate is false, the answer is 200 with status Draft and the duplicates; nothing is queued. " +
                    "At most directory.maxPendingSubmissionsPerMember pending; rate limited (directory.submissionsPerDay, new accounts directory.newAccountSubmissionsPerDay). Members with canPost who are not muted; a membership when the directory is gated.")
                    .Body("DirectorySubmissionRequest").Returns(_Submission, 201).Resp(200, "Draft with possibleDuplicates").Resp(400, "ValidationFailed, ContentFiltered, UnknownOption, or InvalidGeo")
                    .Resp(403, "SubmissionsDisabled, PostingNotAllowed, or Muted").Resp(404, "NotFound").Resp(409, "SubmissionLimitReached, AlreadyClaimed, or AlreadyClosed").Resp(429, "RateLimited"));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/me/directory/submissions", DirectoryCollections.MySubmissions, _Submission,
                () => Doc("List your directory submissions", _Tag, "Your submissions, newest first. Filters status, type. Signed in."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Submissions.MineAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/directory/submissions/{id}/withdraw", WithdrawAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Withdraw a directory submission", _Tag, "The submitter withdraws a pending submission; its unattached media is deleted.")
                    .PathParam("id", "Submission identifier").Returns(_Submission).Resp(404, "NotFound").Resp(409, "NotPending"));
            post.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/me/instructor-links/{instructorId}/unlink", UnlinkAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove your instructor link", _Tag, "The member linked to an instructor entry removes the link (always allowed; the entry stays). Audited as instructor.unlink.")
                    .PathParam("instructorId", "Instructor identifier").Resp(204, "Unlinked").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private async Task SubmitAsync(HttpContextBase ctx)
        {
            DirectorySubmissionRequest request = RouteHelper.ReadJson<DirectorySubmissionRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            DirectorySubmissionView view = await _Directory.For(ctx).Directory.Submissions.SubmitAsync(actor, request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, view.Status == DirectorySubmissionStatusEnum.Draft ? 200 : 201, view).ConfigureAwait(false);
        }

        private async Task WithdrawAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Submissions.WithdrawAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnlinkAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Directory.Instructors.SelfUnlinkAsync(actor, RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest), RouteHelper.Param(ctx, "instructorId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
