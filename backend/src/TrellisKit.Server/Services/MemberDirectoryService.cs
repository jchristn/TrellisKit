namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Member enumeration with profile filters (POST /api/v1/members/enumerate; docs/MULTIPROPERTY_CONTRACT.md section
    /// 9.12): active members, search over display name, handle, and searchable fields the caller can see, and
    /// filters.profile on filterable fields the caller can see.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MemberDirectoryService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly EnumerationService _Enumeration;
        private readonly UserSummaryService _Summaries;
        private readonly ProfileValueService _Profiles;
        private readonly MetadataService _Metadata;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <param name="summaries">UserSummary builder.</param>
        /// <param name="profiles">Profile values (filters).</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MemberDirectoryService(DatabaseDriverBase database, EnumerationService enumeration, UserSummaryService summaries, ProfileValueService profiles, MetadataService metadata)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
            _Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/members/enumerate (and GET /api/v1/members).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Enumeration query; filters.profile.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of members (UserSummary with labels and tags).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<MemberListItem>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            bool staff = actor.IsStaffFor(TargetTypeEnum.User);
            ProfileViewerEnum viewer = staff ? ProfileViewerEnum.Staff : actor.CountsAsMember ? ProfileViewerEnum.Member : ProfileViewerEnum.SignedIn;
            EnumerationRequest request = _Enumeration.Prepare(query, ProfileCollections.Members, staff);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            JsonElement? profile = request.Query.Filters.TryGetValue("profile", out JsonElement element) ? element : null;
            UserFilter filter = new UserFilter
            {
                Banned = false,
                RequireHandle = true,
                MembersOrStaffOnly = MembershipRequired(actor.Access),
                Profile = await _Profiles.CompileFiltersAsync(profile, viewer, errors, token).ConfigureAwait(false),
                MemberSearch = request.Query.Search,
                SearchFieldIds = await _Profiles.SearchableFieldIdsAsync(viewer, token).ConfigureAwait(false)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<User> page = await _Database.Users.EnumerateAsync(request, filter, DateTime.UtcNow, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> summaries = await _Summaries.BuildAsync(page.Objects.Select(u => (string?)u.Id), viewer, token).ConfigureAwait(false);
            List<MemberListItem> items = page.Objects.Select(u =>
            {
                UserSummary s = summaries[u.Id];
                return new MemberListItem
                {
                    Id = s.Id, DisplayName = s.DisplayName, Handle = s.Handle, AvatarUrl = s.AvatarUrl, Rank = s.Rank, RankLevel = s.RankLevel,
                    IsStaff = s.IsStaff, IsDeleted = s.IsDeleted, Profile = s.Profile
                };
            }).ToList();
            await _Metadata.AttachAsync(TargetTypeEnum.User, items, m => m.Id ?? String.Empty, staff, token).ConfigureAwait(false);
            return page.With(items);
        }

        /// <summary>
        /// Whether the access model requires a membership to use the member directory (FullyGated, or PartiallyGated
        /// with directory or members gated); the directory then lists only members and staff.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <returns>True when only members and staff are listed.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static bool MembershipRequired(AccessPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            return policy.Model == AccessModelEnum.FullyGated || policy.IsGated(FeatureKeyEnum.Directory) || policy.IsGated(FeatureKeyEnum.Members);
        }

        #endregion
    }
}
