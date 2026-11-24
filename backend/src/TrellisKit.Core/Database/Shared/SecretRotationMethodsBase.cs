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
    using TrellisKit.Core.Security;

    /// <summary>
    /// Shared secrets rotation methods (tables propertysettings, emailmessages, updatesigningkeys, aiendpoints, aiharnessprofiles, and
    /// aiendpointheaders; the AI columns hold SecretBoxProtector envelopes, unpacked here into SecretBox parts).
    /// </summary>
    internal abstract class SecretRotationMethodsBase : MethodsBase, ISecretRotationMethods
    {
        protected SecretRotationMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<long> CountAsync(IEnumerable<string> keyIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(keyIds);
            List<string> ids = keyIds.Where(k => !String.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return 0;
            string list = D.InList(ids);
            return await CountAsync("SELECT (SELECT COUNT(*) FROM propertysettings WHERE " + Scope() + " AND ciphertext IS NOT NULL AND keyid IN " + list + ") + " +
                "(SELECT COUNT(*) FROM emailmessages WHERE " + Scope() + " AND payloadciphertext IS NOT NULL AND payloadkeyid IN " + list + ") + " +
                "(SELECT COUNT(*) FROM updatesigningkeys WHERE " + Scope() + " AND privatekeyciphertext IS NOT NULL AND privatekeykeyid IN " + list + ") + " +
                "(SELECT COUNT(*) FROM aiendpoints WHERE " + Scope() + " AND authvaluesecret IS NOT NULL AND split_part(authvaluesecret, ':', 2) IN " + list + ") + " +
                "(SELECT COUNT(*) FROM aiendpointheaders WHERE " + Scope() + " AND valuesecret IS NOT NULL AND split_part(valuesecret, ':', 2) IN " + list + ") + " +
                "(SELECT COUNT(*) FROM aiharnessprofiles WHERE " + Scope() + " AND credentialsecret IS NOT NULL AND split_part(credentialsecret, ':', 2) IN " + list + ") AS cnt;", token).ConfigureAwait(false);
        }

        public async Task<List<StoredSecret>> ReadBatchAsync(IEnumerable<string> keyIds, string? afterStore, string? afterRowKey, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(keyIds);
            List<string> ids = keyIds.Where(k => !String.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<StoredSecret>();
            string list = D.InList(ids);
            string after = afterStore == null ? String.Empty : " WHERE (s.store > " + D.Str(afterStore) + " OR (s.store = " + D.Str(afterStore) + " AND s.rowkey > " + D.Str(afterRowKey ?? String.Empty) + "))";
            DataTable table = await QueryAsync("SELECT s.store, s.rowkey, s.ciphertext, s.nonce, s.keyid FROM (" +
                "SELECT " + D.Str(SecretStoreEnum.EmailPayload.ToString()) + " AS store, id AS rowkey, payloadciphertext AS ciphertext, payloadnonce AS nonce, payloadkeyid AS keyid FROM emailmessages WHERE " + Scope() + " AND payloadciphertext IS NOT NULL AND payloadkeyid IN " + list +
                " UNION ALL SELECT " + D.Str(SecretStoreEnum.PropertySetting.ToString()) + " AS store, settingkey AS rowkey, ciphertext, nonce, keyid FROM propertysettings WHERE " + Scope() + " AND ciphertext IS NOT NULL AND keyid IN " + list +
                " UNION ALL SELECT " + D.Str(SecretStoreEnum.UpdateSigningKey.ToString()) + " AS store, keyid AS rowkey, privatekeyciphertext AS ciphertext, privatekeynonce AS nonce, privatekeykeyid AS keyid FROM updatesigningkeys WHERE " + Scope() + " AND privatekeyciphertext IS NOT NULL AND privatekeykeyid IN " + list +
                " UNION ALL SELECT " + D.Str(SecretStoreEnum.AiEndpointAuthValue.ToString()) + " AS store, id AS rowkey, authvaluesecret AS ciphertext, '' AS nonce, split_part(authvaluesecret, ':', 2) AS keyid FROM aiendpoints WHERE " + Scope() + " AND authvaluesecret IS NOT NULL AND split_part(authvaluesecret, ':', 2) IN " + list +
                " UNION ALL SELECT " + D.Str(SecretStoreEnum.AiEndpointHeader.ToString()) + " AS store, endpointid || '|' || namenormalized AS rowkey, valuesecret AS ciphertext, '' AS nonce, split_part(valuesecret, ':', 2) AS keyid FROM aiendpointheaders WHERE " + Scope() + " AND valuesecret IS NOT NULL AND split_part(valuesecret, ':', 2) IN " + list +
                " UNION ALL SELECT " + D.Str(SecretStoreEnum.AiHarnessCredential.ToString()) + " AS store, runtime AS rowkey, credentialsecret AS ciphertext, '' AS nonce, split_part(credentialsecret, ':', 2) AS keyid FROM aiharnessprofiles WHERE " + Scope() + " AND credentialsecret IS NOT NULL AND split_part(credentialsecret, ':', 2) IN " + list +
                ") s" + after + " ORDER BY s.store, s.rowkey LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            List<StoredSecret> result = new List<StoredSecret>();
            foreach (DataRow row in table.Rows)
            {
                StoredSecret value = new StoredSecret
                {
                    Store = RowReader.GetEnum(row, "store", SecretStoreEnum.PropertySetting),
                    RowKey = RowReader.GetString(row, "rowkey"),
                    Ciphertext = RowReader.GetString(row, "ciphertext"),
                    Nonce = RowReader.GetNullableString(row, "nonce") ?? String.Empty,
                    KeyId = RowReader.GetString(row, "keyid")
                };
                if (IsEnvelope(value.Store))
                {
                    SecretCiphertext? parts = SecretBoxProtector.Unpack(value.Ciphertext);
                    if (parts == null) continue;
                    value.Ciphertext = parts.Ciphertext;
                    value.Nonce = parts.Nonce;
                    value.KeyId = parts.KeyId;
                }
                result.Add(value);
            }
            return result;
        }

        public async Task<bool> RewriteAsync(StoredSecret current, SecretCiphertext replacement, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(replacement);
            string sql = current.Store == SecretStoreEnum.PropertySetting
                ? "UPDATE propertysettings SET ciphertext = " + D.Str(replacement.Ciphertext) + ", nonce = " + D.Str(replacement.Nonce) + ", keyid = " + D.Str(replacement.KeyId) +
                    " WHERE " + Scope() + " AND settingkey = " + D.Str(current.RowKey) + " AND keyid = " + D.Str(current.KeyId) + " AND ciphertext = " + D.Str(current.Ciphertext) + " RETURNING settingkey;"
                : current.Store == SecretStoreEnum.AiEndpointAuthValue
                ? "UPDATE aiendpoints SET authvaluesecret = " + D.Str(SecretBoxProtector.Pack(replacement)) +
                    " WHERE " + Scope() + " AND id = " + D.Str(current.RowKey) + " AND authvaluesecret = " + D.Str(SecretBoxProtector.Pack(Parts(current))) + " RETURNING id;"
                : current.Store == SecretStoreEnum.AiEndpointHeader
                ? "UPDATE aiendpointheaders SET valuesecret = " + D.Str(SecretBoxProtector.Pack(replacement)) +
                    " WHERE " + Scope() + " AND endpointid = " + D.Str(HeaderEndpoint(current.RowKey)) + " AND namenormalized = " + D.Str(HeaderName(current.RowKey)) +
                    " AND valuesecret = " + D.Str(SecretBoxProtector.Pack(Parts(current))) + " RETURNING endpointid;"
                : current.Store == SecretStoreEnum.AiHarnessCredential
                ? "UPDATE aiharnessprofiles SET credentialsecret = " + D.Str(SecretBoxProtector.Pack(replacement)) +
                    " WHERE " + Scope() + " AND runtime = " + D.Str(current.RowKey) + " AND credentialsecret = " + D.Str(SecretBoxProtector.Pack(Parts(current))) + " RETURNING runtime;"
                : current.Store == SecretStoreEnum.UpdateSigningKey
                ? "UPDATE updatesigningkeys SET privatekeyciphertext = " + D.Str(replacement.Ciphertext) + ", privatekeynonce = " + D.Str(replacement.Nonce) + ", privatekeykeyid = " + D.Str(replacement.KeyId) +
                    " WHERE " + Scope() + " AND keyid = " + D.Str(current.RowKey) + " AND privatekeykeyid = " + D.Str(current.KeyId) + " AND privatekeyciphertext = " + D.Str(current.Ciphertext) + " RETURNING keyid;"
                : "UPDATE emailmessages SET payloadciphertext = " + D.Str(replacement.Ciphertext) + ", payloadnonce = " + D.Str(replacement.Nonce) + ", payloadkeyid = " + D.Str(replacement.KeyId) +
                    " WHERE " + Scope() + " AND id = " + D.Str(current.RowKey) + " AND payloadkeyid = " + D.Str(current.KeyId) + " AND payloadciphertext = " + D.Str(current.Ciphertext) + " RETURNING id;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }
        private static bool IsEnvelope(SecretStoreEnum store)
        {
            return store == SecretStoreEnum.AiEndpointAuthValue || store == SecretStoreEnum.AiEndpointHeader || store == SecretStoreEnum.AiHarnessCredential;
        }

        private static SecretCiphertext Parts(StoredSecret value)
        {
            return new SecretCiphertext { Ciphertext = value.Ciphertext, Nonce = value.Nonce, KeyId = value.KeyId };
        }

        private static string HeaderEndpoint(string rowKey)
        {
            int bar = rowKey.IndexOf('|');
            return bar < 0 ? rowKey : rowKey.Substring(0, bar);
        }

        private static string HeaderName(string rowKey)
        {
            int bar = rowKey.IndexOf('|');
            return bar < 0 ? String.Empty : rowKey.Substring(bar + 1);
        }

    }
}
