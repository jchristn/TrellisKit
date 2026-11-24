namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared profile value methods (tables profilevalues, profilevalueoptions, profilehistoryentries,
    /// profilehistoryvalues, profilesearchwords). Every write is one transaction that also recomputes the written fields'
    /// valueCount from the source rows.
    /// </summary>
    internal abstract class ProfileValueMethodsBase : MethodsBase, IProfileValueMethods
    {
        protected ProfileValueMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<ProfileValueSet> ReadForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            Dictionary<string, ProfileValueSet> sets = await ReadAsync(new List<string> { userId }, null, token).ConfigureAwait(false);
            return sets.TryGetValue(userId, out ProfileValueSet? set) ? set : new ProfileValueSet { UserId = userId };
        }

        public Task<Dictionary<string, ProfileValueSet>> ReadForUsersAsync(List<string> userIds, List<string> fieldIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            ArgumentNullException.ThrowIfNull(fieldIds);
            if (userIds.Count == 0 || fieldIds.Count == 0) return Task.FromResult(new Dictionary<string, ProfileValueSet>(StringComparer.Ordinal));
            return ReadAsync(userIds.Distinct(StringComparer.Ordinal).Take(1000).ToList(), fieldIds, token);
        }

        public async Task WriteAsync(string userId, List<ProfileFieldWrite> writes, string? updatedByUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(writes);
            if (writes.Count == 0) return;
            string user = D.Str(userId);
            List<string> statements = new List<string>();
            foreach (ProfileFieldWrite write in writes)
            {
                string field = D.Str(write.FieldId);
                string where = Scope() + " AND userid = " + user + " AND fieldid = " + field;
                statements.Add("DELETE FROM profilevalues WHERE " + where + ";");
                statements.Add("DELETE FROM profilevalueoptions WHERE " + where + ";");
                statements.Add("DELETE FROM profilesearchwords WHERE " + where + ";");
                statements.Add("DELETE FROM profilehistoryvalues WHERE " + Scope() + " AND entryid IN (SELECT id FROM profilehistoryentries WHERE " + where + ");");
                statements.Add("DELETE FROM profilehistoryentries WHERE " + where + ";");
                if (write.Value != null)
                {
                    ProfileValue v = write.Value;
                    statements.Add("INSERT INTO profilevalues (propertyid, userid, fieldid, valuetext, valuenormalized, valuenumber, valuedate, valuebool, valuelevel, othertext, updatedbyuserid, updatedutc) VALUES (" +
                        Pid + ", " + user + ", " + field + ", " + D.Str(v.ValueText) + ", " + D.Str(v.ValueNormalized) + ", " + Num(v.ValueNumber) + ", " + D.Str(v.ValueDate) + ", " +
                        Bool(v.ValueBool) + ", " + D.Num(v.ValueLevel) + ", " + D.Str(v.OtherText) + ", " + D.Str(updatedByUserId) + ", " + D.Ts(nowUtc) + ");");
                    for (int i = 0; i < v.OptionKeys.Count; i++)
                        statements.Add("INSERT INTO profilevalueoptions (propertyid, userid, fieldid, optionkey, position) VALUES (" + Pid + ", " + user + ", " + field + ", " + D.Str(v.OptionKeys[i]) + ", " + D.Num(i) + ");");
                }
                if (write.History != null)
                {
                    for (int i = 0; i < write.History.Count; i++)
                    {
                        ProfileHistoryEntry e = write.History[i];
                        statements.Add("INSERT INTO profilehistoryentries (propertyid, id, userid, fieldid, position, createdutc, updatedutc) VALUES (" +
                            Pid + ", " + D.Str(e.Id) + ", " + user + ", " + field + ", " + D.Num(i) + ", " + D.Ts(e.CreatedUtc) + ", " + D.Ts(nowUtc) + ");");
                        foreach (ProfileHistoryValue hv in e.Values)
                            statements.Add("INSERT INTO profilehistoryvalues (propertyid, entryid, subfieldkey, valuetext, valuenormalized, valuenumber, valuedate, valuebool, valuelevel, valueuserid) VALUES (" +
                                Pid + ", " + D.Str(e.Id) + ", " + D.Str(hv.SubFieldKey) + ", " + D.Str(hv.ValueText) + ", " + D.Str(hv.ValueNormalized) + ", " + Num(hv.ValueNumber) + ", " +
                                D.Str(hv.ValueDate) + ", " + Bool(hv.ValueBool) + ", " + D.Num(hv.ValueLevel) + ", " + D.Str(hv.ValueUserId) + ");");
                    }
                }
                foreach (string word in write.SearchWords.Distinct(StringComparer.Ordinal).Take(100))
                    statements.Add("INSERT INTO profilesearchwords (propertyid, userid, fieldid, word) VALUES (" + Pid + ", " + user + ", " + field + ", " + D.Str(word) + ");");
            }
            statements.Add(Recount(writes.Select(w => w.FieldId).Distinct(StringComparer.Ordinal).ToList()));
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task DeleteForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string user = D.Str(userId);
            await TransactionAsync(new List<string>
            {
                "DELETE FROM profilehistoryvalues WHERE " + Scope() + " AND entryid IN (SELECT id FROM profilehistoryentries WHERE " + Scope() + " AND userid = " + user + ");",
                "DELETE FROM profilehistoryentries WHERE " + Scope() + " AND userid = " + user + ";",
                "UPDATE profilehistoryvalues SET valueuserid = NULL WHERE " + Scope() + " AND valueuserid = " + user + ";",
                "DELETE FROM profilevalueoptions WHERE " + Scope() + " AND userid = " + user + ";",
                "DELETE FROM profilesearchwords WHERE " + Scope() + " AND userid = " + user + ";",
                "DELETE FROM profilevalues WHERE " + Scope() + " AND userid = " + user + ";",
                Recount(null)
            }, token).ConfigureAwait(false);
        }

        public async Task DeleteForFieldAsync(string fieldId, CancellationToken token = default)
        {
            RequireText(fieldId, nameof(fieldId));
            string field = D.Str(fieldId);
            await TransactionAsync(new List<string>
            {
                "DELETE FROM profilehistoryvalues WHERE " + Scope() + " AND entryid IN (SELECT id FROM profilehistoryentries WHERE " + Scope() + " AND fieldid = " + field + ");",
                "DELETE FROM profilehistoryentries WHERE " + Scope() + " AND fieldid = " + field + ";",
                "DELETE FROM profilevalueoptions WHERE " + Scope() + " AND fieldid = " + field + ";",
                "DELETE FROM profilesearchwords WHERE " + Scope() + " AND fieldid = " + field + ";",
                "DELETE FROM profilevalues WHERE " + Scope() + " AND fieldid = " + field + ";"
            }, token).ConfigureAwait(false);
        }

        public async Task<ProfileFieldStats> StatsAsync(ProfileField field, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(field);
            string id = D.Str(field.Id);
            ProfileFieldStats stats = new ProfileFieldStats();
            string users = field.Type == ProfileFieldTypeEnum.History
                ? "SELECT DISTINCT userid FROM profilehistoryentries WHERE " + Scope() + " AND fieldid = " + id
                : "SELECT userid FROM profilevalues WHERE " + Scope() + " AND fieldid = " + id;
            stats.ValueCount = await CountAsync("SELECT COUNT(*) AS cnt FROM (" + users + ") x;", token).ConfigureAwait(false);
            long members = await CountAsync("SELECT COUNT(*) AS cnt FROM users WHERE " + Scope() + " AND isdeleted = " + D.Bool(false) + ";", token).ConfigureAwait(false);
            stats.FillRate = members == 0 ? 0 : Math.Round((double)stats.ValueCount / members, 4);
            string since = D.Ts(nowUtc.AddDays(-30));
            stats.Updated30d = field.Type == ProfileFieldTypeEnum.History
                ? await CountAsync("SELECT COUNT(DISTINCT userid) AS cnt FROM profilehistoryentries WHERE " + Scope() + " AND fieldid = " + id + " AND updatedutc >= " + since + ";", token).ConfigureAwait(false)
                : await CountAsync("SELECT COUNT(*) AS cnt FROM profilevalues WHERE " + Scope() + " AND fieldid = " + id + " AND updatedutc >= " + since + ";", token).ConfigureAwait(false);
            if (field.Type == ProfileFieldTypeEnum.SingleSelect || field.Type == ProfileFieldTypeEnum.Rank || field.Type == ProfileFieldTypeEnum.MultiSelect)
            {
                string sql = field.Type == ProfileFieldTypeEnum.MultiSelect
                    ? "SELECT optionkey AS k, COUNT(*) AS cnt FROM profilevalueoptions WHERE " + Scope() + " AND fieldid = " + id + " GROUP BY optionkey LIMIT 200;"
                    : "SELECT valuetext AS k, COUNT(*) AS cnt FROM profilevalues WHERE " + Scope() + " AND fieldid = " + id + " AND valuetext IS NOT NULL GROUP BY valuetext LIMIT 200;";
                DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
                Dictionary<string, long> counts = table.Rows.Cast<DataRow>().ToDictionary(r => RowReader.GetString(r, "k"), r => RowReader.GetLong(r, "cnt"), StringComparer.Ordinal);
                foreach (ProfileFieldOption option in field.Options)
                    stats.OptionCounts.Add(new ProfileOptionCount { Key = option.Key, Count = counts.TryGetValue(option.Key, out long c) ? c : 0 });
            }
            return stats;
        }

        public async Task<List<string>> OptionKeysInUseAsync(string fieldId, List<string> optionKeys, CancellationToken token = default)
        {
            RequireText(fieldId, nameof(fieldId));
            ArgumentNullException.ThrowIfNull(optionKeys);
            if (optionKeys.Count == 0) return new List<string>();
            string field = D.Str(fieldId);
            string keys = D.InList(optionKeys);
            DataTable table = await QueryAsync(
                "SELECT DISTINCT valuetext AS k FROM profilevalues WHERE " + Scope() + " AND fieldid = " + field + " AND valuetext IN " + keys +
                " UNION SELECT DISTINCT optionkey AS k FROM profilevalueoptions WHERE " + Scope() + " AND fieldid = " + field + " AND optionkey IN " + keys + " LIMIT 200;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "k")).ToList();
        }

        public async Task BackfillLegacyAsync(ProfileLegacyFieldIds fields, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(fields);
            string now = D.Ts(nowUtc);
            List<string> statements = new List<string>();
            List<string> written = new List<string>();

            string Missing(string fieldId)
            {
                return "NOT EXISTS (SELECT 1 FROM profilevalues x WHERE x.propertyid = " + Pid + " AND x.userid = u.id AND x.fieldid = " + D.Str(fieldId) + ")";
            }

            void Text(string? fieldId, string column, bool upper)
            {
                if (fieldId == null) return;
                written.Add(fieldId);
                string value = upper ? "upper(trim(u." + column + "))" : "trim(u." + column + ")";
                statements.Add("INSERT INTO profilevalues (propertyid, userid, fieldid, valuetext, valuenormalized, updatedutc) SELECT " + Pid + ", u.id, " + D.Str(fieldId) + ", " + value + ", " +
                    (upper ? "NULL" : "lower(trim(u." + column + "))") + ", " + now + " FROM users u WHERE u.propertyid = " + Pid +
                    " AND u.isdeleted = " + D.Bool(false) + " AND u." + column + " IS NOT NULL AND trim(u." + column + ") <> '' AND " + Missing(fieldId) + ";");
            }

            if (fields.Rank != null)
            {
                written.Add(fields.Rank);
                statements.Add("INSERT INTO profilevalues (propertyid, userid, fieldid, valuetext, valuelevel, updatedutc) SELECT " + Pid + ", u.id, " + D.Str(fields.Rank) + ", u.rank, u.ranklevel, " + now +
                    " FROM users u WHERE u.propertyid = " + Pid + " AND u.isdeleted = " + D.Bool(false) + " AND u.rank IS NOT NULL AND u.rank <> '' AND " + Missing(fields.Rank) + ";");
            }
            Text(fields.Affiliation, "affiliation", false);
            Text(fields.City, "city", false);
            Text(fields.Region, "region", false);
            Text(fields.Country, "country", true);
            if (fields.Goals != null)
            {
                written.Add(fields.Goals);
                string goals = D.Str(fields.Goals);
                statements.Add("INSERT INTO profilevalueoptions (propertyid, userid, fieldid, optionkey, position) SELECT " + Pid + ", g.userid, " + goals + ", g.goal, g.position FROM usergoals g WHERE g.propertyid = " + Pid +
                    " AND NOT EXISTS (SELECT 1 FROM profilevalues x WHERE x.propertyid = " + Pid + " AND x.userid = g.userid AND x.fieldid = " + goals + ") ON CONFLICT DO NOTHING;");
                statements.Add("INSERT INTO profilevalues (propertyid, userid, fieldid, othertext, updatedutc) SELECT " + Pid + ", u.id, " + goals + ", " +
                    (fields.GoalsFreeTextKey == null ? "NULL" : "NULLIF(trim(COALESCE(u.goalsother, '')), '')") + ", " + now + " FROM users u WHERE u.propertyid = " + Pid +
                    " AND u.isdeleted = " + D.Bool(false) + " AND (EXISTS (SELECT 1 FROM usergoals g WHERE g.propertyid = " + Pid + " AND g.userid = u.id) OR trim(COALESCE(u.goalsother, '')) <> '') AND " + Missing(fields.Goals) + ";");
            }
            if (fields.RankHistory != null)
            {
                written.Add(fields.RankHistory);
                string history = D.Str(fields.RankHistory);
                statements.Add("INSERT INTO profilehistoryentries (propertyid, id, userid, fieldid, position, createdutc, updatedutc) SELECT " + Pid + ", r.id, r.userid, " + history + ", r.position, r.createdutc, r.createdutc " +
                    "FROM rankhistoryentries r WHERE r.propertyid = " + Pid + " AND NOT EXISTS (SELECT 1 FROM profilehistoryentries x WHERE x.propertyid = " + Pid + " AND x.userid = r.userid AND x.fieldid = " + history + ") " +
                    "AND NOT EXISTS (SELECT 1 FROM profilehistoryentries y WHERE y.propertyid = " + Pid + " AND y.id = r.id);");
                string join = " FROM rankhistoryentries r JOIN profilehistoryentries e ON e.propertyid = " + Pid + " AND e.id = r.id AND e.fieldid = " + history + " WHERE r.propertyid = " + Pid;
                string Fresh(string subKey)
                {
                    return " AND NOT EXISTS (SELECT 1 FROM profilehistoryvalues hv WHERE hv.propertyid = " + Pid + " AND hv.entryid = r.id AND hv.subfieldkey = " + D.Str(subKey) + ")";
                }
                statements.Add("INSERT INTO profilehistoryvalues (propertyid, entryid, subfieldkey, valuetext) SELECT " + Pid + ", r.id, 'rank', r.rank" + join + " AND r.rank IS NOT NULL AND r.rank <> ''" + Fresh("rank") + ";");
                statements.Add("INSERT INTO profilehistoryvalues (propertyid, entryid, subfieldkey, valuetext, valuenormalized) SELECT " + Pid + ", r.id, 'affiliation', r.affiliation, lower(r.affiliation)" + join + " AND r.affiliation IS NOT NULL" + Fresh("affiliation") + ";");
                statements.Add("INSERT INTO profilehistoryvalues (propertyid, entryid, subfieldkey, valuedate) SELECT " + Pid + ", r.id, 'dateAwarded', r.dateawarded" + join + " AND r.dateawarded IS NOT NULL" + Fresh("dateAwarded") + ";");
                statements.Add("INSERT INTO profilehistoryvalues (propertyid, entryid, subfieldkey, valuetext, valueuserid) SELECT " + Pid + ", r.id, 'awardedBy', COALESCE(r.awardedbyname, ''), r.awardedbyuserid" + join +
                    " AND (r.awardedbyname IS NOT NULL OR r.awardedbyuserid IS NOT NULL)" + Fresh("awardedBy") + ";");
            }
            if (written.Count == 0) return;
            statements.Add(Recount(written));
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task RecountAsync(List<string>? fieldIds, CancellationToken token = default)
        {
            if (fieldIds != null && fieldIds.Count == 0) return;
            await QueryAsync(Recount(fieldIds), token).ConfigureAwait(false);
        }

        private string Recount(List<string>? fieldIds)
        {
            return "UPDATE profilefields f SET valuecount = (SELECT COUNT(*) FROM (SELECT v.userid FROM profilevalues v WHERE v.propertyid = " + Pid + " AND v.fieldid = f.id " +
                "UNION SELECT h.userid FROM profilehistoryentries h WHERE h.propertyid = " + Pid + " AND h.fieldid = f.id) x) WHERE f.propertyid = " + Pid +
                (fieldIds == null ? String.Empty : " AND f.id IN " + D.InList(fieldIds)) + ";";
        }

        private async Task<Dictionary<string, ProfileValueSet>> ReadAsync(List<string> userIds, List<string>? fieldIds, CancellationToken token)
        {
            Dictionary<string, ProfileValueSet> sets = new Dictionary<string, ProfileValueSet>(StringComparer.Ordinal);
            string users = D.InList(userIds);
            string fields = fieldIds == null ? String.Empty : " AND fieldid IN " + D.InList(fieldIds);
            ProfileValueSet SetOf(string userId)
            {
                if (!sets.TryGetValue(userId, out ProfileValueSet? set))
                {
                    set = new ProfileValueSet { UserId = userId };
                    sets[userId] = set;
                }
                return set;
            }

            DataTable values = await QueryAsync("SELECT * FROM profilevalues WHERE " + Scope() + " AND userid IN " + users + fields + " LIMIT 200000;", token).ConfigureAwait(false);
            foreach (DataRow row in values.Rows)
            {
                ProfileValue v = new ProfileValue
                {
                    UserId = RowReader.GetString(row, "userid"),
                    FieldId = RowReader.GetString(row, "fieldid"),
                    ValueText = RowReader.GetNullableString(row, "valuetext"),
                    ValueNormalized = RowReader.GetNullableString(row, "valuenormalized"),
                    ValueNumber = Double(row, "valuenumber"),
                    ValueDate = RowReader.GetNullableString(row, "valuedate"),
                    ValueBool = NullableBool(row, "valuebool"),
                    ValueLevel = RowReader.GetNullableInt(row, "valuelevel"),
                    OtherText = RowReader.GetNullableString(row, "othertext"),
                    UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                    UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
                };
                SetOf(v.UserId).Values[v.FieldId] = v;
            }
            DataTable options = await QueryAsync("SELECT * FROM profilevalueoptions WHERE " + Scope() + " AND userid IN " + users + fields + " ORDER BY userid, fieldid, position LIMIT 200000;", token).ConfigureAwait(false);
            foreach (DataRow row in options.Rows)
            {
                ProfileValueSet set = SetOf(RowReader.GetString(row, "userid"));
                string fieldId = RowReader.GetString(row, "fieldid");
                if (!set.Values.TryGetValue(fieldId, out ProfileValue? v))
                {
                    v = new ProfileValue { UserId = set.UserId, FieldId = fieldId };
                    set.Values[fieldId] = v;
                }
                v.OptionKeys.Add(RowReader.GetString(row, "optionkey"));
            }
            DataTable entries = await QueryAsync("SELECT * FROM profilehistoryentries WHERE " + Scope() + " AND userid IN " + users + fields + " ORDER BY userid, fieldid, position, id LIMIT 50000;", token).ConfigureAwait(false);
            if (entries.Rows.Count == 0) return sets;
            Dictionary<string, ProfileHistoryEntry> byId = new Dictionary<string, ProfileHistoryEntry>(StringComparer.Ordinal);
            foreach (DataRow row in entries.Rows)
            {
                ProfileHistoryEntry e = new ProfileHistoryEntry
                {
                    Id = RowReader.GetString(row, "id"),
                    UserId = RowReader.GetString(row, "userid"),
                    FieldId = RowReader.GetString(row, "fieldid"),
                    Position = RowReader.GetInt(row, "position"),
                    CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                    UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
                };
                byId[e.Id] = e;
                ProfileValueSet set = SetOf(e.UserId);
                if (!set.History.TryGetValue(e.FieldId, out List<ProfileHistoryEntry>? list))
                {
                    list = new List<ProfileHistoryEntry>();
                    set.History[e.FieldId] = list;
                }
                list.Add(e);
            }
            DataTable hvalues = await QueryAsync("SELECT * FROM profilehistoryvalues WHERE " + Scope() + " AND entryid IN " + D.InList(byId.Keys) + " LIMIT 500000;", token).ConfigureAwait(false);
            foreach (DataRow row in hvalues.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "entryid"), out ProfileHistoryEntry? e)) continue;
                e.Values.Add(new ProfileHistoryValue
                {
                    SubFieldKey = RowReader.GetString(row, "subfieldkey"),
                    ValueText = RowReader.GetNullableString(row, "valuetext"),
                    ValueNormalized = RowReader.GetNullableString(row, "valuenormalized"),
                    ValueNumber = Double(row, "valuenumber"),
                    ValueDate = RowReader.GetNullableString(row, "valuedate"),
                    ValueBool = NullableBool(row, "valuebool"),
                    ValueLevel = RowReader.GetNullableInt(row, "valuelevel"),
                    ValueUserId = RowReader.GetNullableString(row, "valueuserid")
                });
            }
            return sets;
        }

        private string Num(double? value)
        {
            return value == null ? "NULL" : D.Num(value.Value);
        }

        private string Bool(bool? value)
        {
            return value == null ? "NULL" : D.Bool(value.Value);
        }

        private static double? Double(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == null || row[column] == DBNull.Value) return null;
            return Convert.ToDouble(row[column], CultureInfo.InvariantCulture);
        }

        private static bool? NullableBool(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == null || row[column] == DBNull.Value) return null;
            return RowReader.GetBool(row, column);
        }
    }
}
