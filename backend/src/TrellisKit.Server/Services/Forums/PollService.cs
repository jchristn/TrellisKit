namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Polls: validation, votes, results visibility, voters (resolved decision 4), early and scheduled close, and edits
    /// before the first vote (docs/PHASE3_CONTRACT.md section 6).
    /// </summary>
    public class PollService
    {
        #region Public-Members

        /// <summary>
        /// Longest option label.
        /// </summary>
        public const int MaxLabelLength = 140;

        /// <summary>
        /// Earliest scheduled close, in minutes ahead.
        /// </summary>
        public const int MinCloseMinutes = 5;

        /// <summary>
        /// Clock used for votes and closes (tests set a fixed clock).
        /// </summary>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public PollService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a poll payload and build the poll of a new thread.
        /// </summary>
        /// <param name="request">Poll payload.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Poll with options.</returns>
        /// <exception cref="ApiErrorException">400 PollInvalid with fieldErrors.</exception>
        public Poll Build(PollRequest request, string threadId, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            Poll poll = new Poll
            {
                Id = IdGenerator.Generate(ForumIdPrefixes.Poll),
                ThreadId = threadId,
                MultipleChoice = request.MultipleChoice ?? false,
                ResultsVisibility = request.ResultsVisibility ?? PollResultsVisibilityEnum.AfterVote,
                VoterVisibility = request.VoterVisibility ?? PollVoterVisibilityEnum.Anonymous,
                AllowVoteChange = request.AllowVoteChange ?? true,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };
            poll.Options = Options(request.Options, poll.Id, nowUtc, errors);
            poll.MaxChoices = MaxChoices(request.MaxChoices, poll.MultipleChoice, poll.Options.Count, errors);
            poll.ClosesUtc = Closes(request.ClosesUtc, nowUtc, errors);
            if (errors.Count > 0) throw new ApiErrorException(400, "PollInvalid", "The poll is invalid.", errors);
            return poll;
        }

        /// <summary>
        /// Validate a poll edit (section 6.4): before the first vote any field may change; after it only closesUtc
        /// (later, or earlier but not in the past).
        /// </summary>
        /// <param name="request">Poll payload.</param>
        /// <param name="current">Current poll.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>The replacement poll.</returns>
        /// <exception cref="ApiErrorException">400 PollInvalid, 409 PollOptionsLocked or PollClosed.</exception>
        public Poll BuildEdit(PollRequest request, Poll current, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(current);
            if (ForumViewBuilder.IsClosed(current, nowUtc)) throw new ApiErrorException(409, "PollClosed", "This poll is closed.");
            bool voted = current.TotalVoters > 0 || current.FirstVoteUtc != null;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (voted)
            {
                bool other = request.Options != null || request.MultipleChoice != null || request.MaxChoices != null || request.ResultsVisibility != null || request.VoterVisibility != null || request.AllowVoteChange != null;
                if (other) throw new ApiErrorException(409, "PollOptionsLocked", "After the first vote only the closing time can change.");
                Poll copy = Copy(current, nowUtc);
                copy.ClosesUtc = request.ClosesUtc == null ? null : Closes(request.ClosesUtc, nowUtc, errors, true);
                if (errors.Count > 0) throw new ApiErrorException(400, "PollInvalid", "The poll is invalid.", errors);
                copy.Options = current.Options;
                return copy;
            }
            Poll replacement = Copy(current, nowUtc);
            if (request.MultipleChoice != null) replacement.MultipleChoice = request.MultipleChoice.Value;
            if (request.ResultsVisibility != null) replacement.ResultsVisibility = request.ResultsVisibility.Value;
            if (request.VoterVisibility != null) replacement.VoterVisibility = request.VoterVisibility.Value;
            if (request.AllowVoteChange != null) replacement.AllowVoteChange = request.AllowVoteChange.Value;
            replacement.Options = request.Options != null ? Options(request.Options, current.Id, nowUtc, errors) : current.Options;
            replacement.MaxChoices = MaxChoices(request.MaxChoices ?? (replacement.MultipleChoice ? current.MaxChoices : null), replacement.MultipleChoice, replacement.Options.Count, errors);
            replacement.ClosesUtc = request.ClosesUtc != null ? Closes(request.ClosesUtc, nowUtc, errors) : current.ClosesUtc;
            if (errors.Count > 0) throw new ApiErrorException(400, "PollInvalid", "The poll is invalid.", errors);
            return replacement;
        }

        /// <summary>
        /// Store a validated poll edit.
        /// </summary>
        /// <param name="current">Current poll.</param>
        /// <param name="replacement">Validated replacement.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">409 PollOptionsLocked when a vote arrived meanwhile.</exception>
        public async Task ApplyEditAsync(Poll current, Poll replacement, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(replacement);
            bool voted = current.TotalVoters > 0 || current.FirstVoteUtc != null;
            if (voted)
            {
                if (!await _Services.Database.Polls.SetClosesAsync(current.Id, replacement.ClosesUtc, nowUtc, token).ConfigureAwait(false))
                    throw new ApiErrorException(409, "PollClosed", "This poll is closed.");
                return;
            }
            if (!await _Services.Database.Polls.ReplaceOptionsAsync(replacement, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "PollOptionsLocked", "The poll received a vote; only the closing time can change now.");
        }

        /// <summary>
        /// PUT /api/v1/polls/{id}/votes (optionIds; empty is a validation error) or DELETE (remove).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="pollId">Poll.</param>
        /// <param name="optionIds">Chosen options, or null to remove the vote.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The poll as the caller sees it.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Muted or ForumMuted, 404 NotFound, 409 PollClosed, ThreadLocked, PollVoteLocked, or ForumArchived, 429 RateLimited.</exception>
        public async Task<PollView> VoteAsync(ContentActor actor, string pollId, List<string>? optionIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            bool remove = optionIds == null;
            string action = remove ? "remove" : "vote";
            try
            {
                PollContext found = await RequirePollAsync(actor, pollId, token).ConfigureAwait(false);
                Poll poll = found.Poll;
                ThreadContext context = found.Thread;
                actor.RequireNotMuted();
                if (ForumAccessService.StateOf(context.Thread) != ThreadStateEnum.Published) throw ApiErrorException.NotFound("Poll");
                await _Services.Forums.Access.RequireNotForumMutedAsync(actor, context.Thread.ForumId, token).ConfigureAwait(false);
                DateTime now = _Clock();
                if (ForumViewBuilder.IsClosed(poll, now)) throw new ApiErrorException(409, "PollClosed", "This poll is closed.");
                ForumAccessService.RequireOpen(context);
                _Services.Limits.EnforceMember(actor, "polls.votes", _Services.Settings.Polls.VotesPerMinute, RateLimitService.Minute);
                List<string> chosen = (optionIds ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList();
                if (!remove)
                {
                    Dictionary<string, string> errors = new Dictionary<string, string>();
                    HashSet<string> known = new HashSet<string>(poll.Options.Select(o => o.Id), StringComparer.Ordinal);
                    if (chosen.Count == 0) errors["optionIds"] = "Choose at least one option.";
                    else if (chosen.Any(c => !known.Contains(c))) errors["optionIds"] = "Unknown option.";
                    else if (!poll.MultipleChoice && chosen.Count > 1) errors["optionIds"] = "Choose one option.";
                    else if (poll.MultipleChoice && poll.MaxChoices != null && chosen.Count > poll.MaxChoices.Value) errors["optionIds"] = "Choose at most " + poll.MaxChoices.Value + " options.";
                    if (errors.Count > 0) throw ApiErrorException.Validation(errors);
                }
                bool recent = await _Services.Database.Polls.VotedWithinAsync(poll.Id, userId, now.AddHours(-1), token).ConfigureAwait(false);
                PollVoteOutcome outcome = await _Services.Database.Polls.VoteAsync(poll.Id, userId, chosen, poll.AllowVoteChange, now, token).ConfigureAwait(false);
                if (outcome.Closed) throw new ApiErrorException(409, "PollClosed", "This poll is closed.");
                if (outcome.Locked) throw new ApiErrorException(409, "PollVoteLocked", "Votes in this poll cannot be changed.");
                if (outcome.Changed && outcome.HadVote && !remove) action = "change";
                if (!remove && !recent) await _Services.Database.Threads.TouchActivityAsync(context.Thread.Id, now, token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.PollVotes, "action", action, "outcome", "success");
                Poll updated = (await _Services.Database.Polls.ReadAsync(poll.Id, token).ConfigureAwait(false))!;
                return await _Services.Forums.Views.PollViewAsync(updated, context.Thread, actor, token).ConfigureAwait(false);
            }
            catch (ApiErrorException)
            {
                ForumTelemetry.Count(ForumTelemetry.PollVotes, "action", action, "outcome", "rejected");
                throw;
            }
        }

        /// <summary>
        /// POST /api/v1/polls/{id}/close (thread author or moderator in scope; idempotent).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="pollId">Poll.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Poll.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<PollView> CloseAsync(ContentActor actor, string pollId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            PollContext found = await RequirePollAsync(actor, pollId, token).ConfigureAwait(false);
            Poll poll = found.Poll;
            ThreadContext context = found.Thread;
            bool isAuthor = actor.UserId != null && String.Equals(context.Thread.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            bool authorIsAdmin = await _Services.Forums.Access.IsSystemAdminAsync(context.Thread.AuthorUserId, token).ConfigureAwait(false);
            if (!isAuthor && !ForumAccessService.CanModerate(actor, context.Thread.ForumId, authorIsAdmin)) throw ApiErrorException.Forbidden("Only the poll author or a moderator in scope can close this poll.");
            DateTime now = _Clock();
            if (poll.ClosedUtc == null && await _Services.Database.Polls.CloseAsync(poll.Id, actor.UserId, now, token).ConfigureAwait(false))
            {
                ForumTelemetry.Count(ForumTelemetry.PollsClosed, "reason", "manual");
                await _Services.Forums.Notifications.StartPollClosedAsync(poll.Id, token).ConfigureAwait(false);
            }
            Poll updated = (await _Services.Database.Polls.ReadAsync(poll.Id, token).ConfigureAwait(false))!;
            return await _Services.Forums.Views.PollViewAsync(updated, context.Thread, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/polls/{id}/voters/enumerate: who voted for what, only for polls with voter visibility Visible
        /// (resolved decision 4). Staff never see Anonymous voters through the API.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="pollId">Poll.</param>
        /// <param name="query">Query (filter optionId).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of voters.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden (anonymous poll, or results not yet visible to the caller), 404 NotFound.</exception>
        public async Task<EnumerationResult<PollVoterView>> VotersAsync(ContentActor actor, string pollId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            PollContext found = await RequirePollAsync(actor, pollId, token).ConfigureAwait(false);
            Poll poll = found.Poll;
            if (poll.VoterVisibility != PollVoterVisibilityEnum.Visible) throw ApiErrorException.Forbidden("This poll's votes are anonymous.");
            PollView visibility = await _Services.Forums.Views.PollViewAsync(poll, found.Thread.Thread, actor, token).ConfigureAwait(false);
            if (!visibility.ResultsVisible) throw ApiErrorException.Forbidden("Voters are shown once this poll's results are visible.");
            EnumerationQuery q = query ?? new EnumerationQuery();
            string? optionId = q.FilterString("optionId");
            if (optionId != null && !poll.Options.Any(o => o.Id == optionId)) throw ApiErrorException.Field("filters.optionId", "Unknown option.");
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.PollVoters, actor.IsStaffFor(TargetTypeEnum.User), "voters|" + poll.Id + "|" + (optionId ?? "*"));
            EnumerationResult<PollVoter> page = await _Services.Database.Polls.EnumerateVotersAsync(poll.Id, optionId, request, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(page.Objects.Select(v => (string?)v.UserId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.User, page.Objects.Select(v => v.UserId), actor.IsStaffFor(TargetTypeEnum.User), token).ConfigureAwait(false);
            Dictionary<string, int> position = poll.Options.ToDictionary(o => o.Id, o => o.Position, StringComparer.Ordinal);
            return page.With(page.Objects.Select(v => new PollVoterView
            {
                User = users.TryGetValue(v.UserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                OptionIds = v.OptionIds.OrderBy(o => position.TryGetValue(o, out int p) ? p : Int32.MaxValue).ToList(),
                CreatedUtc = v.CreatedUtc,
                Labels = metadata.TryGetValue(v.UserId, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                Tags = m != null ? m.Tags : new Dictionary<string, string>()
            }).ToList());
        }

        /// <summary>
        /// Close polls whose scheduled close passed and start their PollClosed fan-outs (the poll worker's pass).
        /// </summary>
        /// <param name="limit">Most polls per pass.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Polls closed.</returns>
        public async Task<int> CloseDueAsync(int limit, CancellationToken token = default)
        {
            List<Poll> closed = await _Services.Database.Polls.ClaimDueAsync(_Clock(), Math.Max(1, limit), token).ConfigureAwait(false);
            foreach (Poll poll in closed)
            {
                token.ThrowIfCancellationRequested();
                ForumTelemetry.Count(ForumTelemetry.PollsClosed, "reason", "scheduled");
                await _Services.Forums.Notifications.StartPollClosedAsync(poll.Id, token).ConfigureAwait(false);
            }
            return closed.Count;
        }

        #endregion

        #region Private-Methods

        private async Task<PollContext> RequirePollAsync(ContentActor actor, string pollId, CancellationToken token)
        {
            _Services.Forums.Access.RequireEnabled();
            Poll? poll = String.IsNullOrWhiteSpace(pollId) ? null : await _Services.Database.Polls.ReadAsync(pollId.Trim(), token).ConfigureAwait(false);
            if (poll == null) throw ApiErrorException.NotFound("Poll");
            ThreadContext? context = await _Services.Forums.Access.LoadAsync(poll.ThreadId, token).ConfigureAwait(false);
            if (context == null || !ForumAccessService.CanSeeThread(context, actor)) throw ApiErrorException.NotFound("Poll");
            return new PollContext { Poll = poll, Thread = context };
        }

        private List<PollOption> Options(List<PollOptionRequest>? options, string pollId, DateTime nowUtc, Dictionary<string, string> errors)
        {
            List<PollOption> result = new List<PollOption>();
            List<PollOptionRequest> list = options ?? new List<PollOptionRequest>();
            int max = _Services.Settings.Polls.MaxOptions;
            if (list.Count < 2 || list.Count > max)
            {
                errors["poll.options"] = "Add 2 to " + max + " options.";
                return result;
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                string raw = list[i]?.Label ?? String.Empty;
                string label = MarkdownNormalizer.IsWellFormed(raw) ? MarkdownNormalizer.NormalizeSingleLine(raw) : String.Empty;
                int length = MarkdownNormalizer.Length(label);
                if (length < 1 || length > MaxLabelLength)
                {
                    errors["poll.options[" + i + "].label"] = "Use 1 to " + MaxLabelLength + " characters.";
                    continue;
                }
                string normalized = label.ToLowerInvariant();
                if (!seen.Add(normalized))
                {
                    errors["poll.options[" + i + "].label"] = "Options must be unique.";
                    continue;
                }
                result.Add(new PollOption { Id = IdGenerator.Generate(ForumIdPrefixes.PollOption), PollId = pollId, Label = label, LabelNormalized = normalized, Position = i + 1, CreatedUtc = nowUtc });
            }
            return result;
        }

        private static int? MaxChoices(int? maxChoices, bool multipleChoice, int optionCount, Dictionary<string, string> errors)
        {
            if (maxChoices == null) return null;
            if (!multipleChoice)
            {
                errors["poll.maxChoices"] = "Only multiple choice polls have maxChoices.";
                return null;
            }
            if (maxChoices.Value < 2 || maxChoices.Value > Math.Max(2, optionCount)) errors["poll.maxChoices"] = "Use 2 to the option count, or null for any number.";
            return maxChoices;
        }

        private DateTime? Closes(DateTime? closesUtc, DateTime nowUtc, Dictionary<string, string> errors, bool allowSooner = false)
        {
            if (closesUtc == null) return null;
            DateTime utc = closesUtc.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(closesUtc.Value, DateTimeKind.Utc) : closesUtc.Value.ToUniversalTime();
            DateTime earliest = allowSooner ? nowUtc : nowUtc.AddMinutes(MinCloseMinutes);
            if (utc < earliest || utc > nowUtc.AddDays(_Services.Settings.Polls.MaxDurationDays))
                errors["poll.closesUtc"] = allowSooner ? "Must be in the future and at most " + _Services.Settings.Polls.MaxDurationDays + " days ahead."
                    : "Must be at least " + MinCloseMinutes + " minutes and at most " + _Services.Settings.Polls.MaxDurationDays + " days in the future.";
            return utc;
        }

        private static Poll Copy(Poll current, DateTime nowUtc)
        {
            return new Poll
            {
                Id = current.Id,
                ThreadId = current.ThreadId,
                MultipleChoice = current.MultipleChoice,
                MaxChoices = current.MaxChoices,
                ResultsVisibility = current.ResultsVisibility,
                VoterVisibility = current.VoterVisibility,
                AllowVoteChange = current.AllowVoteChange,
                ClosesUtc = current.ClosesUtc,
                ClosedUtc = current.ClosedUtc,
                TotalVoters = current.TotalVoters,
                FirstVoteUtc = current.FirstVoteUtc,
                CreatedUtc = current.CreatedUtc,
                UpdatedUtc = nowUtc
            };
        }

        #endregion
    }
}
