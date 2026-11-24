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
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared media attachment and variant methods.
    /// </summary>
    internal abstract class MediaMethodsBase : MethodsBase, IMediaMethods
    {
        private const string _Columns = "id, owneruserid, kind, status, failurereason, contenttype, originalfilename, sizebytes, declareddurationseconds, durationseconds, width, height, " +
            "alttext, blurhash, storagemode, sourcebucket, sourcekey, multipartuploadid, partsizebytes, partcount, outputprefix, hlsmanifestfile, mp4file, posterfile, posterthumbfile, " +
            "isretained, ispublic, processor, processorjobid, attempts, claimedby, claimuntilutc, processingstartedutc, readyutc, uploadexpiresutc, attachedtargettype, attachedtargetid, " +
            "attachedposition, attachedutc, detachedutc, deletedutc, createdutc, updatedutc, purpose, scopeid";

        // processorjobid has a partial unique index; reading it through an expression keeps DataTable from inferring a
        // unique constraint that would reject several rows with a null job id.
        private static readonly string _Select = SelectList(String.Empty);

        private const string _VariantColumns = "mediaid, name, format, width, height, sizebytes, filename, sha256";

        protected MediaMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<MediaAttachment> CreateAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            RequireText(media.Id, nameof(media.Id));
            await UniqueAsync("INSERT INTO mediaattachments (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + Values(media) + ");", "Media " + media.Id, token).ConfigureAwait(false);
            return media;
        }

        public async Task<MediaAttachment?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<MediaAttachment>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<MediaAttachment>();
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<MediaAttachment?> ReadByJobIdAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(jobId)) return null;
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND processorjobid = " + D.Str(jobId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<bool> UpdateAsync(MediaAttachment media, MediaStatusEnum? expectedStatus = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            RequireText(media.Id, nameof(media.Id));
            string sql = UpdateSql(media, expectedStatus) + " RETURNING id;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<MediaAttachment>> ClaimAsync(MediaKindEnum kind, string holder, DateTime nowUtc, DateTime claimUntilUtc, int limit, CancellationToken token = default)
        {
            RequireText(holder, nameof(holder));
            if (limit < 1) return new List<MediaAttachment>();
            string free = "(claimuntilutc IS NULL OR claimuntilutc < " + D.Ts(nowUtc) + ")";
            List<string> statements = new List<string>
            {
                "UPDATE mediaattachments SET claimedby = " + D.Str(holder) + ", claimuntilutc = " + D.Ts(claimUntilUtc) +
                    " WHERE " + Scope() + " AND id IN (SELECT id FROM mediaattachments WHERE " + Scope() + " AND status = " + D.Enum(MediaStatusEnum.Processing) + " AND kind = " + D.Enum(kind) +
                    " AND deletedutc IS NULL AND " + free + " ORDER BY updatedutc, id LIMIT " + D.Num(limit) + ") AND status = " + D.Enum(MediaStatusEnum.Processing) + " AND " + free + ";",
                "SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND claimedby = " + D.Str(holder) + " AND claimuntilutc = " + D.Ts(claimUntilUtc) +
                    " AND status = " + D.Enum(MediaStatusEnum.Processing) + " AND kind = " + D.Enum(kind) + " ORDER BY updatedutc, id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).Where(m => m.ClaimedBy == holder).ToList();
        }

        public async Task<bool> CompleteProcessingAsync(MediaAttachment media, List<MediaVariant> variants, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            ArgumentNullException.ThrowIfNull(variants);
            List<string> statements = new List<string>();
            string stillProcessing = "EXISTS (SELECT 1 FROM mediaattachments ma WHERE " + Scope("ma") + " AND ma.id = " + D.Str(media.Id) + " AND ma.status = " + D.Enum(MediaStatusEnum.Processing) + ")";
            statements.Add("DELETE FROM mediavariants WHERE " + Scope() + " AND mediaid = " + D.Str(media.Id) + " AND " + stillProcessing + ";");
            foreach (MediaVariant variant in variants)
            {
                statements.Add("INSERT INTO mediavariants (propertyid, " + _VariantColumns + ") SELECT " + Pid + ", " + D.Str(media.Id) + ", " + D.Str(variant.Name) + ", " + D.Str(variant.Format) + ", " +
                    D.Num(variant.Width) + ", " + D.Num(variant.Height) + ", " + D.Num(variant.SizeBytes) + ", " + D.Str(variant.FileName) + ", " + D.Str(variant.Sha256) + " WHERE " + stillProcessing + ";");
            }
            statements.Add(UpdateSql(media, MediaStatusEnum.Processing) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<MediaVariant>> ReadVariantsAsync(string mediaId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mediaId)) return new List<MediaVariant>();
            DataTable table = await QueryAsync("SELECT " + _VariantColumns + " FROM mediavariants WHERE " + Scope() + " AND mediaid = " + D.Str(mediaId) + " ORDER BY name, format;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapVariant).ToList();
        }

        public async Task<Dictionary<string, List<MediaVariant>>> ReadVariantsManyAsync(IEnumerable<string> mediaIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(mediaIds);
            Dictionary<string, List<MediaVariant>> result = new Dictionary<string, List<MediaVariant>>(StringComparer.Ordinal);
            List<string> ids = mediaIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT " + _VariantColumns + " FROM mediavariants WHERE " + Scope() + " AND mediaid IN " + D.InList(ids) + " ORDER BY mediaid, name, format;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                MediaVariant variant = MapVariant(row);
                if (!result.TryGetValue(variant.MediaId, out List<MediaVariant>? list))
                {
                    list = new List<MediaVariant>();
                    result[variant.MediaId] = list;
                }
                list.Add(variant);
            }
            return result;
        }

        public async Task DeleteVariantsAsync(string mediaId, CancellationToken token = default)
        {
            RequireText(mediaId, nameof(mediaId));
            await QueryAsync("DELETE FROM mediavariants WHERE " + Scope() + " AND mediaid = " + D.Str(mediaId) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<MediaAttachment>> AttachAsync(TargetTypeEnum targetType, string targetId, List<string> orderedIds, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            ArgumentNullException.ThrowIfNull(orderedIds);
            string target = Scope() + " AND attachedtargettype = " + D.Enum(targetType) + " AND attachedtargetid = " + D.Str(targetId);
            List<string> ids = orderedIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            List<string> statements = new List<string>
            {
                "UPDATE mediaattachments SET attachedtargettype = NULL, attachedtargetid = NULL, attachedposition = 0, attachedutc = NULL, detachedutc = " + D.Ts(nowUtc) +
                    ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + target + (ids.Count == 0 ? String.Empty : " AND id NOT IN " + D.InList(ids)) + ";"
            };
            for (int i = 0; i < ids.Count; i++)
            {
                statements.Add("UPDATE mediaattachments SET attachedutc = CASE WHEN attachedtargetid IS NULL THEN " + D.Ts(nowUtc) + " ELSE attachedutc END, attachedtargettype = " +
                    D.Enum(targetType) + ", attachedtargetid = " + D.Str(targetId) + ", attachedposition = " + D.Num(i) + ", detachedutc = NULL, updatedutc = " + D.Ts(nowUtc) +
                    " WHERE " + Scope() + " AND id = " + D.Str(ids[i]) + " AND deletedutc IS NULL AND (attachedtargetid IS NULL OR (attachedtargettype = " + D.Enum(targetType) + " AND attachedtargetid = " + D.Str(targetId) + "));");
            }
            statements.Add("SELECT " + _Select + " FROM mediaattachments WHERE " + target + " AND deletedutc IS NULL ORDER BY attachedposition, id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<int> DetachAsync(TargetTypeEnum targetType, string targetId, IEnumerable<string>? mediaIds, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            string condition = Scope() + " AND attachedtargettype = " + D.Enum(targetType) + " AND attachedtargetid = " + D.Str(targetId);
            if (mediaIds != null)
            {
                List<string> ids = mediaIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
                if (ids.Count == 0) return 0;
                condition += " AND id IN " + D.InList(ids);
            }
            DataTable table = await QueryAsync("UPDATE mediaattachments SET attachedtargettype = NULL, attachedtargetid = NULL, attachedposition = 0, attachedutc = NULL, detachedutc = " +
                D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + condition + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<List<MediaAttachment>> ListByTargetsAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            List<string> ids = targetIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<MediaAttachment>();
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND attachedtargettype = " + D.Enum(targetType) + " AND attachedtargetid IN " +
                D.InList(ids) + " AND deletedutc IS NULL ORDER BY attachedtargetid, attachedposition, id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<MediaAttachment>> ListByOwnerAsync(string userId, bool unattachedOnly, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND owneruserid = " + D.Str(userId) + " AND deletedutc IS NULL" +
                (unattachedOnly ? " AND attachedtargetid IS NULL" : String.Empty) + " ORDER BY createdutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<MediaAttachment>> ListByOwnerAfterAsync(string ownerUserId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(ownerUserId, nameof(ownerUserId));
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND owneruserid = " + D.Str(ownerUserId) +
                (String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId)) + " ORDER BY id ASC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<long> SumBytesSinceAsync(string userId, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT COALESCE(SUM(sizebytes), 0) AS total FROM mediaattachments WHERE " + Scope() + " AND owneruserid = " + D.Str(userId) + " AND createdutc >= " + D.Ts(sinceUtc) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "total");
        }

        public async Task<List<MediaAttachment>> ListExpiredUploadsAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND status = " + D.Enum(MediaStatusEnum.Uploading) + " AND uploadexpiresutc < " + D.Ts(nowUtc) +
                " AND deletedutc IS NULL ORDER BY uploadexpiresutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<MediaAttachment>> ListUnattachedBeforeAsync(DateTime cutoffUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM mediaattachments WHERE " + Scope() + " AND attachedtargetid IS NULL AND deletedutc IS NULL AND status IN " +
                D.InList(new[] { MediaStatusEnum.Ready.ToString(), MediaStatusEnum.Failed.ToString() }) + " AND COALESCE(detachedutc, createdutc) < " + D.Ts(cutoffUtc) +
                " ORDER BY createdutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> MarkDeletedAsync(string mediaId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(mediaId, nameof(mediaId));
            List<string> statements = new List<string> { "DELETE FROM mediavariants WHERE " + Scope() + " AND mediaid = " + D.Str(mediaId) + ";" };
            statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.Media, mediaId));
            statements.Add("UPDATE mediaattachments SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + ", attachedtargettype = NULL, attachedtargetid = NULL, " +
                "attachedposition = 0, attachedutc = NULL, multipartuploadid = NULL, claimedby = NULL, claimuntilutc = NULL WHERE " + Scope() + " AND id = " + D.Str(mediaId) + " AND deletedutc IS NULL RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> PurgeDeletedAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            string ids = "SELECT id FROM mediaattachments WHERE " + Scope() + " AND deletedutc IS NOT NULL AND deletedutc < " + D.Ts(cutoffUtc);
            List<string> statements = new List<string> { "DELETE FROM mediavariants WHERE " + Scope() + " AND mediaid IN (" + ids + ");" };
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.Media, ids));
            statements.Add("DELETE FROM mediaattachments WHERE " + Scope() + " AND deletedutc IS NOT NULL AND deletedutc < " + D.Ts(cutoffUtc) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public Task<EnumerationResult<MediaAttachment>> EnumerateAsync(EnumerationRequest request, MediaFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string> { "m.deletedutc IS NULL" };
            if (filter.Kind != null) conditions.Add("m.kind = " + D.Enum(filter.Kind.Value));
            if (filter.Status != null) conditions.Add("m.status = " + D.Enum(filter.Status.Value));
            if (!String.IsNullOrEmpty(filter.OwnerUserId)) conditions.Add("m.owneruserid = " + D.Str(filter.OwnerUserId));
            if (filter.Attached == true) conditions.Add("m.attachedtargetid IS NOT NULL");
            if (filter.Attached == false) conditions.Add("m.attachedtargetid IS NULL");
            string select = SelectList("m.");
            return EnumerateAsync(request, select, "mediaattachments m", conditions, Map, token);
        }

        public async Task<MediaSummary> SummaryAsync(DateTime nowUtc, CancellationToken token = default)
        {
            MediaSummary summary = new MediaSummary();
            foreach (string kind in Enum.GetNames<MediaKindEnum>()) summary.ByKind[kind] = 0;
            foreach (string status in Enum.GetNames<MediaStatusEnum>()) summary.ByStatus[status] = 0;
            DataTable groups = await QueryAsync("SELECT kind, status, COUNT(*) AS cnt, COALESCE(SUM(sizebytes), 0) AS bytes FROM mediaattachments WHERE " + Scope() + " AND deletedutc IS NULL GROUP BY kind, status;", token).ConfigureAwait(false);
            foreach (DataRow row in groups.Rows)
            {
                long count = RowReader.GetLong(row, "cnt");
                string kind = RowReader.GetString(row, "kind");
                string status = RowReader.GetString(row, "status");
                summary.TotalCount += count;
                summary.TotalStoredBytes += RowReader.GetLong(row, "bytes");
                if (summary.ByKind.ContainsKey(kind)) summary.ByKind[kind] += count;
                if (summary.ByStatus.ContainsKey(status)) summary.ByStatus[status] += count;
            }
            DataTable variants = await QueryAsync("SELECT COALESCE(SUM(v.sizebytes), 0) AS bytes FROM mediavariants v WHERE " + Scope("v") + " AND v.mediaid IN (SELECT id FROM mediaattachments WHERE " + Scope() + " AND deletedutc IS NULL);", token).ConfigureAwait(false);
            if (variants.Rows.Count > 0) summary.TotalStoredBytes += RowReader.GetLong(variants.Rows[0], "bytes");
            summary.Failed24h = await CountAsync("SELECT COUNT(*) AS cnt FROM mediaattachments WHERE " + Scope() + " AND deletedutc IS NULL AND status = " + D.Enum(MediaStatusEnum.Failed) +
                " AND updatedutc >= " + D.Ts(nowUtc.AddHours(-24)) + ";", token).ConfigureAwait(false);
            return summary;
        }

        private static string SelectList(string prefix)
        {
            return String.Join(", ", _Columns.Split(", ").Select(c => c == "processorjobid" ? "COALESCE(" + prefix + c + ", NULL) AS processorjobid" : prefix + c));
        }

        private string UpdateSql(MediaAttachment m, MediaStatusEnum? expectedStatus)
        {
            return "UPDATE mediaattachments SET status = " + D.Enum(m.Status) + ", failurereason = " + D.Enum(m.FailureReason) + ", contenttype = " + D.Str(m.ContentType) +
                ", sizebytes = " + D.Num(m.SizeBytes) + ", durationseconds = " + Dbl(m.DurationSeconds) + ", width = " + D.Num(m.Width) + ", height = " + D.Num(m.Height) +
                ", alttext = " + D.Str(m.AltText) + ", blurhash = " + D.Str(m.Blurhash) + ", multipartuploadid = " + D.Str(m.MultipartUploadId) + ", outputprefix = " + D.Str(m.OutputPrefix) +
                ", hlsmanifestfile = " + D.Str(m.HlsManifestFile) + ", mp4file = " + D.Str(m.Mp4File) + ", posterfile = " + D.Str(m.PosterFile) + ", posterthumbfile = " + D.Str(m.PosterThumbFile) +
                ", isretained = " + D.Bool(m.IsRetained) + ", ispublic = " + D.Bool(m.IsPublic) + ", processor = " + D.Str(m.Processor) + ", processorjobid = " + D.Str(m.ProcessorJobId) +
                ", attempts = " + D.Num(m.Attempts) + ", claimedby = " + D.Str(m.ClaimedBy) + ", claimuntilutc = " + D.Ts(m.ClaimUntilUtc) + ", processingstartedutc = " + D.Ts(m.ProcessingStartedUtc) +
                ", readyutc = " + D.Ts(m.ReadyUtc) + ", uploadexpiresutc = " + D.Ts(m.UploadExpiresUtc) + ", updatedutc = " + D.Ts(m.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(m.Id) + " AND deletedutc IS NULL" + (expectedStatus == null ? String.Empty : " AND status = " + D.Enum(expectedStatus.Value));
        }

        private string Values(MediaAttachment m)
        {
            return String.Join(", ", new[]
            {
                D.Str(m.Id), D.Str(m.OwnerUserId), D.Enum(m.Kind), D.Enum(m.Status), D.Enum(m.FailureReason), D.Str(m.ContentType), D.Str(m.OriginalFileName), D.Num(m.SizeBytes),
                Dbl(m.DeclaredDurationSeconds), Dbl(m.DurationSeconds), D.Num(m.Width), D.Num(m.Height), D.Str(m.AltText), D.Str(m.Blurhash), D.Enum(m.StorageMode), D.Str(m.SourceBucket),
                D.Str(m.SourceKey), D.Str(m.MultipartUploadId), D.Num(m.PartSizeBytes), D.Num(m.PartCount), D.Str(m.OutputPrefix), D.Str(m.HlsManifestFile), D.Str(m.Mp4File),
                D.Str(m.PosterFile), D.Str(m.PosterThumbFile), D.Bool(m.IsRetained), D.Bool(m.IsPublic), D.Str(m.Processor), D.Str(m.ProcessorJobId), D.Num(m.Attempts), D.Str(m.ClaimedBy),
                D.Ts(m.ClaimUntilUtc), D.Ts(m.ProcessingStartedUtc), D.Ts(m.ReadyUtc), D.Ts(m.UploadExpiresUtc), D.Enum(m.AttachedTargetType), D.Str(m.AttachedTargetId),
                D.Num(m.AttachedPosition), D.Ts(m.AttachedUtc), D.Ts(m.DetachedUtc), D.Ts(m.DeletedUtc), D.Ts(m.CreatedUtc), D.Ts(m.UpdatedUtc),
                D.Enum(m.Purpose), D.Str(m.ScopeId)
            });
        }

        private string Dbl(double? value)
        {
            return value == null ? "NULL" : D.Num(value.Value);
        }

        private static double? NullableDouble(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return null;
            return Convert.ToDouble(row[column], CultureInfo.InvariantCulture);
        }

        private static MediaAttachment Map(DataRow row)
        {
            return new MediaAttachment
            {
                Id = RowReader.GetString(row, "id"),
                OwnerUserId = RowReader.GetString(row, "owneruserid"),
                Kind = RowReader.GetEnum(row, "kind", MediaKindEnum.Image),
                Status = RowReader.GetEnum(row, "status", MediaStatusEnum.Failed),
                FailureReason = RowReader.GetNullableEnum<MediaFailureReasonEnum>(row, "failurereason"),
                ContentType = RowReader.GetString(row, "contenttype"),
                OriginalFileName = RowReader.GetNullableString(row, "originalfilename"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                DeclaredDurationSeconds = NullableDouble(row, "declareddurationseconds"),
                DurationSeconds = NullableDouble(row, "durationseconds"),
                Width = RowReader.GetNullableInt(row, "width"),
                Height = RowReader.GetNullableInt(row, "height"),
                AltText = RowReader.GetNullableString(row, "alttext"),
                Blurhash = RowReader.GetNullableString(row, "blurhash"),
                StorageMode = RowReader.GetEnum(row, "storagemode", MediaStorageModeEnum.Local),
                SourceBucket = RowReader.GetNullableString(row, "sourcebucket"),
                SourceKey = RowReader.GetString(row, "sourcekey"),
                MultipartUploadId = RowReader.GetNullableString(row, "multipartuploadid"),
                PartSizeBytes = RowReader.GetLong(row, "partsizebytes"),
                PartCount = RowReader.GetInt(row, "partcount"),
                OutputPrefix = RowReader.GetNullableString(row, "outputprefix"),
                HlsManifestFile = RowReader.GetNullableString(row, "hlsmanifestfile"),
                Mp4File = RowReader.GetNullableString(row, "mp4file"),
                PosterFile = RowReader.GetNullableString(row, "posterfile"),
                PosterThumbFile = RowReader.GetNullableString(row, "posterthumbfile"),
                IsRetained = RowReader.GetBool(row, "isretained"),
                IsPublic = RowReader.GetBool(row, "ispublic"),
                Processor = RowReader.GetNullableString(row, "processor"),
                ProcessorJobId = RowReader.GetNullableString(row, "processorjobid"),
                Attempts = RowReader.GetInt(row, "attempts"),
                ClaimedBy = RowReader.GetNullableString(row, "claimedby"),
                ClaimUntilUtc = RowReader.GetNullableDateTime(row, "claimuntilutc"),
                ProcessingStartedUtc = RowReader.GetNullableDateTime(row, "processingstartedutc"),
                ReadyUtc = RowReader.GetNullableDateTime(row, "readyutc"),
                UploadExpiresUtc = RowReader.GetDateTime(row, "uploadexpiresutc"),
                AttachedTargetType = RowReader.GetNullableEnum<TargetTypeEnum>(row, "attachedtargettype"),
                AttachedTargetId = RowReader.GetNullableString(row, "attachedtargetid"),
                AttachedPosition = RowReader.GetInt(row, "attachedposition"),
                AttachedUtc = RowReader.GetNullableDateTime(row, "attachedutc"),
                DetachedUtc = RowReader.GetNullableDateTime(row, "detachedutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc"),
                Purpose = RowReader.GetEnum(row, "purpose", MediaPurposeEnum.Content),
                ScopeId = RowReader.GetNullableString(row, "scopeid")
            };
        }

        private static MediaVariant MapVariant(DataRow row)
        {
            return new MediaVariant
            {
                MediaId = RowReader.GetString(row, "mediaid"),
                Name = RowReader.GetString(row, "name"),
                Format = RowReader.GetString(row, "format"),
                Width = RowReader.GetInt(row, "width"),
                Height = RowReader.GetInt(row, "height"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                FileName = RowReader.GetString(row, "filename"),
                Sha256 = RowReader.GetNullableString(row, "sha256")
            };
        }

        public async Task<int> BindScopeAsync(IEnumerable<string> mediaIds, MediaPurposeEnum purpose, string scopeId, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(mediaIds);
            RequireText(scopeId, nameof(scopeId));
            List<string> ids = mediaIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return 0;
            DataTable table = await QueryAsync("UPDATE mediaattachments SET scopeid = " + D.Str(scopeId) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN " + D.InList(ids) +
                " AND purpose = " + D.Enum(purpose) + " AND deletedutc IS NULL AND (scopeid IS NULL OR scopeid = " + D.Str(scopeId) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public Task<long> TotalBytesAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COALESCE(SUM(sizebytes), 0) AS cnt FROM mediaattachments WHERE " + Scope() + " AND deletedutc IS NULL;", token);
        }
    }
}
