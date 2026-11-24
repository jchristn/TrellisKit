namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared refresh token methods.
    /// </summary>
    internal abstract class RefreshTokenMethodsBase : MethodsBase, IRefreshTokenMethods
    {
        protected RefreshTokenMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<RefreshToken> CreateAsync(RefreshToken refreshToken, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(refreshToken);
            string sql = "INSERT INTO refreshtokens (propertyid, id, sessionid, userid, tokenhash, createdutc, expiresutc, usedutc, replacedbyid) VALUES (" +
                Pid + ", " + D.Str(refreshToken.Id) + ", " + D.Str(refreshToken.SessionId) + ", " + D.Str(refreshToken.UserId) + ", " + D.Str(refreshToken.TokenHash) + ", " +
                D.Ts(refreshToken.CreatedUtc) + ", " + D.Ts(refreshToken.ExpiresUtc) + ", " + D.Ts(refreshToken.UsedUtc) + ", " + D.Str(refreshToken.ReplacedById) + ");";
            await UniqueAsync(sql, "That refresh token", token).ConfigureAwait(false);
            return refreshToken;
        }

        public async Task<RefreshToken?> ReadByHashAsync(string tokenHash, CancellationToken token = default)
        {
            RequireText(tokenHash, nameof(tokenHash));
            DataTable table = await QueryAsync("SELECT * FROM refreshtokens WHERE " + Scope() + " AND tokenhash = " + D.Str(tokenHash) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new RefreshToken
            {
                Id = RowReader.GetString(row, "id"),
                SessionId = RowReader.GetString(row, "sessionid"),
                UserId = RowReader.GetString(row, "userid"),
                TokenHash = RowReader.GetString(row, "tokenhash"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                ExpiresUtc = RowReader.GetDateTime(row, "expiresutc"),
                UsedUtc = RowReader.GetNullableDateTime(row, "usedutc"),
                ReplacedById = RowReader.GetNullableString(row, "replacedbyid")
            };
        }

        public async Task<bool> TryMarkUsedAsync(string id, string replacedById, DateTime usedUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE refreshtokens SET usedutc = " + D.Ts(usedUtc) + ", replacedbyid = " + D.Str(replacedById) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND usedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM refreshtokens WHERE " + Scope() + " AND expiresutc < " + D.Ts(beforeUtc) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }
    }
}
