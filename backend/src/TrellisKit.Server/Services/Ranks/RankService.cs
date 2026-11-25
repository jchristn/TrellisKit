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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// Verified ranks (docs/TRELLISKIT.md, Ranks, curricula, and attendance): effective ranks, awards and revokes under the
    /// property's rank.awarding rules, the award history, requirements, eligibility, and the profile field lock (a member
    /// cannot change a Rank field while a verified rank stands). Thread safety: safe for concurrent use.
    /// </summary>
    public class RankService
    {
        #region Public-Members

        /// <summary>
        /// Most requirements per Rank field.
        /// </summary>
        public const int MaxRequirements = 200;

        /// <summary>
        /// Most recent awards and revokes in GET /api/v1/me/rank.
        /// </summary>
        public const int HistoryPreview = 20;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly RankModule _Module;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Rank module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public RankService(ApplicationServices services, RankModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/me/rank.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The caller's ranks.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 for the admin token, 404 NotAvailable.</exception>
        public async Task<MyRankResponse> MineAsync(ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            string userId = actor.RequireUserId();
            List<ProfileField> fields = await _Module.RankFieldsAsync(token).ConfigureAwait(false);
            List<MemberRank> ranks = await _Services.Database.Ranks.ReadMemberRanksAsync(userId, token).ConfigureAwait(false);
            ProfileValueSet values = await _Services.ProfileValues.ReadAsync(userId, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> awarders = await _Services.Summaries.BuildAsync(ranks.Select(r => r.AwardedBy), token).ConfigureAwait(false);
            MyRankResponse response = new MyRankResponse { ShowVerificationLabels = _Services.Settings.Ranks.ShowVerificationLabels };
            foreach (ProfileField field in fields)
            {
                MemberRank? rank = ranks.FirstOrDefault(r => String.Equals(r.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase));
                values.Values.TryGetValue(field.Id, out ProfileValue? value);
                EffectiveRankView current = RankModule.Effective(field, rank, value, awarders);
                List<RankRequirement> requirements = await _Services.Database.Ranks.ListRequirementsAsync(field.Key, token).ConfigureAwait(false);
                response.Fields.Add(new MyRankFieldView { Rank = current, Next = await NextAsync(field, userId, rank, current, requirements, token).ConfigureAwait(false) });
            }
            List<RankPromotion> history = await _Services.Database.Ranks.RecentPromotionsForUserAsync(userId, HistoryPreview, token).ConfigureAwait(false);
            response.History = await PromotionViewsAsync(history, fields, false, token).ConfigureAwait(false);
            response.HistoryTotal = await _Services.Database.Ranks.CountPromotionsForUserAsync(userId, token).ConfigureAwait(false);
            List<LineageEntry> lineage = await _Services.Database.Ranks.ListLineageAsync(userId, null, token).ConfigureAwait(false);
            response.Lineage = await _Module.Lineage.ViewsAsync(lineage.Where(e => fields.Any(f => String.Equals(f.Key, e.FieldKey, StringComparison.OrdinalIgnoreCase))).Take(100).ToList(), fields, true, token).ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// GET /api/v1/members/{id}/rank: the member's effective rank per Rank field the caller may see (profile visibility
        /// rules), with their lineage.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ranks.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotAvailable or NotFound (unknown, deleted, banned, or blocked member).</exception>
        public async Task<MemberRankResponse> MemberAsync(ContentActor actor, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            User? user = String.IsNullOrWhiteSpace(userId) ? null : await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted || (user.IsBanned && !actor.IsSystemAdmin)) throw ApiErrorException.NotFound("Member");
            if (!String.IsNullOrEmpty(actor.UserId) && actor.UserId != user.Id && (await _Services.Blocks.RelatedAsync(actor.UserId, token).ConfigureAwait(false)).Contains(user.Id))
                throw ApiErrorException.NotFound("Member");
            ProfileViewerEnum viewer = ProfileValueService.ViewerOf(actor, user.Id);
            List<ProfileField> fields = (await _Module.RankFieldsAsync(token).ConfigureAwait(false)).Where(f => ProfileValueService.IsVisible(f.Visibility, viewer)).ToList();
            MemberRankResponse response = new MemberRankResponse { UserId = user.Id, ShowVerificationLabels = _Services.Settings.Ranks.ShowVerificationLabels };
            if (fields.Count == 0) return response;
            List<MemberRank> ranks = await _Services.Database.Ranks.ReadMemberRanksAsync(user.Id, token).ConfigureAwait(false);
            ProfileValueSet values = await _Services.ProfileValues.ReadAsync(user.Id, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> awarders = await _Services.Summaries.BuildAsync(ranks.Select(r => r.AwardedBy), token).ConfigureAwait(false);
            foreach (ProfileField field in fields)
            {
                MemberRank? rank = ranks.FirstOrDefault(r => String.Equals(r.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase));
                values.Values.TryGetValue(field.Id, out ProfileValue? value);
                response.Ranks.Add(RankModule.Effective(field, rank, value, awarders));
            }
            List<LineageEntry> lineage = await _Services.Database.Ranks.ListLineageAsync(user.Id, null, token).ConfigureAwait(false);
            response.Lineage = await _Module.Lineage.ViewsAsync(lineage.Where(e => fields.Any(f => String.Equals(f.Key, e.FieldKey, StringComparison.OrdinalIgnoreCase))).Take(100).ToList(), fields, false, token).ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// POST /api/v1/admin/ranks/awards: award a verified rank. The member's profile value of the field becomes the
        /// awarded rank and the field is locked for the member while the award stands. Audited rank.award; the member
        /// gets RankPromoted (in-app and push) and the rank_promoted email.
        /// </summary>
        /// <param name="actor">Caller (RankAwarder or SystemAdmin).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The award.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, UnknownRankOption, LevelOutOfRange, ContentFiltered; 403 Forbidden, SelfAward, AwardAboveOwnRank; 404 NotAvailable or NotFound.</exception>
        public async Task<RankPromotionView> AwardAsync(ContentActor actor, RequestInfo info, RankAwardRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Module.RequireEnabled();
            RequireAwarder(actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string userId = (request.UserId ?? String.Empty).Trim();
            if (userId.Length == 0) errors["userId"] = "Required.";
            if (String.IsNullOrWhiteSpace(request.OptionKey)) errors["optionKey"] = "Required.";
            string? note = String.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (note != null && note.Length > 500) errors["note"] = "Use at most 500 characters.";
            string? eventId = String.IsNullOrWhiteSpace(request.EventId) ? null : request.EventId.Trim();
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            ProfileField field = await _Module.FieldAsync(request.FieldKey, "fieldKey", token).ConfigureAwait(false);
            User? user = await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            if (!String.IsNullOrEmpty(actor.UserId) && actor.UserId == user.Id) throw new ApiErrorException(403, "SelfAward", "You cannot award a rank to yourself.");
            int index = RankAwardRules.IndexOf(field, request.OptionKey);
            if (index < 0 || field.Options[index].Retired)
                throw new ApiErrorException(400, "UnknownRankOption", "Use one of the field's options.", new Dictionary<string, string> { { "optionKey", "Use one of the field's options." } });
            ProfileFieldOption option = field.Options[index];
            int level = request.Level ?? 0;
            int max = RankAwardRules.MaxLevelOf(field, option);
            if (level < 0 || level > max)
                throw new ApiErrorException(400, "LevelOutOfRange", "Use a level from 0 to " + max + ".", new Dictionary<string, string> { { "level", "Use a level from 0 to " + max + "." } });
            if (eventId != null && await _Services.Database.Events.Records.ReadAsync(eventId, token).ConfigureAwait(false) == null) throw ApiErrorException.Field("eventId", "Unknown event.");
            Dictionary<string, MemberRank> held = await _Services.Database.Ranks.ReadMemberRanksAsync(field.Key, new[] { user.Id, actor.UserId ?? user.Id }, token).ConfigureAwait(false);
            held.TryGetValue(user.Id, out MemberRank? target);
            MemberRank? awarder = actor.UserId != null && held.TryGetValue(actor.UserId, out MemberRank? own) ? own : null;
            if (!RankAwardRules.Allows(field, actor.IsSystemAdmin, awarder, target, option.Key, level))
                throw new ApiErrorException(403, "AwardAboveOwnRank", "The property's awarding rules do not let you award this rank.");
            ObjectMetadata metadata = await _Services.Metadata.PrepareNewAsync(actor, TargetTypeEnum.RankPromotion, request.Labels, request.Tags, token).ConfigureAwait(false);
            WordFilterResult? flags = null;
            if (note != null) flags = await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.RankPromotion, new Dictionary<string, string?> { { "note", note } }, token).ConfigureAwait(false);

            DateTime now = DateTime.UtcNow;
            RankPromotion award = new RankPromotion
            {
                Id = IdGenerator.Generate(RankIds.Promotion),
                UserId = user.Id,
                FieldKey = field.Key,
                OptionKey = option.Key,
                Level = level,
                Action = RankPromotionActionEnum.Award,
                ActorId = actor.UserId,
                Note = note,
                EventId = eventId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            MemberRank rank = new MemberRank
            {
                Id = target?.Id ?? IdGenerator.Generate(RankIds.MemberRank),
                UserId = user.Id,
                FieldKey = field.Key,
                OptionKey = option.Key,
                Level = level,
                Status = RankStatusEnum.Verified,
                PromotionId = award.Id,
                AwardedBy = actor.UserId,
                AwardedUtc = now,
                CreatedUtc = target?.CreatedUtc ?? now,
                UpdatedUtc = now
            };
            await _Services.Database.Ranks.AwardAsync(award, rank, token).ConfigureAwait(false);
            if (metadata.Labels.Count > 0 || metadata.Tags.Count > 0) await _Services.Database.Metadata.WriteAsync(TargetTypeEnum.RankPromotion, award.Id, metadata, token).ConfigureAwait(false);
            if (flags != null) await _Services.WordFilter.RecordFlagsAsync(flags, TargetTypeEnum.RankPromotion, award.Id, actor.UserId, token).ConfigureAwait(false);
            await SetProfileValueAsync(user, field, option.Key, level, actor.UserId, token).ConfigureAwait(false);
            string label = RankAwardRules.Label(field, option.Key, level);
            await _Services.Audit.WriteAsync(info, "rank.award", "User", user.Id, "Awarded " + label + " (" + field.Key + ")", token).ConfigureAwait(false);
            await NotifyPromotionAsync(user, actor, rank.Id, label, note, token).ConfigureAwait(false);
            List<RankPromotionView> views = await PromotionViewsAsync(new List<RankPromotion> { award }, new List<ProfileField> { field }, true, token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// POST /api/v1/admin/ranks/awards/{id}/revoke: revoke a standing award. When it is the member's current verified
        /// rank, the rank becomes Revoked and the profile field is editable (self-reported) again. Audited rank.revoke.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin, the original awarder, or a RankAwarder the awarding rules allow).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="awardId">Award identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The revoke row.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden or SelfAward, 404 NotAvailable or NotFound, 409 AlreadyRevoked.</exception>
        public async Task<RankPromotionView> RevokeAsync(ContentActor actor, RequestInfo info, string awardId, RankRevokeRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Module.RequireEnabled();
            RequireAwarder(actor);
            string? reason = String.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason == null) throw ApiErrorException.Field("reason", "Required.");
            if (reason.Length > 500) throw ApiErrorException.Field("reason", "Use at most 500 characters.");
            RankPromotion? award = await _Services.Database.Ranks.ReadPromotionAsync(awardId, token).ConfigureAwait(false);
            if (award == null || award.Action != RankPromotionActionEnum.Award) throw ApiErrorException.NotFound("Award");
            if (!String.IsNullOrEmpty(actor.UserId) && actor.UserId == award.UserId) throw new ApiErrorException(403, "SelfAward", "You cannot revoke your own rank.");
            if (award.RevokedUtc != null) throw new ApiErrorException(409, "AlreadyRevoked", "This award was already revoked.");
            ProfileField field = await _Module.FieldAsync(award.FieldKey, "fieldKey", token).ConfigureAwait(false);
            if (!actor.IsSystemAdmin && actor.UserId != award.ActorId)
            {
                Dictionary<string, MemberRank> held = await _Services.Database.Ranks.ReadMemberRanksAsync(field.Key, new[] { actor.UserId!, award.UserId }, token).ConfigureAwait(false);
                held.TryGetValue(actor.UserId!, out MemberRank? own);
                held.TryGetValue(award.UserId, out MemberRank? target);
                if (!RankAwardRules.Allows(field, false, own, target, award.OptionKey, award.Level)) throw ApiErrorException.Forbidden("The property's awarding rules do not let you revoke this rank.");
            }
            await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.RankPromotion, new Dictionary<string, string?> { { "reason", reason } }, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            RankPromotion revoke = new RankPromotion
            {
                Id = IdGenerator.Generate(RankIds.Promotion),
                UserId = award.UserId,
                FieldKey = award.FieldKey,
                OptionKey = award.OptionKey,
                Level = award.Level,
                Action = RankPromotionActionEnum.Revoke,
                ActorId = actor.UserId,
                Reason = reason,
                AwardId = award.Id,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            if (!await _Services.Database.Ranks.RevokeAsync(award.Id, revoke, token).ConfigureAwait(false)) throw new ApiErrorException(409, "AlreadyRevoked", "This award was already revoked.");
            await _Services.Audit.WriteAsync(info, "rank.revoke", "User", award.UserId, "Revoked " + RankAwardRules.Label(field, award.OptionKey, award.Level) + " (" + award.Id + ")", token).ConfigureAwait(false);
            List<RankPromotionView> views = await PromotionViewsAsync(new List<RankPromotion> { revoke }, new List<ProfileField> { field }, true, token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// GET /api/v1/admin/ranks/awards (RankCollections.Promotions).
        /// </summary>
        /// <param name="actor">Caller (RankAwarder or SystemAdmin).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<RankPromotionView>> ListAwardsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            RequireAwarder(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            RankPromotionFilter filter = new RankPromotionFilter
            {
                UserId = q.FilterString("userId"),
                FieldKey = q.FilterString("fieldKey"),
                OptionKey = q.FilterString("optionKey"),
                ActorId = q.FilterString("actorId"),
                Action = q.FilterEnum<RankPromotionActionEnum>("action", errors),
                FromUtc = q.FilterUtc("fromUtc", errors),
                ToUtc = q.FilterUtc("toUtc", errors)
            };
            string? status = q.FilterString("status");
            if (status != null)
            {
                if (String.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) filter.Revoked = false;
                else if (String.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase)) filter.Revoked = true;
                else errors["filters.status"] = "Use Active or Revoked.";
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, RankCollections.Promotions, actor.IsStaffFor(TargetTypeEnum.RankPromotion), "rank-awards");
            EnumerationResult<RankPromotion> page = await _Services.Database.Ranks.EnumeratePromotionsAsync(request, filter, token).ConfigureAwait(false);
            List<ProfileField> fields = await _Module.RankFieldsAsync(token).ConfigureAwait(false);
            return page.With(await PromotionViewsAsync(page.Objects, fields, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/ranks/eligibility (RankCollections.Eligibility): members holding a value in the Rank field,
        /// with their next rank and requirement progress. With eligibleOnly (default true) a page holds only eligible
        /// members, so it may hold fewer than maxResults objects while endOfResults is false.
        /// </summary>
        /// <param name="actor">Caller (RankAwarder or SystemAdmin).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<RankEligibilityView>> EligibilityAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            RequireAwarder(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool eligibleOnly = q.FilterBool("eligibleOnly", errors) ?? true;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            ProfileField field = await _Module.FieldAsync(q.FilterString("fieldKey"), "filters.fieldKey", token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, RankCollections.Eligibility, actor.IsStaffFor(TargetTypeEnum.User), "rank-eligibility|" + field.Key);
            EnumerationResult<string> page = await _Services.Database.Ranks.EnumerateCandidatesAsync(request, field.Id, field.Key, q.FilterString("optionKey"), token).ConfigureAwait(false);
            List<string> ids = page.Objects;
            Dictionary<string, MemberRank> ranks = await _Services.Database.Ranks.ReadMemberRanksAsync(field.Key, ids, token).ConfigureAwait(false);
            Dictionary<string, ProfileValueSet> values = ids.Count == 0 ? new Dictionary<string, ProfileValueSet>() : await _Services.Database.ProfileValues.ReadForUsersAsync(ids, new List<string> { field.Id }, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(ids.Concat(ranks.Values.Select(r => r.AwardedBy)), token).ConfigureAwait(false);
            List<RankRequirement> requirements = await _Services.Database.Ranks.ListRequirementsAsync(field.Key, token).ConfigureAwait(false);
            List<RankEligibilityView> views = new List<RankEligibilityView>();
            foreach (string id in ids)
            {
                ranks.TryGetValue(id, out MemberRank? rank);
                ProfileValue? value = values.TryGetValue(id, out ProfileValueSet? set) && set.Values.TryGetValue(field.Id, out ProfileValue? found) ? found : null;
                EffectiveRankView current = RankModule.Effective(field, rank, value, summaries);
                RankNextView? next = await NextAsync(field, id, rank, current, requirements, token).ConfigureAwait(false);
                bool eligible = next != null && next.Eligible;
                if (eligibleOnly && !eligible) continue;
                views.Add(new RankEligibilityView { UserId = id, User = summaries.TryGetValue(id, out UserSummary? summary) ? summary : null, Current = current, Next = next, Eligible = eligible });
            }
            await _Services.Metadata.AttachAsync(TargetTypeEnum.User, views, v => v.UserId, actor.IsStaffFor(TargetTypeEnum.User), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/admin/ranks/requirements (RankCollections.Requirements).
        /// </summary>
        /// <param name="actor">Caller (RankAwarder or SystemAdmin).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<RankRequirement>> ListRequirementsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            RequireAwarder(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            EnumerationRequest request = _Services.Enumeration.Prepare(q, RankCollections.Requirements, actor.IsStaffFor(TargetTypeEnum.RankRequirement), "rank-requirements");
            EnumerationResult<RankRequirement> page = await _Services.Database.Ranks.EnumerateRequirementsAsync(request, q.FilterString("fieldKey"), q.FilterString("optionKey"), token).ConfigureAwait(false);
            await _Services.Metadata.AttachAsync(TargetTypeEnum.RankRequirement, page.Objects, r => r.Id, actor.IsStaffFor(TargetTypeEnum.RankRequirement), token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// PUT /api/v1/admin/ranks/requirements: replace every requirement of one Rank field. Audited rank.requirements.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored requirements.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<RankRequirementsResponse> ReplaceRequirementsAsync(ContentActor actor, RequestInfo info, RankRequirementsRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Module.RequireEnabled();
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin can change rank requirements.");
            ProfileField field = await _Module.FieldAsync(request.FieldKey, "fieldKey", token).ConfigureAwait(false);
            List<RankRequirementInput> inputs = request.Requirements ?? new List<RankRequirementInput>();
            if (inputs.Count > MaxRequirements) throw ApiErrorException.Field("requirements", "Use at most " + MaxRequirements + " requirements.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> curriculumIds = inputs.Where(i => i != null && !String.IsNullOrWhiteSpace(i.CurriculumId)).Select(i => i.CurriculumId!.Trim()).ToList();
            Dictionary<string, Curriculum> curricula = await _Services.Database.Curricula.ReadManyAsync(curriculumIds, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            List<RankRequirement> requirements = new List<RankRequirement>();
            for (int i = 0; i < inputs.Count; i++)
            {
                RankRequirementInput? input = inputs[i];
                string p = "requirements[" + i + "].";
                if (input == null)
                {
                    errors["requirements[" + i + "]"] = "Required.";
                    continue;
                }
                int index = RankAwardRules.IndexOf(field, input.OptionKey);
                if (index < 0)
                {
                    errors[p + "optionKey"] = "Use one of the field's options.";
                    continue;
                }
                ProfileFieldOption option = field.Options[index];
                int max = RankAwardRules.MaxLevelOf(field, option);
                if (input.Level != null && (input.Level < 0 || input.Level > max)) errors[p + "level"] = "Use 0 to " + max + ", or null for every level.";
                if (input.MinimumDaysInGrade < 0 || input.MinimumDaysInGrade > 3650) errors[p + "minimumDaysInGrade"] = "Use 0 to 3650.";
                if (input.MinimumAttendance < 0 || input.MinimumAttendance > 10000) errors[p + "minimumAttendance"] = "Use 0 to 10000.";
                string? notes = String.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
                if (notes != null && notes.Length > 1000) errors[p + "notes"] = "Use at most 1000 characters.";
                string? curriculumId = String.IsNullOrWhiteSpace(input.CurriculumId) ? null : input.CurriculumId.Trim();
                if (curriculumId != null && !curricula.ContainsKey(curriculumId)) errors[p + "curriculumId"] = "Unknown curriculum.";
                if (!seen.Add(option.Key + "|" + (input.Level?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*"))) errors[p + "level"] = "Another requirement covers this option and level.";
                await _Services.Metadata.ValidateAsync(actor, input.Labels, input.Tags, true, token, TargetTypeEnum.RankRequirement).ConfigureAwait(false);
                requirements.Add(new RankRequirement
                {
                    Id = IdGenerator.Generate(RankIds.Requirement),
                    FieldKey = field.Key,
                    OptionKey = option.Key,
                    Level = input.Level,
                    MinimumDaysInGrade = input.MinimumDaysInGrade,
                    MinimumAttendance = input.MinimumAttendance,
                    Notes = notes,
                    CurriculumId = curriculumId,
                    CreatedUtc = now,
                    UpdatedUtc = now
                });
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Services.Database.Ranks.ReplaceRequirementsAsync(field.Key, requirements, token).ConfigureAwait(false);
            for (int i = 0; i < requirements.Count; i++)
            {
                RankRequirementInput input = inputs[i];
                if (input.Labels != null || input.Tags != null)
                {
                    ObjectMetadata metadata = await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.RankRequirement, requirements[i].Id, input.Labels, input.Tags, token).ConfigureAwait(false);
                    requirements[i].Labels = metadata.Labels;
                    requirements[i].Tags = metadata.Tags;
                }
            }
            await _Services.Audit.WriteAsync(info, "rank.requirements", "ProfileField", field.Id, "Replaced " + requirements.Count + " requirements of " + field.Key, token).ConfigureAwait(false);
            return new RankRequirementsResponse { FieldKey = field.Key, Updated = requirements.Count, Requirements = requirements };
        }

        /// <summary>
        /// Refuse a member's own edit of a Rank field while a verified rank stands for it (409 RankVerified). Unchanged
        /// values pass, so clients may send the whole profile. Does nothing while ranks.enabled is off.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="writes">Profile writes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">409 RankVerified.</exception>
        public async Task RequireEditableAsync(string userId, List<ProfileFieldWrite> writes, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(writes);
            if (!_Module.Enabled || String.IsNullOrEmpty(userId) || writes.Count == 0) return;
            List<ProfileField> fields = await _Module.RankFieldsAsync(token).ConfigureAwait(false);
            List<ProfileFieldWrite> rankWrites = writes.Where(w => fields.Any(f => f.Id == w.FieldId)).ToList();
            if (rankWrites.Count == 0) return;
            List<MemberRank> ranks = await _Services.Database.Ranks.ReadMemberRanksAsync(userId, token).ConfigureAwait(false);
            foreach (ProfileFieldWrite write in rankWrites)
            {
                ProfileField field = fields.First(f => f.Id == write.FieldId);
                MemberRank? rank = ranks.FirstOrDefault(r => r.Status == RankStatusEnum.Verified && String.Equals(r.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase));
                if (rank == null) continue;
                bool same = write.Value != null && String.Equals(write.Value.ValueText, rank.OptionKey, StringComparison.OrdinalIgnoreCase) && (write.Value.ValueLevel ?? 0) == rank.Level;
                if (!same)
                    throw new ApiErrorException(409, "RankVerified", "Your " + field.Label + " was verified by an instructor and can only be changed by a new award.",
                        new Dictionary<string, string> { { "profile." + field.Key, "Verified ranks can only be changed by a new award." } });
            }
        }

        /// <summary>
        /// The next rank of a member and their requirement progress.
        /// </summary>
        /// <param name="field">Rank field.</param>
        /// <param name="userId">Member.</param>
        /// <param name="rank">The member's rank row, or null.</param>
        /// <param name="current">Effective rank.</param>
        /// <param name="requirements">The field's requirements.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Next rank, or null without a value or at the highest rank.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<RankNextView?> NextAsync(ProfileField field, string userId, MemberRank? rank, EffectiveRankView current, List<RankRequirement> requirements, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(requirements);
            if (current.OptionKey == null) return null;
            ProfileFieldOption? next = RankAwardRules.Next(field, current.OptionKey, current.Level, out int nextLevel);
            if (next == null) return null;
            RankRequirement? requirement = requirements.FirstOrDefault(r => String.Equals(r.OptionKey, next.Key, StringComparison.OrdinalIgnoreCase) && r.Level == nextLevel)
                ?? requirements.FirstOrDefault(r => String.Equals(r.OptionKey, next.Key, StringComparison.OrdinalIgnoreCase) && r.Level == null);
            DateTime? since = rank != null && rank.Status == RankStatusEnum.Verified ? rank.AwardedUtc : null;
            RankNextView view = new RankNextView
            {
                OptionKey = next.Key,
                OptionLabel = next.Label,
                Level = nextLevel,
                RankLabel = RankAwardRules.Label(field, next.Key, nextLevel),
                Requirement = requirement,
                DaysInGrade = since == null ? null : (int)Math.Floor((DateTime.UtcNow - since.Value).TotalDays),
                Attendance = await _Services.Database.Attendance.CountForUserAsync(userId, since, token).ConfigureAwait(false)
            };
            if (requirement == null) return view;
            view.DaysInGradeMet = requirement.MinimumDaysInGrade == 0 || (view.DaysInGrade != null && view.DaysInGrade >= requirement.MinimumDaysInGrade);
            view.AttendanceMet = view.Attendance >= requirement.MinimumAttendance;
            if (!String.IsNullOrEmpty(requirement.CurriculumId))
            {
                view.Curriculum = await _Module.Curricula.SummaryAsync(requirement.CurriculumId, userId, next.Key, token).ConfigureAwait(false);
                view.CurriculumMet = view.Curriculum == null || view.Curriculum.CompletedRequired >= view.Curriculum.Required;
            }
            view.Eligible = view.DaysInGradeMet && view.AttendanceMet && view.CurriculumMet;
            return view;
        }

        #endregion

        #region Private-Methods

        private static void RequireAwarder(ContentActor actor)
        {
            if (!actor.IsRankStaff) throw ApiErrorException.Forbidden("Only rank awarders (RankAwarder or SystemAdmin) can do this.");
        }

        private async Task SetProfileValueAsync(User user, ProfileField field, string optionKey, int level, string? actorId, CancellationToken token)
        {
            ProfileFieldWrite write = new ProfileFieldWrite
            {
                FieldId = field.Id,
                Value = new ProfileValue { UserId = user.Id, FieldId = field.Id, ValueText = optionKey, ValueLevel = level }
            };
            if (await _Services.ProfileValues.WriteAsync(user, new List<ProfileFieldWrite> { write }, actorId ?? user.Id, token).ConfigureAwait(false))
            {
                user.UpdatedUtc = DateTime.UtcNow;
                await _Services.Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            }
        }

        private async Task NotifyPromotionAsync(User user, ContentActor actor, string rankId, string label, string? note, CancellationToken token)
        {
            string title = "You were promoted to " + label;
            await _Services.Notifications.CreateAsync(new NotificationDraft
            {
                RecipientUserId = user.Id,
                Type = NotificationTypeEnum.RankPromoted,
                ActorUserId = actor.UserId,
                TargetType = TargetTypeEnum.MemberRank,
                TargetId = rankId,
                TitleText = title,
                Body = note,
                Path = "/profile"
            }, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(user.Email)) return;
            string awarderName = actor.User?.DisplayName ?? _Services.Property.Property.Name;
            EmailRequest email = new EmailRequest
            {
                Template = RankPromotedEmail.TemplateKey,
                To = user.Email,
                UserId = user.Id,
                Essential = true,
                IdempotencyKey = "rank_promoted:" + rankId + ":" + DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            email.Tokens["memberName"] = user.DisplayName;
            email.Tokens["rank"] = label;
            email.Tokens["awarderName"] = awarderName;
            email.Tokens["note"] = note ?? String.Empty;
            email.Tokens["profileLink"] = _Services.Email.MemberLink("/profile");
            await _Services.Email.QueueAsync(email, token).ConfigureAwait(false);
        }

        private async Task<List<RankPromotionView>> PromotionViewsAsync(List<RankPromotion> rows, List<ProfileField> fields, bool staff, CancellationToken token)
        {
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(rows.Select(r => r.UserId).Concat(rows.Select(r => r.ActorId)), token).ConfigureAwait(false);
            List<RankPromotionView> views = rows.Select(r =>
            {
                ProfileField? field = fields.FirstOrDefault(f => String.Equals(f.Key, r.FieldKey, StringComparison.OrdinalIgnoreCase));
                int index = field == null ? -1 : RankAwardRules.IndexOf(field, r.OptionKey);
                return new RankPromotionView
                {
                    Id = r.Id,
                    UserId = r.UserId,
                    User = summaries.TryGetValue(r.UserId, out UserSummary? user) ? user : null,
                    FieldKey = r.FieldKey,
                    OptionKey = r.OptionKey,
                    OptionLabel = index < 0 ? r.OptionKey : field!.Options[index].Label,
                    Level = r.Level,
                    RankLabel = field == null ? r.OptionKey : RankAwardRules.Label(field, r.OptionKey, r.Level),
                    Action = r.Action,
                    ActorId = r.ActorId,
                    Actor = r.ActorId != null && summaries.TryGetValue(r.ActorId, out UserSummary? actor) ? actor : null,
                    Note = r.Note,
                    EventId = r.EventId,
                    Reason = r.Reason,
                    AwardId = r.AwardId,
                    RevokedUtc = r.RevokedUtc,
                    CreatedUtc = r.CreatedUtc
                };
            }).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.RankPromotion, views, v => v.Id, staff, token).ConfigureAwait(false);
            return views;
        }

        #endregion
    }
}
