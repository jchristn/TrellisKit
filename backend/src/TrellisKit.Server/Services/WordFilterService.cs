namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// The word filter (contract section 9.4): the admin-maintained term list, an in-memory compiled term cache, and
    /// the checks content services run on write. Terms are refreshed on every change made through this instance and
    /// at least every <see cref="CacheTtl"/> (so other instances pick up changes). SystemAdmin authors are never
    /// filtered, and moderation.wordFilterEnabled = false turns every check off without deleting the list.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class WordFilterService
    {
        #region Public-Members

        /// <summary>
        /// Field error text for a Block match. The term is never echoed.
        /// </summary>
        public const string BlockedFieldMessage = "This contains a word that is not allowed here.";

        /// <summary>
        /// Longest term in characters.
        /// </summary>
        public const int MaxTermLength = 100;

        /// <summary>
        /// Longest note in characters.
        /// </summary>
        public const int MaxNoteLength = 500;

        /// <summary>
        /// Most terms per import.
        /// </summary>
        public const int MaxImport = 1000;

        /// <summary>
        /// Longest text accepted by the admin test route.
        /// </summary>
        public const int MaxTestLength = 10000;

        /// <summary>
        /// How long the compiled term cache is trusted before it is reloaded. Default 30 seconds, minimum 1 second,
        /// maximum 1 hour.
        /// </summary>
        public TimeSpan CacheTtl
        {
            get { return _CacheTtl; }
            set { _CacheTtl = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : value; }
        }

        /// <summary>
        /// In-app and push notifications for administrator alerts, or null (no alerts). Default null; the property
        /// services set it.
        /// </summary>
        public NotificationService? Notifications { get; set; } = null;

        /// <summary>
        /// Staff email for administrator alerts, or null (no email). Default null; the property services set it.
        /// </summary>
        public EmailService? Email { get; set; } = null;

        /// <summary>
        /// Logger for alert failures, or null. Default null.
        /// </summary>
        public AppLogger? Logger { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly AuditService _Audit;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private readonly SemaphoreSlim _Reload = new SemaphoreSlim(1, 1);
        private TimeSpan _CacheTtl = TimeSpan.FromSeconds(30);
        private List<WordFilterTerm> _Terms = new List<WordFilterTerm>();
        private DateTime _LoadedUtc = DateTime.MinValue;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Running settings (read live).</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public WordFilterService(DatabaseDriverBase database, AppSettings settings, AuditService audit, EnumerationService enumeration, MetadataService metadata)
        {
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Drop the compiled cache so the next check reloads the terms.
        /// </summary>
        public void Invalidate()
        {
            _LoadedUtc = DateTime.MinValue;
        }

        /// <summary>
        /// Check named text fields of a write. Field names are the request's field paths: title, body, altText,
        /// mediaIds[i].altText, labels[i], tags.key. Null or empty values are skipped. On any Block match a hit is
        /// recorded (target id null) for every Block match and 400 ContentFiltered is thrown with fieldErrors for every
        /// blocked field; otherwise the Flag matches are returned for <see cref="RecordFlagsAsync(WordFilterResult?, TargetTypeEnum, string, string?, CancellationToken)"/> after the save.
        /// SystemAdmin authors (and the admin token) and a disabled filter return an empty result.
        /// </summary>
        /// <param name="author">Author of the write.</param>
        /// <param name="targetType">Kind of content written.</param>
        /// <param name="fields">Field name to text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Flag matches (possibly none).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ContentFiltered.</exception>
        public async Task<WordFilterResult> CheckAsync(ContentActor author, TargetTypeEnum targetType, IReadOnlyDictionary<string, string?> fields, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(author);
            ArgumentNullException.ThrowIfNull(fields);
            WordFilterResult result = new WordFilterResult();
            if (author.IsSystemAdmin || !_Settings.Moderation.WordFilterEnabled) return result;
            List<WordFilterTerm> terms = await TermsAsync(token).ConfigureAwait(false);
            if (terms.Count == 0) return result;

            using (Activity? activity = TrellisKitTelemetry.StartSpan("word_filter.check"))
            {
                List<WordFilterFieldMatch> blocks = new List<WordFilterFieldMatch>();
                foreach (KeyValuePair<string, string?> field in fields)
                {
                    if (String.IsNullOrEmpty(field.Value) || String.IsNullOrEmpty(field.Key)) continue;
                    HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (WordFilterMatchView match in Match(field.Value, terms))
                    {
                        if (!seen.Add(match.TermId)) continue;
                        WordFilterFieldMatch item = new WordFilterFieldMatch { Field = field.Key, TermId = match.TermId, Term = match.Term, Action = match.Action };
                        if (match.Action == WordFilterActionEnum.Block) blocks.Add(item);
                        else
                        {
                            result.Flags.Add(item);
                            if (result.Excerpt == null) result.Excerpt = field.Value;
                        }
                    }
                }
                activity?.SetTag("word_filter.blocks", blocks.Count);
                activity?.SetTag("word_filter.flags", result.Flags.Count);
                if (blocks.Count == 0) return result;

                DateTime now = DateTime.UtcNow;
                await _Database.WordFilter.CreateHitsAsync(blocks.Select(b => Hit(b, targetType, null, now)).ToList(), token).ConfigureAwait(false);
                foreach (WordFilterFieldMatch block in blocks) TrellisKitTelemetry.Add(TrellisKitTelemetry.WordFilterMatches, 1, "action", "block", "field", FieldLabel(block.Field));
                Dictionary<string, string> errors = new Dictionary<string, string>();
                foreach (WordFilterFieldMatch block in blocks) errors[block.Field] = BlockedFieldMessage;
                throw new ApiErrorException(400, "ContentFiltered", "Some of the text contains a word that is not allowed here.", errors);
            }
        }

        /// <summary>
        /// Check member-supplied labels and tag values (docs/ENUMERATION_AND_METADATA.md) with field names labels[i]
        /// and tags.key. Same behavior as <see cref="CheckAsync"/>.
        /// </summary>
        /// <param name="author">Author of the write.</param>
        /// <param name="targetType">Kind of object.</param>
        /// <param name="labels">Labels, or null.</param>
        /// <param name="tags">Tags, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Flag matches (possibly none).</returns>
        /// <exception cref="ArgumentNullException">The author is null.</exception>
        /// <exception cref="ApiErrorException">400 ContentFiltered.</exception>
        public Task<WordFilterResult> CheckMetadataAsync(ContentActor author, TargetTypeEnum targetType, IEnumerable<string?>? labels, IReadOnlyDictionary<string, string?>? tags, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(author);
            Dictionary<string, string?> fields = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (labels != null)
            {
                int i = 0;
                foreach (string? label in labels) fields["labels[" + i++ + "]"] = label;
            }
            if (tags != null)
            {
                foreach (KeyValuePair<string, string?> tag in tags) fields["tags." + tag.Key] = tag.Value;
            }
            return CheckAsync(author, targetType, fields, token);
        }

        /// <summary>
        /// The <see cref="MetadataService.TextFilter"/> hook: checks member-written labels and tag values (fields such as
        /// labels[0] and tags.level) and throws 400 ContentFiltered on a Block match. Block hits are recorded with the
        /// object type and a null target id; Flag matches on metadata are not reported because the hook has no saved target.
        /// </summary>
        /// <param name="actor">Author.</param>
        /// <param name="targetType">Object type.</param>
        /// <param name="fields">Field name and text pairs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ContentFiltered.</exception>
        public async Task MetadataTextFilterAsync(ContentActor actor, TargetTypeEnum targetType, List<KeyValuePair<string, string>> fields, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(fields);
            Dictionary<string, string?> map = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in fields) map[pair.Key] = pair.Value;
            await CheckAsync(actor, targetType, map, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record the Flag matches of a saved write: one hit per match with the target id, and one system report
        /// (no reporter, reason Filter, scope Feed "*", due after moderation.reviewTargetHours) per target, skipped
        /// while an Open system report exists for it. Flags do not count toward auto-hide.
        /// </summary>
        /// <param name="result">Result of <see cref="CheckAsync"/>, or null (nothing to record).</param>
        /// <param name="targetType">Kind of content saved.</param>
        /// <param name="targetId">Saved content.</param>
        /// <param name="targetAuthorUserId">Author, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a new system report was filed.</returns>
        /// <exception cref="ArgumentNullException">The target id is null or empty.</exception>
        public Task<bool> RecordFlagsAsync(WordFilterResult? result, TargetTypeEnum targetType, string targetId, string? targetAuthorUserId, CancellationToken token = default)
        {
            return RecordFlagsAsync(result, targetType, targetId, targetAuthorUserId, "Feed", "*", token);
        }

        /// <summary>
        /// Record the Flag matches of a saved write as <see cref="RecordFlagsAsync(WordFilterResult?, TargetTypeEnum, string, string?, CancellationToken)"/>,
        /// filing the system report in a given moderation scope (scope Assistant with the assistant id for assistant
        /// messages, so only SystemAdmin reviews them).
        /// </summary>
        /// <param name="result">Result of <see cref="CheckAsync"/>, or null (nothing to record).</param>
        /// <param name="targetType">Kind of content saved.</param>
        /// <param name="targetId">Saved content.</param>
        /// <param name="targetAuthorUserId">Author, or null.</param>
        /// <param name="scopeType">Report scope type.</param>
        /// <param name="scopeId">Report scope identifier, or "*".</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a new system report was filed.</returns>
        /// <exception cref="ArgumentNullException">The target id is null or empty.</exception>
        public async Task<bool> RecordFlagsAsync(WordFilterResult? result, TargetTypeEnum targetType, string targetId, string? targetAuthorUserId, string scopeType, string scopeId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetId)) throw new ArgumentNullException(nameof(targetId));
            if (result == null || !result.HasFlags) return false;
            DateTime now = DateTime.UtcNow;
            await _Database.WordFilter.CreateHitsAsync(result.Flags.Select(f => Hit(f, targetType, targetId, now)).ToList(), token).ConfigureAwait(false);
            foreach (WordFilterFieldMatch flag in result.Flags) TrellisKitTelemetry.Add(TrellisKitTelemetry.WordFilterMatches, 1, "action", "flag", "field", FieldLabel(flag.Field));
            string details = "Word filter: " + String.Join("; ", result.Flags.GroupBy(f => f.Field).Select(g => g.Key + " (" + String.Join(", ", g.Select(f => f.Term).Distinct()) + ")"));
            DateTime due = now.AddHours(_Settings.Moderation.ReviewTargetHours);
            string reportId = IdGenerator.Generate(Constants.ReportIdPrefix);
            bool created = await _Database.WordFilter.CreateSystemReportAsync(reportId, targetType, targetId, targetAuthorUserId, details, scopeType, scopeId, now, due, token).ConfigureAwait(false);
            if (created)
            {
                TrellisKitTelemetry.Add(TrellisKitTelemetry.Reports, 1, "target_type", targetType.ToString(), "reason", ReportReasonEnum.Filter.ToString());
                await AlertAdministratorsAsync(result, targetType, targetId, reportId, now, token).ConfigureAwait(false);
            }
            return created;
        }

        /// <summary>
        /// The moderation queue address that opens one target's details, for example
        /// /admin/moderation?target=Comment%3Acmt_x.
        /// </summary>
        /// <param name="targetType">Kind of content.</param>
        /// <param name="targetId">Content identifier.</param>
        /// <returns>Path and query.</returns>
        /// <exception cref="ArgumentNullException">The target id is null or empty.</exception>
        public static string ModerationPath(TargetTypeEnum targetType, string targetId)
        {
            if (String.IsNullOrEmpty(targetId)) throw new ArgumentNullException(nameof(targetId));
            return "/admin/moderation?target=" + Uri.EscapeDataString(targetType.ToString() + ":" + targetId);
        }

        /// <summary>
        /// Add the engine's starter list of common English profanity and slurs (<see cref="WordFilterStarterList"/>)
        /// with action Flag, so matching contributions are saved, filed in the moderation queue, and alerted to
        /// administrators. Terms already on the list are skipped; administrators can change any term to Block or
        /// delete it afterwards.
        /// </summary>
        /// <param name="info">Request information for the audit entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Added and skipped counts.</returns>
        /// <exception cref="ArgumentNullException">info is null.</exception>
        public async Task<WordFilterImportResponse> ImportStarterAsync(RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            WordFilterImportResponse total = new WordFilterImportResponse();
            foreach (WordFilterMatchTypeEnum matchType in new[] { WordFilterMatchTypeEnum.Word, WordFilterMatchTypeEnum.Substring })
            {
                List<string?> terms = WordFilterStarterList.Terms(matchType).Cast<string?>().ToList();
                if (terms.Count == 0) continue;
                WordFilterImportResponse part = await ImportAsync(new WordFilterImportRequest { Terms = terms, MatchType = matchType, Action = WordFilterActionEnum.Flag }, info, token).ConfigureAwait(false);
                total.Added += part.Added;
                total.Skipped += part.Skipped;
            }
            return total;
        }

        /// <summary>
        /// Whether text contains any Block term (used for link preview titles and descriptions). Records nothing.
        /// Returns false when the filter is disabled.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when blocked.</returns>
        public async Task<bool> IsBlockedAsync(string? text, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(text) || !_Settings.Moderation.WordFilterEnabled) return false;
            List<WordFilterTerm> terms = await TermsAsync(token).ConfigureAwait(false);
            return Match(text, terms.Where(t => t.Action == WordFilterActionEnum.Block).ToList()).Count > 0;
        }

        /// <summary>
        /// Every match of the given terms in a text, ordered by start, with positions in the original text.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <param name="terms">Terms with normalized forms.</param>
        /// <returns>Matches.</returns>
        /// <exception cref="ArgumentNullException">The terms are null.</exception>
        public static List<WordFilterMatchView> Match(string? text, IReadOnlyList<WordFilterTerm> terms)
        {
            ArgumentNullException.ThrowIfNull(terms);
            List<WordFilterMatchView> matches = new List<WordFilterMatchView>();
            if (String.IsNullOrEmpty(text) || terms.Count == 0) return matches;
            WordFilterText normalized = WordFilterNormalizer.Normalize(text);
            string n = normalized.Text;
            foreach (WordFilterTerm term in terms)
            {
                string needle = term.NormalizedTerm;
                if (String.IsNullOrEmpty(needle)) continue;
                int from = 0;
                while (from <= n.Length - needle.Length)
                {
                    int index = n.IndexOf(needle, from, StringComparison.Ordinal);
                    if (index < 0) break;
                    int end = index + needle.Length;
                    bool ok = term.MatchType == WordFilterMatchTypeEnum.Substring ||
                        (!WordFilterNormalizer.IsWordChar(n, index - 1) && !WordFilterNormalizer.IsWordChar(n, end));
                    if (ok)
                    {
                        int start = normalized.OriginalStarts[index];
                        int stop = normalized.OriginalEnds[end - 1];
                        matches.Add(new WordFilterMatchView { TermId = term.Id, Term = term.Term, Action = term.Action, Start = start, Length = stop - start });
                    }
                    from = index + 1;
                }
            }
            return matches.OrderBy(m => m.Start).ThenBy(m => m.Length).ToList();
        }

        /// <summary>
        /// Admin list (collection SafetyCollections.WordFilterTerms: orderings CreatedDescending, CreatedAscending,
        /// TermAscending, UpdatedDescending, HitsDescending; search over term and note; filters action and matchType).
        /// </summary>
        /// <param name="query">Enumeration query, or null for defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with labels and tags.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 400 InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<WordFilterTermView>> ListAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, SafetyCollections.WordFilterTerms, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            WordFilterFilter filter = new WordFilterFilter
            {
                Action = request.Query.FilterEnum<WordFilterActionEnum>("action", errors),
                MatchType = request.Query.FilterEnum<WordFilterMatchTypeEnum>("matchType", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<WordFilterTermView> page = await _Database.WordFilter.EnumerateAsync(request, filter, DateTime.UtcNow.AddDays(-30), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.WordFilterTerm, page.Objects.Select(o => o.Id), true, token).ConfigureAwait(false);
            foreach (WordFilterTermView view in page.Objects)
            {
                if (!metadata.TryGetValue(view.Id, out ObjectMetadata? m)) continue;
                view.Labels = m.Labels;
                view.Tags = m.Tags;
            }
            return page;
        }

        /// <summary>
        /// Create a term. Audited as moderation.wordFilter.create.
        /// </summary>
        /// <param name="actor">Caller (an administrator).</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The term.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 409 Conflict.</exception>
        public async Task<WordFilterTermView> CreateAsync(ContentActor actor, WordFilterTermRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(info);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? term = ValidateTerm(request.Term, errors);
            string? note = ValidateNote(request.Note, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            WordFilterTerm created = new WordFilterTerm
            {
                Id = IdGenerator.Generate(Constants.WordFilterTermIdPrefix),
                Term = term!,
                NormalizedTerm = WordFilterNormalizer.NormalizeTerm(term),
                MatchType = request.MatchType ?? WordFilterMatchTypeEnum.Word,
                Action = request.Action ?? WordFilterActionEnum.Block,
                Note = note,
                CreatedByUserId = info.Context?.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                await _Database.WordFilter.CreateAsync(created, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw Conflict();
            }
            if (request.Labels != null || request.Tags != null) await _Metadata.ApplyAsync(actor, TargetTypeEnum.WordFilterTerm, created.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            Invalidate();
            await _Audit.WriteAsync(info, "moderation.wordFilter.create", "WordFilterTerm", created.Id, created.MatchType + " " + created.Action, token).ConfigureAwait(false);
            return await ViewAsync(created, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update any field of a term (fields absent from the body are unchanged; a present null note clears it).
        /// Audited as moderation.wordFilter.update.
        /// </summary>
        /// <param name="actor">Caller (an administrator).</param>
        /// <param name="id">Term identifier.</param>
        /// <param name="request">Body (labels and tags replace when present).</param>
        /// <param name="present">camelCase keys present in the body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The term.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 409 Conflict.</exception>
        public async Task<WordFilterTermView> UpdateAsync(ContentActor actor, string id, WordFilterTermRequest request, HashSet<string> present, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            ArgumentNullException.ThrowIfNull(info);
            WordFilterTerm term = await _Database.WordFilter.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Word filter term");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("term"))
            {
                string? value = ValidateTerm(request.Term, errors);
                if (value != null)
                {
                    term.Term = value;
                    term.NormalizedTerm = WordFilterNormalizer.NormalizeTerm(value);
                }
            }
            if (present.Contains("note")) term.Note = ValidateNote(request.Note, errors);
            if (request.MatchType != null) term.MatchType = request.MatchType.Value;
            if (request.Action != null) term.Action = request.Action.Value;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token).ConfigureAwait(false);
            term.UpdatedUtc = DateTime.UtcNow;
            try
            {
                await _Database.WordFilter.UpdateAsync(term, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw Conflict();
            }
            if (request.Labels != null || request.Tags != null) await _Metadata.ApplyAsync(actor, TargetTypeEnum.WordFilterTerm, term.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            Invalidate();
            await _Audit.WriteAsync(info, "moderation.wordFilter.update", "WordFilterTerm", term.Id, "Changed " + String.Join(", ", present.OrderBy(p => p, StringComparer.Ordinal)), token).ConfigureAwait(false);
            return await ViewAsync(term, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a term. Audited as moderation.wordFilter.delete.
        /// </summary>
        /// <param name="id">Term identifier.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The request information is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task DeleteAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            WordFilterTerm term = await _Database.WordFilter.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Word filter term");
            if (!await _Database.WordFilter.DeleteAsync(term.Id, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Word filter term");
            Invalidate();
            await _Audit.WriteAsync(info, "moderation.wordFilter.delete", "WordFilterTerm", term.Id, term.MatchType + " " + term.Action, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Import up to 1000 terms with one match type and action, skipping duplicates (existing or repeated) and
        /// invalid entries. Audited as moderation.wordFilter.import.
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Added and skipped counts.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors.terms) for an empty list or more than 1000 terms.</exception>
        public async Task<WordFilterImportResponse> ImportAsync(WordFilterImportRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(info);
            List<string?> raw = request.Terms ?? new List<string?>();
            if (raw.Count == 0) throw ApiErrorException.Field("terms", "Provide 1 to " + MaxImport + " terms.");
            if (raw.Count > MaxImport) throw ApiErrorException.Field("terms", "Import at most " + MaxImport + " terms at a time.");
            DateTime now = DateTime.UtcNow;
            List<WordFilterTerm> terms = new List<WordFilterTerm>();
            foreach (string? entry in raw)
            {
                string? value = entry?.Trim();
                if (String.IsNullOrEmpty(value) || value.Length > MaxTermLength) continue;
                string normalized = WordFilterNormalizer.NormalizeTerm(value);
                if (normalized.Length == 0) continue;
                terms.Add(new WordFilterTerm
                {
                    Id = IdGenerator.Generate(Constants.WordFilterTermIdPrefix),
                    Term = value,
                    NormalizedTerm = normalized,
                    MatchType = request.MatchType ?? WordFilterMatchTypeEnum.Word,
                    Action = request.Action ?? WordFilterActionEnum.Block,
                    CreatedByUserId = info.Context?.UserId,
                    CreatedUtc = now,
                    UpdatedUtc = now
                });
            }
            int added = await _Database.WordFilter.ImportAsync(terms, token).ConfigureAwait(false);
            WordFilterImportResponse response = new WordFilterImportResponse { Added = added, Skipped = raw.Count - added };
            Invalidate();
            await _Audit.WriteAsync(info, "moderation.wordFilter.import", "WordFilterTerm", null, "Added " + response.Added + ", skipped " + response.Skipped, token).ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// Test a text against the current list (runs even when the filter is switched off, so admins can check a list).
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matches with positions in the original text.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors.text).</exception>
        public async Task<WordFilterTestResponse> TestAsync(WordFilterTestRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Text == null) throw ApiErrorException.Field("text", "Provide the text to test.");
            if (request.Text.Length > MaxTestLength) throw ApiErrorException.Field("text", "Use at most " + MaxTestLength + " characters.");
            List<WordFilterTerm> terms = await TermsAsync(token).ConfigureAwait(false);
            return new WordFilterTestResponse { Matches = Match(request.Text, terms) };
        }

        #endregion

        #region Private-Methods

        private async Task AlertAdministratorsAsync(WordFilterResult result, TargetTypeEnum targetType, string targetId, string reportId, DateTime now, CancellationToken token)
        {
            string path = ModerationPath(targetType, targetId);
            string noun = TrellisKit.Server.Services.Email.EmailLabels.TargetType(targetType);
            string terms = String.Join(", ", result.Flags.Select(f => f.Term).Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
            try
            {
                if (Notifications != null)
                {
                    List<RoleAssignment> admins = await _Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.SystemAdmin, _Settings.ContentReview.MaxNotifiedAdmins, token).ConfigureAwait(false);
                    List<string> recipients = admins.Select(a => a.UserId).Distinct(StringComparer.Ordinal).ToList();
                    if (recipients.Count > 0)
                    {
                        NotificationDraft draft = new NotificationDraft
                        {
                            Type = NotificationTypeEnum.ContentFlagged,
                            TargetType = targetType,
                            TargetId = targetId,
                            SubjectNoun = noun,
                            TitleText = "Word filter flagged a " + noun + ": " + terms,
                            Body = result.Excerpt,
                            Path = path,
                            EventUtc = now,
                            QueuePush = true
                        };
                        await Notifications.CreateForRecipientsAsync(draft, recipients, token).ConfigureAwait(false);
                    }
                }
                if (Email != null)
                {
                    await Email.NotifyStaffAsync("staff_new_report", "report:" + reportId, token,
                        ("targetType", noun),
                        ("reason", TrellisKit.Server.Services.Email.EmailLabels.ReportReason(ReportReasonEnum.Filter) + " (" + terms + ")"),
                        ("link", Email.Link(path))).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The contribution is saved and the report is filed; a failed alert must not fail the write.
                Logger?.Warn("word filter alert for " + targetType + " " + targetId + " failed: " + e.Message);
            }
        }

        private async Task<List<WordFilterTerm>> TermsAsync(CancellationToken token)
        {
            if (DateTime.UtcNow - _LoadedUtc < _CacheTtl) return _Terms;
            await _Reload.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (DateTime.UtcNow - _LoadedUtc < _CacheTtl) return _Terms;
                List<WordFilterTerm> terms = await _Database.WordFilter.EnumerateAllAsync(token).ConfigureAwait(false);
                _Terms = terms;
                _LoadedUtc = DateTime.UtcNow;
                return terms;
            }
            finally
            {
                _Reload.Release();
            }
        }

        private async Task<WordFilterTermView> ViewAsync(WordFilterTerm term, CancellationToken token)
        {
            string? name = "admin token";
            if (term.CreatedByUserId != null)
            {
                User? user = await _Database.Users.ReadAsync(term.CreatedByUserId, token).ConfigureAwait(false);
                name = user == null || user.IsDeleted ? "Deleted member" : user.DisplayName;
            }
            long hits = await _Database.WordFilter.CountHitsAsync(term.Id, DateTime.UtcNow.AddDays(-30), token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.WordFilterTerm, term.Id, true, token).ConfigureAwait(false);
            return new WordFilterTermView
            {
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                Id = term.Id,
                Term = term.Term,
                MatchType = term.MatchType,
                Action = term.Action,
                Note = term.Note,
                CreatedByName = name,
                CreatedUtc = term.CreatedUtc,
                UpdatedUtc = term.UpdatedUtc,
                HitCount30d = hits
            };
        }

        private static string? ValidateTerm(string? value, Dictionary<string, string> errors)
        {
            string? term = value?.Trim();
            if (String.IsNullOrEmpty(term)) errors["term"] = "Enter a term.";
            else if (term.Length > MaxTermLength) errors["term"] = "Use at most " + MaxTermLength + " characters.";
            else if (WordFilterNormalizer.NormalizeTerm(term).Length == 0) errors["term"] = "The term has no matchable characters.";
            else return term;
            return null;
        }

        private static string? ValidateNote(string? value, Dictionary<string, string> errors)
        {
            string? note = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (note != null && note.Length > MaxNoteLength)
            {
                errors["note"] = "Use at most " + MaxNoteLength + " characters.";
                return null;
            }
            return note;
        }

        private static WordFilterHit Hit(WordFilterFieldMatch match, TargetTypeEnum targetType, string? targetId, DateTime now)
        {
            return new WordFilterHit
            {
                Id = IdGenerator.Generate(Constants.WordFilterHitIdPrefix),
                TermId = match.TermId,
                TargetType = targetType,
                TargetId = targetId,
                Field = match.Field.Length > 200 ? match.Field.Substring(0, 200) : match.Field,
                Action = match.Action,
                CreatedUtc = now
            };
        }

        private static string FieldLabel(string field)
        {
            if (field.StartsWith("tags.", StringComparison.Ordinal)) return "tags";
            string name = field;
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(dot + 1);
            int bracket = name.IndexOf('[');
            if (bracket >= 0) name = name.Substring(0, bracket);
            return name.Length == 0 ? "other" : name;
        }

        private static ApiErrorException Conflict()
        {
            return new ApiErrorException(409, "Conflict", "A term with the same normalized form already exists.");
        }

        #endregion
    }
}
