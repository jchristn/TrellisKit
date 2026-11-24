namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared one-time token methods.
    /// </summary>
    internal abstract class OneTimeTokenMethodsBase : MethodsBase, IOneTimeTokenMethods
    {
        protected OneTimeTokenMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<OneTimeToken> CreateAsync(OneTimeToken oneTimeToken, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(oneTimeToken);
            string sql = "INSERT INTO onetimetokens (propertyid, id, userid, purpose, tokenhash, email, createdutc, expiresutc, usedutc) VALUES (" +
                Pid + ", " + D.Str(oneTimeToken.Id) + ", " + D.Str(oneTimeToken.UserId) + ", " + D.Enum(oneTimeToken.Purpose) + ", " + D.Str(oneTimeToken.TokenHash) + ", " +
                D.Str(oneTimeToken.Email) + ", " + D.Ts(oneTimeToken.CreatedUtc) + ", " + D.Ts(oneTimeToken.ExpiresUtc) + ", " + D.Ts(oneTimeToken.UsedUtc) + ");";
            await UniqueAsync(sql, "That token", token).ConfigureAwait(false);
            return oneTimeToken;
        }

        public async Task<OneTimeToken?> ReadByHashAsync(string tokenHash, CancellationToken token = default)
        {
            RequireText(tokenHash, nameof(tokenHash));
            DataTable table = await QueryAsync("SELECT * FROM onetimetokens WHERE " + Scope() + " AND tokenhash = " + D.Str(tokenHash) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new OneTimeToken
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Purpose = RowReader.GetEnum(row, "purpose", OneTimeTokenPurposeEnum.VerifyEmail),
                TokenHash = RowReader.GetString(row, "tokenhash"),
                Email = RowReader.GetNullableString(row, "email"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                ExpiresUtc = RowReader.GetDateTime(row, "expiresutc"),
                UsedUtc = RowReader.GetNullableDateTime(row, "usedutc")
            };
        }

        public async Task<bool> TryConsumeAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE onetimetokens SET usedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND usedutc IS NULL AND expiresutc > " + D.Ts(nowUtc) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> InvalidateAsync(string userId, OneTimeTokenPurposeEnum purpose, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("UPDATE onetimetokens SET usedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND userid = " + D.Str(userId) +
                " AND purpose = " + D.Enum(purpose) + " AND usedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }
    }
}
