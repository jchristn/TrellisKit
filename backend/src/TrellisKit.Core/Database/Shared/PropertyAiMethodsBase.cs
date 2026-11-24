namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared AI switch methods (table propertyai).
    /// </summary>
    internal abstract class PropertyAiMethodsBase : MethodsBase, IPropertyAiMethods
    {
        protected PropertyAiMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<PropertyAiSwitch?> ReadAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM propertyai WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new PropertyAiSwitch
            {
                Enabled = RowReader.GetBool(row, "enabled"),
                SourceHash = RowReader.GetNullableString(row, "sourcehash"),
                Revision = RowReader.GetInt(row, "revision"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        public async Task WriteAsync(PropertyAiSwitch value, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(value);
            await QueryAsync("INSERT INTO propertyai (propertyid, enabled, sourcehash, revision, updatedbyuserid, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Bool(value.Enabled) + ", " + D.Str(value.SourceHash) + ", " + D.Num(value.Revision) + ", " + D.Str(value.UpdatedByUserId) + ", " + D.Ts(value.CreatedUtc) + ", " + D.Ts(value.UpdatedUtc) +
                ") ON CONFLICT (propertyid) DO UPDATE SET enabled = EXCLUDED.enabled, sourcehash = EXCLUDED.sourcehash, revision = EXCLUDED.revision, " +
                "updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;", token).ConfigureAwait(false);
        }
    }
}
