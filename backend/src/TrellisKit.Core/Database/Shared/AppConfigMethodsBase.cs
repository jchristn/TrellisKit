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
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared feature flag, kill switch, and content version methods (docs/LIVE_UPDATES_CONTRACT.md section 16.3):
    /// flags are immutable revision rows; a publish closes the current revisions of drafted objects and opens the
    /// drafts in one transaction. Kill switches have one live revision edited in place.
    /// </summary>
    internal abstract class AppConfigMethodsBase : MethodsBase, IAppConfigMethods
    {
        private const string _Columns = "revisionid, objectid, flagkey, name, description, kind, valuetype, defaultvalue, offvalue, enabled, message, blocksserver, state, validfromversion, validtoversion, isdraft, editedbyuserid, createdutc, updatedutc";

        private const string _VersionColumns = "id, version, message, kind, restoredfromversion, publishedbyuserid, publishedbyname, flagchanges, stringchanges, bannerchanges, assetchanges, publishedutc";

        private const string _NotKillSwitch = "kind <> 'KillSwitch'";

        protected AppConfigMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        #region Flags

        public Task<FeatureFlag?> ReadDraftAsync(string objectId, CancellationToken token = default)
        {
            return ReadOneAsync("objectid = " + D.Str(objectId ?? String.Empty) + " AND isdraft = TRUE", token);
        }

        public Task<FeatureFlag?> ReadCurrentAsync(string objectId, CancellationToken token = default)
        {
            return ReadOneAsync("objectid = " + D.Str(objectId ?? String.Empty) + " AND isdraft = FALSE AND validtoversion IS NULL", token);
        }

        public Task<FeatureFlag?> ReadRevisionAsync(string revisionId, CancellationToken token = default)
        {
            return ReadOneAsync("revisionid = " + D.Str(revisionId ?? String.Empty), token);
        }

        public async Task<bool> KeyInUseAsync(string key, string? excludeObjectId, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM featureflags WHERE " + Scope() + " AND lower(flagkey) = lower(" + D.Str(key) + ") AND (isdraft = TRUE OR validtoversion IS NULL)" +
                (excludeObjectId == null ? String.Empty : " AND objectid <> " + D.Str(excludeObjectId)) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task SaveDraftAsync(FeatureFlag flag, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(flag);
            RequireText(flag.RevisionId, nameof(flag.RevisionId));
            RequireText(flag.Id, nameof(flag.Id));
            flag.IsDraft = true;
            flag.ValidFromVersion = null;
            flag.ValidToVersion = null;
            List<string> statements = DeleteRevisionStatements("(SELECT revisionid FROM featureflags WHERE " + Scope() + " AND objectid = " + D.Str(flag.Id) + " AND isdraft = TRUE)");
            statements.AddRange(InsertRevisionStatements(flag, "FeatureFlag"));
            await UniqueTransactionAsync(statements, "Feature flag", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteDraftAsync(string objectId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(objectId)) return false;
            long drafts = await CountAsync("SELECT COUNT(*) AS cnt FROM featureflags WHERE " + Scope() + " AND objectid = " + D.Str(objectId) + " AND isdraft = TRUE;", token).ConfigureAwait(false);
            if (drafts == 0) return false;
            List<string> statements = DeleteRevisionStatements("(SELECT revisionid FROM featureflags WHERE " + Scope() + " AND objectid = " + D.Str(objectId) + " AND isdraft = TRUE)");
            long published = await CountAsync("SELECT COUNT(*) AS cnt FROM featureflags WHERE " + Scope() + " AND objectid = " + D.Str(objectId) + " AND isdraft = FALSE;", token).ConfigureAwait(false);
            if (published == 0) statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.FeatureFlag, objectId));
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> HasPublishedRevisionAsync(string objectId, CancellationToken token = default)
        {
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM featureflags WHERE " + Scope() + " AND objectid = " + D.Str(objectId ?? String.Empty) + " AND isdraft = FALSE;", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<List<FeatureFlag>> ListCurrentAsync(bool killSwitches, int limit, CancellationToken token = default)
        {
            return await ListAsync("isdraft = FALSE AND validtoversion IS NULL AND " + (killSwitches ? "kind = 'KillSwitch'" : _NotKillSwitch), "lower(flagkey)", limit, token).ConfigureAwait(false);
        }

        public async Task<List<FeatureFlag>> ListAtVersionAsync(long version, int limit, CancellationToken token = default)
        {
            return await ListAsync("isdraft = FALSE AND " + _NotKillSwitch + " AND validfromversion <= " + D.Num(version) + " AND (validtoversion IS NULL OR validtoversion > " + D.Num(version) + ")",
                "lower(flagkey)", limit, token).ConfigureAwait(false);
        }

        public async Task<List<FeatureFlag>> ListDraftsAsync(int limit, CancellationToken token = default)
        {
            return await ListAsync("isdraft = TRUE AND " + _NotKillSwitch, "lower(flagkey)", limit, token).ConfigureAwait(false);
        }

        public Task<long> CountDraftsAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM featureflags WHERE " + Scope() + " AND isdraft = TRUE AND " + _NotKillSwitch + ";", token);
        }

        public Task<long> CountFlagsAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(DISTINCT objectid) AS cnt FROM featureflags WHERE " + Scope() + " AND " + _NotKillSwitch + ";", token);
        }

        public async Task<EnumerationResult<FeatureFlag>> EnumerateFlagsAsync(EnumerationRequest request, FeatureFlagFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>
            {
                "f.kind <> 'KillSwitch'",
                "(f.isdraft = TRUE OR (f.validtoversion IS NULL AND NOT EXISTS (SELECT 1 FROM featureflags fd WHERE " + Scope("fd") + " AND fd.objectid = f.objectid AND fd.isdraft = TRUE)))"
            };
            if (filter.Kind != null) conditions.Add("f.kind = " + D.Enum(filter.Kind.Value));
            if (filter.State != null) conditions.Add("f.state = " + D.Enum(filter.State.Value));
            if (filter.HasDraftChanges != null) conditions.Add("f.isdraft = " + D.Bool(filter.HasDraftChanges.Value));
            if (!String.IsNullOrEmpty(filter.Client)) conditions.Add("EXISTS (SELECT 1 FROM featureflagclients fc WHERE " + Scope("fc") + " AND fc.revisionid = f.revisionid AND fc.client = " + D.Str(filter.Client) + ")");
            EnumerationResult<FeatureFlag> result = await EnumerateAsync(request, Prefixed("f", _Columns), "featureflags f", conditions, MapFlag, token).ConfigureAwait(false);
            await LoadChildrenAsync(result.Objects, token).ConfigureAwait(false);
            return result;
        }

        public async Task<EnumerationResult<FeatureFlag>> EnumerateDraftsAsync(EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "f.kind <> 'KillSwitch'", "f.isdraft = TRUE" };
            EnumerationResult<FeatureFlag> result = await EnumerateAsync(request, Prefixed("f", _Columns), "featureflags f", conditions, MapFlag, token).ConfigureAwait(false);
            await LoadChildrenAsync(result.Objects, token).ConfigureAwait(false);
            return result;
        }

        public async Task<EnumerationResult<FeatureFlag>> EnumerateHistoryAsync(EnumerationRequest request, string objectId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "f.objectid = " + D.Str(objectId ?? String.Empty), "f.isdraft = FALSE" };
            EnumerationResult<FeatureFlag> result = await EnumerateAsync(request, Prefixed("f", _Columns), "featureflags f", conditions, MapFlag, token).ConfigureAwait(false);
            await LoadChildrenAsync(result.Objects, token).ConfigureAwait(false);
            return result;
        }

        #endregion

        #region Content-Versions

        public async Task<AppContentState> ReadStateAsync(CancellationToken token = default)
        {
            await QueryAsync("INSERT INTO appcontentstate (propertyid, contentversion, killswitchrevision, updatedutc) VALUES (" + Pid + ", 0, 0, " + D.Ts(DateTime.UtcNow) + ") ON CONFLICT (propertyid) DO NOTHING;", token).ConfigureAwait(false);
            DataTable table = await QueryAsync("SELECT contentversion, killswitchrevision, updatedutc FROM appcontentstate WHERE " + Scope() + ";", token).ConfigureAwait(false);
            DataRow row = table.Rows[0];
            return new AppContentState
            {
                ContentVersion = RowReader.GetLong(row, "contentversion"),
                KillSwitchRevision = RowReader.GetLong(row, "killswitchrevision"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        public async Task<bool> PublishAsync(long expectedVersion, ContentVersion record, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            RequireText(record.Id, nameof(record.Id));
            AppContentState state = await ReadStateAsync(token).ConfigureAwait(false);
            if (state.ContentVersion != expectedVersion) return false;
            long n = expectedVersion + 1;
            record.Version = n;
            string drafts = "(SELECT objectid FROM featureflags WHERE " + Scope() + " AND isdraft = TRUE AND " + _NotKillSwitch + ")";
            List<string> statements = new List<string>
            {
                "INSERT INTO contentversions (propertyid, " + _VersionColumns + ") VALUES (" + Pid + ", " + D.Str(record.Id) + ", " + D.Num(n) + ", " + D.Str(record.Message) + ", " + D.Enum(record.Kind) + ", " +
                    NumOrNull(record.RestoredFromVersion) + ", " + D.Str(record.PublishedByUserId) + ", " + D.Str(record.PublishedByName) + ", " + D.Num(record.FlagChanges) + ", " + D.Num(record.StringChanges) + ", " +
                    D.Num(record.BannerChanges) + ", " + D.Num(record.AssetChanges) + ", " + D.Ts(record.PublishedUtc) + ");",
                "INSERT INTO appcontentchanges (propertyid, version, objecttype, objectid, change) SELECT propertyid, " + D.Num(n) + ", 'FeatureFlag', objectid, CASE WHEN state = 'Archived' THEN 'Delete' ELSE 'Upsert' END " +
                    "FROM featureflags WHERE " + Scope() + " AND isdraft = TRUE AND " + _NotKillSwitch + ";",
                "UPDATE featureflags SET validtoversion = " + D.Num(n) + " WHERE " + Scope() + " AND isdraft = FALSE AND validtoversion IS NULL AND " + _NotKillSwitch + " AND objectid IN " + drafts + ";",
                "UPDATE featureflags SET isdraft = FALSE, validfromversion = " + D.Num(n) + ", state = CASE WHEN state = 'Draft' THEN 'Published' ELSE state END WHERE " + Scope() + " AND isdraft = TRUE AND " + _NotKillSwitch + ";",
                "UPDATE appcontentstate SET contentversion = " + D.Num(n) + ", updatedutc = " + D.Ts(record.PublishedUtc) + " WHERE " + Scope() + " AND contentversion = " + D.Num(expectedVersion) + ";"
            };
            if (Driver.AppContent is AppContentMethodsBase content) statements.InsertRange(statements.Count - 1, content.PublishStatements(n));
            try
            {
                await TransactionAsync(statements, token).ConfigureAwait(false);
            }
            catch (Exception e) when (D.IsUniqueViolation(e))
            {
                return false;
            }
            return true;
        }

        public async Task<int> PruneChangesAsync(long belowVersion, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM appcontentchanges WHERE " + Scope() + " AND version < " + D.Num(belowVersion) + " RETURNING version;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public Task<EnumerationResult<ContentVersion>> EnumerateVersionsAsync(EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return EnumerateAsync(request, Prefixed("v", _VersionColumns), "contentversions v", new List<string>(), MapVersion, token);
        }

        public async Task<ContentVersion?> ReadVersionAsync(long version, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _VersionColumns + " FROM contentversions WHERE " + Scope() + " AND version = " + D.Num(version) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapVersion(table.Rows[0]);
        }

        public async Task<EnumerationResult<FeatureFlag>> EnumerateChangesAsync(EnumerationRequest request, long version, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "x.version = " + D.Num(version) };
            EnumerationResult<FeatureFlag> result = await EnumerateAsync(request, Prefixed("f", _Columns), "appcontentchanges x JOIN featureflags f ON f.objectid = x.objectid AND f.validfromversion = x.version AND " +
                Scope("x"), conditions, MapFlag, token).ConfigureAwait(false);
            await LoadChildrenAsync(result.Objects, token).ConfigureAwait(false);
            return result;
        }

        #endregion

        #region Kill-Switches

        public async Task EnsureKillSwitchesAsync(List<FeatureFlag> seeds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(seeds);
            if (seeds.Count == 0) return;
            DataTable table = await QueryAsync("SELECT featurekey FROM killswitchstate WHERE " + Scope() + ";", token).ConfigureAwait(false);
            HashSet<string> existing = new HashSet<string>(table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "featurekey")), StringComparer.Ordinal);
            List<string> statements = new List<string>();
            foreach (FeatureFlag seed in seeds.Where(s => !existing.Contains(s.Key)))
            {
                seed.IsDraft = false;
                seed.ValidFromVersion = 0;
                seed.ValidToVersion = null;
                statements.AddRange(InsertRevisionStatements(seed, "KillSwitch"));
                statements.Add("INSERT INTO killswitchstate (propertyid, featurekey, flagobjectid, revision, lastchangedbyuserid, lastchangedbyname, lastchangedreason, lastchangedutc) VALUES (" + Pid + ", " +
                    D.Str(seed.Key) + ", " + D.Str(seed.Id) + ", 0, NULL, NULL, NULL, NULL) ON CONFLICT (propertyid, featurekey) DO NOTHING;");
            }
            if (statements.Count == 0) return;
            try
            {
                await TransactionAsync(statements, token).ConfigureAwait(false);
            }
            catch (Exception e) when (D.IsUniqueViolation(e))
            {
                // Another task seeded the same switches first.
            }
        }

        public async Task<List<KillSwitchState>> ListKillSwitchStatesAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT featurekey, flagobjectid, revision, lastchangedbyuserid, lastchangedbyname, lastchangedreason, lastchangedutc FROM killswitchstate WHERE " + Scope() +
                " ORDER BY featurekey LIMIT 500;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapState).ToList();
        }

        public async Task<long> SaveKillSwitchAsync(FeatureFlag live, KillSwitchState state, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(live);
            ArgumentNullException.ThrowIfNull(state);
            RequireText(live.RevisionId, nameof(live.RevisionId));
            string revision = "(" + D.Str(live.RevisionId) + ")";
            List<string> statements = new List<string>
            {
                "DELETE FROM featureflagconditionvalues WHERE " + Scope() + " AND revisionid IN " + revision + ";",
                "DELETE FROM featureflagconditions WHERE " + Scope() + " AND revisionid IN " + revision + ";",
                "DELETE FROM featureflagrules WHERE " + Scope() + " AND revisionid IN " + revision + ";",
                "UPDATE featureflags SET enabled = " + D.Bool(live.Enabled) + ", message = " + D.Str(live.Message) + ", blocksserver = " + D.Bool(live.BlocksServer) + ", editedbyuserid = " + D.Str(live.EditedByUserId) +
                    ", updatedutc = " + D.Ts(live.UpdatedUtc) + " WHERE " + Scope() + " AND revisionid = " + D.Str(live.RevisionId) + ";"
            };
            statements.AddRange(InsertRuleStatements(live, "KillSwitch"));
            statements.Add("INSERT INTO appcontentstate (propertyid, contentversion, killswitchrevision, updatedutc) VALUES (" + Pid + ", 0, 0, " + D.Ts(live.UpdatedUtc) + ") ON CONFLICT (propertyid) DO NOTHING;");
            statements.Add("UPDATE appcontentstate SET killswitchrevision = killswitchrevision + 1, updatedutc = " + D.Ts(live.UpdatedUtc) + " WHERE " + Scope() + ";");
            statements.Add("UPDATE killswitchstate SET revision = (SELECT s.killswitchrevision FROM appcontentstate s WHERE " + Scope("s") + "), lastchangedbyuserid = " + D.Str(state.LastChangedByUserId) +
                ", lastchangedbyname = " + D.Str(state.LastChangedByName) + ", lastchangedreason = " + D.Str(state.LastChangedReason) + ", lastchangedutc = " + D.Ts(state.LastChangedUtc) +
                " WHERE " + Scope() + " AND featurekey = " + D.Str(state.FeatureKey) + " RETURNING revision;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "revision");
        }

        public async Task<EnumerationResult<FeatureFlag>> EnumerateKillSwitchesAsync(EnumerationRequest request, bool? killed, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "f.kind = 'KillSwitch'", "f.isdraft = FALSE", "f.validtoversion IS NULL" };
            string anyKill = "EXISTS (SELECT 1 FROM featureflagrules fr WHERE " + Scope("fr") + " AND fr.revisionid = f.revisionid AND fr.value = 'true')";
            if (killed == true) conditions.Add(anyKill);
            if (killed == false) conditions.Add("NOT " + anyKill);
            EnumerationResult<FeatureFlag> result = await EnumerateAsync(request, Prefixed("f", _Columns), "featureflags f", conditions, MapFlag, token).ConfigureAwait(false);
            await LoadChildrenAsync(result.Objects, token).ConfigureAwait(false);
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task<FeatureFlag?> ReadOneAsync(string condition, CancellationToken token)
        {
            List<FeatureFlag> list = await ListAsync(condition, "revisionid", 1, token).ConfigureAwait(false);
            return list.Count == 0 ? null : list[0];
        }

        private async Task<List<FeatureFlag>> ListAsync(string condition, string order, int limit, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM featureflags WHERE " + Scope() + " AND " + condition + " ORDER BY " + order + " LIMIT " + D.Num(Math.Clamp(limit, 1, 5000)) + ";", token).ConfigureAwait(false);
            List<FeatureFlag> flags = table.Rows.Cast<DataRow>().Select(MapFlag).ToList();
            await LoadChildrenAsync(flags, token).ConfigureAwait(false);
            return flags;
        }

        private async Task LoadChildrenAsync(List<FeatureFlag> flags, CancellationToken token)
        {
            if (flags.Count == 0) return;
            Dictionary<string, FeatureFlag> byRevision = flags.GroupBy(f => f.RevisionId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            string ids = D.InList(byRevision.Keys);
            DataTable variants = await QueryAsync("SELECT revisionid, value FROM featureflagvariants WHERE " + Scope() + " AND revisionid IN " + ids + " ORDER BY revisionid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in variants.Rows) byRevision[RowReader.GetString(row, "revisionid")].Variants.Add(RowReader.GetString(row, "value"));
            DataTable clients = await QueryAsync("SELECT revisionid, client FROM featureflagclients WHERE " + Scope() + " AND revisionid IN " + ids + " ORDER BY revisionid, client;", token).ConfigureAwait(false);
            foreach (DataRow row in clients.Rows) byRevision[RowReader.GetString(row, "revisionid")].Clients.Add(RowReader.GetString(row, "client"));
            DataTable rules = await QueryAsync("SELECT revisionid, id, position, description, value, rolloutbasispoints FROM featureflagrules WHERE " + Scope() + " AND revisionid IN " + ids +
                " ORDER BY revisionid, position;", token).ConfigureAwait(false);
            Dictionary<string, FeatureFlagRule> ruleByKey = new Dictionary<string, FeatureFlagRule>(StringComparer.Ordinal);
            foreach (DataRow row in rules.Rows)
            {
                FeatureFlagRule rule = new FeatureFlagRule
                {
                    Id = RowReader.GetString(row, "id"),
                    Position = RowReader.GetInt(row, "position"),
                    Description = RowReader.GetNullableString(row, "description"),
                    Value = RowReader.GetString(row, "value"),
                    RolloutBasisPoints = RowReader.GetInt(row, "rolloutbasispoints")
                };
                string revisionId = RowReader.GetString(row, "revisionid");
                byRevision[revisionId].Rules.Add(rule);
                ruleByKey[revisionId + "|" + rule.Id] = rule;
            }
            if (ruleByKey.Count == 0) return;
            DataTable conditions = await QueryAsync("SELECT revisionid, ruleid, position, attribute, operator, argument FROM featureflagconditions WHERE " + Scope() + " AND revisionid IN " + ids +
                " ORDER BY revisionid, ruleid, position;", token).ConfigureAwait(false);
            Dictionary<string, FeatureFlagCondition> conditionByKey = new Dictionary<string, FeatureFlagCondition>(StringComparer.Ordinal);
            foreach (DataRow row in conditions.Rows)
            {
                string key = RowReader.GetString(row, "revisionid") + "|" + RowReader.GetString(row, "ruleid");
                if (!ruleByKey.TryGetValue(key, out FeatureFlagRule? rule)) continue;
                FeatureFlagCondition condition = new FeatureFlagCondition
                {
                    Position = RowReader.GetInt(row, "position"),
                    Attribute = RowReader.GetString(row, "attribute"),
                    Operator = RowReader.GetString(row, "operator"),
                    Argument = RowReader.GetNullableString(row, "argument")
                };
                condition.Id = rule.Id + "." + condition.Position;
                rule.Conditions.Add(condition);
                conditionByKey[key + "|" + condition.Position] = condition;
            }
            if (conditionByKey.Count == 0) return;
            DataTable values = await QueryAsync("SELECT revisionid, ruleid, conditionposition, value FROM featureflagconditionvalues WHERE " + Scope() + " AND revisionid IN " + ids +
                " ORDER BY revisionid, ruleid, conditionposition, position;", token).ConfigureAwait(false);
            foreach (DataRow row in values.Rows)
            {
                string key = RowReader.GetString(row, "revisionid") + "|" + RowReader.GetString(row, "ruleid") + "|" + RowReader.GetInt(row, "conditionposition");
                if (conditionByKey.TryGetValue(key, out FeatureFlagCondition? condition)) condition.Values.Add(RowReader.GetString(row, "value"));
            }
        }

        private List<string> DeleteRevisionStatements(string revisionSubquery)
        {
            return new List<string>
            {
                "DELETE FROM featureflagconditionvalues WHERE " + Scope() + " AND revisionid IN " + revisionSubquery + ";",
                "DELETE FROM featureflagconditions WHERE " + Scope() + " AND revisionid IN " + revisionSubquery + ";",
                "DELETE FROM featureflagrules WHERE " + Scope() + " AND revisionid IN " + revisionSubquery + ";",
                "DELETE FROM featureflagclients WHERE " + Scope() + " AND revisionid IN " + revisionSubquery + ";",
                "DELETE FROM featureflagvariants WHERE " + Scope() + " AND revisionid IN " + revisionSubquery + ";",
                "DELETE FROM featureflags WHERE " + Scope() + " AND revisionid IN " + revisionSubquery + ";"
            };
        }

        private List<string> InsertRevisionStatements(FeatureFlag f, string ownerType)
        {
            List<string> statements = new List<string>
            {
                "INSERT INTO featureflags (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(f.RevisionId) + ", " + D.Str(f.Id) + ", " + D.Str(f.Key) + ", " + D.Str(f.Name) + ", " +
                    D.Str(f.Description) + ", " + D.Enum(f.Kind) + ", " + D.Enum(f.ValueType) + ", " + D.Str(f.DefaultValue) + ", " + D.Str(f.OffValue) + ", " + D.Bool(f.Enabled) + ", " +
                    D.Str(f.Message) + ", " + D.Bool(f.BlocksServer) + ", " + D.Enum(f.State) + ", " + NumOrNull(f.ValidFromVersion) + ", " + NumOrNull(f.ValidToVersion) + ", " + D.Bool(f.IsDraft) + ", " +
                    D.Str(f.EditedByUserId) + ", " + D.Ts(f.CreatedUtc) + ", " + D.Ts(f.UpdatedUtc) + ");"
            };
            if (f.Variants.Count > 0)
                statements.Add("INSERT INTO featureflagvariants (propertyid, revisionid, position, value) VALUES " + String.Join(", ", f.Variants.Select((v, i) => "(" + Pid + ", " + D.Str(f.RevisionId) + ", " + D.Num(i) + ", " + D.Str(v) + ")")) + ";");
            List<string> clients = f.Clients.Distinct(StringComparer.Ordinal).ToList();
            if (clients.Count > 0)
                statements.Add("INSERT INTO featureflagclients (propertyid, revisionid, client) VALUES " + String.Join(", ", clients.Select(c => "(" + Pid + ", " + D.Str(f.RevisionId) + ", " + D.Str(c) + ")")) + ";");
            statements.AddRange(InsertRuleStatements(f, ownerType));
            return statements;
        }

        private List<string> InsertRuleStatements(FeatureFlag f, string ownerType)
        {
            List<string> statements = new List<string>();
            foreach (FeatureFlagRule rule in f.Rules)
            {
                statements.Add("INSERT INTO featureflagrules (propertyid, revisionid, id, ownertype, position, description, value, rolloutbasispoints) VALUES (" + Pid + ", " + D.Str(f.RevisionId) + ", " +
                    D.Str(rule.Id) + ", " + D.Str(ownerType) + ", " + D.Num(rule.Position) + ", " + D.Str(rule.Description) + ", " + D.Str(rule.Value) + ", " + D.Num(rule.RolloutBasisPoints) + ");");
                for (int c = 0; c < rule.Conditions.Count; c++)
                {
                    FeatureFlagCondition condition = rule.Conditions[c];
                    statements.Add("INSERT INTO featureflagconditions (propertyid, revisionid, ruleid, position, attribute, operator, argument) VALUES (" + Pid + ", " + D.Str(f.RevisionId) + ", " + D.Str(rule.Id) + ", " +
                        D.Num(c) + ", " + D.Str(condition.Attribute) + ", " + D.Str(condition.Operator) + ", " + D.Str(condition.Argument) + ");");
                    if (condition.Values.Count > 0)
                    {
                        int position = c;
                        statements.Add("INSERT INTO featureflagconditionvalues (propertyid, revisionid, ruleid, conditionposition, position, value) VALUES " + String.Join(", ", condition.Values.Select((v, i) => "(" + Pid + ", " +
                            D.Str(f.RevisionId) + ", " + D.Str(rule.Id) + ", " + D.Num(position) + ", " + D.Num(i) + ", " + D.Str(v) + ")")) + ";");
                    }
                }
            }
            return statements;
        }

        private string NumOrNull(long? value)
        {
            return value == null ? "NULL" : D.Num(value.Value);
        }

        private static string Prefixed(string alias, string columns)
        {
            return String.Join(", ", columns.Split(',').Select(c => alias + "." + c.Trim()));
        }

        private static FeatureFlag MapFlag(DataRow row)
        {
            return new FeatureFlag
            {
                RevisionId = RowReader.GetString(row, "revisionid"),
                Id = RowReader.GetString(row, "objectid"),
                Key = RowReader.GetString(row, "flagkey"),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetNullableString(row, "description"),
                Kind = RowReader.GetEnum(row, "kind", FeatureFlagKindEnum.Boolean),
                ValueType = RowReader.GetEnum(row, "valuetype", FeatureFlagValueTypeEnum.Boolean),
                DefaultValue = RowReader.GetString(row, "defaultvalue"),
                OffValue = RowReader.GetString(row, "offvalue"),
                Enabled = RowReader.GetBool(row, "enabled"),
                Message = RowReader.GetNullableString(row, "message"),
                BlocksServer = RowReader.GetBool(row, "blocksserver"),
                State = RowReader.GetEnum(row, "state", FeatureFlagStateEnum.Draft),
                ValidFromVersion = RowReader.GetNullableLong(row, "validfromversion"),
                ValidToVersion = RowReader.GetNullableLong(row, "validtoversion"),
                IsDraft = RowReader.GetBool(row, "isdraft"),
                EditedByUserId = RowReader.GetNullableString(row, "editedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static ContentVersion MapVersion(DataRow row)
        {
            return new ContentVersion
            {
                Id = RowReader.GetString(row, "id"),
                Version = RowReader.GetLong(row, "version"),
                Message = RowReader.GetString(row, "message"),
                Kind = RowReader.GetEnum(row, "kind", ContentVersionKindEnum.Publish),
                RestoredFromVersion = RowReader.GetNullableLong(row, "restoredfromversion"),
                PublishedByUserId = RowReader.GetNullableString(row, "publishedbyuserid"),
                PublishedByName = RowReader.GetString(row, "publishedbyname"),
                FlagChanges = RowReader.GetInt(row, "flagchanges"),
                StringChanges = RowReader.GetInt(row, "stringchanges"),
                BannerChanges = RowReader.GetInt(row, "bannerchanges"),
                AssetChanges = RowReader.GetInt(row, "assetchanges"),
                PublishedUtc = RowReader.GetDateTime(row, "publishedutc")
            };
        }

        private static KillSwitchState MapState(DataRow row)
        {
            return new KillSwitchState
            {
                FeatureKey = RowReader.GetString(row, "featurekey"),
                FlagObjectId = RowReader.GetString(row, "flagobjectid"),
                Revision = RowReader.GetLong(row, "revision"),
                LastChangedByUserId = RowReader.GetNullableString(row, "lastchangedbyuserid"),
                LastChangedByName = RowReader.GetNullableString(row, "lastchangedbyname"),
                LastChangedReason = RowReader.GetNullableString(row, "lastchangedreason"),
                LastChangedUtc = RowReader.GetNullableDateTime(row, "lastchangedutc")
            };
        }

        #endregion
    }
}
