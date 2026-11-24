namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared password credential methods.
    /// </summary>
    internal abstract class CredentialMethodsBase : MethodsBase, ICredentialMethods
    {
        protected CredentialMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<UserCredential?> ReadByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM credentials WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new UserCredential
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                PasswordHash = RowReader.GetString(row, "passwordhash"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        public async Task<UserCredential> UpsertAsync(UserCredential credential, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(credential);
            RequireText(credential.UserId, nameof(credential.UserId));
            string sql = "INSERT INTO credentials (propertyid, id, userid, passwordhash, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(credential.Id) + ", " + D.Str(credential.UserId) + ", " + D.Str(credential.PasswordHash) + ", " +
                D.Ts(credential.CreatedUtc) + ", " + D.Ts(credential.UpdatedUtc) + ") " +
                "ON CONFLICT (propertyid, userid) DO UPDATE SET passwordhash = " + D.Str(credential.PasswordHash) + ", updatedutc = " + D.Ts(credential.UpdatedUtc) + ";";
            await QueryAsync(sql, token).ConfigureAwait(false);
            return credential;
        }

        public async Task<bool> DeleteByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM credentials WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }
    }
}
