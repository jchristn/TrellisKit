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
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared platform administrator methods (deployment-level tables; labels and tags under the platform realm).
    /// </summary>
    internal abstract class PlatformAdminMethodsBase : MethodsBase, IPlatformAdminMethods
    {
        private const string _Columns = "id, email, displayname, passwordhash, invitetokenhash, inviteexpiresutc, lastloginutc, failedlogincount, lockeduntilutc, removedutc, createdutc, updatedutc";
        private const string _SessionColumns = "id, adminid, tokenhash, sourceip, useragent, createdutc, lastusedutc, expiresutc, revokedutc";

        protected override bool ScopesCollectionTable
        {
            get { return false; }
        }

        protected PlatformAdminMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateAsync(PlatformAdmin admin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(admin);
            await UniqueAsync("INSERT INTO platformadmins (" + _Columns + ") VALUES (" + D.Str(admin.Id) + ", " + D.Str(admin.Email.Trim()) + ", " + D.Str(admin.DisplayName) + ", " +
                D.Str(admin.PasswordHash) + ", " + D.Str(admin.InviteTokenHash) + ", " + D.Ts(admin.InviteExpiresUtc) + ", " + D.Ts(admin.LastLoginUtc) + ", " +
                D.Num(admin.FailedLoginCount) + ", " + D.Ts(admin.LockedUntilUtc) + ", " + D.Ts(admin.RemovedUtc) + ", " + D.Ts(admin.CreatedUtc) + ", " + D.Ts(admin.UpdatedUtc) + ");",
                "A platform administrator with that email", token).ConfigureAwait(false);
        }

        public Task<PlatformAdmin?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return ReadOneAsync("id = " + D.Str(id), token);
        }

        public Task<PlatformAdmin?> ReadByEmailAsync(string email, CancellationToken token = default)
        {
            RequireText(email, nameof(email));
            return ReadOneAsync(D.EqualsIgnoreCase("email", email.Trim()) + " AND removedutc IS NULL", token);
        }

        public Task<PlatformAdmin?> ReadByInviteTokenHashAsync(string inviteTokenHash, CancellationToken token = default)
        {
            RequireText(inviteTokenHash, nameof(inviteTokenHash));
            return ReadOneAsync("invitetokenhash = " + D.Str(inviteTokenHash) + " AND removedutc IS NULL", token);
        }

        public async Task UpdateAsync(PlatformAdmin admin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(admin);
            RequireText(admin.Id, nameof(admin.Id));
            await UniqueAsync("UPDATE platformadmins SET email = " + D.Str(admin.Email.Trim()) + ", displayname = " + D.Str(admin.DisplayName) + ", passwordhash = " + D.Str(admin.PasswordHash) +
                ", invitetokenhash = " + D.Str(admin.InviteTokenHash) + ", inviteexpiresutc = " + D.Ts(admin.InviteExpiresUtc) + ", lastloginutc = " + D.Ts(admin.LastLoginUtc) +
                ", failedlogincount = " + D.Num(admin.FailedLoginCount) + ", lockeduntilutc = " + D.Ts(admin.LockedUntilUtc) + ", removedutc = " + D.Ts(admin.RemovedUtc) +
                ", updatedutc = " + D.Ts(admin.UpdatedUtc) + " WHERE id = " + D.Str(admin.Id) + ";", "A platform administrator with that email", token).ConfigureAwait(false);
        }

        public Task<EnumerationResult<PlatformAdmin>> EnumerateAsync(EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "pa.removedutc IS NULL" };
            string select = String.Join(", ", _Columns.Split(", ").Select(c => "pa." + c));
            return EnumerateAsync(request, select, "platformadmins pa", conditions, Map, token);
        }

        public Task<long> CountActiveAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM platformadmins WHERE removedutc IS NULL;", token);
        }

        public async Task CreateSessionAsync(PlatformSession session, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            await QueryAsync("INSERT INTO platformsessions (" + _SessionColumns + ") VALUES (" + D.Str(session.Id) + ", " + D.Str(session.AdminId) + ", " + D.Str(session.TokenHash) + ", " +
                D.Str(session.SourceIp) + ", " + D.Str(session.UserAgent) + ", " + D.Ts(session.CreatedUtc) + ", " + D.Ts(session.LastUsedUtc) + ", " + D.Ts(session.ExpiresUtc) + ", " +
                D.Ts(session.RevokedUtc) + ");", token).ConfigureAwait(false);
        }

        public async Task<PlatformSession?> ReadActiveSessionAsync(string tokenHash, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(tokenHash, nameof(tokenHash));
            DataTable table = await QueryAsync("SELECT " + _SessionColumns + " FROM platformsessions WHERE tokenhash = " + D.Str(tokenHash) +
                " AND revokedutc IS NULL AND expiresutc > " + D.Ts(nowUtc) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapSession(table.Rows[0]);
        }

        public async Task TouchSessionAsync(string sessionId, DateTime usedUtc, CancellationToken token = default)
        {
            RequireText(sessionId, nameof(sessionId));
            await QueryAsync("UPDATE platformsessions SET lastusedutc = " + D.Ts(usedUtc) + " WHERE id = " + D.Str(sessionId) + ";", token).ConfigureAwait(false);
        }

        public async Task RevokeSessionAsync(string sessionId, DateTime revokedUtc, CancellationToken token = default)
        {
            RequireText(sessionId, nameof(sessionId));
            await QueryAsync("UPDATE platformsessions SET revokedutc = " + D.Ts(revokedUtc) + " WHERE id = " + D.Str(sessionId) + " AND revokedutc IS NULL;", token).ConfigureAwait(false);
        }

        public async Task RevokeSessionsForAdminAsync(string adminId, DateTime revokedUtc, CancellationToken token = default)
        {
            RequireText(adminId, nameof(adminId));
            await QueryAsync("UPDATE platformsessions SET revokedutc = " + D.Ts(revokedUtc) + " WHERE adminid = " + D.Str(adminId) + " AND revokedutc IS NULL;", token).ConfigureAwait(false);
        }

        public async Task<int> PruneSessionsAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM platformsessions WHERE expiresutc < " + D.Ts(olderThanUtc) + " RETURNING 1 AS n;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private async Task<PlatformAdmin?> ReadOneAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM platformadmins WHERE " + condition + " LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        private static PlatformAdmin Map(DataRow row)
        {
            return new PlatformAdmin
            {
                Id = RowReader.GetString(row, "id"),
                Email = RowReader.GetString(row, "email"),
                DisplayName = RowReader.GetString(row, "displayname"),
                PasswordHash = RowReader.GetNullableString(row, "passwordhash"),
                InviteTokenHash = RowReader.GetNullableString(row, "invitetokenhash"),
                InviteExpiresUtc = RowReader.GetNullableDateTime(row, "inviteexpiresutc"),
                LastLoginUtc = RowReader.GetNullableDateTime(row, "lastloginutc"),
                FailedLoginCount = RowReader.GetInt(row, "failedlogincount"),
                LockedUntilUtc = RowReader.GetNullableDateTime(row, "lockeduntilutc"),
                RemovedUtc = RowReader.GetNullableDateTime(row, "removedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static PlatformSession MapSession(DataRow row)
        {
            return new PlatformSession
            {
                Id = RowReader.GetString(row, "id"),
                AdminId = RowReader.GetString(row, "adminid"),
                TokenHash = RowReader.GetString(row, "tokenhash"),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                UserAgent = RowReader.GetNullableString(row, "useragent"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUsedUtc = RowReader.GetDateTime(row, "lastusedutc"),
                ExpiresUtc = RowReader.GetDateTime(row, "expiresutc"),
                RevokedUtc = RowReader.GetNullableDateTime(row, "revokedutc")
            };
        }
    }
}
