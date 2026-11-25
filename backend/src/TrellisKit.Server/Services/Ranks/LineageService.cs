namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Self-reported rank lineage (docs/TRELLISKIT.md, Ranks, curricula, and attendance): members list who promoted them
    /// to each option; a promoter who is a member gets a verification request and can verify the entry. Thread safety:
    /// safe for concurrent use.
    /// </summary>
    public class LineageService
    {
        #region Public-Members

        /// <summary>
        /// Most lineage entries per member and Rank field.
        /// </summary>
        public const int MaxEntries = 50;

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
        public LineageService(ApplicationServices services, RankModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// PUT /api/v1/me/lineage: replace the caller's lineage of one Rank field. Unchanged entries keep their
        /// verification; new entries naming a member promoter send them a verification request. Audited lineage.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored lineage.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 403 for the admin token, 404 NotAvailable.</exception>
        public async Task<LineageResponse> PutAsync(ContentActor actor, RequestInfo info, LineageRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Module.RequireEnabled();
            string userId = actor.RequireUserId();
            actor.RequireNotMuted();
            ProfileField field = await _Module.FieldAsync(request.FieldKey, "fieldKey", token).ConfigureAwait(false);
            List<LineageEntryInput> inputs = request.Entries ?? new List<LineageEntryInput>();
            if (inputs.Count > MaxEntries) throw ApiErrorException.Field("entries", "Use at most " + MaxEntries + " entries.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            Dictionary<string, string?> texts = new Dictionary<string, string?>(StringComparer.Ordinal);
            List<string> promoterIds = inputs.Where(i => i != null && !String.IsNullOrWhiteSpace(i.PromotedByUserId)).Select(i => i.PromotedByUserId!.Trim()).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, User> promoters = new Dictionary<string, User>(StringComparer.Ordinal);
            foreach (User u in await _Services.Database.Users.ReadManyAsync(promoterIds, token).ConfigureAwait(false))
            {
                if (!u.IsDeleted) promoters[u.Id] = u;
            }
            List<LineageEntry> existing = await _Services.Database.Ranks.ListLineageAsync(userId, field.Key, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            List<LineageEntry> entries = new List<LineageEntry>();
            List<LineageEntry> added = new List<LineageEntry>();
            for (int i = 0; i < inputs.Count; i++)
            {
                LineageEntryInput? input = inputs[i];
                string p = "entries[" + i + "].";
                if (input == null)
                {
                    errors["entries[" + i + "]"] = "Required.";
                    continue;
                }
                int index = RankAwardRules.IndexOf(field, input.OptionKey);
                if (index < 0) errors[p + "optionKey"] = "Use one of the field's options.";
                string? promoterId = String.IsNullOrWhiteSpace(input.PromotedByUserId) ? null : input.PromotedByUserId.Trim();
                string? name = String.IsNullOrWhiteSpace(input.PromotedByName) ? null : input.PromotedByName.Trim();
                if (promoterId != null && name != null) errors[p + "promotedByName"] = "Send promotedByUserId or promotedByName, not both.";
                if (promoterId != null && (!promoters.ContainsKey(promoterId) || promoterId == userId)) errors[p + "promotedByUserId"] = "Use another member of this community.";
                if (name != null && name.Length > 120) errors[p + "promotedByName"] = "Use at most 120 characters.";
                if (input.PromotedUtc != null && input.PromotedUtc.Value.ToUniversalTime() > now.AddDays(1)) errors[p + "promotedUtc"] = "Cannot be in the future.";
                if (index < 0 || errors.Keys.Any(k => k.StartsWith(p, StringComparison.Ordinal))) continue;
                if (name != null) texts[p + "promotedByName"] = name;
                string optionKey = field.Options[index].Key;
                DateTime? promoted = input.PromotedUtc?.ToUniversalTime();
                LineageEntry? match = existing.FirstOrDefault(e => !entries.Contains(e) && String.Equals(e.OptionKey, optionKey, StringComparison.OrdinalIgnoreCase) &&
                    e.PromotedByUserId == promoterId && String.Equals(e.PromotedByName, name, StringComparison.Ordinal) && SameDate(e.PromotedUtc, promoted));
                if (match != null)
                {
                    entries.Add(match);
                    continue;
                }
                LineageEntry entry = new LineageEntry
                {
                    Id = IdGenerator.Generate(RankIds.Lineage),
                    UserId = userId,
                    FieldKey = field.Key,
                    OptionKey = optionKey,
                    PromotedByUserId = promoterId,
                    PromotedByName = name,
                    PromotedUtc = promoted,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                entries.Add(entry);
                added.Add(entry);
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            WordFilterResult? flags = texts.Count == 0 ? null : await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.LineageEntry, texts, token).ConfigureAwait(false);
            await _Services.Database.Ranks.ReplaceLineageAsync(userId, field.Key, entries, token).ConfigureAwait(false);
            if (flags != null) await _Services.WordFilter.RecordFlagsAsync(flags, TargetTypeEnum.User, userId, userId, token).ConfigureAwait(false);
            foreach (LineageEntry entry in added.Where(e => e.PromotedByUserId != null))
            {
                await _Services.Notifications.CreateAsync(new NotificationDraft
                {
                    RecipientUserId = entry.PromotedByUserId!,
                    Type = NotificationTypeEnum.LineageVerificationRequested,
                    ActorUserId = userId,
                    TargetType = TargetTypeEnum.LineageEntry,
                    TargetId = entry.Id,
                    TitleText = (actor.User?.DisplayName ?? "A member") + " named you as their promoter to " + RankAwardRules.Label(field, entry.OptionKey, 0) + ". Verify it?",
                    Path = "/members/" + userId
                }, token).ConfigureAwait(false);
            }
            await _Services.Audit.WriteAsync(info, "lineage.update", "User", userId, "Lineage of " + field.Key + ": " + entries.Count + " entries", token).ConfigureAwait(false);
            List<LineageEntry> stored = await _Services.Database.Ranks.ListLineageAsync(userId, field.Key, token).ConfigureAwait(false);
            return new LineageResponse { FieldKey = field.Key, Entries = await ViewsAsync(stored, new List<ProfileField> { field }, true, token).ConfigureAwait(false) };
        }

        /// <summary>
        /// POST /api/v1/admin/lineage/{id}/verify: the named promoter or a SystemAdmin verifies a lineage entry. Audited
        /// lineage.verify.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotAvailable or NotFound.</exception>
        public async Task<LineageEntryView> VerifyAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            _Module.RequireEnabled();
            LineageEntry? entry = await _Services.Database.Ranks.ReadLineageAsync(id, token).ConfigureAwait(false);
            if (entry == null) throw ApiErrorException.NotFound("Lineage entry");
            bool promoter = !String.IsNullOrEmpty(actor.UserId) && actor.UserId == entry.PromotedByUserId;
            if (!actor.IsSystemAdmin && !promoter) throw ApiErrorException.Forbidden("Only the named promoter or a SystemAdmin can verify this entry.");
            if (!String.IsNullOrEmpty(actor.UserId) && actor.UserId == entry.UserId) throw ApiErrorException.Forbidden("You cannot verify your own lineage.");
            DateTime now = DateTime.UtcNow;
            await _Services.Database.Ranks.VerifyLineageAsync(entry.Id, actor.UserId, now, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "lineage.verify", "User", entry.UserId, "Verified lineage entry " + entry.Id, token).ConfigureAwait(false);
            entry.Verified = true;
            entry.VerifiedBy = actor.UserId;
            entry.VerifiedUtc = now;
            List<ProfileField> fields = await _Module.RankFieldsAsync(token).ConfigureAwait(false);
            List<LineageEntryView> views = await ViewsAsync(new List<LineageEntry> { entry }, fields, actor.IsStaffFor(TargetTypeEnum.LineageEntry), token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// Lineage views with promoter summaries, option labels, labels, and tags.
        /// </summary>
        /// <param name="entries">Entries.</param>
        /// <param name="fields">Rank fields (for option labels).</param>
        /// <param name="staff">Whether staff labels and tags are included.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in the given order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<LineageEntryView>> ViewsAsync(List<LineageEntry> entries, List<ProfileField> fields, bool staff, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(fields);
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(entries.Select(e => e.PromotedByUserId), token).ConfigureAwait(false);
            List<LineageEntryView> views = entries.Select(e =>
            {
                ProfileField? field = fields.FirstOrDefault(f => String.Equals(f.Key, e.FieldKey, StringComparison.OrdinalIgnoreCase));
                int index = field == null ? -1 : RankAwardRules.IndexOf(field, e.OptionKey);
                return new LineageEntryView
                {
                    Id = e.Id,
                    UserId = e.UserId,
                    FieldKey = e.FieldKey,
                    OptionKey = e.OptionKey,
                    OptionLabel = index < 0 ? e.OptionKey : field!.Options[index].Label,
                    PromotedByUserId = e.PromotedByUserId,
                    PromotedBy = e.PromotedByUserId != null && summaries.TryGetValue(e.PromotedByUserId, out UserSummary? summary) ? summary : null,
                    PromotedByName = e.PromotedByName,
                    PromotedUtc = e.PromotedUtc,
                    Verified = e.Verified,
                    VerifiedBy = e.VerifiedBy,
                    VerifiedUtc = e.VerifiedUtc,
                    Position = e.Position,
                    CreatedUtc = e.CreatedUtc
                };
            }).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.LineageEntry, views, v => v.Id, staff, token).ConfigureAwait(false);
            return views;
        }

        #endregion

        #region Private-Methods

        private static bool SameDate(DateTime? a, DateTime? b)
        {
            if (a == null || b == null) return a == null && b == null;
            return Math.Abs((a.Value.ToUniversalTime() - b.Value.ToUniversalTime()).TotalSeconds) < 1;
        }

        #endregion
    }
}
