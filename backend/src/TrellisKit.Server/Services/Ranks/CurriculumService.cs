namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
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

    /// <summary>
    /// Curricula (docs/TRELLISKIT.md, Ranks, curricula, and attendance): rank-grouped lists of lessons, series, catalog
    /// entries, and custom items. Lesson items are satisfied by LessonProgress.Completed, Series items when every published
    /// lesson is complete, CatalogEntry and Custom items by self-completion (optional items) or verification (required
    /// items); any item a rank awarder verifies is satisfied. Catalog entry titles are not resolved here (clients fetch
    /// them from the catalog when it is available). Thread safety: safe for concurrent use.
    /// </summary>
    public class CurriculumService
    {
        #region Public-Members

        /// <summary>
        /// Most items per curriculum.
        /// </summary>
        public const int MaxItems = 500;

        #endregion

        #region Private-Members

        private static readonly Regex _CatalogId = new Regex("^[A-Za-z0-9_:.\\-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
        public CurriculumService(ApplicationServices services, RankModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/curricula (CurriculumCollections.Curricula): published Members and Public curricula the caller may
        /// see, with the caller's progress.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<CurriculumView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            EnumerationQuery q = query ?? new EnumerationQuery();
            CurriculumFilter filter = new CurriculumFilter { FieldKey = q.FilterString("fieldKey") };
            filter.Statuses.Add(CurriculumStatusEnum.Published);
            filter.Visibilities.Add(CurriculumVisibilityEnum.Public);
            if (IsMemberViewer(actor)) filter.Visibilities.Add(CurriculumVisibilityEnum.Members);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, CurriculumCollections.Curricula, actor.IsStaffFor(TargetTypeEnum.Curriculum), "curricula");
            EnumerationResult<Curriculum> page = await _Services.Database.Curricula.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<CurriculumView> views = new List<CurriculumView>();
            foreach (Curriculum c in page.Objects)
            {
                CurriculumView view = ToView(c);
                if (actor.UserId != null) view.Progress = await SummaryAsync(c.Id, actor.UserId, null, token).ConfigureAwait(false);
                view.UserId = actor.UserId;
                views.Add(view);
            }
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Curriculum, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.Curriculum), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/curricula/{id}: a published curriculum (Hidden ones too) with its items and the caller's progress.
        /// Curriculum staff (SystemAdmin, LibraryEditor, RankAwarder) see every status.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Curriculum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotAvailable or NotFound.</exception>
        public async Task<CurriculumView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            Curriculum curriculum = await RequireVisibleAsync(actor, id, token).ConfigureAwait(false);
            return await DetailAsync(curriculum, actor.UserId, actor.IsStaffFor(TargetTypeEnum.Curriculum), token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/me/curricula/{id}/items/{itemId}/complete (complete true) and DELETE (complete false): self-complete
        /// a CatalogEntry or Custom item, or undo it. Verified items stay verified.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Curriculum.</param>
        /// <param name="itemId">Item.</param>
        /// <param name="complete">True to complete, false to undo.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The item with the caller's state.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotAvailable or NotFound, 409 NotSelfCompletable or AlreadyVerified.</exception>
        public async Task<CurriculumItemView> SelfCompleteAsync(ContentActor actor, string id, string itemId, bool complete, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            string userId = actor.RequireUserId();
            Curriculum curriculum = await RequireVisibleAsync(actor, id, token).ConfigureAwait(false);
            if (curriculum.Status != CurriculumStatusEnum.Published) throw ApiErrorException.NotFound("Curriculum");
            List<CurriculumItem> items = await _Services.Database.Curricula.ReadItemsAsync(curriculum.Id, token).ConfigureAwait(false);
            CurriculumItem item = items.FirstOrDefault(i => i.Id == itemId) ?? throw ApiErrorException.NotFound("Curriculum item");
            if (item.ItemType == CurriculumItemTypeEnum.Lesson || item.ItemType == CurriculumItemTypeEnum.Series)
                throw new ApiErrorException(409, "NotSelfCompletable", "Lesson and series items complete from your library progress.");
            Dictionary<string, CurriculumProgress> progress = await _Services.Database.Curricula.ReadProgressAsync(curriculum.Id, userId, token).ConfigureAwait(false);
            progress.TryGetValue(item.Id, out CurriculumProgress? row);
            if (complete)
            {
                if (row == null)
                {
                    DateTime now = DateTime.UtcNow;
                    await _Services.Database.Curricula.UpsertProgressAsync(new CurriculumProgress
                    {
                        Id = IdGenerator.Generate(RankIds.CurriculumProgress),
                        CurriculumId = curriculum.Id,
                        ItemId = item.Id,
                        UserId = userId,
                        Status = CurriculumProgressStatusEnum.SelfComplete,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    }, token).ConfigureAwait(false);
                }
            }
            else
            {
                if (row != null && row.Status == CurriculumProgressStatusEnum.Verified) throw new ApiErrorException(409, "AlreadyVerified", "A verified item cannot be undone.");
                if (row != null) await _Services.Database.Curricula.DeleteProgressAsync(item.Id, userId, token).ConfigureAwait(false);
            }
            return await ItemForAsync(curriculum, item.Id, userId, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/admin/curricula (CurriculumCollections.AdminCurricula): every curriculum (curriculum staff).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin, LibraryEditor, or RankAwarder).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<CurriculumView>> ListAdminAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            RequireReader(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            CurriculumFilter filter = new CurriculumFilter { FieldKey = q.FilterString("fieldKey") };
            CurriculumStatusEnum? status = q.FilterEnum<CurriculumStatusEnum>("status", errors);
            CurriculumVisibilityEnum? visibility = q.FilterEnum<CurriculumVisibilityEnum>("visibility", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (status != null) filter.Statuses.Add(status.Value);
            if (visibility != null) filter.Visibilities.Add(visibility.Value);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, CurriculumCollections.AdminCurricula, true, "admin-curricula");
            EnumerationResult<Curriculum> page = await _Services.Database.Curricula.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<CurriculumView> views = page.Objects.Select(ToView).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Curriculum, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.Curriculum), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/admin/curricula/{id} (no userId) and GET /api/v1/admin/curricula/{id}/progress/{userId}: a curriculum
        /// in any status with its items, optionally with one member's progress.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin, LibraryEditor, or RankAwarder).</param>
        /// <param name="id">Curriculum.</param>
        /// <param name="userId">Member whose progress to show, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotAvailable or NotFound.</exception>
        public async Task<CurriculumView> GetAdminAsync(ContentActor actor, string id, string? userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            RequireReader(actor);
            Curriculum curriculum = await _Services.Database.Curricula.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Curriculum");
            if (userId != null)
            {
                User? user = await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
                if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            }
            return await DetailAsync(curriculum, userId, actor.IsStaffFor(TargetTypeEnum.Curriculum), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/curricula: create a Draft curriculum. Audited curriculum.create.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or LibraryEditor).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The curriculum.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<CurriculumView> CreateAsync(ContentActor actor, RequestInfo info, CurriculumRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Module.RequireEnabled();
            RequireEditor(actor);
            DateTime now = DateTime.UtcNow;
            Curriculum curriculum = new Curriculum { Id = IdGenerator.Generate(RankIds.Curriculum), CreatedByUserId = actor.UserId, CreatedUtc = now, UpdatedUtc = now };
            HashSet<string> present = new HashSet<string>(StringComparer.Ordinal) { "name", "description", "fieldKey", "visibility", "position" };
            await ApplyAsync(curriculum, request, present, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Services.Metadata.PrepareNewAsync(actor, TargetTypeEnum.Curriculum, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Database.Curricula.CreateAsync(curriculum, token).ConfigureAwait(false);
            if (metadata.Labels.Count > 0 || metadata.Tags.Count > 0) await _Services.Database.Metadata.WriteAsync(TargetTypeEnum.Curriculum, curriculum.Id, metadata, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "curriculum.create", "Curriculum", curriculum.Id, "Created " + curriculum.Name, token).ConfigureAwait(false);
            return await DetailAsync(curriculum, null, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/curricula/{id}: change the fields present in the body. Audited curriculum.update.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or LibraryEditor).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Curriculum.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">camelCase keys present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The curriculum.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable or NotFound.</exception>
        public async Task<CurriculumView> UpdateAsync(ContentActor actor, RequestInfo info, string id, CurriculumRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            _Module.RequireEnabled();
            RequireEditor(actor);
            Curriculum curriculum = await _Services.Database.Curricula.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Curriculum");
            await ApplyAsync(curriculum, request, present, token).ConfigureAwait(false);
            if (curriculum.FieldKey != null || present.Contains("fieldKey"))
            {
                List<CurriculumItem> items = await _Services.Database.Curricula.ReadItemsAsync(curriculum.Id, token).ConfigureAwait(false);
                ProfileField? field = curriculum.FieldKey == null ? null : await _Module.FieldAsync(curriculum.FieldKey, "fieldKey", token).ConfigureAwait(false);
                if (items.Any(i => i.OptionKey != null && (field == null || RankAwardRules.IndexOf(field, i.OptionKey) < 0)))
                    throw ApiErrorException.Field("fieldKey", "Items are grouped by options of another field; change the items first.");
            }
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Curriculum).ConfigureAwait(false);
            curriculum.UpdatedUtc = DateTime.UtcNow;
            if (!await _Services.Database.Curricula.UpdateAsync(curriculum, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Curriculum");
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Curriculum, curriculum.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "curriculum.update", "Curriculum", curriculum.Id, "Changed " + String.Join(", ", present.OrderBy(k => k, StringComparer.Ordinal)), token).ConfigureAwait(false);
            return await DetailAsync(curriculum, null, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/curricula/{id}/items: replace the items in order. Listed identifiers keep their progress;
        /// unlisted items are deleted with their progress. Audited curriculum.items.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or LibraryEditor).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Curriculum.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The curriculum with its items.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable or NotFound.</exception>
        public async Task<CurriculumView> ReplaceItemsAsync(ContentActor actor, RequestInfo info, string id, CurriculumItemsRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Module.RequireEnabled();
            RequireEditor(actor);
            Curriculum curriculum = await _Services.Database.Curricula.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Curriculum");
            List<CurriculumItemInput> inputs = request.Items ?? new List<CurriculumItemInput>();
            if (inputs.Count > MaxItems) throw ApiErrorException.Field("items", "Use at most " + MaxItems + " items.");
            ProfileField? field = curriculum.FieldKey == null ? null : await _Module.FieldAsync(curriculum.FieldKey, "fieldKey", token).ConfigureAwait(false);
            List<CurriculumItem> existing = await _Services.Database.Curricula.ReadItemsAsync(curriculum.Id, token).ConfigureAwait(false);
            HashSet<string> lessons = await _Services.Database.Curricula.ExistingTargetsAsync(CurriculumItemTypeEnum.Lesson,
                inputs.Where(i => i != null && String.Equals(i.ItemType, "Lesson", StringComparison.OrdinalIgnoreCase) && i.TargetId != null).Select(i => i.TargetId!.Trim()), token).ConfigureAwait(false);
            HashSet<string> series = await _Services.Database.Curricula.ExistingTargetsAsync(CurriculumItemTypeEnum.Series,
                inputs.Where(i => i != null && String.Equals(i.ItemType, "Series", StringComparison.OrdinalIgnoreCase) && i.TargetId != null).Select(i => i.TargetId!.Trim()), token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            DateTime now = DateTime.UtcNow;
            List<CurriculumItem> items = new List<CurriculumItem>();
            for (int i = 0; i < inputs.Count; i++)
            {
                CurriculumItemInput? input = inputs[i];
                string p = "items[" + i + "].";
                if (input == null)
                {
                    errors["items[" + i + "]"] = "Required.";
                    continue;
                }
                CurriculumItem? kept = null;
                if (!String.IsNullOrWhiteSpace(input.Id))
                {
                    kept = existing.FirstOrDefault(e => e.Id == input.Id.Trim());
                    if (kept == null) errors[p + "id"] = "Not an item of this curriculum.";
                    else if (!ids.Add(kept.Id)) errors[p + "id"] = "Listed more than once.";
                }
                if (!Enum.TryParse(input.ItemType, true, out CurriculumItemTypeEnum type) || !Enum.IsDefined(type) || Int32.TryParse(input.ItemType, out _))
                {
                    errors[p + "itemType"] = "Use Lesson, Series, CatalogEntry, or Custom.";
                    continue;
                }
                string? targetId = String.IsNullOrWhiteSpace(input.TargetId) ? null : input.TargetId.Trim();
                string? text = String.IsNullOrWhiteSpace(input.CustomText) ? null : input.CustomText.Trim();
                switch (type)
                {
                    case CurriculumItemTypeEnum.Lesson:
                        if (targetId == null || !lessons.Contains(targetId)) errors[p + "targetId"] = "Unknown lesson.";
                        text = null;
                        break;
                    case CurriculumItemTypeEnum.Series:
                        if (targetId == null || !series.Contains(targetId)) errors[p + "targetId"] = "Unknown series.";
                        text = null;
                        break;
                    case CurriculumItemTypeEnum.CatalogEntry:
                        if (targetId == null || !_CatalogId.IsMatch(targetId)) errors[p + "targetId"] = "Use a catalog entry identifier (1 to 100 letters, digits, and _ : . -).";
                        text = null;
                        break;
                    default:
                        targetId = null;
                        if (text == null || text.Length > 300) errors[p + "customText"] = "Use 1 to 300 characters.";
                        break;
                }
                string? optionKey = String.IsNullOrWhiteSpace(input.OptionKey) ? null : input.OptionKey.Trim();
                if (optionKey != null)
                {
                    int index = field == null ? -1 : RankAwardRules.IndexOf(field, optionKey);
                    if (field == null) errors[p + "optionKey"] = "Set the curriculum's fieldKey to group items by rank.";
                    else if (index < 0) errors[p + "optionKey"] = "Use one of the field's options.";
                    else optionKey = field.Options[index].Key;
                }
                await _Services.Metadata.ValidateAsync(actor, input.Labels, input.Tags, true, token, TargetTypeEnum.CurriculumItem).ConfigureAwait(false);
                items.Add(new CurriculumItem
                {
                    Id = kept?.Id ?? IdGenerator.Generate(RankIds.CurriculumItem),
                    CurriculumId = curriculum.Id,
                    OptionKey = optionKey,
                    ItemType = type,
                    TargetId = targetId,
                    CustomText = text,
                    Required = input.Required ?? true,
                    CreatedUtc = kept?.CreatedUtc ?? now,
                    UpdatedUtc = now
                });
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Services.Database.Curricula.ReplaceItemsAsync(curriculum.Id, items, now, token).ConfigureAwait(false);
            for (int i = 0; i < items.Count; i++)
            {
                if (inputs[i].Labels != null || inputs[i].Tags != null)
                    await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.CurriculumItem, items[i].Id, inputs[i].Labels, inputs[i].Tags, token).ConfigureAwait(false);
            }
            await _Services.Audit.WriteAsync(info, "curriculum.items", "Curriculum", curriculum.Id, "Replaced items (" + items.Count + ")", token).ConfigureAwait(false);
            Curriculum stored = await _Services.Database.Curricula.ReadAsync(curriculum.Id, token).ConfigureAwait(false) ?? curriculum;
            return await DetailAsync(stored, null, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/curricula/{id}/publish and /archive. Audited curriculum.publish or curriculum.archive.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or LibraryEditor).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Curriculum.</param>
        /// <param name="status">Published or Archived.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The curriculum.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotAvailable or NotFound, 409 InvalidStatus or CurriculumEmpty.</exception>
        public async Task<CurriculumView> SetStatusAsync(ContentActor actor, RequestInfo info, string id, CurriculumStatusEnum status, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            _Module.RequireEnabled();
            RequireEditor(actor);
            Curriculum curriculum = await _Services.Database.Curricula.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Curriculum");
            if (curriculum.Status == status) throw new ApiErrorException(409, "InvalidStatus", "The curriculum is already " + status + ".");
            if (status == CurriculumStatusEnum.Published && curriculum.ItemCount == 0) throw new ApiErrorException(409, "CurriculumEmpty", "Add at least one item before publishing.");
            DateTime now = DateTime.UtcNow;
            curriculum.Status = status;
            if (status == CurriculumStatusEnum.Published && curriculum.PublishedUtc == null) curriculum.PublishedUtc = now;
            curriculum.UpdatedUtc = now;
            await _Services.Database.Curricula.UpdateAsync(curriculum, token).ConfigureAwait(false);
            string action = status == CurriculumStatusEnum.Published ? "curriculum.publish" : "curriculum.archive";
            await _Services.Audit.WriteAsync(info, action, "Curriculum", curriculum.Id, curriculum.Name, token).ConfigureAwait(false);
            return await DetailAsync(curriculum, null, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/curricula/{id}/progress/{userId}/items/{itemId}/verify: a rank awarder verifies an item for a
        /// member. Audited curriculum.verify.
        /// </summary>
        /// <param name="actor">Caller (RankAwarder or SystemAdmin).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Curriculum.</param>
        /// <param name="userId">Member.</param>
        /// <param name="itemId">Item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The item with the member's state.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotAvailable or NotFound.</exception>
        public async Task<CurriculumItemView> VerifyAsync(ContentActor actor, RequestInfo info, string id, string userId, string itemId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            _Module.RequireEnabled();
            if (!actor.IsRankStaff) throw ApiErrorException.Forbidden("Only rank awarders (RankAwarder or SystemAdmin) can verify curriculum items.");
            if (!String.IsNullOrEmpty(actor.UserId) && actor.UserId == userId) throw ApiErrorException.Forbidden("You cannot verify your own curriculum items.");
            Curriculum curriculum = await _Services.Database.Curricula.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Curriculum");
            List<CurriculumItem> items = await _Services.Database.Curricula.ReadItemsAsync(curriculum.Id, token).ConfigureAwait(false);
            CurriculumItem item = items.FirstOrDefault(i => i.Id == itemId) ?? throw ApiErrorException.NotFound("Curriculum item");
            User? user = await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            Dictionary<string, CurriculumProgress> progress = await _Services.Database.Curricula.ReadProgressAsync(curriculum.Id, user.Id, token).ConfigureAwait(false);
            progress.TryGetValue(item.Id, out CurriculumProgress? row);
            DateTime now = DateTime.UtcNow;
            await _Services.Database.Curricula.UpsertProgressAsync(new CurriculumProgress
            {
                Id = row?.Id ?? IdGenerator.Generate(RankIds.CurriculumProgress),
                CurriculumId = curriculum.Id,
                ItemId = item.Id,
                UserId = user.Id,
                Status = CurriculumProgressStatusEnum.Verified,
                VerifiedBy = actor.UserId,
                VerifiedUtc = now,
                CreatedUtc = row?.CreatedUtc ?? now,
                UpdatedUtc = now
            }, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "curriculum.verify", "User", user.Id, "Verified item " + item.Id + " of " + curriculum.Id, token).ConfigureAwait(false);
            return await ItemForAsync(curriculum, item.Id, user.Id, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A member's completion counts in a curriculum, optionally limited to one rank group.
        /// </summary>
        /// <param name="curriculumId">Curriculum.</param>
        /// <param name="userId">Member.</param>
        /// <param name="optionKey">Rank group, or null for every item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary, or null when the curriculum does not exist.</returns>
        public async Task<CurriculumProgressSummary?> SummaryAsync(string curriculumId, string userId, string? optionKey, CancellationToken token = default)
        {
            Curriculum? curriculum = await _Services.Database.Curricula.ReadAsync(curriculumId, token).ConfigureAwait(false);
            if (curriculum == null) return null;
            List<CurriculumItem> items = await _Services.Database.Curricula.ReadItemsAsync(curriculum.Id, token).ConfigureAwait(false);
            if (optionKey != null) items = items.Where(i => String.Equals(i.OptionKey, optionKey, StringComparison.OrdinalIgnoreCase)).ToList();
            List<CurriculumItemView> views = await ItemViewsAsync(curriculum, items, userId, null, false, token).ConfigureAwait(false);
            CurriculumProgressSummary summary = Summarize(views);
            summary.CurriculumId = curriculum.Id;
            summary.Name = curriculum.Name;
            summary.OptionKey = optionKey;
            return summary;
        }

        #endregion

        #region Private-Methods

        private static bool IsMemberViewer(ContentActor actor)
        {
            return actor.CountsAsMember || actor.IsSystemAdmin || actor.IsStaff;
        }

        private static bool IsStaff(ContentActor actor)
        {
            return actor.IsSystemAdmin || actor.IsLibraryEditor || actor.IsRankAwarder;
        }

        private static void RequireEditor(ContentActor actor)
        {
            if (!actor.IsLibraryStaff) throw ApiErrorException.Forbidden("Only curriculum editors (SystemAdmin or LibraryEditor) can do this.");
        }

        private static void RequireReader(ContentActor actor)
        {
            if (!IsStaff(actor)) throw ApiErrorException.Forbidden("Only curriculum staff (SystemAdmin, LibraryEditor, or RankAwarder) can do this.");
        }

        private async Task<Curriculum> RequireVisibleAsync(ContentActor actor, string id, CancellationToken token)
        {
            Curriculum? curriculum = await _Services.Database.Curricula.ReadAsync(id, token).ConfigureAwait(false);
            if (curriculum == null) throw ApiErrorException.NotFound("Curriculum");
            if (IsStaff(actor)) return curriculum;
            if (curriculum.Status != CurriculumStatusEnum.Published) throw ApiErrorException.NotFound("Curriculum");
            if (curriculum.Visibility == CurriculumVisibilityEnum.Members && !IsMemberViewer(actor)) throw ApiErrorException.NotFound("Curriculum");
            return curriculum;
        }

        private async Task ApplyAsync(Curriculum curriculum, CurriculumRequest request, HashSet<string> present, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("name"))
            {
                string name = (request.Name ?? String.Empty).Trim();
                if (name.Length < 1 || name.Length > 120) errors["name"] = "Use 1 to 120 characters.";
                else curriculum.Name = name;
            }
            if (present.Contains("description"))
            {
                string? description = String.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                if (description != null && description.Length > 2000) errors["description"] = "Use at most 2000 characters.";
                else curriculum.Description = description;
            }
            if (present.Contains("fieldKey"))
            {
                if (String.IsNullOrWhiteSpace(request.FieldKey)) curriculum.FieldKey = null;
                else
                {
                    List<ProfileField> fields = await _Module.RankFieldsAsync(token).ConfigureAwait(false);
                    ProfileField? field = fields.FirstOrDefault(f => String.Equals(f.Key, request.FieldKey.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (field == null) errors["fieldKey"] = "Use the key of an enabled Rank profile field.";
                    else curriculum.FieldKey = field.Key;
                }
            }
            if (present.Contains("visibility") && request.Visibility != null)
            {
                if (Enum.TryParse(request.Visibility, true, out CurriculumVisibilityEnum visibility) && Enum.IsDefined(visibility) && !Int32.TryParse(request.Visibility, out _)) curriculum.Visibility = visibility;
                else errors["visibility"] = "Use Members, Public, or Hidden.";
            }
            if (present.Contains("position") && request.Position != null)
            {
                if (request.Position < 0 || request.Position > 100000) errors["position"] = "Use 0 to 100000.";
                else curriculum.Position = request.Position.Value;
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        private static CurriculumView ToView(Curriculum c)
        {
            return new CurriculumView
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                FieldKey = c.FieldKey,
                Visibility = c.Visibility,
                Status = c.Status,
                Position = c.Position,
                ItemCount = c.ItemCount,
                PublishedUtc = c.PublishedUtc,
                CreatedUtc = c.CreatedUtc,
                UpdatedUtc = c.UpdatedUtc
            };
        }

        private async Task<CurriculumView> DetailAsync(Curriculum curriculum, string? userId, bool staff, CancellationToken token)
        {
            List<CurriculumItem> items = await _Services.Database.Curricula.ReadItemsAsync(curriculum.Id, token).ConfigureAwait(false);
            ProfileField? field = null;
            if (curriculum.FieldKey != null)
            {
                List<ProfileField> fields = await _Module.RankFieldsAsync(token).ConfigureAwait(false);
                field = fields.FirstOrDefault(f => String.Equals(f.Key, curriculum.FieldKey, StringComparison.OrdinalIgnoreCase));
            }
            CurriculumView view = ToView(curriculum);
            view.ItemCount = items.Count;
            view.UserId = userId;
            view.Items = await ItemViewsAsync(curriculum, items, userId, field, staff, token).ConfigureAwait(false);
            if (userId != null) view.Progress = Summarize(view.Items);
            await _Services.Metadata.AttachOneAsync(TargetTypeEnum.Curriculum, view, view.Id, staff, token).ConfigureAwait(false);
            return view;
        }

        private async Task<CurriculumItemView> ItemForAsync(Curriculum curriculum, string itemId, string userId, bool staff, CancellationToken token)
        {
            List<CurriculumItem> items = await _Services.Database.Curricula.ReadItemsAsync(curriculum.Id, token).ConfigureAwait(false);
            ProfileField? field = null;
            if (curriculum.FieldKey != null) field = (await _Module.RankFieldsAsync(token).ConfigureAwait(false)).FirstOrDefault(f => String.Equals(f.Key, curriculum.FieldKey, StringComparison.OrdinalIgnoreCase));
            List<CurriculumItemView> views = await ItemViewsAsync(curriculum, items.Where(i => i.Id == itemId).ToList(), userId, field, staff, token).ConfigureAwait(false);
            if (views.Count == 0) throw ApiErrorException.NotFound("Curriculum item");
            return views[0];
        }

        private async Task<List<CurriculumItemView>> ItemViewsAsync(Curriculum curriculum, List<CurriculumItem> items, string? userId, ProfileField? field, bool staff, CancellationToken token)
        {
            List<string> lessonIds = items.Where(i => i.ItemType == CurriculumItemTypeEnum.Lesson && i.TargetId != null).Select(i => i.TargetId!).ToList();
            List<string> seriesIds = items.Where(i => i.ItemType == CurriculumItemTypeEnum.Series && i.TargetId != null).Select(i => i.TargetId!).ToList();
            Dictionary<string, string> lessonTitles = await _Services.Database.Curricula.TargetTitlesAsync(CurriculumItemTypeEnum.Lesson, lessonIds, token).ConfigureAwait(false);
            Dictionary<string, string> seriesTitles = await _Services.Database.Curricula.TargetTitlesAsync(CurriculumItemTypeEnum.Series, seriesIds, token).ConfigureAwait(false);
            HashSet<string> lessonsDone = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seriesDone = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, CurriculumProgress> progress = new Dictionary<string, CurriculumProgress>(StringComparer.Ordinal);
            if (userId != null)
            {
                lessonsDone = await _Services.Database.Curricula.CompletedLessonIdsAsync(userId, lessonIds, token).ConfigureAwait(false);
                seriesDone = await _Services.Database.Curricula.CompletedSeriesIdsAsync(userId, seriesIds, token).ConfigureAwait(false);
                progress = await _Services.Database.Curricula.ReadProgressAsync(curriculum.Id, userId, token).ConfigureAwait(false);
            }
            List<CurriculumItemView> views = new List<CurriculumItemView>();
            foreach (CurriculumItem item in items)
            {
                CurriculumItemView view = new CurriculumItemView
                {
                    Id = item.Id,
                    CurriculumId = item.CurriculumId,
                    OptionKey = item.OptionKey,
                    ItemType = item.ItemType,
                    TargetId = item.TargetId,
                    CustomText = item.CustomText,
                    Position = item.Position,
                    Required = item.Required,
                    SelfCompletable = item.ItemType == CurriculumItemTypeEnum.CatalogEntry || item.ItemType == CurriculumItemTypeEnum.Custom
                };
                if (item.OptionKey != null && field != null)
                {
                    int index = RankAwardRules.IndexOf(field, item.OptionKey);
                    view.OptionLabel = index < 0 ? item.OptionKey : field.Options[index].Label;
                }
                if (item.ItemType == CurriculumItemTypeEnum.Lesson)
                {
                    view.TargetAvailable = item.TargetId != null && lessonTitles.ContainsKey(item.TargetId);
                    view.Title = item.TargetId != null && lessonTitles.TryGetValue(item.TargetId, out string? t) ? t : null;
                    if (item.TargetId != null && lessonsDone.Contains(item.TargetId)) view.State = "Completed";
                }
                else if (item.ItemType == CurriculumItemTypeEnum.Series)
                {
                    view.TargetAvailable = item.TargetId != null && seriesTitles.ContainsKey(item.TargetId);
                    view.Title = item.TargetId != null && seriesTitles.TryGetValue(item.TargetId, out string? t) ? t : null;
                    if (item.TargetId != null && seriesDone.Contains(item.TargetId)) view.State = "Completed";
                }
                if (progress.TryGetValue(item.Id, out CurriculumProgress? row))
                {
                    if (row.Status == CurriculumProgressStatusEnum.Verified)
                    {
                        view.State = "Verified";
                        view.VerifiedBy = row.VerifiedBy;
                        view.VerifiedUtc = row.VerifiedUtc;
                    }
                    else if (view.State == "Incomplete") view.State = "SelfComplete";
                }
                view.Satisfied = view.State == "Completed" || view.State == "Verified" || (view.State == "SelfComplete" && !item.Required);
                views.Add(view);
            }
            await _Services.Metadata.AttachAsync(TargetTypeEnum.CurriculumItem, views, v => v.Id, staff, token).ConfigureAwait(false);
            return views;
        }

        private static CurriculumProgressSummary Summarize(List<CurriculumItemView> views)
        {
            CurriculumProgressSummary summary = new CurriculumProgressSummary
            {
                Total = views.Count,
                Required = views.Count(v => v.Required),
                Completed = views.Count(v => v.Satisfied),
                CompletedRequired = views.Count(v => v.Required && v.Satisfied)
            };
            summary.Percent = summary.Total == 0 ? 0 : (int)Math.Floor(100.0 * summary.Completed / summary.Total);
            return summary;
        }

        #endregion
    }
}
