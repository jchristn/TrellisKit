namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared external identity methods.
    /// </summary>
    internal abstract class ExternalIdentityMethodsBase : MethodsBase, IExternalIdentityMethods
    {
        protected ExternalIdentityMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<ExternalIdentity> CreateAsync(ExternalIdentity identity, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            string sql = "INSERT INTO externalidentities (propertyid, id, userid, provider, subject, email, emailverified, createdutc, lastusedutc) VALUES (" +
                Pid + ", " + D.Str(identity.Id) + ", " + D.Str(identity.UserId) + ", " + D.Enum(identity.Provider) + ", " + D.Str(identity.Subject) + ", " +
                D.Str(identity.Email) + ", " + D.Bool(identity.EmailVerified) + ", " + D.Ts(identity.CreatedUtc) + ", " + D.Ts(identity.LastUsedUtc) + ");";
            await UniqueAsync(sql, "That external identity", token).ConfigureAwait(false);
            return identity;
        }

        public async Task<ExternalIdentity?> ReadByProviderSubjectAsync(ExternalProviderEnum provider, string subject, CancellationToken token = default)
        {
            RequireText(subject, nameof(subject));
            DataTable table = await QueryAsync("SELECT * FROM externalidentities WHERE " + Scope() + " AND provider = " + D.Enum(provider) + " AND subject = " + D.Str(subject) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<ExternalIdentity>> EnumerateByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM externalidentities WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task UpdateAsync(ExternalIdentity identity, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            await QueryAsync("UPDATE externalidentities SET email = " + D.Str(identity.Email) + ", emailverified = " + D.Bool(identity.EmailVerified) +
                ", lastusedutc = " + D.Ts(identity.LastUsedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(identity.Id) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteAsync(string userId, ExternalProviderEnum provider, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM externalidentities WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND provider = " + D.Enum(provider) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> DeleteByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM externalidentities WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static ExternalIdentity Map(DataRow row)
        {
            return new ExternalIdentity
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Provider = RowReader.GetEnum(row, "provider", ExternalProviderEnum.Apple),
                Subject = RowReader.GetString(row, "subject"),
                Email = RowReader.GetNullableString(row, "email"),
                EmailVerified = RowReader.GetBool(row, "emailverified"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUsedUtc = RowReader.GetNullableDateTime(row, "lastusedutc")
            };
        }
    }
}
