namespace TrellisKit.Server.Services.Documents
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// One property's documents (docs/TRELLISKIT.md section 14): the documents property.json declares, versioned in
    /// propertydocumentversions (seeded from the property folder on the first load; in SingleProperty the folder wins when
    /// a document's version, title, or file changes on disk; administrators publish new versions in either mode), and
    /// members' acceptances in propertydocumentacceptances. Gating documents (RequiredAtSignup, AcceptedWithTerms) are
    /// required at sign-up and checked by the authorization stage through <see cref="Requirements"/>; a member who has not
    /// accepted the required version gets 403 DocumentAcceptanceRequired until they accept. Current versions are served from
    /// an in-memory snapshot refreshed on every property load and after each publish.
    /// Thread safety: <see cref="Snapshot"/> is replaced atomically; seeding and publishing are serialized per instance.
    /// </summary>
    public class PropertyDocumentService
    {
        #region Public-Members

        /// <summary>
        /// The documents of this property with a stored version, in property.json order. Never null.
        /// </summary>
        public IReadOnlyList<DocumentState> Snapshot
        {
            get { return Volatile.Read(ref _Snapshot); }
        }

        /// <summary>
        /// Whether the documents follow the property folder (set by <see cref="EnsureSeededAsync"/>).
        /// </summary>
        public bool SyncsFromFolder
        {
            get { return Volatile.Read(ref _SyncsFromFolder); }
        }

        /// <summary>
        /// Most characters of a change summary.
        /// </summary>
        public const int MaxChangeSummary = 500;

        /// <summary>
        /// Most characters of a display version.
        /// </summary>
        public const int MaxVersion = 32;

        /// <summary>
        /// Most characters of a title.
        /// </summary>
        public const int MaxTitle = 120;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly PropertyConfiguration _Property;
        private readonly AppSettings _Settings;
        private readonly AuditService _Audit;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private readonly UserSummaryService _Summaries;
        private readonly TokenService _Tokens;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private IReadOnlyList<DocumentState> _Snapshot = new List<DocumentState>();
        private bool _SyncsFromFolder = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service. Until <see cref="EnsureSeededAsync"/> runs, the snapshot is empty (no gate, no pages).
        /// </summary>
        /// <param name="database">Database driver bound to the property.</param>
        /// <param name="property">Property configuration.</param>
        /// <param name="settings">Running settings (property.maxDocumentCharacters, read live).</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <param name="metadata">Metadata service.</param>
        /// <param name="summaries">Member summaries.</param>
        /// <param name="tokens">Token service (session cache invalidation after an acceptance).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PropertyDocumentService(DatabaseDriverBase database, PropertyConfiguration property, AppSettings settings, AuditService audit, EnumerationService enumeration,
            MetadataService metadata, UserSummaryService summaries, TokenService tokens, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed every declared document that has no stored version from its file (version 1, source Seed), and when
        /// <paramref name="mergeOnChange"/> is set, store the folder's document as a new version (source DirectorySync,
        /// audited document.sync) when its version, title, or file changed on disk since it was last seeded or synced. A
        /// DirectorySync version requires re-acceptance only when the document has reacceptOnChange and its display version
        /// is new (no stored version of the document carries it). Documents whose file cannot be read are skipped and logged. Then reload the snapshot.
        /// </summary>
        /// <param name="mergeOnChange">Whether the property folder wins on change (SingleProperty folder property).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task EnsureSeededAsync(bool mergeOnChange, CancellationToken token = default)
        {
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Volatile.Write(ref _SyncsFromFolder, mergeOnChange);
                List<PropertyDocumentVersion> stored = await _Database.Documents.ListCurrentAsync(token).ConfigureAwait(false);
                Dictionary<string, PropertyDocumentVersion> current = stored.ToDictionary(v => v.DocumentKey, v => v, StringComparer.Ordinal);
                foreach (PropertyDocument document in Declared())
                {
                    current.TryGetValue(document.Key, out PropertyDocumentVersion? existing);
                    if (existing != null && !mergeOnChange) continue;
                    string? content = ReadFile(document);
                    if (content == null) continue;
                    string hash = SeedHash(document, content);
                    DateTime now = DateTime.UtcNow;
                    if (existing == null)
                    {
                        PropertyDocumentVersion seed = NewVersion(document, content, 1, DocumentVersionSourceEnum.Seed, true, hash, now);
                        try
                        {
                            await _Database.Documents.CreateVersionAsync(seed, token).ConfigureAwait(false);
                            _Logger.Info("[PropertyDocumentService] [p:" + _Property.Id + "] seeded document " + document.Key + " version " + seed.Version + " from " + document.File);
                        }
                        catch (DuplicateRecordException)
                        {
                            // Another instance seeded first.
                        }
                        continue;
                    }
                    if (String.Equals(existing.SeedHash, hash, StringComparison.Ordinal)) continue;
                    bool versionChanged = !await VersionUsedAsync(document.Key, document.Version.Trim(), token).ConfigureAwait(false);
                    PropertyDocumentVersion sync = NewVersion(document, content, existing.Sequence + 1, DocumentVersionSourceEnum.DirectorySync,
                        document.IsGating && document.ReacceptOnChange && versionChanged, hash, now);
                    sync.ChangeSummary = versionChanged ? "Updated from the property folder." : "Updated from the property folder (same version).";
                    try
                    {
                        await _Database.Documents.CreateVersionAsync(sync, token).ConfigureAwait(false);
                    }
                    catch (DuplicateRecordException)
                    {
                        continue;
                    }
                    _Logger.Info("[PropertyDocumentService] [p:" + _Property.Id + "] document " + document.Key + " changed on disk; now version " + sync.Version + " (sequence " + sync.Sequence + ")");
                    await _Audit.WriteAsync((TrellisKit.Core.Security.RequestContext?)null, null, "document.sync", TargetTypeEnum.PropertyDocument.ToString(), document.Key,
                        "sequence " + sync.Sequence + ", version " + existing.Version + " -> " + sync.Version + (sync.RequiresReacceptance ? "; members must accept it again" : String.Empty), token).ConfigureAwait(false);
                }
                await ReloadLockedAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// The gating documents the authorization stage checks. Never null.
        /// </summary>
        /// <returns>Requirements.</returns>
        public IReadOnlyList<DocumentRequirement> Requirements()
        {
            return Snapshot.Where(s => s.RequiredSequence > 0).Select(s => new DocumentRequirement { Key = s.Config.Key, RequiredSequence = s.RequiredSequence }).ToList();
        }

        /// <summary>
        /// The keys of the gating documents a member has not accepted at the required version.
        /// </summary>
        /// <param name="accepted">The member's highest accepted sequence per document.</param>
        /// <returns>Keys in property.json order.</returns>
        /// <exception cref="ArgumentNullException">The map is null.</exception>
        public List<string> PendingKeys(IReadOnlyDictionary<string, int> accepted)
        {
            ArgumentNullException.ThrowIfNull(accepted);
            return Snapshot.Where(s => IsPending(s, accepted)).Select(s => s.Config.Key).ToList();
        }

        /// <summary>
        /// The gating documents a member still has to accept (MeResponse pendingDocuments).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summaries in property.json order.</returns>
        public async Task<List<DocumentSummaryView>> PendingAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId) || !Snapshot.Any(s => s.RequiredSequence > 0)) return new List<DocumentSummaryView>();
            Dictionary<string, int> accepted = await _Database.Documents.AcceptedSequencesAsync(userId, token).ConfigureAwait(false);
            List<DocumentSummaryView> views = Snapshot.Where(s => IsPending(s, accepted)).Select(s =>
            {
                DocumentSummaryView view = Summary(s);
                view.PreviouslyAccepted = accepted.ContainsKey(s.Config.Key);
                return view;
            }).ToList();
            return await _Metadata.AttachAsync(TargetTypeEnum.PropertyDocument, views, v => v.Key, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The highest accepted sequence per document for a member (session principals).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sequence by document key.</returns>
        public Task<Dictionary<string, int>> AcceptedAsync(string userId, CancellationToken token = default)
        {
            return _Database.Documents.AcceptedSequencesAsync(userId, token);
        }

        /// <summary>
        /// The public summaries (GET /api/v1/property documents), in property.json order.
        /// </summary>
        /// <returns>Summaries.</returns>
        public List<DocumentSummaryView> Summaries()
        {
            return Snapshot.Select(s => Summary(s)).ToList();
        }

        /// <summary>
        /// A short revision of the snapshot (document keys and sequences) for ETags.
        /// </summary>
        /// <returns>Hex prefix.</returns>
        public string Revision()
        {
            string text = String.Join("|", Snapshot.Select(s => s.Config.Key + ":" + s.Current.Sequence + ":" + s.RequiredSequence));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 12).ToLowerInvariant();
        }

        /// <summary>
        /// GET /api/v1/public/documents/{key}: the current version with content.
        /// </summary>
        /// <param name="key">Document key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<DocumentView> GetPublicAsync(string? key, CancellationToken token = default)
        {
            DocumentState state = Require(key);
            DocumentView view = new DocumentView();
            Fill(view, state);
            view.Content = state.Current.Content;
            view.ChangeSummary = state.Current.ChangeSummary;
            return await _Metadata.AttachOneAsync(TargetTypeEnum.PropertyDocument, view, state.Config.Key, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Check the documents of a sign-up or acceptance request and return the documents to record. Explicit entries must
        /// name a declared document at its current version; <paramref name="acceptTerms"/> adds every document the terms
        /// checkbox covers. With <paramref name="requireAll"/>, every RequiredAtSignup document with its own checkbox must be
        /// listed. Problems are added to <paramref name="errors"/> under "&lt;prefix&gt;.&lt;key&gt;".
        /// </summary>
        /// <param name="acceptTerms">Whether the terms checkbox was checked.</param>
        /// <param name="items">Explicit entries, or null.</param>
        /// <param name="requireAll">Whether the separately accepted sign-up documents are required.</param>
        /// <param name="prefix">Field error prefix, for example acceptedDocuments.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Documents to record, without duplicates.</returns>
        /// <exception cref="ArgumentNullException">The errors dictionary is null.</exception>
        public List<DocumentState> Check(bool acceptTerms, List<DocumentAcceptanceItem>? items, bool requireAll, string prefix, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            IReadOnlyList<DocumentState> snapshot = Snapshot;
            List<DocumentState> accepted = new List<DocumentState>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<DocumentAcceptanceItem> entries = (items ?? new List<DocumentAcceptanceItem>()).Where(i => i != null).ToList();
            if (entries.Count > PropertyValidator.MaxDocuments)
            {
                errors[prefix] = "At most " + PropertyValidator.MaxDocuments + " documents.";
                return accepted;
            }
            foreach (DocumentAcceptanceItem item in entries)
            {
                string key = (item.Key ?? String.Empty).Trim();
                DocumentState? state = snapshot.FirstOrDefault(s => String.Equals(s.Config.Key, key, StringComparison.OrdinalIgnoreCase));
                string field = prefix + "." + (key.Length == 0 ? "key" : key);
                if (state == null)
                {
                    // A declared document with no stored version (its file was not found) is not required or recorded.
                    if (!Declared().Any(d => String.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase))) errors[field] = "Unknown document.";
                    continue;
                }
                if (!String.Equals((item.Version ?? String.Empty).Trim(), state.Current.Version.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    errors[prefix + "." + state.Config.Key] = "The " + state.Current.Title + " has changed; review version " + state.Current.Version + " and accept it.";
                    continue;
                }
                if (seen.Add(state.Config.Key)) accepted.Add(state);
            }
            if (acceptTerms)
            {
                foreach (DocumentState state in snapshot.Where(s => s.Config.CoveredByTerms))
                {
                    if (seen.Add(state.Config.Key)) accepted.Add(state);
                }
            }
            if (requireAll)
            {
                foreach (DocumentState state in snapshot.Where(s => s.Config.Acceptance == DocumentAcceptanceModeEnum.RequiredAtSignup && s.Config.SeparateCheckbox))
                {
                    string field = prefix + "." + state.Config.Key;
                    if (!seen.Contains(state.Config.Key) && !errors.ContainsKey(field)) errors[field] = "Accept the " + state.Current.Title + " to create an account.";
                }
            }
            return accepted;
        }

        /// <summary>
        /// Record a member's acceptances (audited document.accept) and drop their cached session so the gate sees them. The
        /// method is Reacceptance for a document the member had accepted an earlier version of, and Signup otherwise
        /// (always Signup when <paramref name="signup"/> is set). Versions the member already accepted are skipped.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="documents">Documents to record (current versions).</param>
        /// <param name="signup">Whether this is the sign-up step.</param>
        /// <param name="info">Request information (client, actor, source IP).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Acceptances stored.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<int> RecordAsync(string userId, List<DocumentState> documents, bool signup, RequestInfo info, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            ArgumentNullException.ThrowIfNull(documents);
            ArgumentNullException.ThrowIfNull(info);
            if (documents.Count == 0) return 0;
            Dictionary<string, int> before = await _Database.Documents.AcceptedSequencesAsync(userId, token).ConfigureAwait(false);
            DocumentClientEnum client = ClientOf(info);
            DateTime now = DateTime.UtcNow;
            List<DocumentAcceptance> rows = new List<DocumentAcceptance>();
            foreach (DocumentState state in documents)
            {
                bool had = before.TryGetValue(state.Config.Key, out int highest);
                if (had && highest >= state.Current.Sequence) continue;
                rows.Add(new DocumentAcceptance
                {
                    Id = TrellisKit.Core.Helpers.IdGenerator.Generate(DocumentAcceptance.IdPrefix),
                    UserId = userId,
                    DocumentKey = state.Config.Key,
                    VersionId = state.Current.Id,
                    Version = state.Current.Version,
                    Sequence = state.Current.Sequence,
                    Title = state.Current.Title,
                    Method = !signup && had ? DocumentAcceptanceMethodEnum.Reacceptance : DocumentAcceptanceMethodEnum.Signup,
                    Client = client,
                    AcceptedUtc = now,
                    CreatedUtc = now
                });
            }
            if (rows.Count == 0) return 0;
            await _Database.Documents.CreateAcceptancesAsync(rows, token).ConfigureAwait(false);
            _Tokens.InvalidateUser(userId);
            string summary = String.Join(", ", rows.Select(r => r.DocumentKey + " " + r.Version + " (" + r.Method + ")")) + "; client " + client;
            await _Audit.WriteAsync(info, "document.accept", TargetTypeEnum.User.ToString(), userId, summary, token).ConfigureAwait(false);
            foreach (DocumentAcceptance row in rows) TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", "document.accept." + row.Method.ToString().ToLowerInvariant());
            return rows.Count;
        }

        /// <summary>
        /// POST /api/v1/me/documents/accept: check and record the member's acceptances.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Acceptances stored.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<int> AcceptAsync(string userId, DocumentAcceptRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            List<DocumentState> documents = Check(request.AcceptTerms, request.Documents, false, "documents", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (documents.Count == 0) throw ApiErrorException.Field("documents", "List at least one document to accept.");
            return await RecordAsync(userId, documents, false, info, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/admin/documents: every declared document with a stored version, with counts.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<AdminDocumentListView> AdminListAsync(CancellationToken token = default)
        {
            List<AdminDocumentView> views = new List<AdminDocumentView>();
            IReadOnlyList<DocumentState> snapshot = Snapshot;
            foreach (PropertyDocument document in Declared())
            {
                DocumentState? state = snapshot.FirstOrDefault(s => String.Equals(s.Config.Key, document.Key, StringComparison.Ordinal));
                views.Add(state != null ? await AdminViewAsync(state, false, token).ConfigureAwait(false) : Unpublished(document));
            }
            await _Metadata.AttachAsync(TargetTypeEnum.PropertyDocument, views, v => v.Key, true, token).ConfigureAwait(false);
            return new AdminDocumentListView { Objects = views, SyncsFromFolder = SyncsFromFolder };
        }

        /// <summary>
        /// GET /api/v1/admin/documents/{key}: one document with its current content and counts.
        /// </summary>
        /// <param name="key">Document key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<AdminDocumentView> AdminGetAsync(string? key, CancellationToken token = default)
        {
            DocumentState state = Require(key);
            AdminDocumentView view = await AdminViewAsync(state, true, token).ConfigureAwait(false);
            return await _Metadata.AttachOneAsync(TargetTypeEnum.PropertyDocument, view, state.Config.Key, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/documents/{key}/versions/enumerate: the document's versions (content omitted).
        /// </summary>
        /// <param name="key">Document key.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EnumerationResult<DocumentVersionView>> EnumerateVersionsAsync(string? key, EnumerationQuery? query, CancellationToken token = default)
        {
            DocumentState state = Require(key);
            EnumerationRequest request = _Enumeration.Prepare(query, DocumentCollections.Versions, true, "document-versions|" + state.Config.Key);
            EnumerationResult<PropertyDocumentVersion> page = await _Database.Documents.EnumerateVersionsAsync(request, state.Config.Key, token).ConfigureAwait(false);
            List<DocumentVersionView> views = page.Objects.Select(v => VersionView(v, false)).ToList();
            return page.With(await _Metadata.AttachAsync(TargetTypeEnum.PropertyDocumentVersion, views, v => v.Id, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/documents/{key}/versions/{versionId}: one version with content.
        /// </summary>
        /// <param name="key">Document key.</param>
        /// <param name="versionId">Version id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<DocumentVersionView> GetVersionAsync(string? key, string? versionId, CancellationToken token = default)
        {
            DocumentState state = Require(key);
            PropertyDocumentVersion? version = await _Database.Documents.ReadVersionAsync(versionId ?? String.Empty, token).ConfigureAwait(false);
            if (version == null || !String.Equals(version.DocumentKey, state.Config.Key, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Document version");
            version.AcceptanceCount = await CountVersionAcceptancesAsync(version, token).ConfigureAwait(false);
            DocumentVersionView view = VersionView(version, true);
            return await _Metadata.AttachOneAsync(TargetTypeEnum.PropertyDocumentVersion, view, version.Id, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/documents/{key}/versions: publish a new current version (source Edit, audited
        /// document.publish). requireReacceptance defaults to the document's reacceptOnChange and applies only to gating
        /// documents. Other instances apply it on their next registry reload.
        /// </summary>
        /// <param name="info">Request information (actor and source IP).</param>
        /// <param name="key">Document key.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The document after publishing.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 409 VersionExists or Conflict.</exception>
        public async Task<AdminDocumentView> PublishAsync(RequestInfo info, string? key, DocumentPublishRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            PropertyDocument config = RequireDeclared(key);
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            string version = (request.Version ?? String.Empty).Trim();
            if (version.Length == 0 || version.Length > MaxVersion) errors["version"] = "Must be 1 to " + MaxVersion + " characters.";
            string content = (request.Content ?? String.Empty).Replace("\r\n", "\n");
            int max = _Settings.Property.MaxDocumentCharacters;
            if (content.Trim().Length == 0 || content.Length > max) errors["content"] = "Must be 1 to " + max + " characters.";
            string summary = (request.ChangeSummary ?? String.Empty).Trim();
            if (summary.Length == 0 || summary.Length > MaxChangeSummary) errors["changeSummary"] = "Must be 1 to " + MaxChangeSummary + " characters.";
            string? title = request.Title == null ? null : request.Title.Trim();
            if (title != null && (title.Length == 0 || title.Length > MaxTitle)) errors["title"] = "Must be 1 to " + MaxTitle + " characters.";
            if (request.RequireReacceptance == true && !config.IsGating) errors["requireReacceptance"] = "Applies only to documents members accept (RequiredAtSignup or AcceptedWithTerms).";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                PropertyDocumentVersion? before = await _Database.Documents.ReadCurrentAsync(config.Key, token).ConfigureAwait(false);
                string previousTitle = before?.Title ?? config.Title.Trim();
                if (before != null && await VersionUsedAsync(config.Key, version, token).ConfigureAwait(false))
                    throw new ApiErrorException(409, "VersionExists", "The " + previousTitle + " already has a version " + version + "; choose a new version.");
                DateTime now = DateTime.UtcNow;
                PropertyDocumentVersion next = new PropertyDocumentVersion
                {
                    Id = TrellisKit.Core.Helpers.IdGenerator.Generate(PropertyDocumentVersion.IdPrefix),
                    DocumentKey = config.Key,
                    Sequence = (before?.Sequence ?? 0) + 1,
                    Version = version,
                    Title = title ?? previousTitle,
                    Content = content,
                    ChangeSummary = summary,
                    RequiresReacceptance = before == null || (config.IsGating && (request.RequireReacceptance ?? config.ReacceptOnChange)),
                    Source = DocumentVersionSourceEnum.Edit,
                    SeedHash = before?.SeedHash,
                    CreatedByUserId = info.Context?.UserId ?? info.Context?.PlatformAdminId,
                    CreatedUtc = now
                };
                try
                {
                    await _Database.Documents.CreateVersionAsync(next, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    throw new ApiErrorException(409, "Conflict", "The document changed in the meantime; reload it and try again.");
                }
                await _Audit.WriteAsync(info, "document.publish", TargetTypeEnum.PropertyDocument.ToString(), config.Key,
                    "sequence " + next.Sequence + ", version " + (before?.Version ?? "none") + " -> " + next.Version + (next.RequiresReacceptance && before != null ? "; members must accept it again" : String.Empty) + "; " + summary, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", "document.publish");
                await TouchRegistryAsync(now, token).ConfigureAwait(false);
                await ReloadLockedAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _Lock.Release();
            }
            return await AdminGetAsync(config.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/documents/acceptances/enumerate (and the caller's own acceptances when
        /// <paramref name="ownerUserId"/> is set).
        /// </summary>
        /// <param name="query">Query (filters documentKey, userId, version, method, client; the member route allows documentKey).</param>
        /// <param name="ownerUserId">The member whose own acceptances to list, or null for the staff view.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<DocumentAcceptanceView>> EnumerateAcceptancesAsync(EnumerationQuery? query, string? ownerUserId, CancellationToken token = default)
        {
            bool staff = ownerUserId == null;
            EnumerationCollection collection = staff ? DocumentCollections.Acceptances : DocumentCollections.MyAcceptances;
            EnumerationRequest request = _Enumeration.Prepare(query, collection, staff, staff ? "document-acceptances" : "my-document-acceptances|" + ownerUserId);
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            DocumentAcceptanceFilter filter = new DocumentAcceptanceFilter
            {
                DocumentKey = Filter(request, "documentKey"),
                UserId = staff ? Filter(request, "userId") : ownerUserId,
                Version = staff ? Filter(request, "version") : null
            };
            if (staff)
            {
                filter.Method = ParseFilter<DocumentAcceptanceMethodEnum>(Filter(request, "method"), "filters.method", errors);
                filter.Client = ParseFilter<DocumentClientEnum>(Filter(request, "client"), "filters.client", errors);
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<DocumentAcceptance> page = await _Database.Documents.EnumerateAcceptancesAsync(request, filter, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = staff
                ? await _Summaries.BuildAsync(page.Objects.Select(a => (string?)a.UserId), ProfileViewerEnum.Staff, token).ConfigureAwait(false)
                : new Dictionary<string, UserSummary>(StringComparer.Ordinal);
            List<DocumentAcceptanceView> views = page.Objects.Select(a => new DocumentAcceptanceView
            {
                Id = a.Id,
                DocumentKey = a.DocumentKey,
                DocumentTitle = a.Title,
                Version = a.Version,
                Sequence = a.Sequence,
                VersionId = a.VersionId,
                UserId = a.UserId,
                User = staff ? (users.TryGetValue(a.UserId, out UserSummary? user) ? user : UserSummary.Deleted()) : null,
                Method = a.Method,
                Client = a.Client,
                AcceptedUtc = a.AcceptedUtc,
                CreatedUtc = a.CreatedUtc
            }).ToList();
            return page.With(await _Metadata.AttachAsync(TargetTypeEnum.DocumentAcceptance, views, v => v.Id, staff, token).ConfigureAwait(false));
        }

        /// <summary>
        /// The client an acceptance came from: Web for X-Client web, Ios or Android for X-Client mobile with
        /// X-Client-Platform ios or android, otherwise Other.
        /// </summary>
        /// <param name="info">Request information.</param>
        /// <returns>Client.</returns>
        /// <exception cref="ArgumentNullException">The info is null.</exception>
        public static DocumentClientEnum ClientOf(RequestInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (info.ClientType == ClientTypeEnum.Web) return DocumentClientEnum.Web;
            if (info.ClientType == ClientTypeEnum.Mobile)
            {
                if (String.Equals(info.ClientPlatform, "ios", StringComparison.Ordinal) || String.Equals(info.ClientPlatform, "ipados", StringComparison.Ordinal)) return DocumentClientEnum.Ios;
                if (String.Equals(info.ClientPlatform, "android", StringComparison.Ordinal)) return DocumentClientEnum.Android;
            }
            return DocumentClientEnum.Other;
        }

        /// <summary>
        /// The seed hash of a document declaration and its file content (version, title, and content).
        /// </summary>
        /// <param name="document">Declaration.</param>
        /// <param name="content">Normalized file content.</param>
        /// <returns>Hex SHA-256.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static string SeedHash(PropertyDocument document, string content)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(content);
            string text = document.Version.Trim() + "\n" + document.Title.Trim() + "\n" + content;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }

        #endregion

        #region Private-Methods

        private IEnumerable<PropertyDocument> Declared()
        {
            return (_Property.Documents ?? new List<PropertyDocument>()).Where(d => d != null && !String.IsNullOrWhiteSpace(d.Key));
        }

        private string? ReadFile(PropertyDocument document)
        {
            string? path = PropertyLoader.ResolveDocument(_Property, document.File);
            if (path == null)
            {
                _Logger.Warn("[PropertyDocumentService] [p:" + _Property.Id + "] document " + document.Key + " file " + document.File + " was not found in the property folder; skipped");
                return null;
            }
            try
            {
                string content = File.ReadAllText(path).Replace("\r\n", "\n");
                if (content.Length > _Settings.Property.MaxDocumentCharacters)
                    _Logger.Warn("[PropertyDocumentService] [p:" + _Property.Id + "] document " + document.Key + " is " + content.Length + " characters, above property.maxDocumentCharacters; stored in full");
                return content;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _Logger.Warn("[PropertyDocumentService] [p:" + _Property.Id + "] document " + document.Key + " file could not be read: " + e.Message);
                return null;
            }
        }

        private static PropertyDocumentVersion NewVersion(PropertyDocument document, string content, int sequence, DocumentVersionSourceEnum source, bool requiresReacceptance, string hash, DateTime now)
        {
            return new PropertyDocumentVersion
            {
                Id = TrellisKit.Core.Helpers.IdGenerator.Generate(PropertyDocumentVersion.IdPrefix),
                DocumentKey = document.Key,
                Sequence = sequence,
                Version = document.Version.Trim(),
                Title = document.Title.Trim(),
                Content = content,
                RequiresReacceptance = requiresReacceptance,
                Source = source,
                SeedHash = hash,
                CreatedUtc = now
            };
        }

        private async Task ReloadLockedAsync(CancellationToken token)
        {
            List<PropertyDocumentVersion> stored = await _Database.Documents.ListCurrentAsync(token).ConfigureAwait(false);
            Dictionary<string, int> required = await _Database.Documents.RequiredSequencesAsync(token).ConfigureAwait(false);
            Dictionary<string, PropertyDocumentVersion> current = stored.ToDictionary(v => v.DocumentKey, v => v, StringComparer.Ordinal);
            List<DocumentState> snapshot = new List<DocumentState>();
            foreach (PropertyDocument document in Declared())
            {
                if (!current.TryGetValue(document.Key, out PropertyDocumentVersion? version)) continue;
                int sequence = 0;
                if (document.IsGating) sequence = document.ReacceptOnChange && required.TryGetValue(document.Key, out int latest) ? Math.Max(1, latest) : 1;
                snapshot.Add(new DocumentState(document, version, sequence));
            }
            Interlocked.Exchange(ref _Snapshot, snapshot);
        }

        private static bool IsPending(DocumentState state, IReadOnlyDictionary<string, int> accepted)
        {
            if (state.RequiredSequence <= 0) return false;
            return !accepted.TryGetValue(state.Config.Key, out int highest) || highest < state.RequiredSequence;
        }

        private PropertyDocument RequireDeclared(string? key)
        {
            string wanted = (key ?? String.Empty).Trim();
            PropertyDocument? document = Declared().FirstOrDefault(d => String.Equals(d.Key, wanted, StringComparison.OrdinalIgnoreCase));
            if (document == null) throw ApiErrorException.NotFound("Document");
            return document;
        }

        private AdminDocumentView Unpublished(PropertyDocument document)
        {
            return new AdminDocumentView
            {
                Key = document.Key,
                Title = document.Title.Trim(),
                Kind = document.Kind,
                Acceptance = document.Acceptance,
                SeparateCheckbox = document.SeparateCheckbox,
                ReacceptOnChange = document.ReacceptOnChange,
                Slug = document.Slug,
                Footer = document.Footer,
                Description = String.IsNullOrWhiteSpace(document.Description) ? null : document.Description.Trim(),
                Version = String.Empty,
                Sequence = 0,
                Source = "None",
                SyncsFromFolder = SyncsFromFolder
            };
        }

        private DocumentState Require(string? key)
        {
            string wanted = (key ?? String.Empty).Trim();
            DocumentState? state = Snapshot.FirstOrDefault(s => String.Equals(s.Config.Key, wanted, StringComparison.OrdinalIgnoreCase));
            if (state == null) throw ApiErrorException.NotFound("Document");
            return state;
        }

        private static DocumentSummaryView Summary(DocumentState state)
        {
            DocumentSummaryView view = new DocumentSummaryView();
            Fill(view, state);
            return view;
        }

        private static void Fill(DocumentSummaryView view, DocumentState state)
        {
            view.Key = state.Config.Key;
            view.Title = state.Current.Title;
            view.Kind = state.Config.Kind;
            view.Acceptance = state.Config.Acceptance;
            view.SeparateCheckbox = state.Config.SeparateCheckbox;
            view.ReacceptOnChange = state.Config.ReacceptOnChange;
            view.Slug = state.Config.Slug;
            view.Footer = state.Config.Footer;
            view.Description = String.IsNullOrWhiteSpace(state.Config.Description) ? null : state.Config.Description.Trim();
            view.Version = state.Current.Version;
            view.Sequence = state.Current.Sequence;
            view.UpdatedUtc = state.Current.CreatedUtc;
        }

        private async Task<AdminDocumentView> AdminViewAsync(DocumentState state, bool withContent, CancellationToken token)
        {
            AdminDocumentView view = new AdminDocumentView();
            Fill(view, state);
            view.Source = state.Current.Source.ToString();
            view.VersionId = state.Current.Id;
            view.VersionCount = await _Database.Documents.CountVersionsAsync(state.Config.Key, token).ConfigureAwait(false);
            view.AcceptedCurrentCount = await _Database.Documents.CountAcceptedMembersAsync(state.Config.Key, state.Current.Sequence, token).ConfigureAwait(false);
            view.AcceptedAnyCount = await _Database.Documents.CountAcceptedMembersAsync(state.Config.Key, null, token).ConfigureAwait(false);
            view.RequiredSequence = state.RequiredSequence;
            view.SyncsFromFolder = SyncsFromFolder;
            view.CreatedByUserId = state.Current.CreatedByUserId;
            view.ChangeSummary = state.Current.ChangeSummary;
            if (withContent) view.Content = state.Current.Content;
            return view;
        }

        private Task<long> CountVersionAcceptancesAsync(PropertyDocumentVersion version, CancellationToken token)
        {
            return _Database.Documents.CountAcceptedMembersAsync(version.DocumentKey, version.Sequence, token);
        }

        private static DocumentVersionView VersionView(PropertyDocumentVersion version, bool withContent)
        {
            return new DocumentVersionView
            {
                Id = version.Id,
                DocumentKey = version.DocumentKey,
                Version = version.Version,
                Sequence = version.Sequence,
                Title = version.Title,
                ChangeSummary = version.ChangeSummary,
                RequiresReacceptance = version.RequiresReacceptance,
                Source = version.Source.ToString(),
                ContentLength = version.ContentLength,
                CreatedByUserId = version.CreatedByUserId,
                CreatedUtc = version.CreatedUtc,
                AcceptanceCount = version.AcceptanceCount,
                Content = withContent ? version.Content : null
            };
        }

        private async Task<bool> VersionUsedAsync(string key, string version, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery { MaxResults = 100 };
            string? continuation = null;
            for (int page = 0; page < 1000; page++)
            {
                query.ContinuationToken = continuation;
                EnumerationRequest request = _Enumeration.Prepare(query, DocumentCollections.Versions, true, "document-versions|" + key);
                EnumerationResult<PropertyDocumentVersion> result = await _Database.Documents.EnumerateVersionsAsync(request, key, token).ConfigureAwait(false);
                if (result.Objects.Any(v => String.Equals(v.Version.Trim(), version, StringComparison.OrdinalIgnoreCase))) return true;
                if (result.EndOfResults || result.ContinuationToken == null) return false;
                continuation = result.ContinuationToken;
            }
            return false;
        }

        private static string? Filter(EnumerationRequest request, string name)
        {
            string? value = request.Query.FilterString(name);
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static T? ParseFilter<T>(string? raw, string field, Dictionary<string, string> errors) where T : struct, Enum
        {
            if (raw == null) return null;
            if (Enum.TryParse<T>(raw, true, out T value) && Enum.IsDefined(value)) return value;
            errors[field] = "Must be one of " + String.Join(", ", Enum.GetNames<T>()) + ".";
            return null;
        }

        private async Task TouchRegistryAsync(DateTime now, CancellationToken token)
        {
            try
            {
                PropertyRecord? record = await _Database.Registry.ReadAsync(_Property.Id, token).ConfigureAwait(false);
                if (record == null) return;
                record.UpdatedUtc = now;
                await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[PropertyDocumentService] [p:" + _Property.Id + "] registry touch failed (other instances apply the change on their next reload): " + e.Message);
            }
        }

        #endregion
    }
}
