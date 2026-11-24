namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared member methods.
    /// </summary>
    internal abstract class UserMethodsBase : MethodsBase, IUserMethods
    {
        private const string _Columns =
            "id, email, emailverified, emailverifiedutc, displayname, handle, avatarfile, bio, rank, ranklevel, affiliation, city, region, country, " +
            "goalsother, ageattestedutc, canposttofeed, canpromotetofeed, canuploadvideo, cansenddirectmessages, ismuted, muteduntilutc, mutedreason, " +
            "isbanned, bannedreason, bannedutc, isdeleted, deletedutc, lastseenutc, stripecustomerid, storeaccounttoken, failedlogincount, " +
            "failedloginwindowutc, lockeduntilutc, createdutc, updatedutc, palettepreset";

        private const string _Duplicate = "A member with that email, handle, or store account token";

        protected UserMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<User> CreateAsync(User user, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            await UniqueAsync("INSERT INTO users (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + Values(user) + ");", _Duplicate, token).ConfigureAwait(false);
            return user;
        }

        public Task<User?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return ReadOneAsync("id = " + D.Str(id), token);
        }

        public async Task<List<User>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct().ToList();
            if (list.Count == 0) return new List<User>();
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM users WHERE " + Scope() + " AND id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<User?> ReadByEmailAsync(string email, CancellationToken token = default)
        {
            RequireText(email, nameof(email));
            return ReadOneAsync(D.EqualsIgnoreCase("email", email.Trim()), token);
        }

        public Task<User?> ReadByHandleAsync(string handle, CancellationToken token = default)
        {
            RequireText(handle, nameof(handle));
            return ReadOneAsync(D.EqualsIgnoreCase("handle", handle.Trim()), token);
        }

        public Task<User?> ReadByStoreAccountTokenAsync(string storeAccountToken, CancellationToken token = default)
        {
            RequireText(storeAccountToken, nameof(storeAccountToken));
            return ReadOneAsync(D.EqualsIgnoreCase("storeaccounttoken", storeAccountToken.Trim()), token);
        }

        public Task<User?> ReadByStripeCustomerIdAsync(string customerId, CancellationToken token = default)
        {
            RequireText(customerId, nameof(customerId));
            return ReadOneAsync("stripecustomerid = " + D.Str(customerId), token);
        }

        public async Task<User> UpdateAsync(User user, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(user);
            RequireText(user.Id, nameof(user.Id));
            string sql = "UPDATE users SET " +
                "email = " + D.Str(user.Email) + ", " +
                "emailverified = " + D.Bool(user.EmailVerified) + ", " +
                "emailverifiedutc = " + D.Ts(user.EmailVerifiedUtc) + ", " +
                "displayname = " + D.Str(user.DisplayName) + ", " +
                "handle = " + D.Str(user.Handle) + ", " +
                "avatarfile = " + D.Str(user.AvatarFile) + ", " +
                "bio = " + D.Str(user.Bio) + ", " +
                "rank = " + D.Str(user.Rank) + ", " +
                "ranklevel = " + D.Num(user.RankLevel) + ", " +
                "affiliation = " + D.Str(user.Affiliation) + ", " +
                "city = " + D.Str(user.City) + ", " +
                "region = " + D.Str(user.Region) + ", " +
                "country = " + D.Str(user.Country) + ", " +
                "goalsother = " + D.Str(user.GoalsOther) + ", " +
                "ageattestedutc = " + D.Ts(user.AgeAttestedUtc) + ", " +
                "canposttofeed = " + D.Bool(user.CanPostToFeed) + ", " +
                "canpromotetofeed = " + D.Bool(user.CanPromoteToFeed) + ", " +
                "canuploadvideo = " + D.Bool(user.CanUploadVideo) + ", " +
                "cansenddirectmessages = " + D.Bool(user.CanSendDirectMessages) + ", " +
                "ismuted = " + D.Bool(user.IsMuted) + ", " +
                "muteduntilutc = " + D.Ts(user.MutedUntilUtc) + ", " +
                "mutedreason = " + D.Str(user.MutedReason) + ", " +
                "isbanned = " + D.Bool(user.IsBanned) + ", " +
                "bannedreason = " + D.Str(user.BannedReason) + ", " +
                "bannedutc = " + D.Ts(user.BannedUtc) + ", " +
                "isdeleted = " + D.Bool(user.IsDeleted) + ", " +
                "deletedutc = " + D.Ts(user.DeletedUtc) + ", " +
                "lastseenutc = " + D.Ts(user.LastSeenUtc) + ", " +
                "stripecustomerid = " + D.Str(user.StripeCustomerId) + ", " +
                "storeaccounttoken = " + D.Str(user.StoreAccountToken) + ", " +
                "failedlogincount = " + D.Num(user.FailedLoginCount) + ", " +
                "failedloginwindowutc = " + D.Ts(user.FailedLoginWindowUtc) + ", " +
                "lockeduntilutc = " + D.Ts(user.LockedUntilUtc) + ", " +
                "updatedutc = " + D.Ts(user.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(user.Id) + ";";
            await UniqueAsync(sql, _Duplicate, token).ConfigureAwait(false);
            return user;
        }

        public async Task TouchLastSeenAsync(string id, DateTime seenUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE users SET lastseenutc = " + D.Ts(seenUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task SetPalettePresetAsync(string id, string? presetId, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE users SET palettepreset = " + D.Str(presetId) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public Task<EnumerationResult<User>> EnumerateAsync(EnumerationRequest request, UserFilter filter, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!filter.IncludeDeleted) conditions.Add("u.isdeleted = " + D.Bool(false));
            if (filter.Rank != null) conditions.Add("u.rank = " + D.Str(filter.Rank));
            if (filter.Banned != null) conditions.Add("u.isbanned = " + D.Bool(filter.Banned.Value));
            if (filter.Role != null) conditions.Add("EXISTS (SELECT 1 FROM roleassignments ra WHERE " + Scope("ra") + " AND ra.userid = u.id AND ra.role = " + D.Enum(filter.Role.Value) + ")");
            if (filter.Membership != null)
            {
                string any = ActiveEntitlementExists("u.id", nowUtc, null);
                string paid = ActiveEntitlementExists("u.id", nowUtc, "source = " + D.Enum(MembershipSourceEnum.Subscription));
                switch (filter.Membership.Value)
                {
                    case MembershipFilterEnum.Active:
                        conditions.Add(any);
                        break;
                    case MembershipFilterEnum.Inactive:
                        conditions.Add("NOT " + any);
                        break;
                    case MembershipFilterEnum.Paid:
                        conditions.Add(paid);
                        break;
                    case MembershipFilterEnum.Free:
                        conditions.Add(any + " AND NOT " + paid);
                        break;
                }
            }
            if (filter.RequireHandle) conditions.Add("u.handle IS NOT NULL AND u.handle <> " + D.Str(String.Empty));
            if (filter.MembersOrStaffOnly)
                conditions.Add("(" + ActiveEntitlementExists("u.id", nowUtc, null) + " OR EXISTS (SELECT 1 FROM roleassignments sr WHERE " + Scope("sr") + " AND sr.userid = u.id AND sr.role <> " + D.Enum(RoleEnum.User) + "))");
            foreach (TrellisKit.Core.Models.ProfileFilterClause clause in filter.Profile) conditions.Add(ProfileCondition(clause));
            if (!String.IsNullOrWhiteSpace(filter.MemberSearch))
            {
                string text = filter.MemberSearch.Trim();
                List<string> any = new List<string> { D.Contains("COALESCE(u.displayname, '')", text), D.Contains("COALESCE(u.handle, '')", text) };
                List<string> words = TrellisKit.Core.Helpers.DisplayNameWords.Split(text);
                if (filter.SearchFieldIds.Count > 0 && words.Count > 0)
                    any.Add("EXISTS (SELECT 1 FROM profilesearchwords sw WHERE " + Scope("sw") + " AND sw.userid = u.id AND sw.fieldid IN " + D.InList(filter.SearchFieldIds) + " AND " + D.StartsWith("sw.word", words[0]) + ")");
                conditions.Add("(" + String.Join(" OR ", any) + ")");
            }
            string select = String.Join(", ", _Columns.Split(',').Select(c => "u." + c.Trim()));
            return EnumerateAsync(request, select, "users u", conditions, Map, token);
        }

        public Task<long> CountAsync(DateTime? createdSinceUtc, CancellationToken token = default)
        {
            string sql = "SELECT COUNT(*) AS cnt FROM users WHERE " + Scope() + " AND isdeleted = " + D.Bool(false) +
                (createdSinceUtc == null ? String.Empty : " AND createdutc >= " + D.Ts(createdSinceUtc.Value)) + ";";
            return CountAsync(sql, token);
        }

        private string ProfileCondition(TrellisKit.Core.Models.ProfileFilterClause clause)
        {
            return ProfileFilterSql.Condition(D, Pid, "u.id", clause);
        }

        private async Task<User?> ReadOneAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM users WHERE " + Scope() + " AND (" + condition + ");", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        private string Values(User u)
        {
            return String.Join(", ", new string[]
            {
                D.Str(u.Id), D.Str(u.Email), D.Bool(u.EmailVerified), D.Ts(u.EmailVerifiedUtc), D.Str(u.DisplayName), D.Str(u.Handle),
                D.Str(u.AvatarFile), D.Str(u.Bio), D.Str(u.Rank), D.Num(u.RankLevel), D.Str(u.Affiliation), D.Str(u.City), D.Str(u.Region),
                D.Str(u.Country), D.Str(u.GoalsOther), D.Ts(u.AgeAttestedUtc), D.Bool(u.CanPostToFeed), D.Bool(u.CanPromoteToFeed),
                D.Bool(u.CanUploadVideo), D.Bool(u.CanSendDirectMessages), D.Bool(u.IsMuted), D.Ts(u.MutedUntilUtc), D.Str(u.MutedReason),
                D.Bool(u.IsBanned), D.Str(u.BannedReason), D.Ts(u.BannedUtc), D.Bool(u.IsDeleted), D.Ts(u.DeletedUtc), D.Ts(u.LastSeenUtc),
                D.Str(u.StripeCustomerId), D.Str(u.StoreAccountToken), D.Num(u.FailedLoginCount), D.Ts(u.FailedLoginWindowUtc),
                D.Ts(u.LockedUntilUtc), D.Ts(u.CreatedUtc), D.Ts(u.UpdatedUtc), D.Str(u.PalettePreset)
            });
        }

        internal static User Map(DataRow row)
        {
            return new User
            {
                Id = RowReader.GetString(row, "id"),
                Email = RowReader.GetNullableString(row, "email"),
                EmailVerified = RowReader.GetBool(row, "emailverified"),
                EmailVerifiedUtc = RowReader.GetNullableDateTime(row, "emailverifiedutc"),
                DisplayName = RowReader.GetString(row, "displayname"),
                Handle = RowReader.GetNullableString(row, "handle"),
                AvatarFile = RowReader.GetNullableString(row, "avatarfile"),
                Bio = RowReader.GetNullableString(row, "bio"),
                Rank = RowReader.GetNullableString(row, "rank"),
                RankLevel = RowReader.GetInt(row, "ranklevel"),
                Affiliation = RowReader.GetNullableString(row, "affiliation"),
                City = RowReader.GetNullableString(row, "city"),
                Region = RowReader.GetNullableString(row, "region"),
                Country = RowReader.GetNullableString(row, "country"),
                GoalsOther = RowReader.GetNullableString(row, "goalsother"),
                AgeAttestedUtc = RowReader.GetNullableDateTime(row, "ageattestedutc"),
                CanPostToFeed = RowReader.GetBool(row, "canposttofeed"),
                CanPromoteToFeed = RowReader.GetBool(row, "canpromotetofeed"),
                CanUploadVideo = RowReader.GetBool(row, "canuploadvideo"),
                CanSendDirectMessages = RowReader.GetBool(row, "cansenddirectmessages"),
                IsMuted = RowReader.GetBool(row, "ismuted"),
                MutedUntilUtc = RowReader.GetNullableDateTime(row, "muteduntilutc"),
                MutedReason = RowReader.GetNullableString(row, "mutedreason"),
                IsBanned = RowReader.GetBool(row, "isbanned"),
                BannedReason = RowReader.GetNullableString(row, "bannedreason"),
                BannedUtc = RowReader.GetNullableDateTime(row, "bannedutc"),
                IsDeleted = RowReader.GetBool(row, "isdeleted"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                LastSeenUtc = RowReader.GetNullableDateTime(row, "lastseenutc"),
                StripeCustomerId = RowReader.GetNullableString(row, "stripecustomerid"),
                StoreAccountToken = RowReader.GetString(row, "storeaccounttoken"),
                FailedLoginCount = RowReader.GetInt(row, "failedlogincount"),
                FailedLoginWindowUtc = RowReader.GetNullableDateTime(row, "failedloginwindowutc"),
                LockedUntilUtc = RowReader.GetNullableDateTime(row, "lockeduntilutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc"),
                PalettePreset = RowReader.GetNullableString(row, "palettepreset")
            };
        }

        public Task<long> CountSeenSinceAsync(DateTime sinceUtc, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM users WHERE " + Scope() + " AND isdeleted = " + D.Bool(false) + " AND lastseenutc >= " + D.Ts(sinceUtc) + ";", token);
        }
    }
}
