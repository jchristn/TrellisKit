namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared per-property setting methods (table propertysettings).
    /// </summary>
    internal abstract class PropertySettingMethodsBase : MethodsBase, IPropertySettingMethods
    {
        protected PropertySettingMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<PropertySettingRow>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM propertysettings WHERE " + Scope() + " ORDER BY settingkey LIMIT 500;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new PropertySettingRow
            {
                Key = RowReader.GetString(row, "settingkey"),
                ValueText = RowReader.GetNullableString(row, "valuetext"),
                Ciphertext = RowReader.GetNullableString(row, "ciphertext"),
                Nonce = RowReader.GetNullableString(row, "nonce"),
                KeyId = RowReader.GetNullableString(row, "keyid"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            }).ToList();
        }

        public async Task WriteAsync(List<PropertySettingRow> upserts, List<string> deletes, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(upserts);
            ArgumentNullException.ThrowIfNull(deletes);
            List<string> statements = new List<string>();
            foreach (string key in deletes) statements.Add("DELETE FROM propertysettings WHERE " + Scope() + " AND settingkey = " + D.Str(key) + ";");
            foreach (PropertySettingRow r in upserts)
            {
                statements.Add("INSERT INTO propertysettings (propertyid, settingkey, valuetext, ciphertext, nonce, keyid, updatedbyuserid, updatedutc) VALUES (" +
                    Pid + ", " + D.Str(r.Key) + ", " + D.Str(r.ValueText) + ", " + D.Str(r.Ciphertext) + ", " + D.Str(r.Nonce) + ", " + D.Str(r.KeyId) + ", " + D.Str(r.UpdatedByUserId) + ", " + D.Ts(r.UpdatedUtc) +
                    ") ON CONFLICT (propertyid, settingkey) DO UPDATE SET valuetext = EXCLUDED.valuetext, ciphertext = EXCLUDED.ciphertext, nonce = EXCLUDED.nonce, keyid = EXCLUDED.keyid, " +
                    "updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;");
            }
            if (statements.Count > 0) await TransactionAsync(statements, token).ConfigureAwait(false);
        }
    }
}
