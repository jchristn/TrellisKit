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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// RSVP writes decide inside one transaction: the event row is locked first (SELECT ... FOR UPDATE), so every
    /// later statement of the transaction sees the RSVPs committed by the writes that held the lock before it (READ
    /// COMMITTED takes a new snapshot per statement). The decision is computed in SQL into a temporary table, the RSVP
    /// is written from it, a PL/pgSQL block promotes Waitlisted parties that fit (oldest first), the counters are
    /// recomputed, and a final SELECT returns the outcome.
    /// </summary>
    internal abstract class EventRsvpMethodsBase : MethodsBase, IEventRsvpMethods
    {
        private const string _State = "evrsvpstate";

        protected EventRsvpMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<EventRsvpOutcome> SetAsync(string eventId, string userId, EventRsvpStatusEnum status, int guestCount, bool hideFromAttendeeLists, DateTime nowUtc, DateTime promoteBeforeUtc, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            RequireText(userId, nameof(userId));
            if (status == EventRsvpStatusEnum.Waitlisted) throw new ArgumentException("Request Going or Interested.", nameof(status));
            string e = D.Str(eventId);
            string u = D.Str(userId);
            string now = D.Ts(nowUtc);
            int guests = Math.Max(0, guestCount);
            string ev = "SELECT o.{0} FROM events o WHERE " + Scope("o") + " AND o.id = " + e;
            string open = "EXISTS (SELECT 1 FROM events o WHERE " + Scope("o") + " AND o.id = " + e + " AND o.status = 'Published' AND o.deletedutc IS NULL AND o.startutc > " + now + ")";
            string others = "(SELECT COALESCE(SUM(1 + c.guestcount), 0) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = " + e + " AND c.status = 'Going' AND c.userid <> " + u + ")";
            string current = "(SELECT c.status FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = " + e + " AND c.userid = " + u + ")";
            string decision = status == EventRsvpStatusEnum.Interested
                ? "CASE WHEN NOT " + open + " THEN 'Closed' ELSE 'Interested' END"
                : "CASE WHEN NOT " + open + " THEN 'Closed' " +
                  "WHEN (" + String.Format(ev, "capacity") + ") IS NULL THEN 'Going' " +
                  "WHEN " + others + " + " + D.Num(1 + guests) + " <= (" + String.Format(ev, "capacity") + ") THEN 'Going' " +
                  "WHEN COALESCE(" + current + ", '') = 'Going' THEN 'Full' " +
                  "WHEN (" + String.Format(ev, "waitlistenabled") + ") THEN 'Waitlisted' ELSE 'Full' END";
            List<string> body = new List<string>
            {
                "INSERT INTO " + _State + " (decision) SELECT " + decision + ";",
                "INSERT INTO eventrsvps (propertyid, id, eventid, userid, status, guestcount, hidefromattendeelists, waitlistedutc, promotedutc, checkedinutc, checkedinbyuserid, createdutc, updatedutc) " +
                    "SELECT " + Pid + ", " + D.Str(IdGenerator.Generate(EventIdPrefixes.Rsvp)) + ", " + e + ", " + u + ", s.decision, " + D.Num(guests) + ", " + D.Bool(hideFromAttendeeLists) +
                    ", CASE WHEN s.decision = 'Waitlisted' THEN clock_timestamp() ELSE NULL END, NULL, NULL, NULL, " + now + ", " + now + " FROM " + _State +
                    " s WHERE s.decision IN ('Going', 'Interested', 'Waitlisted') ON CONFLICT (propertyid, eventid, userid) DO UPDATE SET status = excluded.status, guestcount = excluded.guestcount, " +
                    "hidefromattendeelists = excluded.hidefromattendeelists, waitlistedutc = CASE WHEN excluded.status = 'Waitlisted' THEN COALESCE(CASE WHEN eventrsvps.status = 'Waitlisted' THEN eventrsvps.waitlistedutc END, excluded.waitlistedutc) ELSE NULL END, " +
                    "checkedinutc = CASE WHEN excluded.status = 'Going' THEN eventrsvps.checkedinutc ELSE NULL END, checkedinbyuserid = CASE WHEN excluded.status = 'Going' THEN eventrsvps.checkedinbyuserid ELSE NULL END, " +
                    "updatedutc = excluded.updatedutc;"
            };
            return await RunAsync(eventId, body, null, nowUtc, promoteBeforeUtc, "r.userid = " + u, token).ConfigureAwait(false);
        }

        public async Task<EventRsvpOutcome> DeleteAsync(string eventId, string userId, DateTime nowUtc, DateTime promoteBeforeUtc, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            RequireText(userId, nameof(userId));
            string e = D.Str(eventId);
            string u = D.Str(userId);
            List<string> body = new List<string>
            {
                "INSERT INTO " + _State + " (decision) SELECT CASE WHEN EXISTS (SELECT 1 FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = " + e + " AND c.userid = " + u + ") THEN 'Removed' ELSE 'Missing' END;",
                "DELETE FROM eventrsvps WHERE " + Scope() + " AND eventid = " + e + " AND userid = " + u + ";"
            };
            return await RunAsync(eventId, body, null, nowUtc, promoteBeforeUtc, "r.userid = " + u, token).ConfigureAwait(false);
        }

        public async Task<EventRsvpOutcome> StaffActionAsync(string eventId, string rsvpId, string action, DateTime nowUtc, DateTime promoteBeforeUtc, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            RequireText(rsvpId, nameof(rsvpId));
            string e = D.Str(eventId);
            string r = D.Str(rsvpId);
            string now = D.Ts(nowUtc);
            string exists = "EXISTS (SELECT 1 FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = " + e + " AND c.id = " + r + ")";
            List<string> body = new List<string>();
            string? exclude = null;
            switch (action)
            {
                case "promote":
                    body.Add("INSERT INTO " + _State + " (decision) SELECT CASE WHEN " + exists + " THEN 'Going' ELSE 'Missing' END;");
                    body.Add("UPDATE eventrsvps SET status = 'Going', waitlistedutc = NULL, promotedutc = " + now + ", updatedutc = " + now + " WHERE " + Scope() + " AND eventid = " + e + " AND id = " + r + ";");
                    break;
                case "move-to-waitlist":
                    body.Add("INSERT INTO " + _State + " (decision) SELECT CASE WHEN " + exists + " THEN 'Waitlisted' ELSE 'Missing' END;");
                    body.Add("UPDATE eventrsvps SET status = 'Waitlisted', waitlistedutc = clock_timestamp(), checkedinutc = NULL, checkedinbyuserid = NULL, updatedutc = " + now + " WHERE " + Scope() +
                        " AND eventid = " + e + " AND id = " + r + ";");
                    exclude = rsvpId;
                    break;
                case "remove":
                    body.Add("INSERT INTO " + _State + " (decision) SELECT CASE WHEN " + exists + " THEN 'Removed' ELSE 'Missing' END;");
                    body.Add("DELETE FROM eventrsvps WHERE " + Scope() + " AND eventid = " + e + " AND id = " + r + ";");
                    break;
                default:
                    throw new ArgumentException("Unknown RSVP action " + action + ".", nameof(action));
            }
            return await RunAsync(eventId, body, exclude, nowUtc, promoteBeforeUtc, "r.id = " + r, token).ConfigureAwait(false);
        }

        public async Task<EventRsvpOutcome> PromoteAsync(string eventId, DateTime nowUtc, DateTime promoteBeforeUtc, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            List<string> body = new List<string> { "INSERT INTO " + _State + " (decision) SELECT 'Missing';" };
            return await RunAsync(eventId, body, null, nowUtc, promoteBeforeUtc, "1 = 0", token).ConfigureAwait(false);
        }

        public async Task<EventRsvp?> CheckInAsync(string eventId, string rsvpId, bool checkIn, string? byUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            RequireText(rsvpId, nameof(rsvpId));
            string e = D.Str(eventId);
            string r = D.Str(rsvpId);
            string now = D.Ts(nowUtc);
            List<string> statements = new List<string>
            {
                "SELECT id FROM events WHERE " + Scope() + " AND id = " + e + " FOR UPDATE;",
                checkIn
                    ? "UPDATE eventrsvps SET checkedinutc = COALESCE(checkedinutc, " + now + "), checkedinbyuserid = COALESCE(checkedinbyuserid, " + D.Str(byUserId) + "), updatedutc = " + now + " WHERE " + Scope() +
                        " AND eventid = " + e + " AND id = " + r + " AND status = 'Going';"
                    : "UPDATE eventrsvps SET checkedinutc = NULL, checkedinbyuserid = NULL, updatedutc = " + now + " WHERE " + Scope() + " AND eventid = " + e + " AND id = " + r + ";",
                Counters(e),
                "SELECT r.*, " + Position("r") + " AS position FROM eventrsvps r WHERE " + Scope("r") + " AND r.eventid = " + e + " AND r.id = " + r + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<EventRsvp?> ReadAsync(string eventId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(eventId) || String.IsNullOrEmpty(userId)) return null;
            DataTable table = await QueryAsync("SELECT r.*, " + Position("r") + " AS position FROM eventrsvps r WHERE " + Scope("r") + " AND r.eventid = " + D.Str(eventId) + " AND r.userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<EventRsvp?> ReadByIdAsync(string eventId, string rsvpId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(eventId) || String.IsNullOrEmpty(rsvpId)) return null;
            DataTable table = await QueryAsync("SELECT r.*, " + Position("r") + " AS position FROM eventrsvps r WHERE " + Scope("r") + " AND r.eventid = " + D.Str(eventId) + " AND r.id = " + D.Str(rsvpId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<Dictionary<string, EventRsvp>> ReadForUserAsync(string userId, IEnumerable<string> eventIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(eventIds);
            Dictionary<string, EventRsvp> result = new Dictionary<string, EventRsvp>(StringComparer.Ordinal);
            List<string> ids = eventIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (String.IsNullOrEmpty(userId) || ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT r.*, " + Position("r") + " AS position FROM eventrsvps r WHERE " + Scope("r") + " AND r.userid = " + D.Str(userId) + " AND r.eventid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                EventRsvp rsvp = Map(row);
                result[rsvp.EventId] = rsvp;
            }
            return result;
        }

        public Task<EnumerationResult<EventRsvp>> EnumerateAsync(string eventId, List<EventRsvpStatusEnum> statuses, bool? checkedIn, bool includeHidden, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            ArgumentNullException.ThrowIfNull(statuses);
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "r.eventid = " + D.Str(eventId), "u.isdeleted = FALSE" };
            if (statuses.Count > 0) conditions.Add("r.status IN " + D.InList(statuses.Select(s => s.ToString())));
            if (checkedIn == true) conditions.Add("r.checkedinutc IS NOT NULL");
            if (checkedIn == false) conditions.Add("r.checkedinutc IS NULL");
            if (!includeHidden) conditions.Add("r.hidefromattendeelists = FALSE");
            return EnumerateAsync(request, "r.*, " + Position("r") + " AS position", "eventrsvps r JOIN users u ON " + Scope("u") + " AND u.id = r.userid", conditions, Map, token);
        }

        public async Task<List<string>> PreviewAsync(string eventId, bool includeHidden, int limit, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            DataTable table = await QueryAsync("SELECT r.userid FROM eventrsvps r JOIN users u ON " + Scope("u") + " AND u.id = r.userid WHERE " + Scope("r") + " AND r.eventid = " + D.Str(eventId) +
                " AND r.status = 'Going' AND u.isdeleted = FALSE" + (includeHidden ? String.Empty : " AND r.hidefromattendeelists = FALSE") + " ORDER BY r.createdutc, r.id LIMIT " + D.Num(Math.Clamp(limit, 0, 50)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => RowReader.GetString(row, "userid")).ToList();
        }

        public Task<long> CountGoingAsync(string eventId, bool includeHidden, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM eventrsvps r JOIN users u ON " + Scope("u") + " AND u.id = r.userid WHERE " + Scope("r") + " AND r.eventid = " + D.Str(eventId) +
                " AND r.status = 'Going' AND u.isdeleted = FALSE" + (includeHidden ? String.Empty : " AND r.hidefromattendeelists = FALSE") + ";", token);
        }

        public Task<long> CountActiveForUserAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM eventrsvps r JOIN events e ON " + Scope("e") + " AND e.id = r.eventid WHERE " + Scope("r") + " AND r.userid = " + D.Str(userId) +
                " AND e.deletedutc IS NULL AND e.status = 'Published' AND e.startutc > " + D.Ts(nowUtc) + ";", token);
        }

        public async Task<List<EventRsvp>> ListRecipientsAsync(string eventId, List<EventRsvpStatusEnum> statuses, string? afterUserId, int limit, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            ArgumentNullException.ThrowIfNull(statuses);
            if (statuses.Count == 0) return new List<EventRsvp>();
            DataTable table = await QueryAsync("SELECT r.*, " + Position("r") + " AS position FROM eventrsvps r WHERE " + Scope("r") + " AND r.eventid = " + D.Str(eventId) + " AND r.status IN " +
                D.InList(statuses.Select(s => s.ToString())) + (afterUserId == null ? String.Empty : " AND r.userid > " + D.Str(afterUserId)) + " ORDER BY r.userid LIMIT " + D.Num(Math.Clamp(limit, 1, 5000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<EventRsvpRecord>> ListForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT r.*, " + Position("r") + " AS position, e.title AS eventtitle, e.startutc AS eventstartutc, e.status AS eventstatus FROM eventrsvps r JOIN events e ON " +
                Scope("e") + " AND e.id = r.eventid WHERE " + Scope("r") + " AND r.userid = " + D.Str(userId) + (afterId == null ? String.Empty : " AND r.id > " + D.Str(afterId)) +
                " ORDER BY r.id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new EventRsvpRecord
            {
                Rsvp = Map(row),
                EventTitle = RowReader.GetString(row, "eventtitle"),
                EventStartUtc = RowReader.GetDateTime(row, "eventstartutc"),
                EventStatus = RowReader.GetEnum(row, "eventstatus", EventStatusEnum.Published)
            }).ToList();
        }

        public async Task<int> DeleteStartedForUserAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string u = D.Str(userId);
            string started = "SELECT r.eventid FROM eventrsvps r JOIN events e ON " + Scope("e") + " AND e.id = r.eventid WHERE " + Scope("r") + " AND r.userid = " + u + " AND (e.startutc <= " + D.Ts(nowUtc) +
                " OR e.status <> 'Published' OR e.deletedutc IS NOT NULL)";
            List<string> statements = new List<string>
            {
                "CREATE TEMP TABLE IF NOT EXISTS evrsvpgone (eventid TEXT) ON COMMIT DROP;",
                "INSERT INTO evrsvpgone (eventid) " + started + ";",
                "DELETE FROM eventrsvps WHERE " + Scope() + " AND userid = " + u + " AND eventid IN (SELECT eventid FROM evrsvpgone);",
                "UPDATE events SET goingcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Going'), " +
                    "guestcount = (SELECT COALESCE(SUM(c.guestcount), 0) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Going'), " +
                    "interestedcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Interested'), " +
                    "waitlistcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Waitlisted'), " +
                    "checkedincount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.checkedinutc IS NOT NULL) WHERE " + Scope() +
                    " AND id IN (SELECT eventid FROM evrsvpgone);",
                "SELECT COUNT(*) AS cnt FROM evrsvpgone;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public Task<long> CountForEventAsync(string eventId, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM eventrsvps WHERE " + Scope() + " AND eventid = " + D.Str(eventId) + ";", token);
        }

        private async Task<EventRsvpOutcome> RunAsync(string eventId, List<string> body, string? excludeFromPromotion, DateTime nowUtc, DateTime promoteBeforeUtc, string rsvpCondition, CancellationToken token)
        {
            string e = D.Str(eventId);
            List<string> statements = new List<string>
            {
                "SELECT id FROM events WHERE " + Scope() + " AND id = " + e + " FOR UPDATE;",
                "CREATE TEMP TABLE IF NOT EXISTS " + _State + " (decision TEXT NULL, promoted TEXT NULL, ord INTEGER NULL) ON COMMIT DROP;"
            };
            statements.AddRange(body);
            statements.Add(PromotionBlock(e, excludeFromPromotion, nowUtc, promoteBeforeUtc));
            statements.Add(Counters(e));
            statements.Add("SELECT (SELECT s.decision FROM " + _State + " s WHERE s.decision IS NOT NULL LIMIT 1) AS decision, (SELECT string_agg(p.promoted, ',' ORDER BY p.ord) FROM " + _State +
                " p WHERE p.promoted IS NOT NULL) AS promotedusers, r.*, CASE WHEN r.id IS NULL THEN NULL ELSE " + Position("r") + " END AS position FROM (SELECT 1 AS one) x LEFT JOIN eventrsvps r ON " +
                Scope("r") + " AND r.eventid = " + e + " AND " + rsvpCondition + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            EventRsvpOutcome outcome = new EventRsvpOutcome();
            if (table.Rows.Count == 0) return outcome;
            DataRow row = table.Rows[0];
            outcome.Decision = Enum.TryParse(RowReader.GetNullableString(row, "decision") ?? "Missing", out EventRsvpDecisionEnum decision) ? decision : EventRsvpDecisionEnum.Missing;
            outcome.PromotedUserIds = (RowReader.GetNullableString(row, "promotedusers") ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (RowReader.GetNullableString(row, "id") != null) outcome.Rsvp = Map(row);
            return outcome;
        }

        private string PromotionBlock(string e, string? excludeRsvpId, DateTime nowUtc, DateTime promoteBeforeUtc)
        {
            string now = D.Ts(nowUtc);
            string exclude = excludeRsvpId == null ? String.Empty : " AND id <> " + D.Str(excludeRsvpId);
            return "DO $$ DECLARE ok BOOLEAN; cap INTEGER; used INTEGER; n INTEGER := 0; w RECORD; BEGIN " +
                "SELECT TRUE, capacity INTO ok, cap FROM events WHERE " + Scope() + " AND id = " + e + " AND status = 'Published' AND deletedutc IS NULL AND startutc > " + now + " AND " + now + " < " + D.Ts(promoteBeforeUtc) + "; " +
                "IF ok IS NULL THEN RETURN; END IF; " +
                "SELECT COALESCE(SUM(1 + guestcount), 0) INTO used FROM eventrsvps WHERE " + Scope() + " AND eventid = " + e + " AND status = 'Going'; " +
                "FOR w IN SELECT id, userid, guestcount FROM eventrsvps WHERE " + Scope() + " AND eventid = " + e + " AND status = 'Waitlisted'" + exclude + " ORDER BY waitlistedutc, id LOOP " +
                "EXIT WHEN cap IS NOT NULL AND used >= cap; " +
                "IF cap IS NULL OR used + 1 + w.guestcount <= cap THEN " +
                "UPDATE eventrsvps SET status = 'Going', waitlistedutc = NULL, promotedutc = " + now + ", updatedutc = " + now + " WHERE " + Scope() + " AND id = w.id; " +
                "used := used + 1 + w.guestcount; n := n + 1; " +
                "INSERT INTO " + _State + " (promoted, ord) VALUES (w.userid, n); " +
                "END IF; END LOOP; END $$;";
        }

        private string Counters(string e)
        {
            return "UPDATE events SET goingcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Going'), " +
                "guestcount = (SELECT COALESCE(SUM(c.guestcount), 0) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Going'), " +
                "interestedcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Interested'), " +
                "waitlistcount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.status = 'Waitlisted'), " +
                "checkedincount = (SELECT COUNT(*) FROM eventrsvps c WHERE " + Scope("c") + " AND c.eventid = events.id AND c.checkedinutc IS NOT NULL) WHERE " + Scope() + " AND id = " + e + ";";
        }

        private string Position(string alias)
        {
            return "CASE WHEN " + alias + ".status = 'Waitlisted' THEN (SELECT COUNT(*) FROM eventrsvps wp WHERE " + Scope("wp") + " AND wp.eventid = " + alias + ".eventid AND wp.status = 'Waitlisted' AND " +
                "(wp.waitlistedutc < " + alias + ".waitlistedutc OR (wp.waitlistedutc = " + alias + ".waitlistedutc AND wp.id <= " + alias + ".id))) ELSE NULL END";
        }

        private static EventRsvp Map(DataRow row)
        {
            long? position = row.Table.Columns.Contains("position") ? RowReader.GetNullableLong(row, "position") : null;
            return new EventRsvp
            {
                Id = RowReader.GetString(row, "id"),
                EventId = RowReader.GetString(row, "eventid"),
                UserId = RowReader.GetString(row, "userid"),
                Status = RowReader.GetEnum(row, "status", EventRsvpStatusEnum.Going),
                GuestCount = RowReader.GetInt(row, "guestcount"),
                HideFromAttendeeLists = RowReader.GetBool(row, "hidefromattendeelists"),
                WaitlistedUtc = RowReader.GetNullableDateTime(row, "waitlistedutc"),
                PromotedUtc = RowReader.GetNullableDateTime(row, "promotedutc"),
                CheckedInUtc = RowReader.GetNullableDateTime(row, "checkedinutc"),
                CheckedInByUserId = RowReader.GetNullableString(row, "checkedinbyuserid"),
                Position = position == null ? null : (int)position.Value,
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
