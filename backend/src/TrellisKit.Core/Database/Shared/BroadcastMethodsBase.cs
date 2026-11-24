namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared broadcast methods (docs/PHASE5_CONTRACT.md sections 8 and 16). Every audience statement is one bounded
    /// query over users (an aggregate count, a sample with a limit, or a keyset batch ordered by user id).
    /// </summary>
    internal abstract class BroadcastMethodsBase : MethodsBase, IBroadcastMethods
    {
        private const string _Columns =
            "id, title, inapp, push, email, inapptitle, inappbody, pushtitle, pushbody, emailsubject, emailpreheader, emailbody, ctalabel, ctapath, segmentid, category, " +
            "essentialreason, status, istest, sendatutc, startedutc, sentutc, canceledutc, targeted, cursoruserid, statinappcreated, statpushsent, statpushfailed, " +
            "statemailqueued, statemailsent, statemaildelivered, statemailfailed, statemailsuppressed, statskippedpreferences, createdbyuserid, createdutc, updatedutc";

        private const string _SegmentColumns = "id, name, description, createdbyuserid, createdutc, updatedutc";

        private const string _FilterColumns = "id, segmentid, broadcastid, field, operator, value, valuekind, position";

        private const string _DeliveryColumns = "id, broadcastid, userid, inappstatus, pushstatus, emailstatus, notificationid, emailmessageid, emailqueuedutc, createdutc, updatedutc";


        protected BroadcastMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<BroadcastSegment> CreateSegmentAsync(BroadcastSegment segment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(segment);
            RequireText(segment.Id, nameof(segment.Id));
            List<string> statements = new List<string>
            {
                "INSERT INTO broadcastsegments (propertyid, " + _SegmentColumns + ", deletedutc) VALUES (" + Pid + ", " + D.Str(segment.Id) + ", " + D.Str(segment.Name) + ", " +
                    D.Str(segment.Description) + ", " + D.Str(segment.CreatedByUserId) + ", " + D.Ts(segment.CreatedUtc) + ", " + D.Ts(segment.UpdatedUtc) + ", NULL);"
            };
            string? filters = InsertFilters("segmentid", segment.Id, segment.Filters);
            if (filters != null) statements.Add(filters);
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return segment;
        }

        public async Task<BroadcastSegment?> ReadSegmentAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _SegmentColumns + " FROM broadcastsegments WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            BroadcastSegment segment = MapSegment(table.Rows[0]);
            Dictionary<string, List<BroadcastSegmentFilter>> filters = await FiltersAsync("segmentid", new[] { segment.Id }, token).ConfigureAwait(false);
            if (filters.TryGetValue(segment.Id, out List<BroadcastSegmentFilter>? list)) segment.Filters = list;
            return segment;
        }

        public async Task<bool> UpdateSegmentAsync(BroadcastSegment segment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(segment);
            RequireText(segment.Id, nameof(segment.Id));
            string id = D.Str(segment.Id);
            string live = "EXISTS (SELECT 1 FROM broadcastsegments ls WHERE " + Scope("ls") + " AND ls.id = " + id + " AND ls.deletedutc IS NULL)";
            List<string> statements = new List<string>
            {
                "DELETE FROM broadcastsegmentfilters WHERE " + Scope() + " AND segmentid = " + id + " AND " + live + ";"
            };
            string? filters = InsertFilters("segmentid", segment.Id, segment.Filters, live);
            if (filters != null) statements.Add(filters);
            statements.Add("UPDATE broadcastsegments SET name = " + D.Str(segment.Name) + ", description = " + D.Str(segment.Description) + ", updatedutc = " + D.Ts(segment.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + id + " AND deletedutc IS NULL RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteSegmentAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = new List<string>
            {
                "DELETE FROM broadcastsegmentfilters WHERE " + Scope() + " AND segmentid = " + D.Str(id) + ";",
                "DELETE FROM broadcastsegments WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<EnumerationResult<BroadcastSegment>> EnumerateSegmentsAsync(EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "s.deletedutc IS NULL" };
            string select = String.Join(", ", _SegmentColumns.Split(", ").Select(c => "s." + c));
            EnumerationResult<BroadcastSegment> page = await EnumerateAsync(request, select, "broadcastsegments s", conditions, MapSegment, token).ConfigureAwait(false);
            Dictionary<string, List<BroadcastSegmentFilter>> filters = await FiltersAsync("segmentid", page.Objects.Select(s => s.Id), token).ConfigureAwait(false);
            foreach (BroadcastSegment segment in page.Objects)
                if (filters.TryGetValue(segment.Id, out List<BroadcastSegmentFilter>? list)) segment.Filters = list;
            return page;
        }

        public async Task<Broadcast> CreateAsync(Broadcast broadcast, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(broadcast);
            RequireText(broadcast.Id, nameof(broadcast.Id));
            List<string> statements = new List<string> { "INSERT INTO broadcasts (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + Values(broadcast) + ");" };
            string? filters = InsertFilters("broadcastid", broadcast.Id, broadcast.Filters);
            if (filters != null) statements.Add(filters);
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return broadcast;
        }

        public async Task<Broadcast?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM broadcasts WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            Broadcast broadcast = Map(table.Rows[0]);
            Dictionary<string, List<BroadcastSegmentFilter>> filters = await FiltersAsync("broadcastid", new[] { broadcast.Id }, token).ConfigureAwait(false);
            if (filters.TryGetValue(broadcast.Id, out List<BroadcastSegmentFilter>? list)) broadcast.Filters = list;
            return broadcast;
        }

        public async Task<bool> UpdateAsync(Broadcast broadcast, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(broadcast);
            RequireText(broadcast.Id, nameof(broadcast.Id));
            string id = D.Str(broadcast.Id);
            string editable = "status IN " + D.InList(new[] { BroadcastStatusEnum.Draft.ToString(), BroadcastStatusEnum.Scheduled.ToString() });
            string stillEditable = "EXISTS (SELECT 1 FROM broadcasts eb WHERE " + Scope("eb") + " AND eb.id = " + id + " AND eb." + editable + ")";
            List<string> statements = new List<string>
            {
                "DELETE FROM broadcastsegmentfilters WHERE " + Scope() + " AND broadcastid = " + id + " AND " + stillEditable + ";"
            };
            string? filters = InsertFilters("broadcastid", broadcast.Id, broadcast.Filters, stillEditable);
            if (filters != null) statements.Add(filters);
            statements.Add("UPDATE broadcasts SET title = " + D.Str(broadcast.Title) + ", inapp = " + D.Bool(broadcast.InApp) + ", push = " + D.Bool(broadcast.Push) + ", email = " + D.Bool(broadcast.Email) +
                ", inapptitle = " + D.Str(broadcast.InAppTitle) + ", inappbody = " + D.Str(broadcast.InAppBody) + ", pushtitle = " + D.Str(broadcast.PushTitle) + ", pushbody = " + D.Str(broadcast.PushBody) +
                ", emailsubject = " + D.Str(broadcast.EmailSubject) + ", emailpreheader = " + D.Str(broadcast.EmailPreheader) + ", emailbody = " + D.Str(broadcast.EmailBody) +
                ", ctalabel = " + D.Str(broadcast.CtaLabel) + ", ctapath = " + D.Str(broadcast.CtaPath) + ", segmentid = " + D.Str(broadcast.SegmentId) + ", category = " + D.Enum(broadcast.Category) +
                ", essentialreason = " + D.Str(broadcast.EssentialReason) + ", sendatutc = " + D.Ts(broadcast.SendAtUtc) + ", updatedutc = " + D.Ts(broadcast.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + id + " AND " + editable + " RETURNING id;");
            try
            {
                DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
                return table.Rows.Count > 0;
            }
            catch (Exception e) when (D.IsUniqueViolation(e))
            {
                return false;
            }
        }

        public async Task<bool> DeleteDraftAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string bid = D.Str(id);
            string draft = "EXISTS (SELECT 1 FROM broadcasts db WHERE " + Scope("db") + " AND db.id = " + bid + " AND db.status = " + D.Enum(BroadcastStatusEnum.Draft) + ")";
            List<string> statements = new List<string>
            {
                "DELETE FROM broadcastsegmentfilters WHERE " + Scope() + " AND broadcastid = " + bid + " AND " + draft + ";",
                "DELETE FROM broadcasts WHERE " + Scope() + " AND id = " + bid + " AND status = " + D.Enum(BroadcastStatusEnum.Draft) + " RETURNING id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<EnumerationResult<Broadcast>> EnumerateAsync(EnumerationRequest request, List<BroadcastStatusEnum> statuses, BroadcastChannelEnum? channel, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(statuses);
            List<string> conditions = new List<string> { "b.istest = " + D.Bool(false) };
            if (statuses.Count > 0) conditions.Add("b.status IN " + D.InList(statuses.Select(s => s.ToString())));
            if (channel != null) conditions.Add("b." + ChannelColumn(channel.Value) + " = " + D.Bool(true));
            string select = String.Join(", ", _Columns.Split(", ").Select(c => "b." + c));
            EnumerationResult<Broadcast> page = await EnumerateAsync(request, select, "broadcasts b", conditions, Map, token).ConfigureAwait(false);
            Dictionary<string, List<BroadcastSegmentFilter>> filters = await FiltersAsync("broadcastid", page.Objects.Select(b => b.Id), token).ConfigureAwait(false);
            foreach (Broadcast broadcast in page.Objects)
                if (filters.TryGetValue(broadcast.Id, out List<BroadcastSegmentFilter>? list)) broadcast.Filters = list;
            return page;
        }

        public async Task<bool> TransitionAsync(string id, IReadOnlyCollection<BroadcastStatusEnum> from, BroadcastStatusEnum to, DateTime? sendAtUtc, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            ArgumentNullException.ThrowIfNull(from);
            if (from.Count == 0) return false;
            string set = "status = " + D.Enum(to) + ", updatedutc = " + D.Ts(nowUtc);
            if (sendAtUtc != null) set += ", sendatutc = " + D.Ts(sendAtUtc.Value);
            if (to == BroadcastStatusEnum.Sending) set += ", startedutc = COALESCE(startedutc, " + D.Ts(nowUtc) + ")";
            if (to == BroadcastStatusEnum.Canceled) set += ", canceledutc = " + D.Ts(nowUtc);
            if (to == BroadcastStatusEnum.Sent) set += ", sentutc = " + D.Ts(nowUtc);
            DataTable table = await QueryAsync("UPDATE broadcasts SET " + set + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND status IN " + D.InList(from.Select(s => s.ToString())) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<long> CountUsedInWindowAsync(DateTime startUtc, DateTime endUtc, string? exceptId, CancellationToken token = default)
        {
            string window = "((startedutc >= " + D.Ts(startUtc) + " AND startedutc < " + D.Ts(endUtc) + ") OR (status = " + D.Enum(BroadcastStatusEnum.Scheduled) + " AND sendatutc >= " + D.Ts(startUtc) +
                " AND sendatutc < " + D.Ts(endUtc) + "))";
            return CountAsync("SELECT COUNT(*) AS cnt FROM broadcasts WHERE " + Scope() + " AND istest = " + D.Bool(false) + " AND " + window +
                (String.IsNullOrEmpty(exceptId) ? String.Empty : " AND id <> " + D.Str(exceptId)) + ";", token);
        }

        public async Task<List<string>> StartDueAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            string scheduled = "status = " + D.Enum(BroadcastStatusEnum.Scheduled);
            DataTable table = await QueryAsync("UPDATE broadcasts SET status = " + D.Enum(BroadcastStatusEnum.Sending) + ", startedutc = COALESCE(startedutc, " + D.Ts(nowUtc) + "), updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id IN (SELECT d.id FROM broadcasts d WHERE " + Scope("d") + " AND d." + scheduled + " AND d.sendatutc <= " + D.Ts(nowUtc) + " ORDER BY d.sendatutc, d.id LIMIT " +
                D.Num(Math.Max(1, limit)) + ") AND " + scheduled + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<List<string>> ListSendingAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT id FROM broadcasts WHERE " + Scope() + " AND status = " + D.Enum(BroadcastStatusEnum.Sending) + " ORDER BY startedutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<List<string>> ListRecentlyFinishedAsync(DateTime sinceUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT id FROM broadcasts WHERE " + Scope() + " AND istest = " + D.Bool(false) + " AND startedutc IS NOT NULL AND COALESCE(sentutc, canceledutc) >= " + D.Ts(sinceUtc) +
                " ORDER BY COALESCE(sentutc, canceledutc) DESC, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<bool> RecordProgressAsync(string id, string cursorUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(cursorUserId, nameof(cursorUserId));
            DataTable table = await QueryAsync("UPDATE broadcasts SET cursoruserid = " + D.Str(cursorUserId) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND status = " + D.Enum(BroadcastStatusEnum.Sending) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<BroadcastStats> SyncStatsAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string bid = D.Str(id);
            string sent = D.Enum(BroadcastDeliveryStatusEnum.Sent);
            string failed = D.Enum(BroadcastDeliveryStatusEnum.Failed);
            string suppressed = D.Enum(BroadcastDeliveryStatusEnum.Suppressed);
            string delivered = D.Enum(BroadcastDeliveryStatusEnum.Delivered);
            string queued = D.Enum(BroadcastDeliveryStatusEnum.Queued);
            string allErrors = "EXISTS (SELECT 1 FROM pushdeliveries pd WHERE pd.propertyid = " + Pid + " AND pd.notificationid = n.id) AND NOT EXISTS (SELECT 1 FROM pushdeliveries pe WHERE pe.propertyid = " + Pid +
                " AND pe.notificationid = n.id AND pe.status <> " + D.Enum(PushDeliveryStatusEnum.Error) + ")";
            string pushCase = "(CASE n.pushstate WHEN " + D.Enum(PushStateEnum.Sent) + " THEN (CASE WHEN " + allErrors + " THEN " + failed + " ELSE " + sent + " END) WHEN " +
                D.Enum(PushStateEnum.Failed) + " THEN " + failed + " WHEN " + D.Enum(PushStateEnum.Suppressed) + " THEN " + suppressed + " ELSE d.pushstatus END)";
            List<string> statements = new List<string>
            {
                "UPDATE broadcastdeliveries d SET pushstatus = " + pushCase + ", updatedutc = " + D.Ts(nowUtc) + " FROM notifications n WHERE d.propertyid = " + Pid + " AND n.propertyid = " + Pid +
                    " AND n.id = d.notificationid AND d.broadcastid = " + bid + " AND d.pushstatus IN (" + queued + ", " + sent + ") AND n.pushstate <> " + D.Enum(PushStateEnum.Pending) + " AND d.pushstatus <> " + pushCase + ";",
                "UPDATE broadcastdeliveries d SET emailstatus = CASE m.status WHEN " + D.Enum(EmailStatusEnum.Sent) + " THEN " + sent + " WHEN " + D.Enum(EmailStatusEnum.Delivered) + " THEN " + delivered + " WHEN " +
                    D.Enum(EmailStatusEnum.Failed) + " THEN " + failed + " WHEN " + D.Enum(EmailStatusEnum.Suppressed) + " THEN " + suppressed + " ELSE d.emailstatus END, updatedutc = " + D.Ts(nowUtc) +
                    " FROM emailmessages m WHERE d.propertyid = " + Pid + " AND m.propertyid = " + Pid + " AND m.id = d.emailmessageid AND d.broadcastid = " + bid + " AND d.emailstatus IN (" + queued + ", " + sent + ") AND m.status <> " +
                    D.Enum(EmailStatusEnum.Queued) + " AND d.emailstatus <> (CASE m.status WHEN " + D.Enum(EmailStatusEnum.Sent) + " THEN " + sent + " WHEN " + D.Enum(EmailStatusEnum.Delivered) + " THEN " + delivered + " WHEN " +
                    D.Enum(EmailStatusEnum.Failed) + " THEN " + failed + " ELSE " + suppressed + " END);",
                "UPDATE broadcasts SET targeted = a.targeted, statinappcreated = a.inapp, statpushsent = a.pushsent, statpushfailed = a.pushfailed, statemailqueued = a.emailqueued, statemailsent = a.emailsent, " +
                    "statemaildelivered = a.emaildelivered, statemailfailed = a.emailfailed, statemailsuppressed = a.emailsuppressed, statskippedpreferences = a.skipped FROM (SELECT COUNT(*) AS targeted, " +
                    "COUNT(*) FILTER (WHERE inappstatus = " + D.Enum(BroadcastDeliveryStatusEnum.Created) + ") AS inapp, COUNT(*) FILTER (WHERE pushstatus = " + sent + ") AS pushsent, " +
                    "COUNT(*) FILTER (WHERE pushstatus = " + failed + ") AS pushfailed, COUNT(*) FILTER (WHERE emailqueuedutc IS NOT NULL) AS emailqueued, " +
                    "COUNT(*) FILTER (WHERE emailstatus IN (" + sent + ", " + delivered + ")) AS emailsent, COUNT(*) FILTER (WHERE emailstatus = " + delivered + ") AS emaildelivered, " +
                    "COUNT(*) FILTER (WHERE emailstatus = " + failed + ") AS emailfailed, COUNT(*) FILTER (WHERE emailstatus = " + suppressed + ") AS emailsuppressed, " +
                    "COUNT(*) FILTER (WHERE pushstatus = " + D.Enum(BroadcastDeliveryStatusEnum.SkippedPreference) + " OR emailstatus = " + D.Enum(BroadcastDeliveryStatusEnum.SkippedPreference) + ") AS skipped " +
                    "FROM broadcastdeliveries WHERE propertyid = " + Pid + " AND broadcastid = " + bid + ") a WHERE " + Scope() + " AND id = " + bid +
                    " RETURNING broadcasts.targeted AS targeted, statinappcreated, statpushsent, statpushfailed, statemailqueued, statemailsent, statemaildelivered, statemailfailed, statemailsuppressed, statskippedpreferences;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? new BroadcastStats() : MapStats(table.Rows[0]);
        }

        public async Task<BroadcastAudienceCounts> CountAudienceAsync(BroadcastAudience audience, bool essential, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(audience);
            DataTable table = await QueryAsync("SELECT COUNT(*) AS cnt, COUNT(*) FILTER (WHERE " + PushOk(essential, nowUtc) + ") AS pushcnt, COUNT(*) FILTER (WHERE " + EmailOk(essential) + ") AS emailcnt FROM " +
                AudienceFrom() + Where(AudienceConditions(audience, nowUtc)) + ";", token).ConfigureAwait(false);
            DataRow row = table.Rows[0];
            return new BroadcastAudienceCounts { Count = RowReader.GetLong(row, "cnt"), Push = RowReader.GetLong(row, "pushcnt"), Email = RowReader.GetLong(row, "emailcnt") };
        }

        public async Task<List<string>> SampleAudienceAsync(BroadcastAudience audience, int limit, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(audience);
            DataTable table = await QueryAsync("SELECT u.id FROM " + AudienceFrom() + Where(AudienceConditions(audience, nowUtc)) + " ORDER BY u.id LIMIT " + D.Num(Math.Clamp(limit, 1, 100)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<List<BroadcastRecipient>> EnumerateRecipientsAsync(BroadcastAudience audience, string? afterUserId, int limit, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(audience);
            List<string> conditions = AudienceConditions(audience, nowUtc);
            if (!String.IsNullOrEmpty(afterUserId)) conditions.Add("u.id > " + D.Str(afterUserId));
            DataTable table = await QueryAsync("SELECT u.id, u.displayname, u.email, u.emailverified, COALESCE(ap.push, " + D.Bool(true) + ") AS prefpush, COALESCE(ap.email, " + D.Bool(true) + ") AS prefemail, " +
                PushTokenExists(nowUtc) + " AS hastoken, " + Suppressed() + " AS suppressed FROM " + AudienceFrom() + Where(conditions) + " ORDER BY u.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new BroadcastRecipient
            {
                UserId = RowReader.GetString(row, "id"),
                DisplayName = RowReader.GetString(row, "displayname"),
                Email = RowReader.GetNullableString(row, "email"),
                EmailVerified = RowReader.GetBool(row, "emailverified"),
                PushPreference = RowReader.GetBool(row, "prefpush"),
                EmailPreference = RowReader.GetBool(row, "prefemail"),
                HasPushToken = RowReader.GetBool(row, "hastoken"),
                EmailSuppressed = RowReader.GetBool(row, "suppressed")
            }).ToList();
        }

        public async Task<int> CreateDeliveriesAsync(List<BroadcastDelivery> deliveries, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(deliveries);
            if (deliveries.Count == 0) return 0;
            string values = String.Join(", ", deliveries.Select(d => "(" + Pid + ", " + D.Str(d.Id) + ", " + D.Str(d.BroadcastId) + ", " + D.Str(d.UserId) + ", " + D.Enum(d.InAppStatus) + ", " +
                D.Enum(d.PushStatus) + ", " + D.Enum(d.EmailStatus) + ", " + D.Str(d.NotificationId) + ", " + D.Str(d.EmailMessageId) + ", " + D.Ts(d.EmailQueuedUtc) + ", " + D.Ts(d.CreatedUtc) + ", " + D.Ts(d.UpdatedUtc) + ")"));
            DataTable table = await QueryAsync("INSERT INTO broadcastdeliveries (propertyid, " + _DeliveryColumns + ") VALUES " + values + " ON CONFLICT (propertyid, broadcastid, userid) DO NOTHING RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<List<BroadcastDelivery>> ListPendingAsync(string broadcastId, IEnumerable<string> userIds, CancellationToken token = default)
        {
            RequireText(broadcastId, nameof(broadcastId));
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<BroadcastDelivery>();
            string pending = D.Enum(BroadcastDeliveryStatusEnum.Pending);
            DataTable table = await QueryAsync("SELECT " + _DeliveryColumns + " FROM broadcastdeliveries WHERE " + Scope() + " AND broadcastid = " + D.Str(broadcastId) + " AND userid IN " + D.InList(ids) +
                " AND (inappstatus = " + pending + " OR pushstatus = " + pending + " OR emailstatus = " + pending + ") ORDER BY userid;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapDelivery).ToList();
        }

        public async Task UpdateNotificationStatusesAsync(List<BroadcastDelivery> deliveries, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(deliveries);
            if (deliveries.Count == 0) return;
            List<string> statements = deliveries.Select(d => "UPDATE broadcastdeliveries SET inappstatus = " + D.Enum(d.InAppStatus) + ", pushstatus = " + D.Enum(d.PushStatus) + ", notificationid = " +
                D.Str(d.NotificationId) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(d.Id) + ";").ToList();
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<bool> SettleEmailAsync(string deliveryId, BroadcastDeliveryStatusEnum status, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(deliveryId, nameof(deliveryId));
            DataTable table = await QueryAsync("UPDATE broadcastdeliveries SET emailstatus = " + D.Enum(status) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(deliveryId) +
                " AND emailstatus = " + D.Enum(BroadcastDeliveryStatusEnum.Pending) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> ClaimEmailAsync(string deliveryId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(deliveryId, nameof(deliveryId));
            DataTable table = await QueryAsync("UPDATE broadcastdeliveries SET emailstatus = " + D.Enum(BroadcastDeliveryStatusEnum.Queued) + ", emailqueuedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(deliveryId) + " AND emailstatus = " + D.Enum(BroadcastDeliveryStatusEnum.Pending) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task SetEmailMessageAsync(string deliveryId, string? emailMessageId, BroadcastDeliveryStatusEnum status, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(deliveryId, nameof(deliveryId));
            await QueryAsync("UPDATE broadcastdeliveries SET emailmessageid = " + D.Str(emailMessageId) + ", emailstatus = " + D.Enum(status) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " +
                D.Str(deliveryId) + ";", token).ConfigureAwait(false);
        }

        public Task<long> CountEmailsQueuedSinceAsync(DateTime sinceUtc, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM broadcastdeliveries WHERE " + Scope() + " AND emailqueuedutc >= " + D.Ts(sinceUtc) + ";", token);
        }

        public Task<EnumerationResult<BroadcastDelivery>> EnumerateDeliveriesAsync(EnumerationRequest request, string broadcastId, BroadcastChannelEnum? channel, BroadcastDeliveryStatusEnum? status, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(broadcastId, nameof(broadcastId));
            List<string> conditions = new List<string> { "d.broadcastid = " + D.Str(broadcastId) };
            if (status != null)
            {
                string s = D.Enum(status.Value);
                if (channel != null) conditions.Add("d." + StatusColumn(channel.Value) + " = " + s);
                else conditions.Add("(d.inappstatus = " + s + " OR d.pushstatus = " + s + " OR d.emailstatus = " + s + ")");
            }
            else if (channel != null) conditions.Add("d." + StatusColumn(channel.Value) + " <> " + D.Enum(BroadcastDeliveryStatusEnum.NotRequested));
            string select = String.Join(", ", _DeliveryColumns.Split(", ").Select(c => "d." + c));
            return EnumerateAsync(request, select, "broadcastdeliveries d", conditions, MapDelivery, token);
        }

        public async Task<BroadcastDelivery?> ReadDeliveryAsync(string broadcastId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(broadcastId) || String.IsNullOrEmpty(userId)) return null;
            DataTable table = await QueryAsync("SELECT " + _DeliveryColumns + " FROM broadcastdeliveries WHERE " + Scope() + " AND broadcastid = " + D.Str(broadcastId) + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapDelivery(table.Rows[0]);
        }

        public Task<EnumerationResult<Broadcast>> EnumerateReceivedAsync(EnumerationRequest request, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(userId, nameof(userId));
            List<string> conditions = new List<string>
            {
                Scope("d"),
                "d.userid = " + D.Str(userId),
                "b.istest = " + D.Bool(false),
                "b.startedutc IS NOT NULL"
            };
            string select = String.Join(", ", _Columns.Split(", ").Select(c => "b." + c));
            return EnumerateAsync(request, select, "broadcastdeliveries d JOIN broadcasts b ON b.propertyid = " + Pid + " AND b.id = d.broadcastid", conditions, Map, token);
        }

        public async Task<int> DeleteDeliveriesForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM broadcastdeliveries WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<Dictionary<string, string>> NotificationIdsAsync(string broadcastId, IEnumerable<string> userIds, CancellationToken token = default)
        {
            RequireText(broadcastId, nameof(broadcastId));
            ArgumentNullException.ThrowIfNull(userIds);
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT id, recipientuserid FROM notifications WHERE " + Scope() + " AND type = " + D.Enum(NotificationTypeEnum.Broadcast) + " AND targetid = " + D.Str(broadcastId) +
                " AND recipientuserid IN " + D.InList(ids) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                string user = RowReader.GetString(row, "recipientuserid");
                if (!result.ContainsKey(user)) result[user] = RowReader.GetString(row, "id");
            }
            return result;
        }

        public async Task<HashSet<string>> PreferenceExemptAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (list.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT id FROM broadcasts WHERE " + Scope() + " AND id IN " + D.InList(list) + " AND (category = " + D.Enum(BroadcastCategoryEnum.Essential) + " OR istest = " + D.Bool(true) + ");", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "id"));
            return result;
        }

        private string AudienceFrom()
        {
            return "users u LEFT JOIN notificationpreferences ap ON ap.propertyid = " + Pid + " AND ap.userid = u.id AND ap.category = " + D.Enum(NotificationCategoryEnum.Announcements);
        }

        private string PushTokenExists(DateTime nowUtc)
        {
            return "EXISTS (SELECT 1 FROM pushtokens pt JOIN authsessions ps ON ps.propertyid = " + Pid + " AND ps.id = pt.sessionid WHERE pt.propertyid = " + Pid +
                " AND pt.userid = u.id AND pt.disabledutc IS NULL AND ps.revokedutc IS NULL AND ps.expiresutc > " + D.Ts(nowUtc) + ")";
        }

        private string Suppressed()
        {
            return "EXISTS (SELECT 1 FROM emailsuppressions es WHERE es.propertyid = " + Pid + " AND es.emailnormalized = lower(trim(u.email)))";
        }

        private string Verified()
        {
            return "(u.emailverified = " + D.Bool(true) + " AND u.email IS NOT NULL AND u.email <> " + D.Str(String.Empty) + ")";
        }

        private string PushOk(bool essential, DateTime nowUtc)
        {
            return PushTokenExists(nowUtc) + (essential ? String.Empty : " AND COALESCE(ap.push, " + D.Bool(true) + ")");
        }

        private string EmailOk(bool essential)
        {
            return Verified() + (essential ? String.Empty : " AND COALESCE(ap.email, " + D.Bool(true) + ")") + " AND NOT " + Suppressed();
        }

        private List<string> AudienceConditions(BroadcastAudience a, DateTime nowUtc)
        {
            List<string> c = new List<string>
            {
                Scope("u"),
                "u.isdeleted = " + D.Bool(false),
                "u.isbanned = " + D.Bool(false),
                "u.ageattestedutc IS NOT NULL"
            };
            if (a.MatchesNothing) c.Add("1 = 0");
            if (a.UserIds.Count > 0) c.Add("u.id IN " + D.InList(a.UserIds));
            if (a.RequireMembershipOrStaff)
                c.Add("(" + ActiveEntitlementExists("u.id", nowUtc, null) + " OR EXISTS (SELECT 1 FROM roleassignments sr WHERE " + Scope("sr") + " AND sr.userid = u.id AND sr.role <> " + D.Enum(RoleEnum.User) + "))");
            if (a.MembershipActive == true) c.Add(ActiveEntitlementExists("u.id", nowUtc, null));
            if (a.MembershipActive == false) c.Add("NOT " + ActiveEntitlementExists("u.id", nowUtc, null));
            foreach (List<string> group in a.MembershipSourceGroups.Where(g => g.Count > 0))
                c.Add(ActiveEntitlementExists("u.id", nowUtc, "source IN " + D.InList(group)));
            foreach (List<string> group in a.StoreGroups.Where(g => g.Count > 0))
                c.Add("EXISTS (SELECT 1 FROM entitlements se JOIN subscriptions ss ON ss.propertyid = " + Pid + " AND ss.id = se.sourceid WHERE se.propertyid = " + Pid + " AND se.userid = u.id AND se.source = " +
                    D.Enum(MembershipSourceEnum.Subscription) + " AND se.revokedutc IS NULL AND se.startsutc <= " + D.Ts(nowUtc) + " AND (se.endsutc IS NULL OR se.endsutc > " + D.Ts(nowUtc) + ") AND ss.store IN " + D.InList(group) + ")");
            foreach (ProfileFilterClause clause in a.Profile) c.Add(ProfileFilterSql.Condition(D, Pid, "u.id", clause));
            string user = D.Enum(TargetTypeEnum.User);
            foreach (List<string> group in a.LabelAnyGroups.Where(g => g.Count > 0))
                c.Add("EXISTS (SELECT 1 FROM labels ul WHERE " + Scope("ul") + " AND ul.targettype = " + user + " AND ul.targetid = u.id AND ul.labelnormalized IN " + D.InList(group) + ")");
            foreach (string label in a.LabelsAll)
                c.Add("EXISTS (SELECT 1 FROM labels ul WHERE " + Scope("ul") + " AND ul.targettype = " + user + " AND ul.targetid = u.id AND ul.labelnormalized = " + D.Str(label) + ")");
            foreach (BroadcastTagFilter tag in a.Tags)
                c.Add("EXISTS (SELECT 1 FROM tags ut WHERE " + Scope("ut") + " AND ut.targettype = " + user + " AND ut.targetid = u.id AND ut.tagkeynormalized = " + D.Str(tag.Key) +
                    (tag.Value == null ? String.Empty : " AND ut.tagvalue = " + D.Str(tag.Value)) + ")");
            if (a.SignedUpAfterUtc != null) c.Add("u.createdutc >= " + D.Ts(a.SignedUpAfterUtc.Value));
            if (a.SignedUpBeforeUtc != null) c.Add("u.createdutc < " + D.Ts(a.SignedUpBeforeUtc.Value));
            if (a.ActiveWithinDays != null) c.Add("u.lastseenutc >= " + D.Ts(nowUtc.AddDays(-a.ActiveWithinDays.Value)));
            if (a.InactiveForDays != null) c.Add("(u.lastseenutc IS NULL OR u.lastseenutc < " + D.Ts(nowUtc.AddDays(-a.InactiveForDays.Value)) + ")");
            if (a.HasPushToken == true) c.Add(PushTokenExists(nowUtc));
            if (a.HasPushToken == false) c.Add("NOT " + PushTokenExists(nowUtc));
            if (a.HasVerifiedEmail == true) c.Add(Verified());
            if (a.HasVerifiedEmail == false) c.Add("NOT " + Verified());
            foreach (List<string> group in a.RoleGroups.Where(g => g.Count > 0))
                c.Add("EXISTS (SELECT 1 FROM roleassignments rr WHERE " + Scope("rr") + " AND rr.userid = u.id AND rr.role IN " + D.InList(group) + ")");
            foreach (BroadcastEventRsvpFilter rsvp in a.EventRsvps)
                c.Add("EXISTS (SELECT 1 FROM eventrsvps er WHERE er.propertyid = " + Pid + " AND er.userid = u.id AND er.eventid = " + D.Str(rsvp.EventId) +
                    (rsvp.Status == null ? String.Empty : " AND er.status = " + D.Str(rsvp.Status)) + ")");
            foreach (string series in a.SeriesIds)
                c.Add("EXISTS (SELECT 1 FROM seriesfollows sf WHERE sf.propertyid = " + Pid + " AND sf.userid = u.id AND sf.seriesid = " + D.Str(series) + " AND sf.isoptedout = " + D.Bool(false) + ")");
            return c;
        }

        private string? InsertFilters(string ownerColumn, string ownerId, List<BroadcastSegmentFilter> filters, string? guard = null)
        {
            if (filters == null || filters.Count == 0) return null;
            string segment = ownerColumn == "segmentid" ? D.Str(ownerId) : "NULL::text";
            string broadcast = ownerColumn == "broadcastid" ? D.Str(ownerId) : "NULL::text";
            string rows = String.Join(", ", filters.Select(f => "(" + Pid + ", " + D.Str(f.Id) + ", " + segment + ", " + broadcast + ", " +
                D.Str(f.Field) + ", " + D.Str(f.Operator) + ", " + D.Str(f.Value) + ", " + D.Str(f.Kind) + ", " + D.Num(f.Position) + ")"));
            if (guard == null) return "INSERT INTO broadcastsegmentfilters (propertyid, " + _FilterColumns + ") VALUES " + rows + ";";
            return "INSERT INTO broadcastsegmentfilters (propertyid, " + _FilterColumns + ") SELECT v.* FROM (VALUES " + rows + ") AS v(propertyid, " + _FilterColumns + ") WHERE " + guard + ";";
        }

        private async Task<Dictionary<string, List<BroadcastSegmentFilter>>> FiltersAsync(string ownerColumn, IEnumerable<string> ownerIds, CancellationToken token)
        {
            Dictionary<string, List<BroadcastSegmentFilter>> result = new Dictionary<string, List<BroadcastSegmentFilter>>(StringComparer.Ordinal);
            List<string> ids = ownerIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT " + _FilterColumns + " FROM broadcastsegmentfilters WHERE " + Scope() + " AND " + ownerColumn + " IN " + D.InList(ids) + " ORDER BY position, id;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                string owner = RowReader.GetString(row, ownerColumn);
                if (!result.TryGetValue(owner, out List<BroadcastSegmentFilter>? list))
                {
                    list = new List<BroadcastSegmentFilter>();
                    result[owner] = list;
                }
                list.Add(new BroadcastSegmentFilter
                {
                    Id = RowReader.GetString(row, "id"),
                    Field = RowReader.GetString(row, "field"),
                    Operator = RowReader.GetString(row, "operator"),
                    Value = RowReader.GetString(row, "value"),
                    Kind = RowReader.GetString(row, "valuekind"),
                    Position = RowReader.GetInt(row, "position")
                });
            }
            return result;
        }

        private static string ChannelColumn(BroadcastChannelEnum channel)
        {
            switch (channel)
            {
                case BroadcastChannelEnum.Push:
                    return "push";
                case BroadcastChannelEnum.Email:
                    return "email";
                default:
                    return "inapp";
            }
        }

        private static string StatusColumn(BroadcastChannelEnum channel)
        {
            return ChannelColumn(channel) + "status";
        }

        private string Values(Broadcast b)
        {
            BroadcastStats s = b.Stats;
            return String.Join(", ", new string[]
            {
                D.Str(b.Id), D.Str(b.Title), D.Bool(b.InApp), D.Bool(b.Push), D.Bool(b.Email), D.Str(b.InAppTitle), D.Str(b.InAppBody), D.Str(b.PushTitle), D.Str(b.PushBody),
                D.Str(b.EmailSubject), D.Str(b.EmailPreheader), D.Str(b.EmailBody), D.Str(b.CtaLabel), D.Str(b.CtaPath), D.Str(b.SegmentId), D.Enum(b.Category), D.Str(b.EssentialReason),
                D.Enum(b.Status), D.Bool(b.IsTest), D.Ts(b.SendAtUtc), D.Ts(b.StartedUtc), D.Ts(b.SentUtc), D.Ts(b.CanceledUtc), D.Num(s.Targeted), D.Str(b.CursorUserId),
                D.Num(s.InAppCreated), D.Num(s.PushSent), D.Num(s.PushFailed), D.Num(s.EmailQueued), D.Num(s.EmailSent), D.Num(s.EmailDelivered), D.Num(s.EmailFailed), D.Num(s.EmailSuppressed),
                D.Num(s.SkippedPreferences), D.Str(b.CreatedByUserId), D.Ts(b.CreatedUtc), D.Ts(b.UpdatedUtc)
            });
        }

        private static Broadcast Map(DataRow row)
        {
            return new Broadcast
            {
                Id = RowReader.GetString(row, "id"),
                Title = RowReader.GetString(row, "title"),
                InApp = RowReader.GetBool(row, "inapp"),
                Push = RowReader.GetBool(row, "push"),
                Email = RowReader.GetBool(row, "email"),
                InAppTitle = RowReader.GetNullableString(row, "inapptitle"),
                InAppBody = RowReader.GetNullableString(row, "inappbody"),
                PushTitle = RowReader.GetNullableString(row, "pushtitle"),
                PushBody = RowReader.GetNullableString(row, "pushbody"),
                EmailSubject = RowReader.GetNullableString(row, "emailsubject"),
                EmailPreheader = RowReader.GetNullableString(row, "emailpreheader"),
                EmailBody = RowReader.GetNullableString(row, "emailbody"),
                CtaLabel = RowReader.GetNullableString(row, "ctalabel"),
                CtaPath = RowReader.GetNullableString(row, "ctapath"),
                SegmentId = RowReader.GetNullableString(row, "segmentid"),
                Category = RowReader.GetEnum(row, "category", BroadcastCategoryEnum.Optional),
                EssentialReason = RowReader.GetNullableString(row, "essentialreason"),
                Status = RowReader.GetEnum(row, "status", BroadcastStatusEnum.Draft),
                IsTest = RowReader.GetBool(row, "istest"),
                SendAtUtc = RowReader.GetNullableDateTime(row, "sendatutc"),
                StartedUtc = RowReader.GetNullableDateTime(row, "startedutc"),
                SentUtc = RowReader.GetNullableDateTime(row, "sentutc"),
                CanceledUtc = RowReader.GetNullableDateTime(row, "canceledutc"),
                CursorUserId = RowReader.GetNullableString(row, "cursoruserid"),
                Stats = MapStats(row),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static BroadcastStats MapStats(DataRow row)
        {
            return new BroadcastStats
            {
                Targeted = RowReader.GetInt(row, "targeted"),
                InAppCreated = RowReader.GetInt(row, "statinappcreated"),
                PushSent = RowReader.GetInt(row, "statpushsent"),
                PushFailed = RowReader.GetInt(row, "statpushfailed"),
                EmailQueued = RowReader.GetInt(row, "statemailqueued"),
                EmailSent = RowReader.GetInt(row, "statemailsent"),
                EmailDelivered = RowReader.GetInt(row, "statemaildelivered"),
                EmailFailed = RowReader.GetInt(row, "statemailfailed"),
                EmailSuppressed = RowReader.GetInt(row, "statemailsuppressed"),
                SkippedPreferences = RowReader.GetInt(row, "statskippedpreferences")
            };
        }

        private static BroadcastSegment MapSegment(DataRow row)
        {
            return new BroadcastSegment
            {
                Id = RowReader.GetString(row, "id"),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetNullableString(row, "description"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static BroadcastDelivery MapDelivery(DataRow row)
        {
            return new BroadcastDelivery
            {
                Id = RowReader.GetString(row, "id"),
                BroadcastId = RowReader.GetString(row, "broadcastid"),
                UserId = RowReader.GetString(row, "userid"),
                InAppStatus = RowReader.GetEnum(row, "inappstatus", BroadcastDeliveryStatusEnum.NotRequested),
                PushStatus = RowReader.GetEnum(row, "pushstatus", BroadcastDeliveryStatusEnum.NotRequested),
                EmailStatus = RowReader.GetEnum(row, "emailstatus", BroadcastDeliveryStatusEnum.NotRequested),
                NotificationId = RowReader.GetNullableString(row, "notificationid"),
                EmailMessageId = RowReader.GetNullableString(row, "emailmessageid"),
                EmailQueuedUtc = RowReader.GetNullableDateTime(row, "emailqueuedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
