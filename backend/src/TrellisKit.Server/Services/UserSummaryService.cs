namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Builds <see cref="UserSummary"/> objects in batches (one users query and one role query per batch). Unknown and
    /// deleted accounts become the "Deleted member" summary.
    /// Thread safety: stateless apart from the shared driver; safe to call concurrently.
    /// </summary>
    public class UserSummaryService
    {
        #region Public-Members

        /// <summary>
        /// Profile values (card profile maps). Set by the composition root; null leaves profile empty.
        /// </summary>
        public ProfileValueService? ProfileValues { get; set; } = null;

        /// <summary>
        /// Profile field definitions (legacy rank visibility). Set by the composition root.
        /// </summary>
        public ProfileFieldService? ProfileFields { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly PropertyContext _Property;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="property">Active property.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UserSummaryService(DatabaseDriverBase database, PropertyContext property)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a role makes its holder staff (SystemAdmin, Moderator, or FeedModerator).
        /// </summary>
        /// <param name="role">Role.</param>
        /// <returns>True for staff roles.</returns>
        public static bool IsStaffRole(RoleEnum role)
        {
            return role == RoleEnum.SystemAdmin || role == RoleEnum.Moderator || role == RoleEnum.FeedModerator || role == RoleEnum.DirectoryModerator || role == RoleEnum.EventManager || role == RoleEnum.LibraryEditor || role == RoleEnum.SupportAgent || role == RoleEnum.Communicator || role == RoleEnum.ReleaseManager || role == RoleEnum.RankAwarder;
        }

        /// <summary>
        /// Build summaries for several members.
        /// </summary>
        /// <param name="userIds">Member identifiers (nulls and duplicates are ignored).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summaries keyed by every distinct non-empty requested identifier.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        public Task<Dictionary<string, UserSummary>> BuildAsync(IEnumerable<string?> userIds, CancellationToken token = default)
        {
            return BuildAsync(userIds, ProfileViewerEnum.Member, token);
        }

        /// <summary>
        /// Build summaries for several members as seen by a viewer (card profile fields are filtered by visibility).
        /// </summary>
        /// <param name="userIds">Member identifiers (nulls and duplicates are ignored).</param>
        /// <param name="viewer">Viewer: Anonymous for signed-out surfaces, Staff for admin lists; Member otherwise.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summaries keyed by every distinct non-empty requested identifier.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        public async Task<Dictionary<string, UserSummary>> BuildAsync(IEnumerable<string?> userIds, ProfileViewerEnum viewer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Select(i => i!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, UserSummary> result = new Dictionary<string, UserSummary>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            List<User> users = await _Database.Users.ReadManyAsync(ids, token).ConfigureAwait(false);
            HashSet<string> staff = await StaffAsync(ids, token).ConfigureAwait(false);
            Dictionary<string, User> byId = users.ToDictionary(u => u.Id, StringComparer.Ordinal);
            Dictionary<string, Dictionary<string, object?>> cards = ProfileValues == null
                ? new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal)
                : await ProfileValues.CardsAsync(users.Where(u => !u.IsDeleted).Select(u => u.Id).ToList(), viewer, token).ConfigureAwait(false);
            foreach (string id in ids)
            {
                result[id] = byId.TryGetValue(id, out User? user) ? From(user, staff.Contains(id), viewer) : UserSummary.Deleted();
                if (cards.TryGetValue(id, out Dictionary<string, object?>? card) && !result[id].IsDeleted) result[id].Profile = card;
            }
            return result;
        }

        /// <summary>
        /// Build one summary.
        /// </summary>
        /// <param name="userId">Member identifier, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary (the deleted summary when null or unknown).</returns>
        public async Task<UserSummary> BuildOneAsync(string? userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) return UserSummary.Deleted();
            Dictionary<string, UserSummary> map = await BuildAsync(new[] { userId }, token).ConfigureAwait(false);
            return map[userId];
        }

        /// <summary>
        /// Summary of a loaded member.
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="isStaff">Whether the member holds a staff role.</param>
        /// <returns>Summary (the deleted summary for a deleted account).</returns>
        /// <exception cref="ArgumentNullException">The member is null.</exception>
        public UserSummary From(User user, bool isStaff)
        {
            return From(user, isStaff, ProfileViewerEnum.Member);
        }

        /// <summary>
        /// Summary of a loaded member as seen by a viewer (legacy rank fields follow the rank field's visibility; the
        /// card profile map is filled by <see cref="BuildAsync(IEnumerable{string}, ProfileViewerEnum, CancellationToken)"/>).
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="isStaff">Whether the member holds a staff role.</param>
        /// <param name="viewer">Viewer.</param>
        /// <returns>Summary (the deleted summary for a deleted account).</returns>
        /// <exception cref="ArgumentNullException">The member is null.</exception>
        public UserSummary From(User user, bool isStaff, ProfileViewerEnum viewer)
        {
            ArgumentNullException.ThrowIfNull(user);
            if (user.IsDeleted) return UserSummary.Deleted();
            bool rankEnabled = ProfileFields == null ? _Property.Property.Profile.Rank.Enabled : ProfileFields.LegacyField("rank", viewer) != null;
            return new UserSummary
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                Handle = user.Handle,
                AvatarUrl = MemberViewBuilder.AvatarUrl(user),
                Rank = rankEnabled ? user.Rank : null,
                RankLevel = rankEnabled ? user.RankLevel : 0,
                IsStaff = isStaff,
                IsDeleted = false
            };
        }

        /// <summary>
        /// Which of the members hold a staff role.
        /// </summary>
        /// <param name="userIds">Member identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers of staff members.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        public async Task<HashSet<string>> StaffAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<RoleAssignment> roles = await _Database.RoleAssignments.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false);
            return new HashSet<string>(roles.Where(r => IsStaffRole(r.Role)).Select(r => r.UserId), StringComparer.Ordinal);
        }

        /// <summary>
        /// Which of the members hold SystemAdmin.
        /// </summary>
        /// <param name="userIds">Member identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers of SystemAdmin members.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        public async Task<HashSet<string>> SystemAdminsAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<RoleAssignment> roles = await _Database.RoleAssignments.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false);
            return new HashSet<string>(roles.Where(r => r.Role == RoleEnum.SystemAdmin).Select(r => r.UserId), StringComparer.Ordinal);
        }

        #endregion
    }
}
