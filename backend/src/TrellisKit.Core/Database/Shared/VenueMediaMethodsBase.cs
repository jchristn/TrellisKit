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
    using TrellisKit.Core.Responses;

    internal abstract class VenueMediaMethodsBase : MethodsBase, IVenueMediaMethods
    {
        protected VenueMediaMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateManyAsync(List<VenueMediaItem> items, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(items);
            if (items.Count == 0) return;
            List<string> statements = new List<string>
            {
                "INSERT INTO venuemedia (propertyid, id, venueid, mediaid, uploaderuserid, kind, openreportcount, moderationstate, createdutc, updatedutc) VALUES " +
                    String.Join(", ", items.Select(i => "(" + Pid + ", " + D.Str(i.Id) + ", " + D.Str(i.VenueId) + ", " + D.Str(i.MediaId) + ", " + D.Str(i.UploaderUserId) + ", " +
                        D.Enum(i.Kind) + ", 0, " + D.Enum(i.ModerationState) + ", " + D.Ts(i.CreatedUtc) + ", " + D.Ts(i.UpdatedUtc) + ")")) + ";"
            };
            foreach (string venue in items.Select(i => i.VenueId).Distinct(StringComparer.Ordinal)) statements.Add(RecomputeStatement(venue));
            await UniqueTransactionAsync(statements, "A venue media item for this media", token).ConfigureAwait(false);
        }

        public async Task<VenueMediaItem?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            List<VenueMediaItem> rows = await LoadAsync("m.id = " + D.Str(id), token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        public async Task<List<VenueMediaItem>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = DirectorySql.Distinct(ids);
            if (list.Count == 0) return new List<VenueMediaItem>();
            return await LoadAsync("m.id IN " + D.InList(list), token).ConfigureAwait(false);
        }

        public async Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            VenueMediaItem? item = await ReadAsync(id, token).ConfigureAwait(false);
            if (item == null || item.DeletedUtc != null) return false;
            List<string> statements = new List<string>
            {
                "UPDATE venuemedia SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL;",
                RecomputeStatement(item.VenueId),
                "SELECT id FROM venuemedia WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc = " + D.Ts(nowUtc) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            VenueMediaItem? item = await ReadAsync(id, token).ConfigureAwait(false);
            if (item == null || item.DeletedUtc != null) return false;
            List<string> statements = new List<string>
            {
                "UPDATE venuemedia SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(byUserId) + ", moderationreason = " + D.Str(reason) +
                    ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL;",
                RecomputeStatement(item.VenueId)
            };
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return true;
        }

        public async Task SetOpenReportCountAsync(string id, int count, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE venuemedia SET openreportcount = " + D.Num(Math.Max(0, count)) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<EnumerationResult<VenueMediaItem>> EnumerateAsync(EnumerationRequest request, VenueContributionFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            RequireText(filter.VenueId, nameof(filter.VenueId));
            List<string> conditions = new List<string> { "m.venueid = " + D.Str(filter.VenueId), "m.deletedutc IS NULL" };
            if (!filter.IncludeModerated) conditions.Add("m.moderationstate = 'None'");
            List<string> hidden = DirectorySql.Distinct(filter.HiddenAuthorIds);
            if (hidden.Count > 0) conditions.Add("m.uploaderuserid NOT IN " + D.InList(hidden));
            if (!String.IsNullOrEmpty(filter.AuthorUserId)) conditions.Add("m.uploaderuserid = " + D.Str(filter.AuthorUserId));
            if (filter.Kind != null) conditions.Add("m.kind = " + D.Enum(filter.Kind.Value));
            return await EnumerateAsync(request, "m.*", "venuemedia m", conditions, Map, token).ConfigureAwait(false);
        }

        public async Task<List<VenueMediaItem>> EraseForUserAsync(string userId, DateTime nowUtc, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            List<VenueMediaItem> items = await LoadAsync("m.uploaderuserid = " + D.Str(userId) + " AND m.deletedutc IS NULL", token, Math.Clamp(limit, 1, 1000)).ConfigureAwait(false);
            if (items.Count == 0) return items;
            List<string> statements = new List<string>
            {
                "UPDATE venuemedia SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN " + D.InList(items.Select(i => i.Id)) + " AND deletedutc IS NULL;"
            };
            foreach (string venue in items.Select(i => i.VenueId).Distinct(StringComparer.Ordinal)) statements.Add(RecomputeStatement(venue));
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return items;
        }

        private string RecomputeStatement(string venueId)
        {
            RequireText(venueId, nameof(venueId));
            return "UPDATE venues SET mediacount = (SELECT COUNT(*) FROM venuemedia c WHERE " + Scope("c") + " AND c.venueid = " + D.Str(venueId) +
                " AND c.deletedutc IS NULL AND c.moderationstate = 'None') WHERE " + Scope() + " AND id = " + D.Str(venueId) + ";";
        }

        private async Task<List<VenueMediaItem>> LoadAsync(string condition, CancellationToken token, int limit = 1000)
        {
            DataTable table = await QueryAsync("SELECT m.* FROM venuemedia m WHERE " + Scope("m") + " AND " + condition + " ORDER BY m.id LIMIT " + D.Num(limit) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        private static VenueMediaItem Map(DataRow row)
        {
            return new VenueMediaItem
            {
                Id = RowReader.GetString(row, "id"),
                VenueId = RowReader.GetString(row, "venueid"),
                MediaId = RowReader.GetString(row, "mediaid"),
                UploaderUserId = RowReader.GetString(row, "uploaderuserid"),
                Kind = RowReader.GetEnum(row, "kind", MediaKindEnum.Image),
                OpenReportCount = RowReader.GetInt(row, "openreportcount"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
