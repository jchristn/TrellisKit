namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared live update methods (docs/LIVE_UPDATES_CONTRACT.md section 23). Every statement is property-scoped.
    /// </summary>
    internal abstract class LiveUpdateMethodsBase : MethodsBase, ILiveUpdateMethods
    {
        private const string _GroupColumns = "id, kind, channel, message, status, rolloutbasispoints, critical, criticalmessage, gitcommit, gitdirty, republishof, promotedfrom, clientgroupid, " +
            "createdbytype, createdbyid, createdbyname, autohaltenabled, autohaltpercent, autohaltmininstalls, haltedutc, haltreason, publishedutc, pausedutc, rolledbackutc, supersededutc, createdutc, updatedutc";

        private const string _UpdateColumns = "id, groupid, uuid, platform, runtimeversion, channel, kind, status, rolloutbasispoints, createdat, launchassethash, launchassetsizebytes, assetcount, totalbytes, createdutc, updatedutc";

        private const string _KeyColumns = "id, keyid, algorithm, keybits, source, certificatepem, certificatesha256, certificatenotafterutc, isca, privatekeyciphertext, privatekeynonce, privatekeykeyid, status, lastusedutc, createdutc, retiredutc";

        private const string _InstallColumns = "id, installidhash, platform, channel, runtimeversion, currentupdateuuid, embeddedupdateuuid, keyid, appversion, firstseenutc, lastcheckutc";

        protected LiveUpdateMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        #region Groups

        public async Task CreateGroupAsync(UpdateGroup group, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(group);
            RequireText(group.Id, nameof(group.Id));
            await UniqueAsync("INSERT INTO updategroups (propertyid, " + _GroupColumns + ") VALUES (" + Pid + ", " + D.Str(group.Id) + ", " + D.Enum(group.Kind) + ", " + D.Str(group.Channel) + ", " +
                D.Str(group.Message) + ", " + D.Enum(group.Status) + ", " + D.Num(group.RolloutBasisPoints) + ", " + D.Bool(group.Critical) + ", " + D.Str(group.CriticalMessage) + ", " +
                D.Str(group.GitCommit) + ", " + D.Bool(group.GitDirty) + ", " + D.Str(group.RepublishOf) + ", " + D.Str(group.PromotedFrom) + ", " + D.Str(group.ClientGroupId) + ", " +
                D.Enum(group.CreatedByType) + ", " + D.Str(group.CreatedById) + ", " + D.Str(group.CreatedByName) + ", " + D.Bool(group.AutoHaltEnabled) + ", " + D.Num(group.AutoHaltFailurePercent) + ", " +
                D.Num(group.AutoHaltMinInstalls) + ", " + D.Ts(group.HaltedUtc) + ", " + D.Str(group.HaltReason) + ", " + D.Ts(group.PublishedUtc) + ", " + D.Ts(group.PausedUtc) + ", " +
                D.Ts(group.RolledBackUtc) + ", " + D.Ts(group.SupersededUtc) + ", " + D.Ts(group.CreatedUtc) + ", " + D.Ts(group.UpdatedUtc) + ");", "Update group", token).ConfigureAwait(false);
        }

        public async Task<UpdateGroup?> ReadGroupAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _GroupColumns + " FROM updategroups WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapGroup(table.Rows[0]);
        }

        public async Task<UpdateGroup?> ReadGroupByClientIdAsync(string clientGroupId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(clientGroupId)) return null;
            DataTable table = await QueryAsync("SELECT " + _GroupColumns + " FROM updategroups WHERE " + Scope() + " AND clientgroupid = " + D.Str(clientGroupId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapGroup(table.Rows[0]);
        }

        public async Task<UpdateGroup?> FindJoinableGroupAsync(string channel, string? gitCommit, string message, string platform, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(channel, nameof(channel));
            RequireText(platform, nameof(platform));
            string commit = gitCommit == null ? "g.gitcommit IS NULL" : "g.gitcommit = " + D.Str(gitCommit);
            DataTable table = await QueryAsync("SELECT " + Prefixed("g", _GroupColumns) + " FROM updategroups g WHERE " + Scope("g") + " AND g.kind = " + D.Enum(UpdateGroupKindEnum.Publish) +
                " AND g.channel = " + D.Str(channel) + " AND " + commit + " AND g.message = " + D.Str(message) + " AND g.clientgroupid IS NULL AND g.createdutc >= " + D.Ts(sinceUtc) +
                " AND g.status IN (" + D.Enum(UpdateGroupStatusEnum.Draft) + ", " + D.Enum(UpdateGroupStatusEnum.Published) + ")" +
                " AND NOT EXISTS (SELECT 1 FROM updates u WHERE " + Scope("u") + " AND u.groupid = g.id AND u.platform = " + D.Str(platform) + ") ORDER BY g.createdutc DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapGroup(table.Rows[0]);
        }

        public async Task SaveGroupAsync(UpdateGroup group, bool syncRollout, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(group);
            RequireText(group.Id, nameof(group.Id));
            List<string> statements = new List<string>
            {
                "UPDATE updategroups SET message = " + D.Str(group.Message) + ", status = " + D.Enum(group.Status) + ", rolloutbasispoints = " + D.Num(group.RolloutBasisPoints) +
                ", critical = " + D.Bool(group.Critical) + ", criticalmessage = " + D.Str(group.CriticalMessage) + ", autohaltenabled = " + D.Bool(group.AutoHaltEnabled) +
                ", autohaltpercent = " + D.Num(group.AutoHaltFailurePercent) + ", autohaltmininstalls = " + D.Num(group.AutoHaltMinInstalls) + ", haltedutc = " + D.Ts(group.HaltedUtc) +
                ", haltreason = " + D.Str(group.HaltReason) + ", publishedutc = " + D.Ts(group.PublishedUtc) + ", pausedutc = " + D.Ts(group.PausedUtc) + ", rolledbackutc = " + D.Ts(group.RolledBackUtc) +
                ", supersededutc = " + D.Ts(group.SupersededUtc) + ", updatedutc = " + D.Ts(group.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(group.Id) + ";",
                "UPDATE updates SET status = " + D.Enum(group.Status) + (syncRollout ? ", rolloutbasispoints = " + D.Num(group.RolloutBasisPoints) : String.Empty) +
                ", updatedutc = " + D.Ts(group.UpdatedUtc) + " WHERE " + Scope() + " AND groupid = " + D.Str(group.Id) + ";"
            };
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task SetUpdateRolloutAsync(string updateId, int basisPoints, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(updateId, nameof(updateId));
            await QueryAsync("UPDATE updates SET rolloutbasispoints = " + D.Num(basisPoints) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(updateId) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteDraftGroupAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return false;
            string updates = "(SELECT id FROM updates WHERE " + Scope() + " AND groupid = " + D.Str(id) + ")";
            List<string> statements = Driver.Metadata.DeleteStatements(TargetTypeEnum.UpdateGroup, id);
            statements.Add("DELETE FROM updateassetrefs WHERE " + Scope() + " AND updateid IN " + updates + ";");
            // Private source maps of the deleted updates are queued for UpdateAssetGcWorker (another update may share a map,
            // so the worker re-checks references before deleting the object).
            statements.Add("INSERT INTO updategcobjects (propertyid, kind, sha256hex, storagekey, sizebytes, queuedutc) SELECT DISTINCT ON (m.sha256hex) " + Pid + ", " + D.Str(UpdateGcObject.SourceMapKind) +
                ", m.sha256hex, m.storagekey, m.sizebytes, " + D.Ts(DateTime.UtcNow) + " FROM updatesourcemaps m WHERE " + Scope("m") + " AND m.updateid IN " + updates +
                " ORDER BY m.sha256hex ON CONFLICT (propertyid, kind, sha256hex) DO UPDATE SET queuedutc = EXCLUDED.queuedutc;");
            statements.Add("DELETE FROM updatesourcemaps WHERE " + Scope() + " AND updateid IN " + updates + ";");
            statements.Add("DELETE FROM updategroupconfigs WHERE " + Scope() + " AND updateid IN " + updates + ";");
            statements.Add("DELETE FROM updaterolloutstages WHERE " + Scope() + " AND groupid = " + D.Str(id) + ";");
            statements.Add("DELETE FROM updates WHERE " + Scope() + " AND groupid = " + D.Str(id) + ";");
            statements.Add("DELETE FROM updategroups WHERE " + Scope() + " AND id = " + D.Str(id) + " AND status = " + D.Enum(UpdateGroupStatusEnum.Draft) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<EnumerationResult<UpdateGroup>> EnumerateGroupsAsync(EnumerationRequest request, UpdateGroupFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(filter.Channel)) conditions.Add("g.channel = " + D.Str(filter.Channel));
            if (filter.Status != null) conditions.Add("g.status = " + D.Enum(filter.Status.Value));
            if (filter.Kind != null) conditions.Add("g.kind = " + D.Enum(filter.Kind.Value));
            if (filter.Critical != null) conditions.Add("g.critical = " + D.Bool(filter.Critical.Value));
            if (!String.IsNullOrEmpty(filter.GitCommit)) conditions.Add(D.StartsWith("lower(COALESCE(g.gitcommit, ''))", filter.GitCommit.ToLowerInvariant()));
            if (!String.IsNullOrEmpty(filter.Platform) || !String.IsNullOrEmpty(filter.RuntimeVersion))
            {
                string inner = "EXISTS (SELECT 1 FROM updates fu WHERE " + Scope("fu") + " AND fu.groupid = g.id";
                if (!String.IsNullOrEmpty(filter.Platform)) inner += " AND fu.platform = " + D.Str(filter.Platform);
                if (!String.IsNullOrEmpty(filter.RuntimeVersion)) inner += " AND fu.runtimeversion = " + D.Str(filter.RuntimeVersion);
                conditions.Add(inner + ")");
            }
            return EnumerateAsync(request, Prefixed("g", _GroupColumns), "updategroups g", conditions, MapGroup, token);
        }

        #endregion

        #region Updates

        public async Task AddUpdateAsync(UpdateRecord update, string expoClient, List<UpdateAssetRef> assets, List<UpdateSourceMap> sourceMaps, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(update);
            ArgumentNullException.ThrowIfNull(assets);
            ArgumentNullException.ThrowIfNull(sourceMaps);
            RequireText(update.Id, nameof(update.Id));
            List<string> statements = new List<string> { InsertUpdate(update) };
            statements.Add("INSERT INTO updategroupconfigs (propertyid, updateid, expoclient) VALUES (" + Pid + ", " + D.Str(update.Id) + ", " + D.Str(expoClient ?? "{}") + ");");
            foreach (UpdateAssetRef[] chunk in assets.Chunk(500))
            {
                statements.Add("INSERT INTO updateassetrefs (propertyid, updateid, position, sha256hex, assetkey, fileextension, contenttype, islaunchasset) VALUES " +
                    String.Join(", ", chunk.Select(a => "(" + Pid + ", " + D.Str(update.Id) + ", " + D.Num(a.Position) + ", " + D.Str(a.Sha256Hex) + ", " + D.Str(a.Key) + ", " +
                        D.Str(a.FileExtension) + ", " + D.Str(a.ContentType) + ", " + D.Bool(a.IsLaunchAsset) + ")")) + ";");
            }
            foreach (UpdateSourceMap map in sourceMaps)
            {
                statements.Add("INSERT INTO updatesourcemaps (propertyid, updateid, position, sha256hex, mapkey, bundlekey, sizebytes, storagekey) VALUES (" + Pid + ", " + D.Str(update.Id) + ", " +
                    D.Num(map.Position) + ", " + D.Str(map.Sha256Hex) + ", " + D.Str(map.Key) + ", " + D.Str(map.BundleKey) + ", " + D.Num(map.SizeBytes) + ", " + D.Str(map.StorageKey) + ");");
            }
            if (assets.Count > 0)
            {
                statements.Add("UPDATE updateassets SET lastreferencedutc = " + D.Ts(update.CreatedUtc) + " WHERE " + Scope() + " AND sha256hex IN " + D.InList(assets.Select(a => a.Sha256Hex)) + ";");
            }
            await UniqueTransactionAsync(statements, "Update", token).ConfigureAwait(false);
        }

        public async Task CopyUpdateAsync(string sourceUpdateId, UpdateRecord target, CancellationToken token = default)
        {
            RequireText(sourceUpdateId, nameof(sourceUpdateId));
            ArgumentNullException.ThrowIfNull(target);
            List<string> statements = new List<string>
            {
                InsertUpdate(target),
                "INSERT INTO updategroupconfigs (propertyid, updateid, expoclient) SELECT " + Pid + ", " + D.Str(target.Id) + ", c.expoclient FROM updategroupconfigs c WHERE " + Scope("c") +
                    " AND c.updateid = " + D.Str(sourceUpdateId) + ";",
                "INSERT INTO updateassetrefs (propertyid, updateid, position, sha256hex, assetkey, fileextension, contenttype, islaunchasset) SELECT " + Pid + ", " + D.Str(target.Id) +
                    ", r.position, r.sha256hex, r.assetkey, r.fileextension, r.contenttype, r.islaunchasset FROM updateassetrefs r WHERE " + Scope("r") + " AND r.updateid = " + D.Str(sourceUpdateId) + ";",
                "INSERT INTO updatesourcemaps (propertyid, updateid, position, sha256hex, mapkey, bundlekey, sizebytes, storagekey) SELECT " + Pid + ", " + D.Str(target.Id) +
                    ", m.position, m.sha256hex, m.mapkey, m.bundlekey, m.sizebytes, m.storagekey FROM updatesourcemaps m WHERE " + Scope("m") + " AND m.updateid = " + D.Str(sourceUpdateId) + ";"
            };
            await UniqueTransactionAsync(statements, "Update", token).ConfigureAwait(false);
        }

        public async Task AddDirectiveUpdateAsync(UpdateRecord update, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(update);
            await UniqueAsync(InsertUpdate(update), "Update", token).ConfigureAwait(false);
        }

        public async Task<List<UpdateRecord>> ListGroupUpdatesAsync(string groupId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(groupId)) return new List<UpdateRecord>();
            DataTable table = await QueryAsync("SELECT " + _UpdateColumns + " FROM updates WHERE " + Scope() + " AND groupid = " + D.Str(groupId) + " ORDER BY platform LIMIT 10;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapUpdate).ToList();
        }

        public async Task<UpdateRecord?> ReadUpdateAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _UpdateColumns + " FROM updates WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapUpdate(table.Rows[0]);
        }

        public async Task<UpdateRecord?> ReadUpdateByUuidAsync(string uuid, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(uuid)) return null;
            DataTable table = await QueryAsync("SELECT " + _UpdateColumns + " FROM updates WHERE " + Scope() + " AND uuid = " + D.Str(uuid) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapUpdate(table.Rows[0]);
        }

        public async Task<List<UpdateRecord>> ListCandidatesAsync(string platform, string runtimeVersion, string channel, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _UpdateColumns + " FROM updates WHERE " + Scope() + " AND platform = " + D.Str(platform) + " AND runtimeversion = " + D.Str(runtimeVersion) +
                " AND channel = " + D.Str(channel) + " AND status IN (" + D.Enum(UpdateGroupStatusEnum.Published) + ", " + D.Enum(UpdateGroupStatusEnum.Paused) + ") ORDER BY createdat DESC, id DESC LIMIT " +
                D.Num(Math.Clamp(limit, 1, 500)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapUpdate).ToList();
        }

        public async Task<DateTime?> MaxCreatedAtAsync(string platform, string runtimeVersion, string channel, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT MAX(createdat) AS m FROM updates WHERE " + Scope() + " AND platform = " + D.Str(platform) + " AND runtimeversion = " + D.Str(runtimeVersion) +
                " AND channel = " + D.Str(channel) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetNullableDateTime(table.Rows[0], "m");
        }

        public async Task<List<UpdateRecord>> ListPublishedOnTargetAsync(string platform, string runtimeVersion, string channel, string excludeGroupId, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _UpdateColumns + " FROM updates WHERE " + Scope() + " AND platform = " + D.Str(platform) + " AND runtimeversion = " + D.Str(runtimeVersion) +
                " AND channel = " + D.Str(channel) + " AND status = " + D.Enum(UpdateGroupStatusEnum.Published) + " AND groupid <> " + D.Str(excludeGroupId ?? String.Empty) +
                " ORDER BY createdat DESC LIMIT 100;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapUpdate).ToList();
        }

        public async Task<UpdateRecord?> ReadLatestAsync(string channel, string platform, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _UpdateColumns + " FROM updates WHERE " + Scope() + " AND channel = " + D.Str(channel) + " AND platform = " + D.Str(platform) +
                " AND status = " + D.Enum(UpdateGroupStatusEnum.Published) + " ORDER BY createdat DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapUpdate(table.Rows[0]);
        }

        public async Task<string?> ReadExpoClientAsync(string updateId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(updateId)) return null;
            DataTable table = await QueryAsync("SELECT expoclient FROM updategroupconfigs WHERE " + Scope() + " AND updateid = " + D.Str(updateId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetNullableString(table.Rows[0], "expoclient");
        }

        public async Task<List<UpdateAssetRef>> ListAssetRefsAsync(string updateId, int limit, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(updateId)) return new List<UpdateAssetRef>();
            DataTable table = await QueryAsync("SELECT r.updateid, r.position, r.sha256hex, r.assetkey, r.fileextension, r.contenttype, r.islaunchasset, a.hashb64url, a.sizebytes FROM updateassetrefs r " +
                "JOIN updateassets a ON a.sha256hex = r.sha256hex AND " + Scope("a") + " WHERE " + Scope("r") + " AND r.updateid = " + D.Str(updateId) + " ORDER BY r.position LIMIT " +
                D.Num(Math.Clamp(limit, 1, 20000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRef).ToList();
        }

        public Task<EnumerationResult<UpdateAssetRef>> EnumerateAssetRefsAsync(EnumerationRequest request, string updateId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "r.updateid = " + D.Str(updateId ?? String.Empty) };
            return EnumerateAsync(request, "r.updateid, r.position, r.sha256hex, r.assetkey, r.fileextension, r.contenttype, r.islaunchasset, a.hashb64url, a.sizebytes",
                "updateassetrefs r JOIN updateassets a ON a.sha256hex = r.sha256hex AND " + Scope("a"), conditions, MapRef, token);
        }

        public Task<long> CountSourceMapsAsync(string updateId, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM updatesourcemaps WHERE " + Scope() + " AND updateid = " + D.Str(updateId ?? String.Empty) + ";", token);
        }

        #endregion

        #region Assets

        public async Task<UpdateAsset?> ReadAssetAsync(string sha256Hex, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(sha256Hex)) return null;
            DataTable table = await QueryAsync("SELECT sha256hex, hashb64url, contenttype, sizebytes, storedutc, lastreferencedutc FROM updateassets WHERE " + Scope() + " AND sha256hex = " + D.Str(sha256Hex) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new UpdateAsset
            {
                Sha256Hex = RowReader.GetString(row, "sha256hex"),
                HashB64Url = RowReader.GetString(row, "hashb64url"),
                ContentType = RowReader.GetString(row, "contenttype"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                StoredUtc = RowReader.GetNullableDateTime(row, "storedutc"),
                LastReferencedUtc = RowReader.GetDateTime(row, "lastreferencedutc")
            };
        }

        public async Task<HashSet<string>> StoredAssetsAsync(IEnumerable<string> sha256Hexes, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(sha256Hexes);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] chunk in sha256Hexes.Distinct(StringComparer.Ordinal).Chunk(1000))
            {
                DataTable table = await QueryAsync("SELECT sha256hex FROM updateassets WHERE " + Scope() + " AND storedutc IS NOT NULL AND sha256hex IN " + D.InList(chunk) + ";", token).ConfigureAwait(false);
                foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "sha256hex"));
            }
            return result;
        }

        public async Task UpsertAssetAsync(UpdateAsset asset, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(asset);
            RequireText(asset.Sha256Hex, nameof(asset.Sha256Hex));
            await QueryAsync("INSERT INTO updateassets (propertyid, sha256hex, hashb64url, contenttype, sizebytes, storedutc, lastreferencedutc) VALUES (" + Pid + ", " + D.Str(asset.Sha256Hex) + ", " +
                D.Str(asset.HashB64Url) + ", " + D.Str(asset.ContentType) + ", " + D.Num(asset.SizeBytes) + ", " + D.Ts(asset.StoredUtc) + ", " + D.Ts(asset.LastReferencedUtc) +
                ") ON CONFLICT (propertyid, sha256hex) DO UPDATE SET storedutc = COALESCE(updateassets.storedutc, EXCLUDED.storedutc), lastreferencedutc = EXCLUDED.lastreferencedutc;", token).ConfigureAwait(false);
        }

        #endregion

        #region Rollout

        public async Task ReplaceStagesAsync(string groupId, List<UpdateRolloutStage> stages, CancellationToken token = default)
        {
            RequireText(groupId, nameof(groupId));
            ArgumentNullException.ThrowIfNull(stages);
            List<string> statements = new List<string> { "DELETE FROM updaterolloutstages WHERE " + Scope() + " AND groupid = " + D.Str(groupId) + ";" };
            if (stages.Count > 0)
            {
                statements.Add("INSERT INTO updaterolloutstages (propertyid, groupid, position, basispoints, startsutc, appliedutc) VALUES " + String.Join(", ", stages.Select(s => "(" + Pid + ", " +
                    D.Str(groupId) + ", " + D.Num(s.Position) + ", " + D.Num(s.BasisPoints) + ", " + D.Ts(s.StartsUtc) + ", " + D.Ts(s.AppliedUtc) + ")")) + ";");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<List<UpdateRolloutStage>> ListStagesAsync(string groupId, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT position, basispoints, startsutc, appliedutc FROM updaterolloutstages WHERE " + Scope() + " AND groupid = " + D.Str(groupId ?? String.Empty) +
                " ORDER BY position LIMIT 20;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new UpdateRolloutStage
            {
                Position = RowReader.GetInt(row, "position"),
                BasisPoints = RowReader.GetInt(row, "basispoints"),
                StartsUtc = RowReader.GetDateTime(row, "startsutc"),
                AppliedUtc = RowReader.GetNullableDateTime(row, "appliedutc")
            }).ToList();
        }

        public async Task<List<UpdateDueStage>> ListDueStagesAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT s.groupid, s.position, s.basispoints FROM updaterolloutstages s JOIN updategroups g ON g.id = s.groupid AND " + Scope("g") +
                " WHERE " + Scope("s") + " AND s.appliedutc IS NULL AND s.startsutc <= " + D.Ts(nowUtc) + " AND g.status = " + D.Enum(UpdateGroupStatusEnum.Published) +
                " AND g.haltedutc IS NULL ORDER BY s.startsutc, s.position LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new UpdateDueStage
            {
                GroupId = RowReader.GetString(row, "groupid"),
                Position = RowReader.GetInt(row, "position"),
                BasisPoints = RowReader.GetInt(row, "basispoints")
            }).ToList();
        }

        public async Task MarkStageAppliedAsync(string groupId, int position, DateTime nowUtc, CancellationToken token = default)
        {
            await QueryAsync("UPDATE updaterolloutstages SET appliedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND groupid = " + D.Str(groupId) + " AND position <= " + D.Num(position) +
                " AND appliedutc IS NULL;", token).ConfigureAwait(false);
        }

        public async Task<List<UpdateGroup>> ListAutoHaltCandidatesAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _GroupColumns + " FROM updategroups WHERE " + Scope() + " AND status = " + D.Enum(UpdateGroupStatusEnum.Published) +
                " AND autohaltenabled = TRUE AND haltedutc IS NULL AND kind <> " + D.Enum(UpdateGroupKindEnum.RollBackToEmbedded) + " ORDER BY createdutc DESC LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapGroup).ToList();
        }

        public async Task<UpdateHealthCounts> CountHealthAsync(string groupId, DateTime activeSinceUtc, CancellationToken token = default)
        {
            string uuids = "(SELECT uuid FROM updates WHERE " + Scope() + " AND groupid = " + D.Str(groupId ?? String.Empty) + ")";
            long failures = await CountAsync("SELECT COUNT(*) AS cnt FROM (SELECT installidhash FROM updateinstallfailures WHERE " + Scope() + " AND updateuuid IN " + uuids +
                " UNION SELECT installidhash FROM updateevents WHERE " + Scope() + " AND type = " + D.Enum(UpdateEventTypeEnum.LaunchFailed) + " AND updateuuid IN " + uuids + ") t;", token).ConfigureAwait(false);
            long successes = await CountAsync("SELECT COUNT(*) AS cnt FROM updateinstalls WHERE " + Scope() + " AND currentupdateuuid IN " + uuids + " AND lastcheckutc >= " + D.Ts(activeSinceUtc) + ";", token).ConfigureAwait(false);
            return new UpdateHealthCounts { Failures = failures, Successes = successes };
        }

        #endregion

        #region Channels

        public async Task EnsureChannelsAsync(IEnumerable<string> builtIn, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(builtIn);
            List<string> names = builtIn.ToList();
            if (names.Count == 0) return;
            await QueryAsync("INSERT INTO updatechannels (propertyid, name, builtin, createdutc) VALUES " + String.Join(", ", names.Select(n => "(" + Pid + ", " + D.Str(n) + ", TRUE, " + D.Ts(nowUtc) + ")")) +
                " ON CONFLICT (propertyid, name) DO NOTHING;", token).ConfigureAwait(false);
        }

        public async Task<List<UpdateChannel>> ListChannelsAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT name, builtin, createdutc FROM updatechannels WHERE " + Scope() + " ORDER BY builtin DESC, name LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapChannel).ToList();
        }

        public async Task<UpdateChannel?> ReadChannelAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) return null;
            DataTable table = await QueryAsync("SELECT name, builtin, createdutc FROM updatechannels WHERE " + Scope() + " AND name = " + D.Str(name) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapChannel(table.Rows[0]);
        }

        public Task<EnumerationResult<UpdateChannel>> EnumerateChannelsAsync(EnumerationRequest request, bool? builtIn, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (builtIn != null) conditions.Add("c.builtin = " + D.Bool(builtIn.Value));
            return EnumerateAsync(request, "c.name, c.builtin, c.createdutc", "updatechannels c", conditions, MapChannel, token);
        }

        public async Task CreateChannelAsync(UpdateChannel channel, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(channel);
            RequireText(channel.Name, nameof(channel.Name));
            await UniqueAsync("INSERT INTO updatechannels (propertyid, name, builtin, createdutc) VALUES (" + Pid + ", " + D.Str(channel.Name) + ", " + D.Bool(channel.BuiltIn) + ", " + D.Ts(channel.CreatedUtc) + ");", "Channel", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteChannelAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) return false;
            DataTable table = await QueryAsync("DELETE FROM updatechannels WHERE " + Scope() + " AND name = " + D.Str(name) + " AND builtin = FALSE RETURNING name;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<long> CountLiveGroupsInChannelAsync(string name, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM updategroups WHERE " + Scope() + " AND channel = " + D.Str(name ?? String.Empty) + " AND status IN (" + D.Enum(UpdateGroupStatusEnum.Published) + ", " +
                D.Enum(UpdateGroupStatusEnum.Paused) + ");", token);
        }

        #endregion

        #region Deploy-Keys

        public async Task CreateDeployKeyAsync(UpdateDeployKey key, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(key);
            RequireText(key.Id, nameof(key.Id));
            List<string> statements = new List<string>
            {
                "INSERT INTO updatedeploykeys (propertyid, id, name, secrethash, expiresutc, lastusedutc, revokedutc, createdbyuserid, createdutc) VALUES (" + Pid + ", " + D.Str(key.Id) + ", " +
                D.Str(key.Name) + ", " + D.Str(key.SecretHash) + ", " + D.Ts(key.ExpiresUtc) + ", NULL, NULL, " + D.Str(key.CreatedByUserId) + ", " + D.Ts(key.CreatedUtc) + ");"
            };
            if (key.Channels.Count > 0)
            {
                statements.Add("INSERT INTO updatedeploykeychannels (propertyid, deploykeyid, channel) VALUES " + String.Join(", ", key.Channels.Distinct(StringComparer.Ordinal).Select(c => "(" + Pid + ", " +
                    D.Str(key.Id) + ", " + D.Str(c) + ")")) + ";");
            }
            await UniqueTransactionAsync(statements, "Deploy key", token).ConfigureAwait(false);
        }

        public async Task<UpdateDeployKey?> ReadDeployKeyAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            return await ReadDeployKeyWhereAsync("k.id = " + D.Str(id), token).ConfigureAwait(false);
        }

        public async Task<UpdateDeployKey?> ReadDeployKeyBySecretAsync(string secretHash, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(secretHash)) return null;
            return await ReadDeployKeyWhereAsync("k.secrethash = " + D.Str(secretHash), token).ConfigureAwait(false);
        }

        public async Task<EnumerationResult<UpdateDeployKey>> EnumerateDeployKeysAsync(EnumerationRequest request, bool? active, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            string activeSql = "(k.revokedutc IS NULL AND (k.expiresutc IS NULL OR k.expiresutc > " + D.Ts(nowUtc) + "))";
            if (active == true) conditions.Add(activeSql);
            if (active == false) conditions.Add("NOT " + activeSql);
            EnumerationResult<UpdateDeployKey> result = await EnumerateAsync(request, "k.id, k.name, k.secrethash, k.expiresutc, k.lastusedutc, k.revokedutc, k.createdbyuserid, k.createdutc",
                "updatedeploykeys k", conditions, MapDeployKey, token).ConfigureAwait(false);
            await AttachChannelsAsync(result.Objects, token).ConfigureAwait(false);
            return result;
        }

        public async Task<bool> RevokeDeployKeyAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return false;
            DataTable table = await QueryAsync("UPDATE updatedeploykeys SET revokedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND revokedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task TouchDeployKeyAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            await QueryAsync("UPDATE updatedeploykeys SET lastusedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id ?? String.Empty) + ";", token).ConfigureAwait(false);
        }

        #endregion

        #region Signing-Keys

        public async Task<List<UpdateSigningKey>> ListSigningKeysAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _KeyColumns + " FROM updatesigningkeys WHERE " + Scope() + " ORDER BY createdutc LIMIT 100;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapSigningKey).ToList();
        }

        public async Task<UpdateSigningKey?> ReadSigningKeyAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _KeyColumns + " FROM updatesigningkeys WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapSigningKey(table.Rows[0]);
        }

        public async Task CreateSigningKeyAsync(UpdateSigningKey key, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(key);
            RequireText(key.Id, nameof(key.Id));
            await UniqueAsync("INSERT INTO updatesigningkeys (propertyid, " + _KeyColumns + ") VALUES (" + Pid + ", " + D.Str(key.Id) + ", " + D.Str(key.KeyId) + ", " + D.Str(key.Algorithm) + ", " +
                D.Num(key.KeyBits) + ", " + D.Enum(key.Source) + ", " + D.Str(key.CertificatePem) + ", " + D.Str(key.CertificateSha256) + ", " + D.Ts(key.CertificateNotAfterUtc) + ", " +
                D.Bool(key.IsCertificateAuthority) + ", " + D.Str(key.PrivateKeyCiphertext) + ", " + D.Str(key.PrivateKeyNonce) + ", " + D.Str(key.PrivateKeyKeyId) + ", " + D.Enum(key.Status) + ", " +
                D.Ts(key.LastUsedUtc) + ", " + D.Ts(key.CreatedUtc) + ", " + D.Ts(key.RetiredUtc) + ");", "Signing key", token).ConfigureAwait(false);
        }

        public async Task SetSigningKeyStatusAsync(string id, UpdateSigningKeyStatusEnum status, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = new List<string>();
            if (status == UpdateSigningKeyStatusEnum.Active)
                statements.Add("UPDATE updatesigningkeys SET status = " + D.Enum(UpdateSigningKeyStatusEnum.Standby) + " WHERE " + Scope() + " AND status = " + D.Enum(UpdateSigningKeyStatusEnum.Active) + " AND id <> " + D.Str(id) + ";");
            statements.Add("UPDATE updatesigningkeys SET status = " + D.Enum(status) + ", retiredutc = " + (status == UpdateSigningKeyStatusEnum.Retired ? D.Ts(nowUtc) : "NULL") +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + ";");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task TouchSigningKeyAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            await QueryAsync("UPDATE updatesigningkeys SET lastusedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id ?? String.Empty) + ";", token).ConfigureAwait(false);
        }

        public Task<EnumerationResult<UpdateSigningKey>> EnumerateSigningKeysAsync(EnumerationRequest request, UpdateSigningKeyStatusEnum? status, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (status != null) conditions.Add("s.status = " + D.Enum(status.Value));
            return EnumerateAsync(request, Prefixed("s", _KeyColumns), "updatesigningkeys s", conditions, MapSigningKey, token);
        }

        #endregion

        #region Installs-and-Events

        public async Task UpsertInstallAsync(UpdateInstall install, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(install);
            RequireText(install.InstallIdHash, nameof(install.InstallIdHash));
            await QueryAsync("INSERT INTO updateinstalls (propertyid, " + _InstallColumns + ") VALUES (" + Pid + ", " + D.Str(install.Id) + ", " + D.Str(install.InstallIdHash) + ", " +
                D.Str(install.Platform) + ", " + D.Str(install.Channel) + ", " + D.Str(install.RuntimeVersion) + ", " + D.Str(install.CurrentUpdateUuid) + ", " + D.Str(install.EmbeddedUpdateUuid) + ", " +
                D.Str(install.KeyId) + ", " + D.Str(install.AppVersion) + ", " + D.Ts(install.FirstSeenUtc) + ", " + D.Ts(install.LastCheckUtc) + ") ON CONFLICT (propertyid, installidhash) DO UPDATE SET " +
                "platform = EXCLUDED.platform, channel = EXCLUDED.channel, runtimeversion = EXCLUDED.runtimeversion, currentupdateuuid = EXCLUDED.currentupdateuuid, embeddedupdateuuid = EXCLUDED.embeddedupdateuuid, " +
                "keyid = EXCLUDED.keyid, appversion = COALESCE(EXCLUDED.appversion, updateinstalls.appversion), lastcheckutc = EXCLUDED.lastcheckutc;", token).ConfigureAwait(false);
        }

        public async Task AddInstallFailuresAsync(string installIdHash, List<string> updateUuids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(updateUuids);
            if (String.IsNullOrEmpty(installIdHash) || updateUuids.Count == 0) return;
            await QueryAsync("INSERT INTO updateinstallfailures (propertyid, installidhash, updateuuid, createdutc) VALUES " + String.Join(", ", updateUuids.Distinct(StringComparer.Ordinal).Take(50)
                .Select(u => "(" + Pid + ", " + D.Str(installIdHash) + ", " + D.Str(u) + ", " + D.Ts(nowUtc) + ")")) + " ON CONFLICT (propertyid, installidhash, updateuuid) DO NOTHING;", token).ConfigureAwait(false);
        }

        public async Task<HashSet<string>> ListInstallFailuresAsync(string installIdHash, CancellationToken token = default)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (String.IsNullOrEmpty(installIdHash)) return result;
            DataTable table = await QueryAsync("SELECT updateuuid FROM updateinstallfailures WHERE " + Scope() + " AND installidhash = " + D.Str(installIdHash) + " ORDER BY createdutc DESC LIMIT 200;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "updateuuid"));
            return result;
        }

        public Task<long> CountInstallsAsync(string? platform, string? channel, string? currentUpdateUuid, DateTime activeSinceUtc, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { Scope(), "lastcheckutc >= " + D.Ts(activeSinceUtc) };
            if (!String.IsNullOrEmpty(platform)) conditions.Add("platform = " + D.Str(platform));
            if (!String.IsNullOrEmpty(channel)) conditions.Add("channel = " + D.Str(channel));
            if (!String.IsNullOrEmpty(currentUpdateUuid)) conditions.Add("currentupdateuuid = " + D.Str(currentUpdateUuid));
            return CountAsync("SELECT COUNT(*) AS cnt FROM updateinstalls" + Where(conditions) + ";", token);
        }

        public Task<EnumerationResult<UpdateRuntimeVersionRow>> EnumerateRuntimeVersionsAsync(EnumerationRequest request, string? platform, string? channel, DateTime activeSinceUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> inner = new List<string> { Scope() };
            if (!String.IsNullOrEmpty(platform)) inner.Add("platform = " + D.Str(platform));
            if (!String.IsNullOrEmpty(channel)) inner.Add("channel = " + D.Str(channel));
            string from = "(SELECT propertyid, platform, runtimeversion, platform || ':' || runtimeversion AS enumkey, MIN(firstseenutc) AS firstseenutc, MAX(lastcheckutc) AS lastseenutc, " +
                "COUNT(*) FILTER (WHERE lastcheckutc >= " + D.Ts(activeSinceUtc) + ") AS active FROM updateinstalls" + Where(inner) + " GROUP BY propertyid, platform, runtimeversion) rv";
            return EnumerateAsync(request, "rv.platform, rv.runtimeversion, rv.firstseenutc, rv.lastseenutc, rv.active", from, new List<string>(), row => new UpdateRuntimeVersionRow
            {
                Platform = RowReader.GetString(row, "platform"),
                RuntimeVersion = RowReader.GetString(row, "runtimeversion"),
                FirstSeenUtc = RowReader.GetDateTime(row, "firstseenutc"),
                LastSeenUtc = RowReader.GetDateTime(row, "lastseenutc"),
                ActiveInstalls30d = RowReader.GetLong(row, "active")
            }, token);
        }

        public Task<EnumerationResult<UpdateCountRow>> EnumerateInstallCountsAsync(EnumerationRequest request, string column, IReadOnlyCollection<string>? updateIds, DateTime activeSinceUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (column != "keyid" && column != "appversion") throw new ArgumentException("Unsupported install grouping.", nameof(column));
            List<string> inner = new List<string> { Scope(), "lastcheckutc >= " + D.Ts(activeSinceUtc) };
            if (updateIds != null) inner.Add(updateIds.Count == 0 ? "1 = 0" : "currentupdateuuid IN " + D.InList(updateIds));
            string key = "COALESCE(" + column + ", '')";
            string from = "(SELECT propertyid, " + key + " AS k, 'key:' || " + key + " AS enumkey, MIN(firstseenutc) AS firstseenutc, COUNT(*) AS cnt FROM updateinstalls" + Where(inner) + " GROUP BY propertyid, " + key + ") ic";
            List<string> conditions = new List<string>();
            if (request.Query.Labels.Count > 0 || request.Query.Tags.Count > 0) conditions.Add("1 = 0");
            return EnumerateAsync(request, "ic.k, ic.cnt", from, conditions, row => new UpdateCountRow { Key = RowReader.GetString(row, "k"), Count = RowReader.GetLong(row, "cnt") }, token);
        }

        public async Task<List<UpdateCountRow>> CountInstallsByAsync(string column, string? platform, string? runtimeVersion, string? currentUpdateUuid, DateTime activeSinceUtc, int limit, CancellationToken token = default)
        {
            if (column != "appversion" && column != "keyid" && column != "channel") throw new ArgumentException("Unsupported grouping column " + column + ".", nameof(column));
            List<string> conditions = new List<string> { Scope(), "lastcheckutc >= " + D.Ts(activeSinceUtc) };
            if (!String.IsNullOrEmpty(platform)) conditions.Add("platform = " + D.Str(platform));
            if (!String.IsNullOrEmpty(runtimeVersion)) conditions.Add("runtimeversion = " + D.Str(runtimeVersion));
            if (!String.IsNullOrEmpty(currentUpdateUuid)) conditions.Add("currentupdateuuid = " + D.Str(currentUpdateUuid));
            DataTable table = await QueryAsync("SELECT COALESCE(" + column + ", '') AS k, COUNT(*) AS cnt FROM updateinstalls" + Where(conditions) + " GROUP BY COALESCE(" + column + ", '') ORDER BY cnt DESC, k LIMIT " +
                D.Num(Math.Clamp(limit, 1, 100)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new UpdateCountRow { Key = RowReader.GetString(row, "k"), Count = RowReader.GetLong(row, "cnt") }).ToList();
        }

        public Task<long> CountDistinctInstallValuesAsync(string column, string? platform, string? runtimeVersion, string? currentUpdateUuid, DateTime activeSinceUtc, CancellationToken token = default)
        {
            if (column != "appversion" && column != "keyid" && column != "channel") throw new ArgumentException("Unsupported grouping column " + column + ".", nameof(column));
            List<string> conditions = new List<string> { Scope(), "lastcheckutc >= " + D.Ts(activeSinceUtc) };
            if (!String.IsNullOrEmpty(platform)) conditions.Add("platform = " + D.Str(platform));
            if (!String.IsNullOrEmpty(runtimeVersion)) conditions.Add("runtimeversion = " + D.Str(runtimeVersion));
            if (!String.IsNullOrEmpty(currentUpdateUuid)) conditions.Add("currentupdateuuid = " + D.Str(currentUpdateUuid));
            return CountAsync("SELECT COUNT(DISTINCT COALESCE(" + column + ", '')) AS cnt FROM updateinstalls" + Where(conditions) + ";", token);
        }

        public async Task InsertEventsAsync(List<UpdateEvent> events, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(events);
            if (events.Count == 0) return;
            List<string> statements = new List<string>
            {
                "INSERT INTO updateevents (propertyid, id, installidhash, updateuuid, type, errorcode, launchdurationms, occurredutc, receivedutc) VALUES " + String.Join(", ", events.Select(e => "(" + Pid + ", " +
                    D.Str(e.Id) + ", " + D.Str(e.InstallIdHash) + ", " + D.Str(e.UpdateUuid) + ", " + D.Enum(e.Type) + ", " + D.Enum(e.ErrorCode) + ", " + D.Num(e.LaunchDurationMs) + ", " +
                    D.Ts(e.OccurredUtc) + ", " + D.Ts(e.ReceivedUtc) + ")")) + " ON CONFLICT (id) DO NOTHING;"
            };
            foreach (IGrouping<string, UpdateEvent> group in events.Where(e => !String.IsNullOrEmpty(e.UpdateUuid)).GroupBy(e => e.UpdateUuid + "|" + e.OccurredUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "|" + e.Type))
            {
                UpdateEvent first = group.First();
                statements.Add(IncrementSql(first.UpdateUuid!, first.OccurredUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), first.Type.ToString(), group.Count()));
            }
            List<UpdateEvent> detailed = events.Where(e => !String.IsNullOrEmpty(e.Message) || !String.IsNullOrEmpty(e.StackTrace)).ToList();
            if (detailed.Count > 0)
            {
                statements.Add("INSERT INTO updateeventdetails (propertyid, eventid, updateuuid, type, errorcode, message, stacktrace, occurredutc, receivedutc) VALUES " + String.Join(", ", detailed.Select(e => "(" + Pid + ", " +
                    D.Str(e.Id) + ", " + D.Str(e.UpdateUuid) + ", " + D.Enum(e.Type) + ", " + D.Enum(e.ErrorCode) + ", " + D.Str(e.Message) + ", " + D.Str(e.StackTrace) + ", " + D.Ts(e.OccurredUtc) + ", " +
                    D.Ts(e.ReceivedUtc) + ")")) + " ON CONFLICT (eventid) DO NOTHING;");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task IncrementCountAsync(string updateUuid, string day, string type, CancellationToken token = default)
        {
            RequireText(updateUuid, nameof(updateUuid));
            await QueryAsync(IncrementSql(updateUuid, day, type, 1), token).ConfigureAwait(false);
        }

        public async Task<List<UpdateCountRow>> EventCountsAsync(List<string> updateUuids, string sinceDay, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(updateUuids);
            if (updateUuids.Count == 0) return new List<UpdateCountRow>();
            DataTable table = await QueryAsync("SELECT day, type, SUM(count) AS cnt FROM updateeventcounts WHERE " + Scope() + " AND updateuuid IN " + D.InList(updateUuids) + " AND day >= " + D.Str(sinceDay) +
                " GROUP BY day, type ORDER BY day LIMIT 2000;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new UpdateCountRow { Key = RowReader.GetString(row, "day"), Type = RowReader.GetString(row, "type"), Count = RowReader.GetLong(row, "cnt") }).ToList();
        }

        public async Task<int> PruneInstallsAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM updateinstalls WHERE " + Scope() + " AND lastcheckutc < " + D.Ts(cutoffUtc) + " RETURNING id;", token).ConfigureAwait(false);
            await QueryAsync("DELETE FROM updateinstallfailures WHERE " + Scope() + " AND createdutc < " + D.Ts(cutoffUtc) + ";", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> PruneEventsAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM updateevents WHERE " + Scope() + " AND receivedutc < " + D.Ts(cutoffUtc) + " RETURNING id;", token).ConfigureAwait(false);
            await QueryAsync("DELETE FROM updateeventdetails WHERE " + Scope() + " AND receivedutc < " + D.Ts(cutoffUtc) + ";", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<EnumerationResult<UpdateEventDetail>> EnumerateEventDetailsAsync(EnumerationRequest request, string updateUuid, UpdateEventTypeEnum? type, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "d.updateuuid = " + D.Str(updateUuid ?? String.Empty) };
            if (type != null) conditions.Add("d.type = " + D.Enum(type.Value));
            return await EnumerateAsync(request, "d.eventid, d.updateuuid, d.type, d.errorcode, d.message, d.stacktrace, d.occurredutc, d.receivedutc", "updateeventdetails d", conditions, MapDetail, token).ConfigureAwait(false);
        }

        public async Task<UpdateEventDetail?> ReadEventDetailAsync(string eventId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(eventId)) return null;
            DataTable table = await QueryAsync("SELECT eventid, updateuuid, type, errorcode, message, stacktrace, occurredutc, receivedutc FROM updateeventdetails WHERE " + Scope() + " AND eventid = " + D.Str(eventId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapDetail(table.Rows[0]);
        }

        public async Task<List<UpdateSourceMap>> ListSourceMapsAsync(string updateId, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT updateid, position, sha256hex, mapkey, bundlekey, sizebytes, storagekey FROM updatesourcemaps WHERE " + Scope() + " AND updateid = " + D.Str(updateId ?? String.Empty) +
                " ORDER BY position LIMIT " + D.Num(Math.Clamp(limit, 1, 500)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new UpdateSourceMap
            {
                UpdateId = RowReader.GetString(r, "updateid"),
                Position = RowReader.GetInt(r, "position"),
                Sha256Hex = RowReader.GetString(r, "sha256hex"),
                Key = RowReader.GetString(r, "mapkey"),
                BundleKey = RowReader.GetString(r, "bundlekey"),
                SizeBytes = RowReader.GetLong(r, "sizebytes"),
                StorageKey = RowReader.GetString(r, "storagekey")
            }).ToList();
        }

        public async Task<UpdateInstall?> ReadInstallAsync(string installIdHash, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(installIdHash)) return null;
            DataTable table = await QueryAsync("SELECT " + _InstallColumns + " FROM updateinstalls WHERE " + Scope() + " AND installidhash = " + D.Str(installIdHash) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new UpdateInstall
            {
                Id = RowReader.GetString(row, "id"),
                InstallIdHash = RowReader.GetString(row, "installidhash"),
                Platform = RowReader.GetString(row, "platform"),
                Channel = RowReader.GetString(row, "channel"),
                RuntimeVersion = RowReader.GetString(row, "runtimeversion"),
                CurrentUpdateUuid = RowReader.GetNullableString(row, "currentupdateuuid"),
                EmbeddedUpdateUuid = RowReader.GetNullableString(row, "embeddedupdateuuid"),
                KeyId = RowReader.GetNullableString(row, "keyid"),
                AppVersion = RowReader.GetNullableString(row, "appversion"),
                FirstSeenUtc = RowReader.GetDateTime(row, "firstseenutc"),
                LastCheckUtc = RowReader.GetDateTime(row, "lastcheckutc")
            };
        }

        public async Task<List<UpdateAsset>> DeleteUnreferencedAssetsAsync(DateTime cutoffUtc, int limit, CancellationToken token = default)
        {
            string candidates = "(SELECT x.sha256hex FROM updateassets x WHERE " + Scope("x") + " AND x.lastreferencedutc < " + D.Ts(cutoffUtc) +
                " AND NOT EXISTS (SELECT 1 FROM updateassetrefs r WHERE " + Scope("r") + " AND r.sha256hex = x.sha256hex) ORDER BY x.lastreferencedutc, x.sha256hex LIMIT " + D.Num(Math.Clamp(limit, 1, 10000)) + ")";
            DataTable table = await QueryAsync("DELETE FROM updateassets a WHERE " + Scope("a") + " AND a.sha256hex IN " + candidates +
                " AND NOT EXISTS (SELECT 1 FROM updateassetrefs q WHERE " + Scope("q") + " AND q.sha256hex = a.sha256hex) RETURNING a.sha256hex, a.hashb64url, a.contenttype, a.sizebytes, a.storedutc, a.lastreferencedutc;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new UpdateAsset
            {
                Sha256Hex = RowReader.GetString(row, "sha256hex"),
                HashB64Url = RowReader.GetString(row, "hashb64url"),
                ContentType = RowReader.GetString(row, "contenttype"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                StoredUtc = RowReader.GetNullableDateTime(row, "storedutc"),
                LastReferencedUtc = RowReader.GetDateTime(row, "lastreferencedutc")
            }).ToList();
        }

        public async Task QueueGcObjectAsync(UpdateGcObject item, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            RequireText(item.Sha256Hex, nameof(item.Sha256Hex));
            RequireText(item.StorageKey, nameof(item.StorageKey));
            GcReference(item.Kind, "g");
            await QueryAsync("INSERT INTO updategcobjects (propertyid, kind, sha256hex, storagekey, sizebytes, queuedutc) VALUES (" + Pid + ", " + D.Str(item.Kind) + ", " + D.Str(item.Sha256Hex) + ", " +
                D.Str(item.StorageKey) + ", " + D.Num(item.SizeBytes) + ", " + D.Ts(nowUtc) + ") ON CONFLICT (propertyid, kind, sha256hex) DO UPDATE SET queuedutc = EXCLUDED.queuedutc, sizebytes = EXCLUDED.sizebytes;", token).ConfigureAwait(false);
        }

        public async Task<List<UpdateGcObject>> DeleteUnreferencedGcObjectsAsync(string kind, DateTime cutoffUtc, DateTime nowUtc, int limit, CancellationToken token = default)
        {
            string referencedG = GcReference(kind, "g");
            string referencedA = GcReference(kind, "a");
            string bound = D.Num(Math.Clamp(limit, 1, 10000));
            string due = "FROM updategcobjects g WHERE " + Scope("g") + " AND g.kind = " + D.Str(kind) + " AND g.queuedutc < " + D.Ts(cutoffUtc);
            List<string> statements = new List<string>
            {
                "UPDATE updategcobjects SET queuedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND kind = " + D.Str(kind) + " AND sha256hex IN (SELECT g.sha256hex " + due + " AND " + referencedG +
                    " ORDER BY g.queuedutc, g.sha256hex LIMIT " + bound + ");",
                "DELETE FROM updategcobjects a WHERE " + Scope("a") + " AND a.kind = " + D.Str(kind) + " AND a.sha256hex IN (SELECT g.sha256hex " + due + " AND NOT " + referencedG +
                    " ORDER BY g.queuedutc, g.sha256hex LIMIT " + bound + ") AND NOT " + referencedA + " RETURNING a.kind, a.sha256hex, a.storagekey, a.sizebytes, a.queuedutc;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new UpdateGcObject
            {
                Kind = RowReader.GetString(row, "kind"),
                Sha256Hex = RowReader.GetString(row, "sha256hex"),
                StorageKey = RowReader.GetString(row, "storagekey"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                QueuedUtc = RowReader.GetDateTime(row, "queuedutc")
            }).ToList();
        }

        private string GcReference(string kind, string alias)
        {
            if (String.Equals(kind, UpdateGcObject.SourceMapKind, StringComparison.Ordinal))
                return "EXISTS (SELECT 1 FROM updatesourcemaps rm WHERE " + Scope("rm") + " AND rm.sha256hex = " + alias + ".sha256hex)";
            if (String.Equals(kind, UpdateGcObject.ContentAssetKind, StringComparison.Ordinal))
                return "EXISTS (SELECT 1 FROM remoteassets ra WHERE " + Scope("ra") + " AND (ra.sha256hex = " + alias + ".sha256hex OR ra.darksha256hex = " + alias + ".sha256hex))";
            throw new ArgumentException("Unknown garbage collection kind " + kind + ".", nameof(kind));
        }

        private static UpdateEventDetail MapDetail(DataRow row)
        {
            string? code = RowReader.GetNullableString(row, "errorcode");
            return new UpdateEventDetail
            {
                EventId = RowReader.GetString(row, "eventid"),
                UpdateUuid = RowReader.GetNullableString(row, "updateuuid"),
                Type = RowReader.GetEnum(row, "type", UpdateEventTypeEnum.LaunchFailed),
                ErrorCode = code != null && Enum.TryParse(code, false, out UpdateErrorCodeEnum parsed) ? parsed : null,
                Message = RowReader.GetNullableString(row, "message"),
                StackTrace = RowReader.GetNullableString(row, "stacktrace"),
                OccurredUtc = RowReader.GetDateTime(row, "occurredutc"),
                ReceivedUtc = RowReader.GetDateTime(row, "receivedutc")
            };
        }

        #endregion

        #region Private-Methods

        private string IncrementSql(string uuid, string day, string type, long amount)
        {
            return "INSERT INTO updateeventcounts (propertyid, updateuuid, day, type, count) VALUES (" + Pid + ", " + D.Str(uuid) + ", " + D.Str(day) + ", " + D.Str(type) + ", " + D.Num(amount) +
                ") ON CONFLICT (propertyid, updateuuid, day, type) DO UPDATE SET count = updateeventcounts.count + EXCLUDED.count;";
        }

        private string InsertUpdate(UpdateRecord u)
        {
            return "INSERT INTO updates (propertyid, " + _UpdateColumns + ") VALUES (" + Pid + ", " + D.Str(u.Id) + ", " + D.Str(u.GroupId) + ", " + D.Str(u.Uuid) + ", " + D.Str(u.Platform) + ", " +
                D.Str(u.RuntimeVersion) + ", " + D.Str(u.Channel) + ", " + D.Enum(u.Kind) + ", " + D.Enum(u.Status) + ", " + D.Num(u.RolloutBasisPoints) + ", " + D.Ts(u.CreatedAt) + ", " +
                D.Str(u.LaunchAssetSha256) + ", " + D.Num(u.LaunchAssetSizeBytes) + ", " + D.Num(u.AssetCount) + ", " + D.Num(u.TotalBytes) + ", " + D.Ts(u.CreatedUtc) + ", " + D.Ts(u.UpdatedUtc) + ");";
        }

        private async Task<UpdateDeployKey?> ReadDeployKeyWhereAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT k.id, k.name, k.secrethash, k.expiresutc, k.lastusedutc, k.revokedutc, k.createdbyuserid, k.createdutc FROM updatedeploykeys k WHERE " + Scope("k") +
                " AND " + condition + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            List<UpdateDeployKey> keys = new List<UpdateDeployKey> { MapDeployKey(table.Rows[0]) };
            await AttachChannelsAsync(keys, token).ConfigureAwait(false);
            return keys[0];
        }

        private async Task AttachChannelsAsync(List<UpdateDeployKey> keys, CancellationToken token)
        {
            if (keys.Count == 0) return;
            DataTable table = await QueryAsync("SELECT deploykeyid, channel FROM updatedeploykeychannels WHERE " + Scope() + " AND deploykeyid IN " + D.InList(keys.Select(k => k.Id)) + " ORDER BY channel;", token).ConfigureAwait(false);
            Dictionary<string, UpdateDeployKey> byId = keys.ToDictionary(k => k.Id, StringComparer.Ordinal);
            foreach (DataRow row in table.Rows)
            {
                if (byId.TryGetValue(RowReader.GetString(row, "deploykeyid"), out UpdateDeployKey? key)) key.Channels.Add(RowReader.GetString(row, "channel"));
            }
        }

        private static string Prefixed(string alias, string columns)
        {
            return String.Join(", ", columns.Split(',').Select(c => alias + "." + c.Trim()));
        }

        private static UpdateGroup MapGroup(DataRow row)
        {
            return new UpdateGroup
            {
                Id = RowReader.GetString(row, "id"),
                Kind = RowReader.GetEnum(row, "kind", UpdateGroupKindEnum.Publish),
                Channel = RowReader.GetString(row, "channel"),
                Message = RowReader.GetString(row, "message"),
                Status = RowReader.GetEnum(row, "status", UpdateGroupStatusEnum.Draft),
                RolloutBasisPoints = RowReader.GetInt(row, "rolloutbasispoints"),
                Critical = RowReader.GetBool(row, "critical"),
                CriticalMessage = RowReader.GetNullableString(row, "criticalmessage"),
                GitCommit = RowReader.GetNullableString(row, "gitcommit"),
                GitDirty = RowReader.GetBool(row, "gitdirty"),
                RepublishOf = RowReader.GetNullableString(row, "republishof"),
                PromotedFrom = RowReader.GetNullableString(row, "promotedfrom"),
                ClientGroupId = RowReader.GetNullableString(row, "clientgroupid"),
                CreatedByType = RowReader.GetEnum(row, "createdbytype", UpdateCreatorTypeEnum.Member),
                CreatedById = RowReader.GetNullableString(row, "createdbyid"),
                CreatedByName = RowReader.GetString(row, "createdbyname"),
                AutoHaltEnabled = RowReader.GetBool(row, "autohaltenabled"),
                AutoHaltFailurePercent = RowReader.GetInt(row, "autohaltpercent"),
                AutoHaltMinInstalls = RowReader.GetInt(row, "autohaltmininstalls"),
                HaltedUtc = RowReader.GetNullableDateTime(row, "haltedutc"),
                HaltReason = RowReader.GetNullableString(row, "haltreason"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc"),
                PausedUtc = RowReader.GetNullableDateTime(row, "pausedutc"),
                RolledBackUtc = RowReader.GetNullableDateTime(row, "rolledbackutc"),
                SupersededUtc = RowReader.GetNullableDateTime(row, "supersededutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static UpdateRecord MapUpdate(DataRow row)
        {
            return new UpdateRecord
            {
                Id = RowReader.GetString(row, "id"),
                GroupId = RowReader.GetString(row, "groupid"),
                Uuid = RowReader.GetString(row, "uuid"),
                Platform = RowReader.GetString(row, "platform"),
                RuntimeVersion = RowReader.GetString(row, "runtimeversion"),
                Channel = RowReader.GetString(row, "channel"),
                Kind = RowReader.GetEnum(row, "kind", UpdateGroupKindEnum.Publish),
                Status = RowReader.GetEnum(row, "status", UpdateGroupStatusEnum.Draft),
                RolloutBasisPoints = RowReader.GetInt(row, "rolloutbasispoints"),
                CreatedAt = RowReader.GetDateTime(row, "createdat"),
                LaunchAssetSha256 = RowReader.GetNullableString(row, "launchassethash"),
                LaunchAssetSizeBytes = RowReader.GetLong(row, "launchassetsizebytes"),
                AssetCount = RowReader.GetInt(row, "assetcount"),
                TotalBytes = RowReader.GetLong(row, "totalbytes"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static UpdateAssetRef MapRef(DataRow row)
        {
            return new UpdateAssetRef
            {
                UpdateId = RowReader.GetString(row, "updateid"),
                Position = RowReader.GetInt(row, "position"),
                Sha256Hex = RowReader.GetString(row, "sha256hex"),
                Key = RowReader.GetString(row, "assetkey"),
                FileExtension = RowReader.GetString(row, "fileextension"),
                ContentType = RowReader.GetString(row, "contenttype"),
                IsLaunchAsset = RowReader.GetBool(row, "islaunchasset"),
                HashB64Url = RowReader.GetString(row, "hashb64url"),
                SizeBytes = RowReader.GetLong(row, "sizebytes")
            };
        }

        private static UpdateChannel MapChannel(DataRow row)
        {
            return new UpdateChannel { Name = RowReader.GetString(row, "name"), BuiltIn = RowReader.GetBool(row, "builtin"), CreatedUtc = RowReader.GetDateTime(row, "createdutc") };
        }

        private static UpdateDeployKey MapDeployKey(DataRow row)
        {
            return new UpdateDeployKey
            {
                Id = RowReader.GetString(row, "id"),
                Name = RowReader.GetString(row, "name"),
                SecretHash = RowReader.GetString(row, "secrethash"),
                ExpiresUtc = RowReader.GetNullableDateTime(row, "expiresutc"),
                LastUsedUtc = RowReader.GetNullableDateTime(row, "lastusedutc"),
                RevokedUtc = RowReader.GetNullableDateTime(row, "revokedutc"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        private static UpdateSigningKey MapSigningKey(DataRow row)
        {
            return new UpdateSigningKey
            {
                Id = RowReader.GetString(row, "id"),
                KeyId = RowReader.GetString(row, "keyid"),
                Algorithm = RowReader.GetString(row, "algorithm"),
                KeyBits = RowReader.GetInt(row, "keybits"),
                Source = RowReader.GetEnum(row, "source", UpdateSigningKeySourceEnum.Environment),
                CertificatePem = RowReader.GetString(row, "certificatepem"),
                CertificateSha256 = RowReader.GetString(row, "certificatesha256"),
                CertificateNotAfterUtc = RowReader.GetNullableDateTime(row, "certificatenotafterutc"),
                IsCertificateAuthority = RowReader.GetBool(row, "isca"),
                PrivateKeyCiphertext = RowReader.GetNullableString(row, "privatekeyciphertext"),
                PrivateKeyNonce = RowReader.GetNullableString(row, "privatekeynonce"),
                PrivateKeyKeyId = RowReader.GetNullableString(row, "privatekeykeyid"),
                Status = RowReader.GetEnum(row, "status", UpdateSigningKeyStatusEnum.Standby),
                LastUsedUtc = RowReader.GetNullableDateTime(row, "lastusedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                RetiredUtc = RowReader.GetNullableDateTime(row, "retiredutc")
            };
        }

        #endregion
    }
}
