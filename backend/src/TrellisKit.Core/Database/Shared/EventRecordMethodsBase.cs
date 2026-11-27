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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class EventRecordMethodsBase : MethodsBase, IEventRecordMethods
    {
        private const string _Columns = "id, slug, title, summary, description, categorykey, format, status, startutc, endutc, timezone, isallday, recurrenceid, originalstartutc, isexception, " +
            "occurrenceindex, venueid, locationname, line1, line2, locality, region, postalcode, countrycode, addressvisibility, lat, lng, onlineplatformlabel, onlinejoinurl, capacity, " +
            "maxguestsperrsvp, waitlistenabled, rsvpaudience, rsvpopensutc, rsvpclosesutc, attendeevisibility, externalurl, covermediaid, commentsenabled, goingcount, guestcount, " +
            "interestedcount, waitlistcount, checkedincount, commentcount, reactiontotal, openreportcount, icssequence, audience, moderationstate, moderatedutc, moderatedbyuserid, " +
            "moderationreason, canceledutc, cancelreason, canceledbyuserid, proposedbyuserid, proposalnote, reviewnote, revieweruserid, reviewedutc, promotedfeedpostid, publishedutc, " +
            "createdbyuserid, deletedutc, createdutc, updatedutc";

        internal const string NotTemplate = "e.status <> 'Template'";

        protected EventRecordMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateManyAsync(List<EventRecord> events, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(events);
            if (events.Count == 0) return;
            List<string> statements = new List<string>();
            foreach (EventRecord e in events)
            {
                RequireText(e.Id, nameof(e.Id));
                statements.Add("INSERT INTO events (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + Values(e) + ");");
                statements.AddRange(ChildStatements(e, false, nowUtc));
            }
            await UniqueTransactionAsync(statements, "Event", token).ConfigureAwait(false);
        }

        public async Task<EventRecord?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            List<EventRecord> rows = await LoadAsync("e.id = " + D.Str(id.Trim()), token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        public async Task<EventRecord?> ReadBySlugAsync(string slug, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return null;
            string s = D.Str(slug.Trim().ToLowerInvariant());
            List<EventRecord> rows = await LoadAsync("lower(e.slug) = " + s, token).ConfigureAwait(false);
            if (rows.Count > 0) return rows[0];
            // A previous slug (docs/PHASE4_CONTRACT.md section 18.1) finds the event; the payload carries the current slug.
            rows = await LoadAsync("e.id = (SELECT r.eventid FROM eventslugs r WHERE " + Scope("r") + " AND r.slug = " + s + " LIMIT 1)", token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        public async Task<List<EventRecord>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<EventRecord>();
            return await LoadAsync("e.id IN " + D.InList(list), token).ConfigureAwait(false);
        }

        public async Task<HashSet<string>> TakenSlugsAsync(IEnumerable<string> slugs, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(slugs);
            List<string> list = slugs.Where(s => !String.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
            HashSet<string> taken = new HashSet<string>(StringComparer.Ordinal);
            if (list.Count == 0) return taken;
            DataTable table = await QueryAsync("SELECT lower(slug) AS s FROM events WHERE " + Scope() + " AND deletedutc IS NULL AND lower(slug) IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) taken.Add(RowReader.GetString(row, "s"));
            return taken;
        }

        public async Task<bool> UpdateAsync(EventRecord e, bool replaceChildren, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(e);
            RequireText(e.Id, nameof(e.Id));
            string slug = D.Str((e.Slug ?? String.Empty).ToLowerInvariant());
            List<string> statements = new List<string>
            {
                // A changed slug keeps the previous one as a redirect (docs/PHASE4_CONTRACT.md section 18.1); a slug taken
                // over from another event's redirect stops redirecting.
                "INSERT INTO eventslugs (propertyid, slug, eventid, createdutc) SELECT " + Pid + ", lower(x.slug), x.id, " + D.Ts(nowUtc) + " FROM events x WHERE " + Scope("x") + " AND x.id = " + D.Str(e.Id) +
                    " AND x.deletedutc IS NULL AND x.status <> 'Template' AND lower(x.slug) <> " + slug + " ON CONFLICT (propertyid, slug) DO UPDATE SET eventid = EXCLUDED.eventid, createdutc = EXCLUDED.createdutc;",
                "DELETE FROM eventslugs WHERE " + Scope() + " AND slug = " + slug + ";",
                "UPDATE events SET slug = " + D.Str(e.Slug) + ", title = " + D.Str(e.Title) + ", summary = " + D.Str(e.Summary) + ", description = " + D.Str(e.Description) +
                    ", categorykey = " + D.Str(e.CategoryKey) + ", format = " + D.Enum(e.Format) + ", status = " + D.Enum(e.Status) + ", startutc = " + D.Ts(e.StartUtc) + ", endutc = " + D.Ts(e.EndUtc) +
                    ", timezone = " + D.Str(e.TimeZone) + ", isallday = " + D.Bool(e.IsAllDay) + ", recurrenceid = " + D.Str(e.RecurrenceId) + ", originalstartutc = " + D.Ts(e.OriginalStartUtc) +
                    ", isexception = " + D.Bool(e.IsException) + ", occurrenceindex = " + D.Num(e.OccurrenceIndex) + ", venueid = " + D.Str(e.VenueId) + ", locationname = " + D.Str(e.LocationName) +
                    ", line1 = " + D.Str(e.Line1) + ", line2 = " + D.Str(e.Line2) + ", locality = " + D.Str(e.Locality) + ", region = " + D.Str(e.Region) + ", postalcode = " + D.Str(e.PostalCode) +
                    ", countrycode = " + D.Str(e.CountryCode) + ", addressvisibility = " + D.Str(e.AddressVisibility) + ", lat = " + Dbl(e.Lat) + ", lng = " + Dbl(e.Lng) +
                    ", onlineplatformlabel = " + D.Str(e.OnlinePlatformLabel) + ", onlinejoinurl = " + D.Str(e.OnlineJoinUrl) + ", capacity = " + D.Num(e.Capacity) +
                    ", maxguestsperrsvp = " + D.Num(e.MaxGuestsPerRsvp) + ", waitlistenabled = " + D.Bool(e.WaitlistEnabled) + ", rsvpaudience = " + D.Enum(e.RsvpAudience) +
                    ", rsvpopensutc = " + D.Ts(e.RsvpOpensUtc) + ", rsvpclosesutc = " + D.Ts(e.RsvpClosesUtc) + ", attendeevisibility = " + D.Enum(e.AttendeeVisibility) +
                    ", externalurl = " + D.Str(e.ExternalUrl) + ", covermediaid = " + D.Str(e.CoverMediaId) + ", commentsenabled = " + D.Bool(e.CommentsEnabled) + ", icssequence = " + D.Num(e.IcsSequence) +
                    ", audience = " + D.Enum(e.Audience) + ", moderationstate = " + D.Enum(e.ModerationState) + ", moderatedutc = " + D.Ts(e.ModeratedUtc) + ", moderatedbyuserid = " + D.Str(e.ModeratedByUserId) +
                    ", moderationreason = " + D.Str(e.ModerationReason) + ", canceledutc = " + D.Ts(e.CanceledUtc) + ", cancelreason = " + D.Str(e.CancelReason) + ", canceledbyuserid = " + D.Str(e.CanceledByUserId) +
                    ", proposedbyuserid = " + D.Str(e.ProposedByUserId) + ", proposalnote = " + D.Str(e.ProposalNote) + ", reviewnote = " + D.Str(e.ReviewNote) + ", revieweruserid = " + D.Str(e.ReviewerUserId) +
                    ", reviewedutc = " + D.Ts(e.ReviewedUtc) + ", promotedfeedpostid = " + D.Str(e.PromotedFeedPostId) + ", publishedutc = " + D.Ts(e.PublishedUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                    " WHERE " + Scope() + " AND id = " + D.Str(e.Id) + " AND deletedutc IS NULL;"
            };
            if (replaceChildren) statements.AddRange(ChildStatements(e, true, nowUtc));
            else if (e.Status != EventStatusEnum.Template) statements.AddRange(RescheduleStatements(e, nowUtc));
            statements.Add("SELECT id FROM events WHERE " + Scope() + " AND id = " + D.Str(e.Id) + " AND deletedutc IS NULL;");
            DataTable table = await UniqueTransactionAsync(statements, "Event", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> DeleteAsync(IEnumerable<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return 0;
            List<string> statements = new List<string>
            {
                "DELETE FROM eventreminders WHERE " + Scope() + " AND eventid IN " + D.InList(list) + " AND sentutc IS NULL;",
                "UPDATE events SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN " + D.InList(list) + " AND deletedutc IS NULL RETURNING id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<EnumerationResult<EventRecord>> EnumerateAsync(EventListFilter filter, EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = Conditions(filter);
            if (filter.ByDistance && filter.NearLat != null && filter.NearLng != null)
            {
                request.Ordering = new EnumerationOrdering
                {
                    Name = request.Ordering.Name,
                    Expression = "CAST(ROUND(" + Distance(filter.NearLat.Value, filter.NearLng.Value) + " * 1000) AS BIGINT)",
                    Descending = false,
                    KeyKind = EnumerationKeyKindEnum.Number
                };
            }
            EnumerationResult<EventRecord> page = await EnumerateAsync(request, "e.*", "events e", conditions, Map, token).ConfigureAwait(false);
            await FillChildrenAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public Task<long> CountMapAsync(EventListFilter filter, EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(request);
            return CountAsync("SELECT COUNT(*) AS cnt FROM " + MapFrom() + " WHERE " + MapConditions(filter, request) + ";", token);
        }

        public async Task<List<EventMapPoint>> MapPointsAsync(EventListFilter filter, EnumerationRequest request, int limit, IEnumerable<string>? ids = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(request);
            string where = MapConditions(filter, request);
            if (ids != null)
            {
                List<string> list = ids.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
                if (list.Count == 0) return new List<EventMapPoint>();
                where += " AND e.id IN " + D.InList(list);
            }
            DataTable table = await QueryAsync("SELECT e.*, " + MapVisibility(filter) + " AS mapvisibility FROM " + MapFrom() + " WHERE " + where + " ORDER BY e.startutc, e.id LIMIT " +
                D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            List<EventMapPoint> points = table.Rows.Cast<DataRow>().Select(r => new EventMapPoint { Event = Map(r), Visibility = RowReader.GetString(r, "mapvisibility") }).ToList();
            await FillChildrenAsync(points.Select(p => p.Event).ToList(), token).ConfigureAwait(false);
            return points;
        }

        public async Task<List<MapCluster>> MapClustersAsync(EventListFilter filter, EnumerationRequest request, double cellDegrees, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(request);
            string approximate = MapVisibility(filter) + " = 'Approximate'";
            DataTable table = await QueryAsync(DirectorySql.ClusterQuery(D, DirectorySql.MapCoordinate(approximate, "e.lat"), DirectorySql.MapCoordinate(approximate, "e.lng"), "e.lat", "e.lng", "e.id",
                MapFrom(), MapConditions(filter, request), cellDegrees, limit), token).ConfigureAwait(false);
            return DirectorySql.Clusters(table);
        }

        public async Task<List<EventRecord>> ListOccurrencesAsync(string recurrenceId, int limit, CancellationToken token = default)
        {
            RequireText(recurrenceId, nameof(recurrenceId));
            DataTable table = await QueryAsync("SELECT e.* FROM events e WHERE " + Scope("e") + " AND e.recurrenceid = " + D.Str(recurrenceId) + " AND e.deletedutc IS NULL AND " + NotTemplate +
                " ORDER BY e.startutc, e.id LIMIT " + D.Num(Math.Clamp(limit, 1, 5000)) + ";", token).ConfigureAwait(false);
            List<EventRecord> rows = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillChildrenAsync(rows, token).ConfigureAwait(false);
            return rows;
        }

        public async Task<bool> IsHostAsync(string eventId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(eventId) || String.IsNullOrEmpty(userId)) return false;
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM eventhosts WHERE " + Scope() + " AND eventid = " + D.Str(eventId) + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<int> RemoveHostEverywhereAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM eventhosts WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING eventid;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<Dictionary<string, long>> ProposalStatusCountsSinceAsync(string userId, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT status, COUNT(*) AS cnt FROM events WHERE " + Scope() + " AND proposedbyuserid = " + D.Str(userId) +
                " AND createdutc >= " + D.Ts(sinceUtc) + " GROUP BY status ORDER BY status LIMIT 20;", token).ConfigureAwait(false);
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows) counts[RowReader.GetString(row, "status")] = Convert.ToInt64(row["cnt"], CultureInfo.InvariantCulture);
            return counts;
        }

        public Task<long> CountPendingProposalsAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM events WHERE " + Scope() + " AND proposedbyuserid = " + D.Str(userId) + " AND status = 'Proposed' AND deletedutc IS NULL;", token);
        }

        public Task<long> CountPublicWithCoverAsync(string mediaId, CancellationToken token = default)
        {
            RequireText(mediaId, nameof(mediaId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM events WHERE " + Scope() + " AND covermediaid = " + D.Str(mediaId) + " AND audience = " + D.Enum(ContentAudienceEnum.Public) +
                " AND moderationstate = " + D.Enum(ModerationStateEnum.None) + " AND status IN (" + D.Enum(EventStatusEnum.Published) + ", " + D.Enum(EventStatusEnum.Canceled) + ") AND deletedutc IS NULL;", token);
        }

        public async Task RecomputeCountersAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return;
            await QueryAsync(CounterStatement("id IN " + D.InList(list)), token).ConfigureAwait(false);
        }

        public async Task SetCounterAsync(string id, string column, int value, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            if (column != "commentcount" && column != "reactiontotal" && column != "openreportcount") throw new ArgumentException("Unknown counter " + column + ".", nameof(column));
            await QueryAsync("UPDATE events SET " + column + " = " + D.Num(Math.Max(0, value)) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task RecountEngagementAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE events SET commentcount = (SELECT COUNT(*) FROM comments c WHERE " + Scope("c") + " AND c.targettype = 'Event' AND c.targetid = events.id AND c.deletedutc IS NULL AND c.moderationstate = 'None'), " +
                "reactiontotal = (SELECT COUNT(*) FROM reactions x WHERE " + Scope("x") + " AND x.targettype = 'Event' AND x.targetid = events.id), " +
                "openreportcount = (SELECT COUNT(*) FROM reports p WHERE " + Scope("p") + " AND p.targettype = 'Event' AND p.targetid = events.id AND p.status = 'Open') WHERE " + Scope() +
                " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<EventRecord>> ListFeedEventsAsync(string userId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT e.* FROM events e JOIN eventrsvps r ON " + Scope("r") + " AND r.eventid = e.id AND r.userid = " + D.Str(userId) +
                " AND r.status IN ('Going', 'Waitlisted') WHERE " + Scope("e") + " AND e.deletedutc IS NULL AND e.status IN ('Published', 'Canceled') AND e.startutc >= " + D.Ts(fromUtc) +
                " AND e.startutc < " + D.Ts(toUtc) + " ORDER BY e.startutc, e.id LIMIT " + D.Num(Math.Clamp(limit, 1, 5000)) + ";", token).ConfigureAwait(false);
            List<EventRecord> rows = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillChildrenAsync(rows, token).ConfigureAwait(false);
            return rows;
        }

        public async Task<List<string>> ChangedIdsAsync(DateTime? sinceUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT id FROM events WHERE " + Scope() + " AND status <> 'Template'" + (sinceUtc == null ? String.Empty : " AND updatedutc >= " + D.Ts(sinceUtc.Value)) +
                " ORDER BY updatedutc, id LIMIT " + D.Num(Math.Clamp(limit, 1, 10000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<List<EventDayCount>> EventsByDayAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken token = default)
        {
            string day = D.DayOf("e.startutc");
            DataTable table = await QueryAsync("SELECT " + day + " AS d, COUNT(*) AS cnt FROM events e WHERE " + Scope("e") + " AND e.deletedutc IS NULL AND e.status IN ('Published', 'Canceled') AND e.startutc >= " +
                D.Ts(sinceUtc) + " AND e.startutc < " + D.Ts(untilUtc) + " GROUP BY " + day + " ORDER BY 1;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new EventDayCount { Day = RowReader.GetString(r, "d"), Count = RowReader.GetLong(r, "cnt") }).ToList();
        }

        public async Task<List<EventDayCount>> RsvpsByDayAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken token = default)
        {
            string day = D.DayOf("r.createdutc");
            DataTable table = await QueryAsync("SELECT " + day + " AS d, COUNT(*) AS cnt FROM eventrsvps r WHERE " + Scope("r") + " AND r.createdutc >= " + D.Ts(sinceUtc) + " AND r.createdutc < " + D.Ts(untilUtc) +
                " GROUP BY " + day + " ORDER BY 1;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new EventDayCount { Day = RowReader.GetString(r, "d"), Count = RowReader.GetLong(r, "cnt") }).ToList();
        }

        public async Task<long[]> CheckInTotalsAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT COALESCE(SUM(e.goingcount), 0) AS going, COALESCE(SUM(e.checkedincount), 0) AS checkedin FROM events e WHERE " + Scope("e") +
                " AND e.deletedutc IS NULL AND e.status = 'Published' AND e.endutc >= " + D.Ts(sinceUtc) + " AND e.endutc < " + D.Ts(untilUtc) + " AND e.checkedincount > 0;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return new long[] { 0, 0 };
            return new long[] { RowReader.GetLong(table.Rows[0], "going"), RowReader.GetLong(table.Rows[0], "checkedin") };
        }

        public async Task<List<EventCategoryCount>> CategoryCountsAsync(DateTime sinceUtc, DateTime untilUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT COALESCE(e.categorykey, '') AS k, COUNT(*) AS events, COALESCE(SUM(e.goingcount), 0) AS rsvps FROM events e WHERE " + Scope("e") +
                " AND e.deletedutc IS NULL AND e.status IN ('Published', 'Canceled') AND e.startutc >= " + D.Ts(sinceUtc) + " AND e.startutc < " + D.Ts(untilUtc) +
                " GROUP BY COALESCE(e.categorykey, '') ORDER BY 2 DESC, 1 LIMIT " + D.Num(Math.Clamp(limit, 1, 500)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new EventCategoryCount { Key = RowReader.GetString(r, "k"), Events = RowReader.GetLong(r, "events"), Rsvps = RowReader.GetLong(r, "rsvps") }).ToList();
        }

        public async Task<List<EventRsvpDigestRow>> RsvpDigestAsync(DateTime sinceUtc, DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT e.id, e.title, e.goingcount, e.waitlistcount, (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = e.id AND c.updatedutc >= " +
                D.Ts(sinceUtc) + ") AS changes FROM events e WHERE " + Scope("e") + " AND e.deletedutc IS NULL AND e.status = 'Published' AND e.startutc > " + D.Ts(nowUtc) +
                " AND EXISTS (SELECT 1 FROM eventrsvps x WHERE " + Scope("x") + " AND x.eventid = e.id AND x.updatedutc >= " + D.Ts(sinceUtc) + ") ORDER BY e.startutc, e.id LIMIT " +
                D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new EventRsvpDigestRow
            {
                EventId = RowReader.GetString(r, "id"),
                Title = RowReader.GetString(r, "title"),
                Going = RowReader.GetInt(r, "goingcount"),
                Waitlisted = RowReader.GetInt(r, "waitlistcount"),
                Changes = RowReader.GetLong(r, "changes")
            }).ToList();
        }

        public async Task<HashSet<string>> RefreshUnreadNoticesAsync(NotificationTypeEnum type, string eventId, IEnumerable<string> recipientUserIds, string title, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            ArgumentNullException.ThrowIfNull(recipientUserIds);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<string> ids = recipientUserIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("UPDATE notifications SET title = " + D.Str(title) + ", updatedutc = " + D.Ts(nowUtc) + ", createdutc = " + D.Ts(nowUtc) + ", pushstate = 'Pending' WHERE " + Scope() +
                " AND type = " + D.Enum(type) + " AND targettype = 'Event' AND targetid = " + D.Str(eventId) + " AND readutc IS NULL AND recipientuserid IN " + D.InList(ids) + " RETURNING recipientuserid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "recipientuserid"));
            return result;
        }

        public async Task<EventVenueSnapshot?> ReadVenueAsync(string venueId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(venueId)) return null;
            Dictionary<string, EventVenueSnapshot> venues = await ReadVenuesAsync(new[] { venueId.Trim() }, token).ConfigureAwait(false);
            return venues.TryGetValue(venueId.Trim(), out EventVenueSnapshot? venue) ? venue : null;
        }

        public async Task<Dictionary<string, EventVenueSnapshot>> ReadVenuesAsync(IEnumerable<string> venueIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(venueIds);
            Dictionary<string, EventVenueSnapshot> result = new Dictionary<string, EventVenueSnapshot>(StringComparer.Ordinal);
            List<string> ids = venueIds.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT v.id, v.slug, v.name, v.line1, v.line2, v.locality, v.region, v.postalcode, v.countrycode, v.addressvisibility, v.lat, v.lng, v.timezone, v.audience, " +
                "(v.status = 'Active' AND v.deletedutc IS NULL AND v.moderationstate = 'None') AS listed FROM venues v WHERE " + Scope("v") + " AND v.id IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                EventVenueSnapshot venue = new EventVenueSnapshot
                {
                    Id = RowReader.GetString(row, "id"),
                    Slug = RowReader.GetString(row, "slug"),
                    Name = RowReader.GetString(row, "name"),
                    Line1 = RowReader.GetNullableString(row, "line1"),
                    Line2 = RowReader.GetNullableString(row, "line2"),
                    Locality = RowReader.GetNullableString(row, "locality"),
                    Region = RowReader.GetNullableString(row, "region"),
                    PostalCode = RowReader.GetNullableString(row, "postalcode"),
                    CountryCode = RowReader.GetNullableString(row, "countrycode"),
                    AddressVisibility = RowReader.GetNullableString(row, "addressvisibility") ?? "Exact",
                    Lat = NullableDouble(row, "lat"),
                    Lng = NullableDouble(row, "lng"),
                    TimeZone = RowReader.GetNullableString(row, "timezone"),
                    Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                    IsListed = RowReader.GetBool(row, "listed")
                };
                result[venue.Id] = venue;
            }
            return result;
        }

        public async Task<Dictionary<string, EventInstructorSnapshot>> ReadInstructorsAsync(IEnumerable<string> instructorIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(instructorIds);
            Dictionary<string, EventInstructorSnapshot> result = new Dictionary<string, EventInstructorSnapshot>(StringComparer.Ordinal);
            List<string> ids = instructorIds.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT i.id, i.slug, i.displayname, i.photomediaid, i.audience, (i.status = 'Active' AND i.deletedutc IS NULL AND i.moderationstate = 'None') AS listed FROM instructors i WHERE " +
                Scope("i") + " AND i.id IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                EventInstructorSnapshot instructor = new EventInstructorSnapshot
                {
                    Id = RowReader.GetString(row, "id"),
                    Slug = RowReader.GetString(row, "slug"),
                    DisplayName = RowReader.GetString(row, "displayname"),
                    PhotoMediaId = RowReader.GetNullableString(row, "photomediaid"),
                    Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                    IsListed = RowReader.GetBool(row, "listed")
                };
                result[instructor.Id] = instructor;
            }
            return result;
        }

        internal string CounterStatement(string where)
        {
            return "UPDATE events SET goingcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Going'), " +
                "guestcount = (SELECT COALESCE(SUM(c.guestcount), 0) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Going'), " +
                "interestedcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Interested'), " +
                "waitlistcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Waitlisted'), " +
                "checkedincount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.checkedinutc IS NOT NULL) WHERE " + Scope() + " AND " + where + ";";
        }

        private List<string> Conditions(EventListFilter f)
        {
            List<string> c = new List<string> { "e.deletedutc IS NULL", NotTemplate };
            List<EventStatusEnum> statuses = f.Statuses.Where(s => s != EventStatusEnum.Template).Distinct().ToList();
            if (statuses.Count == 0)
            {
                statuses.Add(EventStatusEnum.Published);
                if (f.IncludeCanceled) statuses.Add(EventStatusEnum.Canceled);
            }
            string statusList = "e.status IN " + D.InList(statuses.Select(s => s.ToString()));
            if (!String.IsNullOrEmpty(f.DraftHostUserId))
                c.Add("(" + statusList + " OR (e.status = 'Draft' AND EXISTS (SELECT 1 FROM eventhosts dh WHERE " + Scope("dh") + " AND dh.eventid = e.id AND dh.userid = " + D.Str(f.DraftHostUserId) + ")))");
            else c.Add(statusList);
            if (f.OnlyUnmoderated) c.Add("e.moderationstate = 'None'");
            if (f.Audiences != null) c.Add(f.Audiences.Count == 0 ? "1 = 0" : "e.audience IN " + D.InList(f.Audiences.Select(a => a.ToString())));
            if (f.When == EventWhenEnum.Upcoming) c.Add("e.endutc > " + D.Ts(f.NowUtc));
            else if (f.When == EventWhenEnum.Past) c.Add("e.endutc <= " + D.Ts(f.NowUtc));
            if (f.StartsAfterUtc != null) c.Add("e.startutc >= " + D.Ts(f.StartsAfterUtc.Value));
            if (f.StartsBeforeUtc != null) c.Add("e.startutc < " + D.Ts(f.StartsBeforeUtc.Value));
            if (f.Categories.Count > 0) c.Add("lower(e.categorykey) IN " + D.InList(f.Categories.Select(k => k.ToLowerInvariant())));
            if (f.Formats.Count > 0) c.Add("e.format IN " + D.InList(f.Formats.Select(x => x.ToString())));
            if (!String.IsNullOrEmpty(f.VenueId)) c.Add("e.venueid = " + D.Str(f.VenueId));
            if (!String.IsNullOrEmpty(f.EventId)) c.Add("e.id = " + D.Str(f.EventId));
            if (!String.IsNullOrEmpty(f.RecurrenceId)) c.Add("e.recurrenceid = " + D.Str(f.RecurrenceId));
            if (!String.IsNullOrEmpty(f.ProposedByUserId)) c.Add("e.proposedbyuserid = " + D.Str(f.ProposedByUserId));
            if (f.OnlyProposals) c.Add("e.proposedbyuserid IS NOT NULL");
            if (!String.IsNullOrEmpty(f.HostUserId)) c.Add("EXISTS (SELECT 1 FROM eventhosts h WHERE " + Scope("h") + " AND h.eventid = e.id AND h.userid = " + D.Str(f.HostUserId) + ")");
            if (!String.IsNullOrEmpty(f.InstructorId)) c.Add("EXISTS (SELECT 1 FROM eventinstructors ei WHERE " + Scope("ei") + " AND ei.eventid = e.id AND ei.instructorid = " + D.Str(f.InstructorId) + ")");
            if (f.HasSpots) c.Add("(e.capacity IS NULL OR e.capacity - e.goingcount - e.guestcount > 0)");
            if (f.NearLat != null && f.NearLng != null && f.NearRadiusKm != null)
            {
                double lat = f.NearLat.Value;
                double lng = f.NearLng.Value;
                double radius = f.NearRadiusKm.Value;
                double dLat = radius / 111.0;
                double cos = Math.Cos(lat * Math.PI / 180.0);
                double dLng = cos < 0.01 ? 180.0 : Math.Min(180.0, radius / (111.0 * cos));
                string box = "e.lat BETWEEN " + D.Num(lat - dLat) + " AND " + D.Num(lat + dLat) + (dLng >= 180.0 ? String.Empty : " AND " + LngBox(lng, dLng));
                string near = "(e.lat IS NOT NULL AND e.lng IS NOT NULL AND " + box + " AND " + Distance(lat, lng) + " <= " + D.Num(radius) + ")";
                c.Add(f.IncludeOnline ? "(" + near + " OR e.format = 'Online')" : near);
            }
            if (f.Box != null)
            {
                string inBox = "(e.lat IS NOT NULL AND e.lng IS NOT NULL AND e.format <> 'Online' AND " + DirectorySql.Box(D, f.Box, "e.lat", "e.lng") + ")";
                c.Add(f.IncludeOnline ? "(" + inBox + " OR e.format = 'Online')" : inBox);
            }
            if (!String.IsNullOrEmpty(f.RsvpUserId))
            {
                string mine = "SELECT 1 FROM eventrsvps mr WHERE " + Scope("mr") + " AND mr.eventid = e.id AND mr.userid = " + D.Str(f.RsvpUserId);
                List<string> any = new List<string>();
                if (f.RsvpStatuses.Count > 0) any.Add("EXISTS (" + mine + " AND mr.status IN " + D.InList(f.RsvpStatuses.Select(s => s.ToString())) + ")");
                if (f.RsvpNone) any.Add("NOT EXISTS (" + mine + ")");
                if (any.Count > 0) c.Add("(" + String.Join(" OR ", any) + ")");
            }
            if (!String.IsNullOrEmpty(f.MineUserId))
            {
                c.Add("(EXISTS (SELECT 1 FROM eventrsvps me WHERE " + Scope("me") + " AND me.eventid = e.id AND me.userid = " + D.Str(f.MineUserId) + ") OR EXISTS (SELECT 1 FROM eventhosts mh WHERE " +
                    Scope("mh") + " AND mh.eventid = e.id AND mh.userid = " + D.Str(f.MineUserId) + "))");
            }
            if (!String.IsNullOrEmpty(f.BlockerUserId))
            {
                string me = D.Str(f.BlockerUserId);
                c.Add("NOT (EXISTS (SELECT 1 FROM eventhosts bh WHERE " + Scope("bh") + " AND bh.eventid = e.id) AND NOT EXISTS (SELECT 1 FROM eventhosts oh WHERE " + Scope("oh") +
                    " AND oh.eventid = e.id AND NOT EXISTS (SELECT 1 FROM blocks b WHERE " + Scope("b") + " AND ((b.blockeruserid = " + me + " AND b.blockeduserid = oh.userid) OR (b.blockeduserid = " + me +
                    " AND b.blockeruserid = oh.userid)))))");
            }
            return c;
        }

        private string MapFrom()
        {
            return "events e LEFT JOIN venues mv ON " + Scope("mv") + " AND mv.id = e.venueid";
        }

        private string MapVisibility(EventListFilter f)
        {
            // Staff see exact positions. Everyone else gets the stricter of the event's and its venue's address
            // visibility, and LocalityOnly when the venue is not one the caller may read.
            if (f.MapVenueAudiences == null) return "'Exact'";
            string readable = f.MapVenueAudiences.Count == 0 ? "(1 = 0)" : "(mv.id IS NOT NULL AND mv.status = 'Active' AND mv.deletedutc IS NULL AND mv.moderationstate = 'None' AND mv.audience IN " +
                D.InList(f.MapVenueAudiences.Select(a => a.ToString())) + ")";
            return "(CASE WHEN e.venueid IS NOT NULL AND NOT " + readable + " THEN 'LocalityOnly' WHEN e.addressvisibility = 'LocalityOnly' OR mv.addressvisibility = 'LocalityOnly' THEN 'LocalityOnly' " +
                "WHEN e.addressvisibility = 'Approximate' OR mv.addressvisibility = 'Approximate' THEN 'Approximate' ELSE 'Exact' END)";
        }

        private string MapConditions(EventListFilter f, EnumerationRequest request)
        {
            List<string> all = Conditions(f);
            all.Add("e.format <> 'Online'");
            all.Add("e.lat IS NOT NULL AND e.lng IS NOT NULL");
            all.Add(MapVisibility(f) + " <> 'LocalityOnly'");
            all.AddRange(EnumerationConditions(request));
            return String.Join(" AND ", all);
        }

        private string LngBox(double lng, double dLng)
        {
            double min = lng - dLng;
            double max = lng + dLng;
            if (min < -180.0) return "(e.lng >= " + D.Num(min + 360.0) + " OR e.lng <= " + D.Num(max) + ")";
            if (max > 180.0) return "(e.lng >= " + D.Num(min) + " OR e.lng <= " + D.Num(max - 360.0) + ")";
            return "e.lng BETWEEN " + D.Num(min) + " AND " + D.Num(max);
        }

        private string Distance(double lat, double lng)
        {
            return "(6371.0 * 2 * asin(sqrt(power(sin(radians(COALESCE(e.lat, 0) - " + D.Num(lat) + ") / 2), 2) + cos(radians(" + D.Num(lat) + ")) * cos(radians(COALESCE(e.lat, 0))) * " +
                "power(sin(radians(COALESCE(e.lng, 0) - " + D.Num(lng) + ") / 2), 2))))";
        }

        private List<string> ChildStatements(EventRecord e, bool replace, DateTime nowUtc)
        {
            string id = D.Str(e.Id);
            List<string> s = new List<string>();
            if (replace)
            {
                s.Add("DELETE FROM eventhosts WHERE " + Scope() + " AND eventid = " + id + ";");
                s.Add("DELETE FROM eventinstructors WHERE " + Scope() + " AND eventid = " + id + ";");
                List<int> keep = e.ReminderOffsetsMinutes.Distinct().ToList();
                s.Add("DELETE FROM eventreminders WHERE " + Scope() + " AND eventid = " + id + (keep.Count == 0 ? String.Empty : " AND offsetminutes NOT IN (" + String.Join(", ", keep.Select(o => D.Num(o))) + ")") + ";");
            }
            List<string> hosts = e.HostUserIds.Where(h => !String.IsNullOrWhiteSpace(h)).Distinct(StringComparer.Ordinal).ToList();
            if (hosts.Count > 0)
                s.Add("INSERT INTO eventhosts (propertyid, eventid, userid, position) VALUES " + String.Join(", ", hosts.Select((h, i) => "(" + Pid + ", " + id + ", " + D.Str(h) + ", " + D.Num(i) + ")")) + " ON CONFLICT DO NOTHING;");
            List<string> instructors = e.InstructorIds.Where(h => !String.IsNullOrWhiteSpace(h)).Distinct(StringComparer.Ordinal).ToList();
            if (instructors.Count > 0)
                s.Add("INSERT INTO eventinstructors (propertyid, eventid, instructorid, position) VALUES " + String.Join(", ", instructors.Select((h, i) => "(" + Pid + ", " + id + ", " + D.Str(h) + ", " + D.Num(i) + ")")) + " ON CONFLICT DO NOTHING;");
            if (e.Status == EventStatusEnum.Template)
            {
                // The series template keeps its offsets for new occurrences; its rows are skipped so they never send.
                foreach (int offset in e.ReminderOffsetsMinutes.Distinct())
                {
                    DateTime due = DateTime.SpecifyKind(e.StartUtc.AddMinutes(-offset), DateTimeKind.Utc);
                    s.Add("INSERT INTO eventreminders (propertyid, id, eventid, offsetminutes, dueutc, sentutc, skippedutc) VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate(EventIdPrefixes.Reminder)) + ", " + id + ", " +
                        D.Num(offset) + ", " + D.Ts(due) + ", NULL, " + D.Ts(nowUtc) + ") ON CONFLICT (propertyid, eventid, offsetminutes) DO UPDATE SET dueutc = excluded.dueutc, skippedutc = excluded.skippedutc;");
                }
            }
            else
            {
                foreach (int offset in e.ReminderOffsetsMinutes.Distinct())
                {
                    DateTime due = DateTime.SpecifyKind(e.StartUtc.AddMinutes(-offset), DateTimeKind.Utc);
                    s.Add("INSERT INTO eventreminders (propertyid, id, eventid, offsetminutes, dueutc, sentutc, skippedutc) VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate(EventIdPrefixes.Reminder)) + ", " + id + ", " +
                        D.Num(offset) + ", " + D.Ts(due) + ", NULL, NULL) ON CONFLICT (propertyid, eventid, offsetminutes) DO UPDATE SET dueutc = excluded.dueutc, sentutc = CASE WHEN excluded.dueutc > " +
                        D.Ts(nowUtc) + " THEN NULL ELSE eventreminders.sentutc END, skippedutc = CASE WHEN excluded.dueutc > " + D.Ts(nowUtc) + " THEN NULL ELSE eventreminders.skippedutc END;");
                }
            }
            return s;
        }

        private List<string> RescheduleStatements(EventRecord e, DateTime nowUtc)
        {
            string id = D.Str(e.Id);
            return new List<string>
            {
                "UPDATE eventreminders SET dueutc = " + D.Ts(e.StartUtc) + " - (offsetminutes * INTERVAL '1 minute'), sentutc = CASE WHEN " + D.Ts(e.StartUtc) + " - (offsetminutes * INTERVAL '1 minute') > " +
                    D.Ts(nowUtc) + " THEN NULL ELSE sentutc END, skippedutc = CASE WHEN " + D.Ts(e.StartUtc) + " - (offsetminutes * INTERVAL '1 minute') > " + D.Ts(nowUtc) + " THEN NULL ELSE skippedutc END WHERE " +
                    Scope() + " AND eventid = " + id + ";"
            };
        }

        private string Values(EventRecord e)
        {
            return String.Join(", ", new[]
            {
                D.Str(e.Id), D.Str(e.Slug), D.Str(e.Title), D.Str(e.Summary), D.Str(e.Description), D.Str(e.CategoryKey), D.Enum(e.Format), D.Enum(e.Status), D.Ts(e.StartUtc), D.Ts(e.EndUtc),
                D.Str(e.TimeZone), D.Bool(e.IsAllDay), D.Str(e.RecurrenceId), D.Ts(e.OriginalStartUtc), D.Bool(e.IsException), D.Num(e.OccurrenceIndex), D.Str(e.VenueId), D.Str(e.LocationName),
                D.Str(e.Line1), D.Str(e.Line2), D.Str(e.Locality), D.Str(e.Region), D.Str(e.PostalCode), D.Str(e.CountryCode), D.Str(e.AddressVisibility), Dbl(e.Lat), Dbl(e.Lng),
                D.Str(e.OnlinePlatformLabel), D.Str(e.OnlineJoinUrl), D.Num(e.Capacity), D.Num(e.MaxGuestsPerRsvp), D.Bool(e.WaitlistEnabled), D.Enum(e.RsvpAudience), D.Ts(e.RsvpOpensUtc),
                D.Ts(e.RsvpClosesUtc), D.Enum(e.AttendeeVisibility), D.Str(e.ExternalUrl), D.Str(e.CoverMediaId), D.Bool(e.CommentsEnabled), "0", "0", "0", "0", "0", "0", "0", "0",
                D.Num(e.IcsSequence), D.Enum(e.Audience), D.Enum(e.ModerationState), D.Ts(e.ModeratedUtc), D.Str(e.ModeratedByUserId), D.Str(e.ModerationReason), D.Ts(e.CanceledUtc),
                D.Str(e.CancelReason), D.Str(e.CanceledByUserId), D.Str(e.ProposedByUserId), D.Str(e.ProposalNote), D.Str(e.ReviewNote), D.Str(e.ReviewerUserId), D.Ts(e.ReviewedUtc),
                D.Str(e.PromotedFeedPostId), D.Ts(e.PublishedUtc), D.Str(e.CreatedByUserId), D.Ts(e.DeletedUtc), D.Ts(e.CreatedUtc), D.Ts(e.UpdatedUtc)
            });
        }

        private string Dbl(double? value)
        {
            return value == null ? "NULL" : D.Num(value.Value);
        }

        private async Task<List<EventRecord>> LoadAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT e.* FROM events e WHERE " + Scope("e") + " AND e.deletedutc IS NULL AND " + condition + ";", token).ConfigureAwait(false);
            List<EventRecord> rows = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillChildrenAsync(rows, token).ConfigureAwait(false);
            return rows;
        }

        private async Task FillChildrenAsync(List<EventRecord> rows, CancellationToken token)
        {
            if (rows.Count == 0) return;
            string ids = D.InList(rows.Select(r => r.Id));
            Dictionary<string, EventRecord> byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            DataTable hosts = await QueryAsync("SELECT eventid, userid FROM eventhosts WHERE " + Scope() + " AND eventid IN " + ids + " ORDER BY eventid, position, userid;", token).ConfigureAwait(false);
            foreach (DataRow row in hosts.Rows) if (byId.TryGetValue(RowReader.GetString(row, "eventid"), out EventRecord? e)) e.HostUserIds.Add(RowReader.GetString(row, "userid"));
            DataTable instructors = await QueryAsync("SELECT eventid, instructorid FROM eventinstructors WHERE " + Scope() + " AND eventid IN " + ids + " ORDER BY eventid, position, instructorid;", token).ConfigureAwait(false);
            foreach (DataRow row in instructors.Rows) if (byId.TryGetValue(RowReader.GetString(row, "eventid"), out EventRecord? e)) e.InstructorIds.Add(RowReader.GetString(row, "instructorid"));
            DataTable reminders = await QueryAsync("SELECT eventid, offsetminutes FROM eventreminders WHERE " + Scope() + " AND eventid IN " + ids + " ORDER BY eventid, offsetminutes DESC;", token).ConfigureAwait(false);
            foreach (DataRow row in reminders.Rows) if (byId.TryGetValue(RowReader.GetString(row, "eventid"), out EventRecord? e)) e.ReminderOffsetsMinutes.Add(RowReader.GetInt(row, "offsetminutes"));
        }

        internal static double? NullableDouble(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column)) return null;
            object value = row[column];
            if (value == null || value is DBNull) return null;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        internal static EventRecord Map(DataRow row)
        {
            return new EventRecord
            {
                Id = RowReader.GetString(row, "id"),
                Slug = RowReader.GetString(row, "slug"),
                Title = RowReader.GetString(row, "title"),
                Summary = RowReader.GetNullableString(row, "summary"),
                Description = RowReader.GetNullableString(row, "description"),
                CategoryKey = RowReader.GetNullableString(row, "categorykey"),
                Format = RowReader.GetEnum(row, "format", EventFormatEnum.InPerson),
                Status = RowReader.GetEnum(row, "status", EventStatusEnum.Draft),
                StartUtc = RowReader.GetDateTime(row, "startutc"),
                EndUtc = RowReader.GetDateTime(row, "endutc"),
                TimeZone = RowReader.GetString(row, "timezone"),
                IsAllDay = RowReader.GetBool(row, "isallday"),
                RecurrenceId = RowReader.GetNullableString(row, "recurrenceid"),
                OriginalStartUtc = RowReader.GetNullableDateTime(row, "originalstartutc"),
                IsException = RowReader.GetBool(row, "isexception"),
                OccurrenceIndex = RowReader.GetNullableInt(row, "occurrenceindex"),
                VenueId = RowReader.GetNullableString(row, "venueid"),
                LocationName = RowReader.GetNullableString(row, "locationname"),
                Line1 = RowReader.GetNullableString(row, "line1"),
                Line2 = RowReader.GetNullableString(row, "line2"),
                Locality = RowReader.GetNullableString(row, "locality"),
                Region = RowReader.GetNullableString(row, "region"),
                PostalCode = RowReader.GetNullableString(row, "postalcode"),
                CountryCode = RowReader.GetNullableString(row, "countrycode"),
                AddressVisibility = RowReader.GetNullableString(row, "addressvisibility") ?? "Exact",
                Lat = NullableDouble(row, "lat"),
                Lng = NullableDouble(row, "lng"),
                OnlinePlatformLabel = RowReader.GetNullableString(row, "onlineplatformlabel"),
                OnlineJoinUrl = RowReader.GetNullableString(row, "onlinejoinurl"),
                Capacity = RowReader.GetNullableInt(row, "capacity"),
                MaxGuestsPerRsvp = RowReader.GetInt(row, "maxguestsperrsvp"),
                WaitlistEnabled = RowReader.GetBool(row, "waitlistenabled"),
                RsvpAudience = RowReader.GetEnum(row, "rsvpaudience", ContentAudienceEnum.Members),
                RsvpOpensUtc = RowReader.GetNullableDateTime(row, "rsvpopensutc"),
                RsvpClosesUtc = RowReader.GetNullableDateTime(row, "rsvpclosesutc"),
                AttendeeVisibility = RowReader.GetEnum(row, "attendeevisibility", EventAttendeeVisibilityEnum.Attendees),
                ExternalUrl = RowReader.GetNullableString(row, "externalurl"),
                CoverMediaId = RowReader.GetNullableString(row, "covermediaid"),
                CommentsEnabled = RowReader.GetBool(row, "commentsenabled"),
                GoingCount = RowReader.GetInt(row, "goingcount"),
                GuestCount = RowReader.GetInt(row, "guestcount"),
                InterestedCount = RowReader.GetInt(row, "interestedcount"),
                WaitlistCount = RowReader.GetInt(row, "waitlistcount"),
                CheckedInCount = RowReader.GetInt(row, "checkedincount"),
                CommentCount = RowReader.GetInt(row, "commentcount"),
                ReactionTotal = RowReader.GetInt(row, "reactiontotal"),
                OpenReportCount = RowReader.GetInt(row, "openreportcount"),
                IcsSequence = RowReader.GetInt(row, "icssequence"),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                CanceledUtc = RowReader.GetNullableDateTime(row, "canceledutc"),
                CancelReason = RowReader.GetNullableString(row, "cancelreason"),
                CanceledByUserId = RowReader.GetNullableString(row, "canceledbyuserid"),
                ProposedByUserId = RowReader.GetNullableString(row, "proposedbyuserid"),
                ProposalNote = RowReader.GetNullableString(row, "proposalnote"),
                ReviewNote = RowReader.GetNullableString(row, "reviewnote"),
                ReviewerUserId = RowReader.GetNullableString(row, "revieweruserid"),
                ReviewedUtc = RowReader.GetNullableDateTime(row, "reviewedutc"),
                PromotedFeedPostId = RowReader.GetNullableString(row, "promotedfeedpostid"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
