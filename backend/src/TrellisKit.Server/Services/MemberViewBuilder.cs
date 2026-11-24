namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Builds member-facing and admin-facing views from stored records.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public class MemberViewBuilder
    {
        #region Public-Members

        /// <summary>
        /// Public landing pages (set by the composition root): the profile page URL of public profiles and the page settings
        /// of MeResponse. Null leaves them out.
        /// </summary>
        public TrellisKit.Server.Services.PublicPages.PublicPageService? PublicPages { get; set; } = null;

        /// <summary>
        /// URL path prefix of served avatars.
        /// </summary>
        public const string AvatarPathPrefix = Constants.ApiPrefix + "/media/avatars/";

        /// <summary>
        /// Profile values (profile maps and completeness). Set by the composition root.
        /// </summary>
        public ProfileValueService? ProfileValues { get; set; } = null;

        /// <summary>
        /// Profile field definitions (legacy field visibility). Set by the composition root.
        /// </summary>
        public ProfileFieldService? ProfileFields { get; set; } = null;

        /// <summary>
        /// Supplies the gating documents a member still has to accept (MeResponse pendingDocuments; docs/TRELLISKIT.md
        /// section 14). Null leaves the list empty.
        /// </summary>
        public Func<string, CancellationToken, Task<List<DocumentSummaryView>>>? PendingDocuments { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly MembershipService _Membership;
        private readonly PropertyContext _Property;
        private readonly MetadataService _Metadata;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the builder.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="membership">Membership service.</param>
        /// <param name="property">Active property context (profile completeness rules).</param>
        /// <param name="metadata">Labels and tags (the Me payload carries the member's own).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MemberViewBuilder(DatabaseDriverBase database, MembershipService membership, PropertyContext property, MetadataService metadata)
        {
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the Me view of a member.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Me.</returns>
        /// <exception cref="ArgumentNullException">The member is null.</exception>
        public async Task<MeResponse> BuildMeAsync(User user, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            MeResponse me = new MeResponse();
            MembershipState state = await _Membership.GetStateAsync(user.Id, token).ConfigureAwait(false);
            await FillMeAsync(me, user, state, token).ConfigureAwait(false);
            return me;
        }

        /// <summary>
        /// Build the Me view of a member by identifier.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Me.</returns>
        /// <exception cref="TrellisKit.Core.Exceptions.ApiErrorException">The member does not exist.</exception>
        public async Task<MeResponse> BuildMeAsync(string userId, CancellationToken token = default)
        {
            User user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false) ?? throw TrellisKit.Core.Exceptions.ApiErrorException.NotFound("Member");
            return await BuildMeAsync(user, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Fill the Me fields of a view (also used by the admin member detail).
        /// </summary>
        /// <param name="me">Target view.</param>
        /// <param name="user">Member.</param>
        /// <param name="state">Membership state.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task FillMeAsync(MeResponse me, User user, MembershipState state, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(me);
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(state);
            DateTime now = DateTime.UtcNow;

            List<string> goals = await _Database.Goals.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            List<RankHistoryEntry> rankHistory = await _Database.RankHistory.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            List<RoleAssignment> roles = await _Database.RoleAssignments.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            List<ExternalIdentity> identities = await _Database.ExternalIdentities.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            bool hasPassword = await _Database.Credentials.ReadByUserAsync(user.Id, token).ConfigureAwait(false) != null;
            UserPermission permission = await _Database.UserPermissions.ReadAsync(user.Id, token).ConfigureAwait(false);
            if (PublicPages != null) me.PublicPage = await PublicPages.MeViewAsync(user.Id, token).ConfigureAwait(false);

            me.Id = user.Id;
            me.Email = user.Email;
            me.EmailVerified = user.EmailVerified;
            me.DisplayName = user.DisplayName;
            me.Handle = user.Handle;
            me.AvatarUrl = AvatarUrl(user);
            me.Bio = user.Bio;
            me.Rank = user.Rank;
            me.RankLevel = user.RankLevel;
            me.Affiliation = user.Affiliation;
            me.City = user.City;
            me.Region = user.Region;
            me.Country = user.Country;
            me.Goals = goals;
            me.GoalsOther = user.GoalsOther;
            me.RankHistory = rankHistory.Select(ToView).ToList();
            me.AgeAttested = user.AgeAttestedUtc != null;
            me.ProfileComplete = IsProfileComplete(user, _Property.Property);
            if (ProfileValues != null)
            {
                ProfileValueSet set = await ProfileValues.ReadAsync(user.Id, token).ConfigureAwait(false);
                bool staffView = me is AdminMemberDetail;
                me.Profile = await ProfileValues.MapAsync(set, staffView ? ProfileMapKindEnum.Staff : ProfileMapKindEnum.Me, staffView ? ProfileViewerEnum.Staff : ProfileViewerEnum.Self, token).ConfigureAwait(false);
                me.ProfileMissing = await ProfileValues.MissingAsync(user, set, token).ConfigureAwait(false);
                me.ProfileComplete = me.AgeAttested && me.ProfileMissing.Count == 0;
                ApplyLegacyRules(me, staffView ? ProfileViewerEnum.Staff : ProfileViewerEnum.Self);
            }
            me.SignInMethods = SignInMethods(hasPassword, identities);
            me.Roles = roles.Select(ToView).ToList();
            me.Flags = new MemberFlags
            {
                CanPostToFeed = user.CanPostToFeed,
                CanPromoteToFeed = user.CanPromoteToFeed,
                CanUploadVideo = user.CanUploadVideo,
                CanPost = permission.CanPost,
                CanPostMedia = permission.CanPostMedia,
                CanSendDirectMessages = user.CanSendDirectMessages,
                IsMuted = user.IsMutedAt(now),
                MutedUntilUtc = user.IsMutedAt(now) ? user.MutedUntilUtc : null
            };
            me.Membership = state.Summary;
            // docs/ACCESS_MODELS_CONTRACT.md section 5: the caller's tier under the access model, and whether the
            // client must show the paywall before content (FullyGated only). In Open the member surfaces carry an
            // inactive membership (entitlements are kept, so switching back restores members); staff views keep the truth.
            AccessPolicy access = _Property.Access;
            bool staffRole = roles.Any(r => r.Role == RoleEnum.SystemAdmin);
            bool memberView = me is not AdminMemberDetail;
            if (memberView && access.Model == AccessModelEnum.Open) me.Membership = new MembershipSummary();
            me.AccessTier = staffRole ? AccessTierEnum.Staff : access.Model != AccessModelEnum.Open && state.Summary.IsActive ? AccessTierEnum.Member : AccessTierEnum.SignedIn;
            me.MembershipRequired = access.Model == AccessModelEnum.FullyGated && me.AccessTier == AccessTierEnum.SignedIn;
            me.CreatedUtc = user.CreatedUtc;
            if (PendingDocuments != null) me.PendingDocuments = await PendingDocuments(user.Id, token).ConfigureAwait(false);
            await _Metadata.AttachOneAsync(TargetTypeEnum.User, me, user.Id, roles.Any(r => r.Role == RoleEnum.SystemAdmin), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether a profile is complete: display name and handle set, age attested, and a rank set when the property
        /// enables ranks.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="property">Active property.</param>
        /// <returns>True when complete.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static bool IsProfileComplete(User user, TrellisKit.Core.PropertyConfig.PropertyConfiguration property)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(property);
            bool rankRequired = property.Profile.Rank.Enabled;
            return !String.IsNullOrWhiteSpace(user.DisplayName)
                && !String.IsNullOrWhiteSpace(user.Handle)
                && user.AgeAttestedUtc != null
                && (!rankRequired || !String.IsNullOrWhiteSpace(user.Rank));
        }

        /// <summary>
        /// Null the legacy profile fields (rank, rankLevel, affiliation, city, region, country, goals, goalsOther,
        /// rankHistory) whose seeded field is missing, disabled, or invisible to the viewer, or all of them when
        /// profileFields.legacyFieldsMode is Off (docs/MULTIPROPERTY_CONTRACT.md section 9.14).
        /// </summary>
        /// <param name="me">View.</param>
        /// <param name="viewer">Viewer.</param>
        public void ApplyLegacyRules(MeResponse me, ProfileViewerEnum viewer)
        {
            ArgumentNullException.ThrowIfNull(me);
            if (ProfileFields == null) return;
            if (ProfileFields.LegacyField("rank", viewer) == null) { me.Rank = null; me.RankLevel = 0; }
            if (ProfileFields.LegacyField("affiliation", viewer) == null) me.Affiliation = null;
            if (ProfileFields.LegacyField("city", viewer) == null) me.City = null;
            if (ProfileFields.LegacyField("region", viewer) == null) me.Region = null;
            if (ProfileFields.LegacyField("country", viewer) == null) me.Country = null;
            if (ProfileFields.LegacyField("goals", viewer) == null) { me.Goals = new List<string>(); me.GoalsOther = null; }
            if (ProfileFields.LegacyField("rankHistory", viewer) == null) me.RankHistory = new List<RankHistoryView>();
        }

        /// <summary>
        /// Sign-in methods from a password flag and linked identities.
        /// </summary>
        /// <param name="hasPassword">Whether a password is set.</param>
        /// <param name="identities">Linked identities.</param>
        /// <returns>Methods.</returns>
        public static List<SignInMethodEnum> SignInMethods(bool hasPassword, List<ExternalIdentity> identities)
        {
            List<SignInMethodEnum> methods = new List<SignInMethodEnum>();
            if (hasPassword) methods.Add(SignInMethodEnum.Email);
            foreach (ExternalIdentity identity in identities.OrderBy(i => i.Provider))
            {
                SignInMethodEnum method = (SignInMethodEnum)Enum.Parse(typeof(SignInMethodEnum), identity.Provider.ToString());
                if (!methods.Contains(method)) methods.Add(method);
            }
            return methods;
        }

        /// <summary>
        /// Avatar URL path of a member, or null.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <returns>URL path, or null.</returns>
        public static string? AvatarUrl(User user)
        {
            if (user == null || String.IsNullOrEmpty(user.AvatarFile)) return null;
            return AvatarPathPrefix + user.AvatarFile;
        }

        /// <summary>
        /// Public profile view with the profile map and legacy fields for a viewer.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="viewer">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The member is null.</exception>
        public async Task<PublicProfileResponse> BuildPublicAsync(User user, ProfileViewerEnum viewer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            List<RankHistoryEntry> rankHistory = await _Database.RankHistory.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            PublicProfileResponse view = ToPublic(user, rankHistory);
            if (PublicPages != null) view.PublicUrl = await PublicPages.ProfileUrlAsync(user, token).ConfigureAwait(false);
            if (ProfileValues == null || ProfileFields == null) return view;
            ProfileValueSet set = await ProfileValues.ReadAsync(user.Id, token).ConfigureAwait(false);
            view.Profile = await ProfileValues.MapAsync(set, ProfileMapKindEnum.Profile, viewer, token).ConfigureAwait(false);
            ProfileField? rank = ProfileFields.LegacyField("rank", viewer);
            if (rank == null) { view.Rank = null; view.RankLevel = 0; }
            if (ProfileFields.LegacyField("affiliation", viewer) == null) view.Affiliation = null;
            if (ProfileFields.LegacyField("city", viewer) == null) view.City = null;
            if (ProfileFields.LegacyField("region", viewer) == null) view.Region = null;
            if (ProfileFields.LegacyField("country", viewer) == null) view.Country = null;
            if (ProfileFields.LegacyField("rankHistory", viewer) == null) view.RankHistory = new List<RankHistoryView>();
            ProfileField? bio = ProfileFields.Snapshot.FirstOrDefault(f => f.BuiltIn && f.Key == "bio");
            if (bio != null && (!bio.Enabled || !bio.ShowOnProfile || !ProfileValueService.IsVisible(bio.Visibility, viewer))) view.Bio = null;
            return view;
        }

        /// <summary>
        /// Public profile view.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="rankHistory">Rank history.</param>
        /// <returns>View.</returns>
        public static PublicProfileResponse ToPublic(User user, List<RankHistoryEntry> rankHistory)
        {
            return new PublicProfileResponse
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                Handle = user.Handle,
                AvatarUrl = AvatarUrl(user),
                Bio = user.Bio,
                Rank = user.Rank,
                RankLevel = user.RankLevel,
                Affiliation = user.Affiliation,
                City = user.City,
                Region = user.Region,
                Country = user.Country,
                RankHistory = rankHistory.Select(ToView).ToList(),
                CreatedUtc = user.CreatedUtc
            };
        }

        /// <summary>
        /// Rank history view.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <returns>View.</returns>
        public static RankHistoryView ToView(RankHistoryEntry entry)
        {
            return new RankHistoryView
            {
                Id = entry.Id,
                Rank = entry.Rank,
                AwardedByName = entry.AwardedByName,
                AwardedByUserId = entry.AwardedByUserId,
                Affiliation = entry.Affiliation,
                DateAwarded = entry.DateAwarded
            };
        }

        /// <summary>
        /// Role view.
        /// </summary>
        /// <param name="assignment">Assignment.</param>
        /// <returns>View.</returns>
        public static RoleView ToView(RoleAssignment assignment)
        {
            return new RoleView { Role = assignment.Role, ResourceType = assignment.ResourceType, ResourceId = assignment.ResourceId };
        }

        /// <summary>
        /// Session view.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <returns>View.</returns>
        public static SessionView ToView(AuthSession session)
        {
            return new SessionView
            {
                Id = session.Id,
                Client = session.ClientType,
                CreatedUtc = session.CreatedUtc,
                LastUsedUtc = session.LastUsedUtc,
                ExpiresUtc = session.ExpiresUtc,
                SourceIp = session.SourceIp,
                UserAgent = session.UserAgent
            };
        }

        /// <summary>
        /// Admin subscription item.
        /// </summary>
        /// <param name="subscription">Subscription.</param>
        /// <param name="user">Member, or null when unknown.</param>
        /// <returns>Item.</returns>
        public static AdminSubscriptionItem ToItem(Subscription subscription, User? user)
        {
            AdminSubscriptionItem item = new AdminSubscriptionItem();
            FillItem(item, subscription, user);
            return item;
        }

        /// <summary>
        /// Fill an admin subscription item (also used by the detail).
        /// </summary>
        /// <param name="item">Target.</param>
        /// <param name="subscription">Subscription.</param>
        /// <param name="user">Member, or null.</param>
        public static void FillItem(AdminSubscriptionItem item, Subscription subscription, User? user)
        {
            item.Id = subscription.Id;
            item.UserId = subscription.UserId;
            item.MemberDisplayName = user?.DisplayName;
            item.MemberEmail = user?.Email;
            item.Store = subscription.Store;
            item.ProductId = subscription.ProductId;
            item.Status = subscription.Status;
            item.CurrentPeriodStartUtc = subscription.CurrentPeriodStartUtc;
            item.CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc;
            item.AutoRenew = subscription.AutoRenew;
            item.Environment = subscription.Environment;
            item.CreatedUtc = subscription.CreatedUtc;
            item.LastEventUtc = subscription.LastEventUtc;
        }

        /// <summary>
        /// Comp view.
        /// </summary>
        /// <param name="entitlement">AdminComp entitlement.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>View.</returns>
        public static CompView ToComp(Entitlement entitlement, DateTime nowUtc)
        {
            GrantTypeEnum grantType = entitlement.GrantType ?? (entitlement.EndsUtc == null ? GrantTypeEnum.Lifetime : GrantTypeEnum.FixedEndDate);
            int months = entitlement.EndsUtc == null ? 0 : (int)Math.Round((entitlement.EndsUtc.Value - entitlement.StartsUtc).TotalDays / 30.4375);
            return new CompView
            {
                Id = entitlement.Id,
                GrantType = grantType,
                GrantSummary = SignupCodeService.GrantSummary(grantType, months, entitlement.EndsUtc),
                StartsUtc = entitlement.StartsUtc,
                EndsUtc = entitlement.EndsUtc,
                Reason = entitlement.Reason,
                GrantedByUserId = entitlement.GrantedByUserId,
                RevokedUtc = entitlement.RevokedUtc,
                RevokeReason = entitlement.RevokeReason,
                Status = entitlement.StatusAt(nowUtc),
                CreatedUtc = entitlement.CreatedUtc
            };
        }

        /// <summary>
        /// Redemption view.
        /// </summary>
        /// <param name="redemption">Redemption.</param>
        /// <param name="user">Member, or null.</param>
        /// <param name="code">Code, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>View.</returns>
        public static RedemptionView ToView(SignupCodeRedemption redemption, User? user, SignupCode? code, DateTime nowUtc)
        {
            return new RedemptionView
            {
                Id = redemption.Id,
                SignupCodeId = redemption.SignupCodeId,
                Code = code?.Code,
                UserId = redemption.UserId,
                MemberDisplayName = user?.DisplayName,
                MemberEmail = user?.Email,
                RedeemedUtc = redemption.RedeemedUtc,
                MembershipEndsUtc = redemption.MembershipEndsUtc,
                IsNewAccount = redemption.IsNewAccount,
                Status = redemption.StatusAt(nowUtc),
                ConvertedToPaid = redemption.ConvertedToPaid,
                RevokedUtc = redemption.RevokedUtc,
                RevokeReason = redemption.RevokeReason
            };
        }

        #endregion
    }
}
