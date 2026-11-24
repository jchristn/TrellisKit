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
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared remote string, banner, remote asset, and string catalog methods (docs/LIVE_UPDATES_CONTRACT.md sections 14
    /// and 16.3). Every content table holds immutable revision rows with one pending draft per object; a content publish
    /// (<see cref="PublishStatements"/>, run inside the flag publish transaction) closes the current revisions of drafted
    /// objects and opens the drafts. Banner targeting rules are featureflagrules rows with ownertype Banner.
    /// </summary>
    internal abstract class AppContentMethodsBase : MethodsBase, IAppContentMethods
    {
        private const string _StringColumns = "revisionid, objectid, stringkey, value, notes, state, validfromversion, validtoversion, isdraft, editedbyuserid, createdutc, updatedutc";

        private const string _BannerColumns = "revisionid, objectid, title, body, style, placement, dismissible, priority, startsutc, endsutc, linklabel, linkpath, linkurl, imageassetobjectid, state, " +
            "validfromversion, validtoversion, isdraft, editedbyuserid, createdutc, updatedutc";

        private const string _AssetColumns = "revisionid, objectid, assetkey, contenttype, sizebytes, width, height, sha256hex, darksha256hex, darkwidth, darkheight, darksizebytes, alttext, state, " +
            "validfromversion, validtoversion, isdraft, editedbyuserid, createdutc, updatedutc";

        private const string _CatalogColumns = "stringkey, client, defaultvalue, tokens, maxlength";

        private const string _Current = "isdraft = FALSE AND validtoversion IS NULL";

        protected AppContentMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        #region Catalog

        public async Task<bool> SyncCatalogAsync(string sha256, List<StringCatalogEntry> entries, CancellationToken token = default)
        {
            RequireText(sha256, nameof(sha256));
            ArgumentNullException.ThrowIfNull(entries);
            long matching = await CountAsync("SELECT COUNT(*) AS cnt FROM appstringcatalog WHERE " + Scope() + " AND catalogsha = " + D.Str(sha256) + ";", token).ConfigureAwait(false);
            long total = await CountAsync("SELECT COUNT(*) AS cnt FROM appstringcatalog WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (matching == entries.Count && total == entries.Count) return false;
            List<string> statements = new List<string> { "DELETE FROM appstringcatalog WHERE " + Scope() + ";" };
            foreach (StringCatalogEntry[] chunk in entries.Chunk(400))
            {
                statements.Add("INSERT INTO appstringcatalog (propertyid, " + _CatalogColumns + ", catalogsha) VALUES " + String.Join(", ", chunk.Select(e => "(" + Pid + ", " + D.Str(e.Key) + ", " +
                    D.Str(e.Client) + ", " + D.Str(e.DefaultValue) + ", " + D.Str(String.Join(",", e.Tokens)) + ", " + D.Num(e.MaxLength) + ", " + D.Str(sha256) + ")")) +
                    " ON CONFLICT (propertyid, stringkey) DO UPDATE SET client = EXCLUDED.client, defaultvalue = EXCLUDED.defaultvalue, tokens = EXCLUDED.tokens, maxlength = EXCLUDED.maxlength, catalogsha = EXCLUDED.catalogsha;");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return true;
        }

        public async Task<StringCatalogEntry?> ReadCatalogEntryAsync(string key, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _CatalogColumns + " FROM appstringcatalog WHERE " + Scope() + " AND stringkey = " + D.Str(key ?? String.Empty) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapCatalog(table.Rows[0]);
        }

        public async Task<EnumerationResult<StringCatalogEntry>> EnumerateCatalogAsync(EnumerationRequest request, RemoteStringFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(filter.Client)) conditions.Add("c.client = " + D.Str(filter.Client));
            string published = "EXISTS (SELECT 1 FROM remotestrings so WHERE " + Scope("so") + " AND so.stringkey = c.stringkey AND so.isdraft = FALSE AND so.validtoversion IS NULL AND so.value IS NOT NULL)";
            string draft = "EXISTS (SELECT 1 FROM remotestrings sd WHERE " + Scope("sd") + " AND sd.stringkey = c.stringkey AND sd.isdraft = TRUE)";
            if (filter.Overridden == true) conditions.Add(published);
            if (filter.Overridden == false) conditions.Add("NOT " + published);
            if (filter.HasDraftChanges == true) conditions.Add(draft);
            if (filter.HasDraftChanges == false) conditions.Add("NOT " + draft);
            return await EnumerateAsync(request, Prefixed("c", _CatalogColumns), "appstringcatalog c", conditions, MapCatalog, token).ConfigureAwait(false);
        }

        #endregion

        #region Strings

        public Task<RemoteStringOverride?> ReadStringDraftAsync(string key, CancellationToken token = default)
        {
            return OneStringAsync("stringkey = " + D.Str(key ?? String.Empty) + " AND isdraft = TRUE", token);
        }

        public Task<RemoteStringOverride?> ReadStringCurrentAsync(string key, CancellationToken token = default)
        {
            return OneStringAsync("stringkey = " + D.Str(key ?? String.Empty) + " AND " + _Current, token);
        }

        public async Task<List<RemoteStringOverride>> ListStringHeadsAsync(IEnumerable<string> keys, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(keys);
            List<string> list = keys.Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<RemoteStringOverride>();
            return await StringsAsync("stringkey IN " + D.InList(list) + " AND (isdraft = TRUE OR validtoversion IS NULL)", "stringkey", 2 * list.Count + 1, token).ConfigureAwait(false);
        }

        public async Task SaveStringDraftAsync(RemoteStringOverride value, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(value);
            RequireText(value.RevisionId, nameof(value.RevisionId));
            RequireText(value.Key, nameof(value.Key));
            List<string> statements = new List<string>
            {
                "DELETE FROM remotestrings WHERE " + Scope() + " AND stringkey = " + D.Str(value.Key) + " AND isdraft = TRUE;",
                "INSERT INTO remotestrings (propertyid, " + _StringColumns + ") VALUES (" + Pid + ", " + D.Str(value.RevisionId) + ", " + D.Str(value.Id) + ", " + D.Str(value.Key) + ", " +
                    D.Str(value.Value) + ", " + D.Str(value.Notes) + ", " + D.Enum(value.State) + ", NULL, NULL, TRUE, " + D.Str(value.EditedByUserId) + ", " + D.Ts(value.CreatedUtc) + ", " + D.Ts(value.UpdatedUtc) + ");"
            };
            await UniqueTransactionAsync(statements, "Remote string", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteStringDraftAsync(string key, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM remotestrings WHERE " + Scope() + " AND stringkey = " + D.Str(key ?? String.Empty) + " AND isdraft = TRUE RETURNING revisionid;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<List<RemoteStringOverride>> ListCurrentStringsAsync(int limit, CancellationToken token = default)
        {
            return StringsAsync(_Current + " AND value IS NOT NULL", "stringkey", limit, token);
        }

        public Task<List<RemoteStringOverride>> ListStringsAtVersionAsync(long version, int limit, CancellationToken token = default)
        {
            return StringsAsync(AtVersion(version) + " AND value IS NOT NULL", "stringkey", limit, token);
        }

        public Task<List<RemoteStringOverride>> ListStringDraftsAsync(int limit, CancellationToken token = default)
        {
            return StringsAsync("isdraft = TRUE", "stringkey", limit, token);
        }

        public Task<List<RemoteStringOverride>> ListStringsByKeysAsync(IEnumerable<string> keys, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(keys);
            List<string> list = keys.Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return Task.FromResult(new List<RemoteStringOverride>());
            return StringsAsync(_Current + " AND value IS NOT NULL AND stringkey IN " + D.InList(list), "stringkey", list.Count, token);
        }

        public Task<long> CountStringOverridesAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(DISTINCT stringkey) AS cnt FROM remotestrings WHERE " + Scope() + " AND (isdraft = TRUE OR validtoversion IS NULL) AND value IS NOT NULL;", token);
        }

        public Task<EnumerationResult<RemoteStringOverride>> EnumerateStringHistoryAsync(EnumerationRequest request, string key, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "s.stringkey = " + D.Str(key ?? String.Empty), "s.isdraft = FALSE" };
            return EnumerateAsync(request, Prefixed("s", _StringColumns), "remotestrings s", conditions, MapString, token);
        }

        public async Task<EnumerationResult<RemoteStringOverride>> EnumeratePublishedStringsAsync(EnumerationRequest request, string client, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "s.isdraft = FALSE", "s.validtoversion IS NULL", "s.value IS NOT NULL",
                "(" + D.StartsWith("s.stringkey", (client ?? "web") + ".") + " OR " + D.StartsWith("s.stringkey", "shared.") + ")" };
            return await EnumerateAsync(request, Prefixed("s", _StringColumns), "remotestrings s", conditions, MapString, token).ConfigureAwait(false);
        }

        #endregion

        #region Banners

        public Task<AppBanner?> ReadBannerDraftAsync(string id, CancellationToken token = default)
        {
            return OneBannerAsync("objectid = " + D.Str(id ?? String.Empty) + " AND isdraft = TRUE", token);
        }

        public Task<AppBanner?> ReadBannerCurrentAsync(string id, CancellationToken token = default)
        {
            return OneBannerAsync("objectid = " + D.Str(id ?? String.Empty) + " AND " + _Current, token);
        }

        public async Task SaveBannerDraftAsync(AppBanner banner, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(banner);
            RequireText(banner.RevisionId, nameof(banner.RevisionId));
            RequireText(banner.Id, nameof(banner.Id));
            string drafts = "(SELECT revisionid FROM banners WHERE " + Scope() + " AND objectid = " + D.Str(banner.Id) + " AND isdraft = TRUE)";
            List<string> statements = DeleteBannerRevisionStatements(drafts);
            statements.Add("INSERT INTO banners (propertyid, " + _BannerColumns + ") VALUES (" + Pid + ", " + D.Str(banner.RevisionId) + ", " + D.Str(banner.Id) + ", " + D.Str(banner.Title) + ", " +
                D.Str(banner.Body) + ", " + D.Str(banner.Style) + ", " + D.Str(banner.Placement) + ", " + D.Bool(banner.Dismissible) + ", " + D.Num(banner.Priority) + ", " + D.Ts(banner.StartsUtc) + ", " +
                D.Ts(banner.EndsUtc) + ", " + D.Str(banner.LinkLabel) + ", " + D.Str(banner.LinkPath) + ", " + D.Str(banner.LinkUrl) + ", " + D.Str(banner.ImageAssetId) + ", " + D.Enum(banner.State) +
                ", NULL, NULL, TRUE, " + D.Str(banner.EditedByUserId) + ", " + D.Ts(banner.CreatedUtc) + ", " + D.Ts(banner.UpdatedUtc) + ");");
            List<string> clients = banner.Clients.Distinct(StringComparer.Ordinal).ToList();
            if (clients.Count > 0)
                statements.Add("INSERT INTO bannerclients (propertyid, revisionid, client) VALUES " + String.Join(", ", clients.Select(c => "(" + Pid + ", " + D.Str(banner.RevisionId) + ", " + D.Str(c) + ")")) + ";");
            statements.AddRange(InsertRuleStatements(banner.RevisionId, banner.Rules, "Banner"));
            await UniqueTransactionAsync(statements, "Banner", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteBannerDraftAsync(string id, bool removeMetadata, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return false;
            long drafts = await CountAsync("SELECT COUNT(*) AS cnt FROM banners WHERE " + Scope() + " AND objectid = " + D.Str(id) + " AND isdraft = TRUE;", token).ConfigureAwait(false);
            if (drafts == 0) return false;
            List<string> statements = DeleteBannerRevisionStatements("(SELECT revisionid FROM banners WHERE " + Scope() + " AND objectid = " + D.Str(id) + " AND isdraft = TRUE)");
            if (removeMetadata) statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.Banner, id));
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> HasPublishedBannerAsync(string id, CancellationToken token = default)
        {
            return await CountAsync("SELECT COUNT(*) AS cnt FROM banners WHERE " + Scope() + " AND objectid = " + D.Str(id ?? String.Empty) + " AND isdraft = FALSE;", token).ConfigureAwait(false) > 0;
        }

        public Task<List<AppBanner>> ListCurrentBannersAsync(int limit, CancellationToken token = default)
        {
            return BannersAsync(_Current, "priority DESC, objectid", limit, token);
        }

        public Task<List<AppBanner>> ListBannersAtVersionAsync(long version, int limit, CancellationToken token = default)
        {
            return BannersAsync(AtVersion(version), "objectid", limit, token);
        }

        public Task<List<AppBanner>> ListBannerDraftsAsync(int limit, CancellationToken token = default)
        {
            return BannersAsync("isdraft = TRUE", "objectid", limit, token);
        }

        public Task<long> CountBannersAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(DISTINCT objectid) AS cnt FROM banners WHERE " + Scope() + " AND (isdraft = TRUE OR validtoversion IS NULL) AND state <> 'Archived';", token);
        }

        public async Task<EnumerationResult<AppBanner>> EnumerateBannersAsync(EnumerationRequest request, BannerFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>
            {
                "(b.isdraft = TRUE OR (b.validtoversion IS NULL AND NOT EXISTS (SELECT 1 FROM banners bd WHERE " + Scope("bd") + " AND bd.objectid = b.objectid AND bd.isdraft = TRUE)))"
            };
            if (filter.Active != null)
            {
                string active = "EXISTS (SELECT 1 FROM banners ba WHERE " + Scope("ba") + " AND ba.objectid = b.objectid AND ba.isdraft = FALSE AND ba.validtoversion IS NULL AND ba.state <> 'Archived' " +
                    "AND (ba.startsutc IS NULL OR ba.startsutc <= " + D.Ts(filter.NowUtc) + ") AND (ba.endsutc IS NULL OR ba.endsutc > " + D.Ts(filter.NowUtc) + "))";
                conditions.Add(filter.Active.Value ? active : "NOT " + active);
            }
            if (!String.IsNullOrEmpty(filter.Placement)) conditions.Add("b.placement = " + D.Str(filter.Placement));
            if (!String.IsNullOrEmpty(filter.Style)) conditions.Add("b.style = " + D.Str(filter.Style));
            if (filter.State != null) conditions.Add("b.state = " + D.Enum(filter.State.Value));
            if (filter.HasDraftChanges != null) conditions.Add("b.isdraft = " + D.Bool(filter.HasDraftChanges.Value));
            EnumerationResult<AppBanner> result = await EnumerateAsync(request, Prefixed("b", _BannerColumns), "banners b", conditions, MapBanner, token).ConfigureAwait(false);
            await LoadBannerChildrenAsync(result.Objects, token).ConfigureAwait(false);
            return result;
        }

        public async Task<EnumerationResult<AppBanner>> EnumerateBannerHistoryAsync(EnumerationRequest request, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "b.objectid = " + D.Str(id ?? String.Empty), "b.isdraft = FALSE" };
            EnumerationResult<AppBanner> result = await EnumerateAsync(request, Prefixed("b", _BannerColumns), "banners b", conditions, MapBanner, token).ConfigureAwait(false);
            await LoadBannerChildrenAsync(result.Objects, token).ConfigureAwait(false);
            return result;
        }

        #endregion

        #region Assets

        public Task<RemoteAsset?> ReadAssetDraftAsync(string id, CancellationToken token = default)
        {
            return OneAssetAsync("objectid = " + D.Str(id ?? String.Empty) + " AND isdraft = TRUE", token);
        }

        public Task<RemoteAsset?> ReadAssetCurrentAsync(string id, CancellationToken token = default)
        {
            return OneAssetAsync("objectid = " + D.Str(id ?? String.Empty) + " AND " + _Current, token);
        }

        public async Task<bool> AssetKeyInUseAsync(string key, string? excludeId, CancellationToken token = default)
        {
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM remoteassets WHERE " + Scope() + " AND lower(assetkey) = lower(" + D.Str(key ?? String.Empty) + ") AND (isdraft = TRUE OR validtoversion IS NULL)" +
                (excludeId == null ? String.Empty : " AND objectid <> " + D.Str(excludeId)) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task SaveAssetDraftAsync(RemoteAsset asset, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(asset);
            RequireText(asset.RevisionId, nameof(asset.RevisionId));
            RequireText(asset.Id, nameof(asset.Id));
            List<string> statements = new List<string>
            {
                "DELETE FROM remoteassets WHERE " + Scope() + " AND objectid = " + D.Str(asset.Id) + " AND isdraft = TRUE;",
                "INSERT INTO remoteassets (propertyid, " + _AssetColumns + ") VALUES (" + Pid + ", " + D.Str(asset.RevisionId) + ", " + D.Str(asset.Id) + ", " + D.Str(asset.Key) + ", " +
                    D.Str(asset.ContentType) + ", " + D.Num(asset.SizeBytes) + ", " + D.Num(asset.Width) + ", " + D.Num(asset.Height) + ", " + D.Str(asset.Sha256Hex) + ", " + D.Str(asset.DarkSha256Hex) + ", " +
                    D.Num(asset.DarkWidth) + ", " + D.Num(asset.DarkHeight) + ", " + (asset.DarkSizeBytes == null ? "NULL" : D.Num(asset.DarkSizeBytes.Value)) + ", " + D.Str(asset.AltText) + ", " + D.Enum(asset.State) +
                    ", NULL, NULL, TRUE, " + D.Str(asset.EditedByUserId) + ", " + D.Ts(asset.CreatedUtc) + ", " + D.Ts(asset.UpdatedUtc) + ");"
            };
            await UniqueTransactionAsync(statements, "Remote asset", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteAssetDraftAsync(string id, bool removeMetadata, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return false;
            List<string> statements = removeMetadata ? Driver.Metadata.DeleteStatements(TargetTypeEnum.RemoteAsset, id) : new List<string>();
            statements.Add("DELETE FROM remoteassets WHERE " + Scope() + " AND objectid = " + D.Str(id) + " AND isdraft = TRUE RETURNING revisionid;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> HasPublishedAssetAsync(string id, CancellationToken token = default)
        {
            return await CountAsync("SELECT COUNT(*) AS cnt FROM remoteassets WHERE " + Scope() + " AND objectid = " + D.Str(id ?? String.Empty) + " AND isdraft = FALSE;", token).ConfigureAwait(false) > 0;
        }

        public Task<List<RemoteAsset>> ListCurrentAssetsAsync(int limit, CancellationToken token = default)
        {
            return AssetsAsync(_Current, "lower(assetkey)", limit, token);
        }

        public Task<List<RemoteAsset>> ListAssetsAtVersionAsync(long version, int limit, CancellationToken token = default)
        {
            return AssetsAsync(AtVersion(version), "objectid", limit, token);
        }

        public Task<List<RemoteAsset>> ListAssetDraftsAsync(int limit, CancellationToken token = default)
        {
            return AssetsAsync("isdraft = TRUE", "objectid", limit, token);
        }

        public Task<long> CountAssetsAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(DISTINCT objectid) AS cnt FROM remoteassets WHERE " + Scope() + " AND (isdraft = TRUE OR validtoversion IS NULL) AND state <> 'Archived';", token);
        }

        public async Task<bool> IsContentHashReferencedAsync(string sha256Hex, CancellationToken token = default)
        {
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM remoteassets WHERE " + Scope() + " AND (sha256hex = " + D.Str(sha256Hex ?? String.Empty) + " OR darksha256hex = " + D.Str(sha256Hex ?? String.Empty) + ");", token).ConfigureAwait(false);
            return count > 0;
        }

        public Task<EnumerationResult<RemoteAsset>> EnumerateAssetsAsync(EnumerationRequest request, RemoteAssetFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>
            {
                "(a.isdraft = TRUE OR (a.validtoversion IS NULL AND NOT EXISTS (SELECT 1 FROM remoteassets ad WHERE " + Scope("ad") + " AND ad.objectid = a.objectid AND ad.isdraft = TRUE)))"
            };
            if (filter.State != null) conditions.Add("a.state = " + D.Enum(filter.State.Value));
            if (filter.HasDraftChanges != null) conditions.Add("a.isdraft = " + D.Bool(filter.HasDraftChanges.Value));
            return EnumerateAsync(request, Prefixed("a", _AssetColumns), "remoteassets a", conditions, MapAsset, token);
        }

        public Task<EnumerationResult<RemoteAsset>> EnumerateAssetHistoryAsync(EnumerationRequest request, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "a.objectid = " + D.Str(id ?? String.Empty), "a.isdraft = FALSE" };
            return EnumerateAsync(request, Prefixed("a", _AssetColumns), "remoteassets a", conditions, MapAsset, token);
        }

        public Task<EnumerationResult<RemoteAsset>> EnumeratePublishedAssetsAsync(EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "a.isdraft = FALSE", "a.validtoversion IS NULL", "a.state <> 'Archived'", "a.sha256hex IS NOT NULL" };
            return EnumerateAsync(request, Prefixed("a", _AssetColumns), "remoteassets a", conditions, MapAsset, token);
        }

        #endregion

        #region Publishing

        public async Task<ContentDraftCounts> CountDraftsAsync(CancellationToken token = default)
        {
            return new ContentDraftCounts
            {
                Strings = await CountAsync("SELECT COUNT(*) AS cnt FROM remotestrings WHERE " + Scope() + " AND isdraft = TRUE;", token).ConfigureAwait(false),
                Banners = await CountAsync("SELECT COUNT(*) AS cnt FROM banners WHERE " + Scope() + " AND isdraft = TRUE;", token).ConfigureAwait(false),
                Assets = await CountAsync("SELECT COUNT(*) AS cnt FROM remoteassets WHERE " + Scope() + " AND isdraft = TRUE;", token).ConfigureAwait(false)
            };
        }

        public async Task<List<ContentChange>> ListChangesSinceAsync(long since, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT objecttype, objectid, MAX(version) AS version FROM appcontentchanges WHERE " + Scope() + " AND version > " + D.Num(since) +
                " AND objecttype IN ('RemoteString', 'Banner', 'RemoteAsset') GROUP BY objecttype, objectid ORDER BY MAX(version), objecttype, objectid LIMIT " + D.Num(Math.Clamp(limit, 1, 100000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new ContentChange
            {
                Version = RowReader.GetLong(r, "version"),
                ObjectType = RowReader.GetString(r, "objecttype"),
                ObjectId = RowReader.GetString(r, "objectid")
            }).ToList();
        }

        public async Task<long?> OldestChangeVersionAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT MIN(version) AS version FROM appcontentchanges WHERE " + Scope() + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetNullableLong(table.Rows[0], "version");
        }

        /// <summary>
        /// Statements that publish every string, banner, and asset draft as content version <paramref name="version"/>
        /// (change log rows, close current revisions, open drafts). Run inside the flag publish transaction.
        /// </summary>
        internal List<string> PublishStatements(long version)
        {
            string n = D.Num(version);
            List<string> statements = new List<string>();
            statements.AddRange(TablePublish("remotestrings", "stringkey", "RemoteString", "value IS NULL", n));
            statements.AddRange(TablePublish("banners", "objectid", "Banner", "state = 'Archived'", n));
            statements.AddRange(TablePublish("remoteassets", "objectid", "RemoteAsset", "state = 'Archived'", n));
            return statements;
        }

        #endregion

        #region Private-Methods

        private List<string> TablePublish(string table, string identity, string objectType, string deleted, string n)
        {
            return new List<string>
            {
                "INSERT INTO appcontentchanges (propertyid, version, objecttype, objectid, change) SELECT propertyid, " + n + ", '" + objectType + "', " + identity + ", CASE WHEN " + deleted +
                    " THEN 'Delete' ELSE 'Upsert' END FROM " + table + " WHERE " + Scope() + " AND isdraft = TRUE ON CONFLICT DO NOTHING;",
                "UPDATE " + table + " SET validtoversion = " + n + " WHERE " + Scope() + " AND " + _Current + " AND " + identity + " IN (SELECT " + identity + " FROM " + table + " WHERE " + Scope() + " AND isdraft = TRUE);",
                "UPDATE " + table + " SET isdraft = FALSE, validfromversion = " + n + ", state = CASE WHEN state = 'Draft' THEN 'Published' ELSE state END WHERE " + Scope() + " AND isdraft = TRUE;"
            };
        }

        private string AtVersion(long version)
        {
            return "isdraft = FALSE AND validfromversion <= " + D.Num(version) + " AND (validtoversion IS NULL OR validtoversion > " + D.Num(version) + ")";
        }

        private async Task<RemoteStringOverride?> OneStringAsync(string condition, CancellationToken token)
        {
            List<RemoteStringOverride> list = await StringsAsync(condition, "revisionid", 1, token).ConfigureAwait(false);
            return list.Count == 0 ? null : list[0];
        }

        private async Task<List<RemoteStringOverride>> StringsAsync(string condition, string order, int limit, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _StringColumns + " FROM remotestrings WHERE " + Scope() + " AND " + condition + " ORDER BY " + order + " LIMIT " + D.Num(Math.Clamp(limit, 1, 50000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapString).ToList();
        }

        private async Task<AppBanner?> OneBannerAsync(string condition, CancellationToken token)
        {
            List<AppBanner> list = await BannersAsync(condition, "revisionid", 1, token).ConfigureAwait(false);
            return list.Count == 0 ? null : list[0];
        }

        private async Task<List<AppBanner>> BannersAsync(string condition, string order, int limit, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _BannerColumns + " FROM banners WHERE " + Scope() + " AND " + condition + " ORDER BY " + order + " LIMIT " + D.Num(Math.Clamp(limit, 1, 5000)) + ";", token).ConfigureAwait(false);
            List<AppBanner> banners = table.Rows.Cast<DataRow>().Select(MapBanner).ToList();
            await LoadBannerChildrenAsync(banners, token).ConfigureAwait(false);
            return banners;
        }

        private async Task<RemoteAsset?> OneAssetAsync(string condition, CancellationToken token)
        {
            List<RemoteAsset> list = await AssetsAsync(condition, "revisionid", 1, token).ConfigureAwait(false);
            return list.Count == 0 ? null : list[0];
        }

        private async Task<List<RemoteAsset>> AssetsAsync(string condition, string order, int limit, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _AssetColumns + " FROM remoteassets WHERE " + Scope() + " AND " + condition + " ORDER BY " + order + " LIMIT " + D.Num(Math.Clamp(limit, 1, 5000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapAsset).ToList();
        }

        private List<string> DeleteBannerRevisionStatements(string revisions)
        {
            return new List<string>
            {
                "DELETE FROM featureflagconditionvalues WHERE " + Scope() + " AND revisionid IN " + revisions + ";",
                "DELETE FROM featureflagconditions WHERE " + Scope() + " AND revisionid IN " + revisions + ";",
                "DELETE FROM featureflagrules WHERE " + Scope() + " AND revisionid IN " + revisions + ";",
                "DELETE FROM bannerclients WHERE " + Scope() + " AND revisionid IN " + revisions + ";",
                "DELETE FROM banners WHERE " + Scope() + " AND revisionid IN " + revisions + ";"
            };
        }

        private List<string> InsertRuleStatements(string revisionId, List<FeatureFlagRule> rules, string ownerType)
        {
            List<string> statements = new List<string>();
            foreach (FeatureFlagRule rule in rules)
            {
                statements.Add("INSERT INTO featureflagrules (propertyid, revisionid, id, ownertype, position, description, value, rolloutbasispoints) VALUES (" + Pid + ", " + D.Str(revisionId) + ", " +
                    D.Str(rule.Id) + ", " + D.Str(ownerType) + ", " + D.Num(rule.Position) + ", " + D.Str(rule.Description) + ", " + D.Str(rule.Value) + ", " + D.Num(rule.RolloutBasisPoints) + ");");
                for (int c = 0; c < rule.Conditions.Count; c++)
                {
                    FeatureFlagCondition condition = rule.Conditions[c];
                    statements.Add("INSERT INTO featureflagconditions (propertyid, revisionid, ruleid, position, attribute, operator, argument) VALUES (" + Pid + ", " + D.Str(revisionId) + ", " + D.Str(rule.Id) + ", " +
                        D.Num(c) + ", " + D.Str(condition.Attribute) + ", " + D.Str(condition.Operator) + ", " + D.Str(condition.Argument) + ");");
                    if (condition.Values.Count == 0) continue;
                    int position = c;
                    statements.Add("INSERT INTO featureflagconditionvalues (propertyid, revisionid, ruleid, conditionposition, position, value) VALUES " + String.Join(", ", condition.Values.Select((v, i) => "(" + Pid + ", " +
                        D.Str(revisionId) + ", " + D.Str(rule.Id) + ", " + D.Num(position) + ", " + D.Num(i) + ", " + D.Str(v) + ")")) + ";");
                }
            }
            return statements;
        }

        private async Task LoadBannerChildrenAsync(List<AppBanner> banners, CancellationToken token)
        {
            if (banners.Count == 0) return;
            Dictionary<string, AppBanner> byRevision = banners.GroupBy(b => b.RevisionId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            string ids = D.InList(byRevision.Keys);
            DataTable clients = await QueryAsync("SELECT revisionid, client FROM bannerclients WHERE " + Scope() + " AND revisionid IN " + ids + " ORDER BY revisionid, client;", token).ConfigureAwait(false);
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

        private static string Prefixed(string alias, string columns)
        {
            return String.Join(", ", columns.Split(',').Select(c => alias + "." + c.Trim()));
        }

        private static StringCatalogEntry MapCatalog(DataRow row)
        {
            string tokens = RowReader.GetString(row, "tokens");
            return new StringCatalogEntry
            {
                Key = RowReader.GetString(row, "stringkey"),
                Client = RowReader.GetString(row, "client"),
                DefaultValue = RowReader.GetString(row, "defaultvalue"),
                Tokens = tokens.Length == 0 ? new List<string>() : tokens.Split(',').ToList(),
                MaxLength = RowReader.GetInt(row, "maxlength")
            };
        }

        private static RemoteStringOverride MapString(DataRow row)
        {
            return new RemoteStringOverride
            {
                RevisionId = RowReader.GetString(row, "revisionid"),
                Id = RowReader.GetString(row, "objectid"),
                Key = RowReader.GetString(row, "stringkey"),
                Value = RowReader.GetNullableString(row, "value"),
                Notes = RowReader.GetNullableString(row, "notes"),
                State = RowReader.GetEnum(row, "state", FeatureFlagStateEnum.Draft),
                ValidFromVersion = RowReader.GetNullableLong(row, "validfromversion"),
                ValidToVersion = RowReader.GetNullableLong(row, "validtoversion"),
                IsDraft = RowReader.GetBool(row, "isdraft"),
                EditedByUserId = RowReader.GetNullableString(row, "editedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static AppBanner MapBanner(DataRow row)
        {
            return new AppBanner
            {
                RevisionId = RowReader.GetString(row, "revisionid"),
                Id = RowReader.GetString(row, "objectid"),
                Title = RowReader.GetString(row, "title"),
                Body = RowReader.GetString(row, "body"),
                Style = RowReader.GetString(row, "style"),
                Placement = RowReader.GetString(row, "placement"),
                Dismissible = RowReader.GetBool(row, "dismissible"),
                Priority = RowReader.GetInt(row, "priority"),
                StartsUtc = RowReader.GetNullableDateTime(row, "startsutc"),
                EndsUtc = RowReader.GetNullableDateTime(row, "endsutc"),
                LinkLabel = RowReader.GetNullableString(row, "linklabel"),
                LinkPath = RowReader.GetNullableString(row, "linkpath"),
                LinkUrl = RowReader.GetNullableString(row, "linkurl"),
                ImageAssetId = RowReader.GetNullableString(row, "imageassetobjectid"),
                State = RowReader.GetEnum(row, "state", FeatureFlagStateEnum.Draft),
                ValidFromVersion = RowReader.GetNullableLong(row, "validfromversion"),
                ValidToVersion = RowReader.GetNullableLong(row, "validtoversion"),
                IsDraft = RowReader.GetBool(row, "isdraft"),
                EditedByUserId = RowReader.GetNullableString(row, "editedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static RemoteAsset MapAsset(DataRow row)
        {
            return new RemoteAsset
            {
                RevisionId = RowReader.GetString(row, "revisionid"),
                Id = RowReader.GetString(row, "objectid"),
                Key = RowReader.GetString(row, "assetkey"),
                ContentType = RowReader.GetString(row, "contenttype"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                Width = RowReader.GetNullableInt(row, "width"),
                Height = RowReader.GetNullableInt(row, "height"),
                Sha256Hex = RowReader.GetNullableString(row, "sha256hex"),
                DarkSha256Hex = RowReader.GetNullableString(row, "darksha256hex"),
                DarkWidth = RowReader.GetNullableInt(row, "darkwidth"),
                DarkHeight = RowReader.GetNullableInt(row, "darkheight"),
                DarkSizeBytes = RowReader.GetNullableLong(row, "darksizebytes"),
                AltText = RowReader.GetNullableString(row, "alttext"),
                State = RowReader.GetEnum(row, "state", FeatureFlagStateEnum.Draft),
                ValidFromVersion = RowReader.GetNullableLong(row, "validfromversion"),
                ValidToVersion = RowReader.GetNullableLong(row, "validtoversion"),
                IsDraft = RowReader.GetBool(row, "isdraft"),
                EditedByUserId = RowReader.GetNullableString(row, "editedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        #endregion
    }
}
