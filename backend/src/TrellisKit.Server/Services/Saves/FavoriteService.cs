namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
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
    /// Generic saves and named private collections (REST_API.md, Saves). A member saves lessons, series, catalog
    /// entries, venues, instructors, events, posts, and threads; each type follows its own feature switch and access
    /// rules through a <see cref="SaveTargetResolver"/>, so a saved list never shows the title of content the member
    /// can no longer open. Notes pass the word filter and the content changelog. Thread safety: stateless apart from
    /// the resolver table, which is written during construction and by <see cref="RegisterResolver"/>.
    /// </summary>
    public class FavoriteService
    {
        #region Public-Members

        /// <summary>
        /// Prefix of save identifiers.
        /// </summary>
        public const string FavoriteIdPrefix = "fav_";

        /// <summary>
        /// Prefix of collection identifiers.
        /// </summary>
        public const string CollectionIdPrefix = "fvc_";

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly Dictionary<FavoriteTargetTypeEnum, SaveTargetResolver> _Resolvers = new Dictionary<FavoriteTargetTypeEnum, SaveTargetResolver>();
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the engine resolvers: Lesson, Series, Venue, Instructor, Event, and CatalogEntry through the
        /// content target registry (CatalogEntry stays unavailable until a provider of that type registers), Post and
        /// Thread through the feed and forum rules.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public FavoriteService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            ContentTargetRegistry registry = services.ContentTargets;
            _Resolvers[FavoriteTargetTypeEnum.Lesson] = new ContentTargetSaveResolver(registry, "Lesson", () => _Services.Property.Property.Features.Library);
            _Resolvers[FavoriteTargetTypeEnum.Series] = new ContentTargetSaveResolver(registry, "Series", () => _Services.Property.Property.Features.Library);
            _Resolvers[FavoriteTargetTypeEnum.Venue] = new ContentTargetSaveResolver(registry, "Venue", () => _Services.Property.Property.Features.Directory);
            _Resolvers[FavoriteTargetTypeEnum.Instructor] = new ContentTargetSaveResolver(registry, "Instructor", () => _Services.Property.Property.Features.Directory);
            _Resolvers[FavoriteTargetTypeEnum.Event] = new ContentTargetSaveResolver(registry, "Event", () => _Services.Property.Property.Features.Events);
            _Resolvers[FavoriteTargetTypeEnum.CatalogEntry] = new ContentTargetSaveResolver(registry, "CatalogEntry", () => true);
            _Resolvers[FavoriteTargetTypeEnum.Post] = new FeedPostSaveResolver(services);
            _Resolvers[FavoriteTargetTypeEnum.Thread] = new ThreadSaveResolver(services);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register account export sections (saves and saveCollections) and account deletion.
        /// </summary>
        public void Attach()
        {
            _Services.AccountExports.Register(new PagedExportSection<Favorite>("saves", async (userId, after, limit, token) =>
            {
                List<Favorite> rows = await _Services.Database.Favorites.ReadForUserAsync(userId, after, limit, token).ConfigureAwait(false);
                await _Services.Metadata.AttachAsync(TargetTypeEnum.Favorite, rows, f => f.Id, false, token).ConfigureAwait(false);
                return new ContentExportPage<Favorite> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
            }));
            _Services.AccountExports.Register(new PagedExportSection<FavoriteCollection>("saveCollections", async (userId, after, limit, token) =>
            {
                List<FavoriteCollection> rows = await _Services.Database.Favorites.ReadCollectionsForUserAsync(userId, after, limit, token).ConfigureAwait(false);
                await _Services.Metadata.AttachAsync(TargetTypeEnum.FavoriteCollection, rows, c => c.Id, false, token).ConfigureAwait(false);
                return new ContentExportPage<FavoriteCollection> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
            }));
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                int removed = await _Services.Database.Favorites.DeleteForUserAsync(userId, token).ConfigureAwait(false);
                SavesTelemetry.CountRemoved(null, removed);
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Replace the resolver of a target type (for example a catalog module serving CatalogEntry with its own rules).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="resolver">Resolver.</param>
        /// <exception cref="ArgumentNullException">The resolver is null.</exception>
        public void RegisterResolver(FavoriteTargetTypeEnum targetType, SaveTargetResolver resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            lock (_Lock) _Resolvers[targetType] = resolver;
        }

        /// <summary>
        /// The resolver of a target type.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>Resolver.</returns>
        public SaveTargetResolver ResolverFor(FavoriteTargetTypeEnum targetType)
        {
            lock (_Lock) return _Resolvers[targetType];
        }

        /// <summary>
        /// Targets were deleted: remove every member's saves of them. Content services call this where they delete a
        /// target; archived, unpublished, or moderated targets keep their saves and show as unavailable instead.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetIds">Deleted targets.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Saves removed.</returns>
        public async Task<int> TargetsDeletedAsync(FavoriteTargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            int removed = 0;
            foreach (string[] chunk in targetIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).Chunk(1000))
                removed += await _Services.Database.Favorites.DeleteForTargetAsync(targetType, chunk, token).ConfigureAwait(false);
            SavesTelemetry.CountRemoved(targetType, removed);
            return removed;
        }

        /// <summary>
        /// Targets were deleted (single identifier form).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Deleted target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Saves removed.</returns>
        public Task<int> TargetDeletedAsync(FavoriteTargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return TargetsDeletedAsync(targetType, new[] { targetId }, token);
        }

        /// <summary>
        /// PUT /api/v1/me/saves/{targetType}/{targetId}: save a target (idempotent; creates or updates).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="rawType">Target type name.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Body fields present (camelCase).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The save.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 402 MembershipRequired, 403 Forbidden, 404 NotFound or NotAvailable, 409 LimitReached.</exception>
        public async Task<FavoriteView> SaveAsync(ContentActor actor, string rawType, string targetId, FavoriteRequest request, ISet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            RequireEnabled();
            string userId = actor.RequireUserId();
            FavoriteTargetTypeEnum type = ParseType(rawType, "targetType");
            if (String.IsNullOrWhiteSpace(targetId) || targetId.Length > 128) throw ApiErrorException.Field("targetId", "Provide the target identifier.");
            targetId = targetId.Trim();
            RequireWriteTier(actor);
            SaveTargetResolver resolver = ResolverFor(type);
            if (!resolver.IsSupported()) throw NotAvailable(type + " saves are not available in this community.");
            FavoriteTargetSummary? summary = await resolver.ResolveAsync(actor, targetId, token).ConfigureAwait(false);
            if (summary == null) throw ApiErrorException.NotFound(type.ToString());
            if (!summary.Available) throw ApiErrorException.Forbidden("You cannot open this " + type + ", so you cannot save it.");

            bool setNote = present.Contains("note");
            string? note = setNote ? CleanNote(request.Note) : null;
            List<string>? collectionIds = request.CollectionIds == null ? null : request.CollectionIds.Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (collectionIds != null && collectionIds.Count > FavoriteRequest.MaxCollectionIds)
                throw ApiErrorException.Field("collectionIds", "At most " + FavoriteRequest.MaxCollectionIds + " collections.");
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Favorite), token, TargetTypeEnum.Favorite).ConfigureAwait(false);
            WordFilterResult filter = await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Favorite, new Dictionary<string, string?> { { "note", note } }, token).ConfigureAwait(false);

            Favorite? existing = await _Services.Database.Favorites.ReadByTargetAsync(userId, type, targetId, token).ConfigureAwait(false);
            if (existing == null)
            {
                long count = await _Services.Database.Favorites.CountForUserAsync(userId, token).ConfigureAwait(false);
                int max = _Services.Settings.Saves.MaxPerMember;
                if (count >= max) throw new ApiErrorException(409, "LimitReached", "You can keep at most " + max + " saves. Remove some to save more.");
            }
            if (collectionIds != null && collectionIds.Count > 0)
            {
                List<FavoriteCollection> owned = await _Services.Database.Favorites.ReadCollectionsAsync(userId, collectionIds, token).ConfigureAwait(false);
                if (owned.Count != collectionIds.Count) throw ApiErrorException.NotFound("Collection");
            }

            DateTime now = DateTime.UtcNow;
            Favorite candidate = new Favorite
            {
                Id = IdGenerator.Generate(FavoriteIdPrefix),
                UserId = userId,
                TargetType = type,
                TargetId = targetId,
                Note = note,
                CreatedUtc = now,
                LastUpdateUtc = now
            };
            FavoriteUpsertResult result = await _Services.Database.Favorites.UpsertAsync(candidate, setNote, token).ConfigureAwait(false);
            Favorite favorite = result.Favorite;
            if (result.Created) SavesTelemetry.CountCreated(type);
            if (collectionIds != null)
            {
                if (!await _Services.Database.Favorites.SetCollectionsAsync(userId, favorite.Id, collectionIds, token).ConfigureAwait(false))
                    throw ApiErrorException.NotFound("Collection");
            }
            if (request.Labels != null || request.Tags != null)
                await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Favorite, favorite.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            if (setNote && note != null) await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.Favorite, favorite.Id, userId, token).ConfigureAwait(false);
            Favorite stored = await _Services.Database.Favorites.ReadByTargetAsync(userId, type, targetId, token).ConfigureAwait(false) ?? favorite;
            List<FavoriteView> views = await ViewsAsync(actor, new List<Favorite> { stored }, token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// DELETE /api/v1/me/saves/{targetType}/{targetId}: remove a save (idempotent).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="rawType">Target type name.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable.</exception>
        public async Task UnsaveAsync(ContentActor actor, string rawType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            string userId = actor.RequireUserId();
            FavoriteTargetTypeEnum type = ParseType(rawType, "targetType");
            if (String.IsNullOrWhiteSpace(targetId)) return;
            if (await _Services.Database.Favorites.DeleteAsync(userId, type, targetId.Trim(), token).ConfigureAwait(false)) SavesTelemetry.CountRemoved(type);
        }

        /// <summary>
        /// GET /api/v1/me/saves and POST /api/v1/me/saves/enumerate: the caller's saves with target summaries.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query (filters targetTypes, collectionId).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotAvailable or NotFound (collection).</exception>
        public async Task<EnumerationResult<FavoriteView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            string userId = actor.RequireUserId();
            EnumerationQuery q = query ?? new EnumerationQuery();
            FavoriteFilter filter = new FavoriteFilter { UserId = userId };
            List<string>? types = ForumFilters.List(q, "targetTypes");
            if (types != null)
            {
                if (types.Count > 8) throw ApiErrorException.Field("filters.targetTypes", "At most 8 target types.");
                foreach (string raw in types) filter.TargetTypes.Add(ParseType(raw, "filters.targetTypes"));
            }
            string? collectionId = q.FilterString("collectionId");
            if (!String.IsNullOrEmpty(collectionId))
            {
                if (await _Services.Database.Favorites.ReadCollectionAsync(userId, collectionId, token).ConfigureAwait(false) == null) throw ApiErrorException.NotFound("Collection");
                filter.CollectionId = collectionId;
            }
            EnumerationRequest request = _Services.Enumeration.Prepare(q, SaveCollections.Saves, false, userId);
            EnumerationResult<Favorite> page = await _Services.Database.Favorites.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/me/saves/state: whether the caller saved each of 1 to 100 targets, in request order.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable.</exception>
        public async Task<FavoriteStateResponse> StateAsync(ContentActor actor, FavoriteStateRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            string userId = actor.RequireUserId();
            List<FavoriteTargetRef> targets = request.Targets ?? new List<FavoriteTargetRef>();
            if (targets.Count < 1 || targets.Count > FavoriteStateRequest.MaxTargets)
                throw ApiErrorException.Field("targets", "Provide 1 to " + FavoriteStateRequest.MaxTargets + " targets.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<FavoriteTargetKey> keys = new List<FavoriteTargetKey>();
            for (int i = 0; i < targets.Count; i++)
            {
                FavoriteTargetRef target = targets[i] ?? new FavoriteTargetRef();
                if (!TryParseType(target.TargetType, out FavoriteTargetTypeEnum type)) errors["targets[" + i + "].targetType"] = "Unknown target type.";
                if (String.IsNullOrWhiteSpace(target.TargetId)) errors["targets[" + i + "].targetId"] = "Provide the target identifier.";
                keys.Add(new FavoriteTargetKey { TargetType = type, TargetId = (target.TargetId ?? String.Empty).Trim() });
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<Favorite> saved = await _Services.Database.Favorites.StateAsync(userId, keys, token).ConfigureAwait(false);
            Dictionary<string, Favorite> byTarget = new Dictionary<string, Favorite>(StringComparer.Ordinal);
            foreach (Favorite favorite in saved) byTarget[favorite.TargetType + ":" + favorite.TargetId] = favorite;
            FavoriteStateResponse response = new FavoriteStateResponse();
            foreach (FavoriteTargetKey key in keys)
            {
                byTarget.TryGetValue(key.TargetType + ":" + key.TargetId, out Favorite? found);
                response.Items.Add(new FavoriteStateItem { TargetType = key.TargetType, TargetId = key.TargetId, Saved = found != null, FavoriteId = found?.Id });
            }
            return response;
        }

        /// <summary>
        /// GET /api/v1/me/save-collections and POST .../enumerate: the caller's collections with item counts.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<FavoriteCollection>> ListCollectionsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            string userId = actor.RequireUserId();
            EnumerationRequest request = _Services.Enumeration.Prepare(query, SaveCollections.Collections, false, userId);
            EnumerationResult<FavoriteCollection> page = await _Services.Database.Favorites.EnumerateCollectionsAsync(request, userId, token).ConfigureAwait(false);
            await _Services.Metadata.AttachAsync(TargetTypeEnum.FavoriteCollection, page.Objects, c => c.Id, false, token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// POST /api/v1/me/save-collections: create a collection at the end of the caller's order.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The collection.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 402 MembershipRequired, 404 NotAvailable, 409 DuplicateName or LimitReached.</exception>
        public async Task<FavoriteCollection> CreateCollectionAsync(ContentActor actor, FavoriteCollectionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            string userId = actor.RequireUserId();
            RequireWriteTier(actor);
            string name = CleanName(request.Name);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.FavoriteCollection), token, TargetTypeEnum.FavoriteCollection).ConfigureAwait(false);
            await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.FavoriteCollection, new Dictionary<string, string?> { { "name", name } }, token).ConfigureAwait(false);
            long count = await _Services.Database.Favorites.CountCollectionsAsync(userId, token).ConfigureAwait(false);
            int max = _Services.Settings.Saves.MaxCollectionsPerMember;
            if (count >= max) throw new ApiErrorException(409, "LimitReached", "You can keep at most " + max + " collections.");
            DateTime now = DateTime.UtcNow;
            FavoriteCollection collection;
            try
            {
                collection = await _Services.Database.Favorites.CreateCollectionAsync(new FavoriteCollection
                {
                    Id = IdGenerator.Generate(CollectionIdPrefix),
                    UserId = userId,
                    Name = name,
                    CreatedUtc = now,
                    LastUpdateUtc = now
                }, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw DuplicateName();
            }
            ObjectMetadata metadata = await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.FavoriteCollection, collection.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            collection.Labels = metadata.Labels;
            collection.Tags = metadata.Tags;
            return collection;
        }

        /// <summary>
        /// PUT /api/v1/me/save-collections/{id}: rename a collection or replace its labels and tags.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Collection.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The collection.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 404 NotFound or NotAvailable, 409 DuplicateName.</exception>
        public async Task<FavoriteCollection> UpdateCollectionAsync(ContentActor actor, string id, FavoriteCollectionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            string userId = actor.RequireUserId();
            FavoriteCollection collection = await _Services.Database.Favorites.ReadCollectionAsync(userId, id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Collection");
            string? name = request.Name == null ? null : CleanName(request.Name);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.FavoriteCollection), token, TargetTypeEnum.FavoriteCollection).ConfigureAwait(false);
            if (name != null && !String.Equals(name, collection.Name, StringComparison.Ordinal))
            {
                await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.FavoriteCollection, new Dictionary<string, string?> { { "name", name } }, token).ConfigureAwait(false);
                try
                {
                    await _Services.Database.Favorites.RenameCollectionAsync(userId, collection.Id, name, DateTime.UtcNow, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    throw DuplicateName();
                }
            }
            if (request.Labels != null || request.Tags != null)
                await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.FavoriteCollection, collection.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            FavoriteCollection updated = await _Services.Database.Favorites.ReadCollectionAsync(userId, collection.Id, token).ConfigureAwait(false) ?? collection;
            ObjectMetadata metadata = await _Services.Metadata.LoadOneAsync(TargetTypeEnum.FavoriteCollection, updated.Id, false, token).ConfigureAwait(false);
            updated.Labels = metadata.Labels;
            updated.Tags = metadata.Tags;
            return updated;
        }

        /// <summary>
        /// PUT /api/v1/me/save-collections/order: every collection of the caller exactly once, in the new order.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable.</exception>
        public async Task ReorderCollectionsAsync(ContentActor actor, FavoriteCollectionOrderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            string userId = actor.RequireUserId();
            List<string> ids = request.Ids ?? new List<string>();
            int max = _Services.Settings.Saves.MaxCollectionsPerMember;
            if (ids.Count > max) throw ApiErrorException.Field("ids", "At most " + max + " collections.");
            List<string> current = await _Services.Database.Favorites.CollectionIdsAsync(userId, SavesSettingsLimit(), token).ConfigureAwait(false);
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Count != current.Count || ids.Except(current, StringComparer.Ordinal).Any())
                throw ApiErrorException.Field("ids", "List every one of your collections exactly once.");
            await _Services.Database.Favorites.ReorderCollectionsAsync(userId, ids, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/me/save-collections/{id}: delete a collection; its saves stay.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Collection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or NotAvailable.</exception>
        public async Task DeleteCollectionAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            string userId = actor.RequireUserId();
            if (String.IsNullOrWhiteSpace(id) || !await _Services.Database.Favorites.DeleteCollectionAsync(userId, id.Trim(), token).ConfigureAwait(false))
                throw ApiErrorException.NotFound("Collection");
        }

        /// <summary>
        /// Build save views: metadata and target summaries resolved for the caller (unavailable targets show no title).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="favorites">Saves (at most one page).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in the same order.</returns>
        public async Task<List<FavoriteView>> ViewsAsync(ContentActor actor, IReadOnlyList<Favorite> favorites, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(favorites);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Favorite, favorites.Select(f => f.Id), false, token).ConfigureAwait(false);
            List<FavoriteView> views = new List<FavoriteView>();
            foreach (Favorite favorite in favorites)
            {
                metadata.TryGetValue(favorite.Id, out ObjectMetadata? meta);
                views.Add(new FavoriteView
                {
                    Id = favorite.Id,
                    TargetType = favorite.TargetType,
                    TargetId = favorite.TargetId,
                    Note = favorite.Note,
                    CollectionIds = favorite.CollectionIds,
                    Target = await SummaryAsync(actor, favorite, token).ConfigureAwait(false),
                    Labels = meta?.Labels ?? new List<string>(),
                    Tags = meta?.Tags ?? new Dictionary<string, string>(),
                    CreatedUtc = favorite.CreatedUtc,
                    LastUpdateUtc = favorite.LastUpdateUtc
                });
            }
            return views;
        }

        #endregion

        #region Private-Methods

        private async Task<FavoriteTargetSummary> SummaryAsync(ContentActor actor, Favorite favorite, CancellationToken token)
        {
            SaveTargetResolver resolver = ResolverFor(favorite.TargetType);
            if (!resolver.IsSupported()) return FavoriteTargetSummary.Unavailable();
            try
            {
                FavoriteTargetSummary? summary = await resolver.ResolveAsync(actor, favorite.TargetId, token).ConfigureAwait(false);
                if (summary == null || !summary.Available) return FavoriteTargetSummary.Unavailable();
                return summary;
            }
            catch (ApiErrorException)
            {
                return FavoriteTargetSummary.Unavailable();
            }
        }

        private void RequireEnabled()
        {
            if (!_Services.Property.Property.Features.Saves) throw NotAvailable("Saves are not available in this community.");
        }

        private static void RequireWriteTier(ContentActor actor)
        {
            if (AccessDecider.Meets(AccessDecider.TierOf(actor), AccessDecider.WriteTier(actor.Access, FeatureKeyEnum.Saves))) return;
            AccessDeniedResponse body = new AccessDeniedResponse
            {
                Error = "MembershipRequired",
                Message = "Saving is for members.",
                Required = "Membership",
                Feature = AccessPolicy.FeatureKey(FeatureKeyEnum.Saves)
            };
            throw new ApiErrorException(402, "MembershipRequired", "Saving is for members.", null, body);
        }

        private int SavesSettingsLimit()
        {
            return Math.Min(1000, Math.Max(_Services.Settings.Saves.MaxCollectionsPerMember, 1) + 1);
        }

        private static ApiErrorException NotAvailable(string message)
        {
            return new ApiErrorException(404, "NotAvailable", message);
        }

        private static ApiErrorException DuplicateName()
        {
            return new ApiErrorException(409, "DuplicateName", "You already have a collection with this name.");
        }

        private static string? CleanNote(string? note)
        {
            if (String.IsNullOrWhiteSpace(note)) return null;
            string trimmed = note.Trim();
            if (trimmed.Length > Favorite.MaxNoteLength) throw ApiErrorException.Field("note", "At most " + Favorite.MaxNoteLength + " characters.");
            return trimmed;
        }

        private static string CleanName(string? name)
        {
            string trimmed = (name ?? String.Empty).Trim();
            if (trimmed.Length < 1 || trimmed.Length > FavoriteCollection.MaxNameLength)
                throw ApiErrorException.Field("name", "Use 1 to " + FavoriteCollection.MaxNameLength + " characters.");
            return trimmed;
        }

        private static FavoriteTargetTypeEnum ParseType(string? raw, string field)
        {
            if (!TryParseType(raw, out FavoriteTargetTypeEnum type))
                throw ApiErrorException.Field(field, "Use one of " + String.Join(", ", Enum.GetNames<FavoriteTargetTypeEnum>()) + ".");
            return type;
        }

        private static bool TryParseType(string? raw, out FavoriteTargetTypeEnum type)
        {
            type = FavoriteTargetTypeEnum.Lesson;
            if (String.IsNullOrWhiteSpace(raw)) return false;
            string trimmed = raw.Trim();
            if (Int32.TryParse(trimmed, out int _)) return false;
            return Enum.TryParse(trimmed, true, out type) && Enum.IsDefined(type);
        }

        #endregion
    }
}
