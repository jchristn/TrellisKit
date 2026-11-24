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
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared session methods.
    /// </summary>
    internal abstract class AuthSessionMethodsBase : MethodsBase, IAuthSessionMethods
    {
        protected AuthSessionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<AuthSession> CreateAsync(AuthSession session, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            string sql = "INSERT INTO authsessions (propertyid, id, userid, clienttype, principalkind, sourceip, useragent, createdutc, lastusedutc, expiresutc, revokedutc, revokedreason) VALUES (" +
                Pid + ", " + D.Str(session.Id) + ", " + D.Str(session.UserId) + ", " + D.Enum(session.ClientType) + ", " + D.Enum(session.PrincipalKind) + ", " + D.Str(session.SourceIp) + ", " +
                D.Str(session.UserAgent) + ", " + D.Ts(session.CreatedUtc) + ", " + D.Ts(session.LastUsedUtc) + ", " + D.Ts(session.ExpiresUtc) + ", " +
                D.Ts(session.RevokedUtc) + ", " + D.Str(session.RevokedReason) + ");";
            await QueryAsync(sql, token).ConfigureAwait(false);
            return session;
        }

        public async Task<AuthSession?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT * FROM authsessions WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<AuthSession>> EnumerateActiveByUserAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM authsessions WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND revokedutc IS NULL AND expiresutc > " +
                D.Ts(nowUtc) + " ORDER BY createdutc DESC, id DESC;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<AuthSession>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = afterId == null ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT * FROM authsessions" + " WHERE " + Scope() + " AND (userid = " + D.Str(userId) + ")" + after + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task TouchAsync(string id, DateTime usedUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE authsessions SET lastusedutc = " + D.Ts(usedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> RevokeAsync(string id, string reason, DateTime revokedUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            // Push tokens bound to the session are deleted with it (Phase 2 contract 8.7), deliveries first.
            string tokens = "SELECT id FROM pushtokens WHERE " + Scope() + " AND sessionid = " + D.Str(id);
            List<string> statements = new List<string> { "DELETE FROM pushdeliveries WHERE " + Scope() + " AND pushtokenid IN (" + tokens + ");" };
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.PushToken, tokens));
            statements.Add("DELETE FROM pushtokens WHERE " + Scope() + " AND sessionid = " + D.Str(id) + ";");
            statements.Add("UPDATE authsessions SET revokedutc = " + D.Ts(revokedUtc) + ", revokedreason = " + D.Str(reason) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND revokedutc IS NULL RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<string>> RevokeAllForUserAsync(string userId, string? exceptSessionId, string reason, DateTime revokedUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string except = exceptSessionId == null ? String.Empty : " AND id <> " + D.Str(exceptSessionId);
            string sessions = "SELECT id FROM authsessions WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND revokedutc IS NULL" + except;
            string tokens = "SELECT id FROM pushtokens WHERE " + Scope() + " AND sessionid IN (" + sessions + ")";
            // Push tokens bound to the revoked sessions are deleted in the same batch (Phase 2 contract 8.7).
            List<string> statements = new List<string> { "DELETE FROM pushdeliveries WHERE " + Scope() + " AND pushtokenid IN (" + tokens + ");" };
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.PushToken, tokens));
            statements.Add("DELETE FROM pushtokens WHERE " + Scope() + " AND sessionid IN (" + sessions + ");");
            statements.Add("UPDATE authsessions SET revokedutc = " + D.Ts(revokedUtc) + ", revokedreason = " + D.Str(reason) +
                " WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND revokedutc IS NULL" + except + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public Task<EnumerationResult<AuthSession>> EnumerateByUserAsync(string userId, EnumerationRequest request, bool includeInactive, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "a.userid = " + D.Str(userId) };
            if (!includeInactive) conditions.Add("a.revokedutc IS NULL AND a.expiresutc > " + D.Ts(nowUtc));
            return EnumerateAsync(request, "a.id, a.userid, a.clienttype, a.principalkind, a.sourceip, a.useragent, a.createdutc, a.lastusedutc, a.expiresutc, a.revokedutc, a.revokedreason",
                "authsessions a", conditions, Map, token);
        }

        private static AuthSession Map(DataRow row)
        {
            return new AuthSession
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                ClientType = RowReader.GetEnum(row, "clienttype", ClientTypeEnum.Other),
                PrincipalKind = RowReader.GetEnum(row, "principalkind", PrincipalKindEnum.Member),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                UserAgent = RowReader.GetNullableString(row, "useragent"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUsedUtc = RowReader.GetDateTime(row, "lastusedutc"),
                ExpiresUtc = RowReader.GetDateTime(row, "expiresutc"),
                RevokedUtc = RowReader.GetNullableDateTime(row, "revokedutc"),
                RevokedReason = RowReader.GetNullableString(row, "revokedreason")
            };
        }
    }
}
