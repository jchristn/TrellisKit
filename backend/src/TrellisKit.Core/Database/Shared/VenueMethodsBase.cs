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

    internal abstract class VenueMethodsBase : MethodsBase, IVenueMethods
    {
        protected VenueMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public List<string> CreateStatements(Venue venue)
        {
            ArgumentNullException.ThrowIfNull(venue);
            RequireText(venue.Id, nameof(venue.Id));
            RequireText(venue.Slug, nameof(venue.Slug));
            List<string> statements = new List<string>
            {
                "DELETE FROM venueslugs WHERE " + Scope() + " AND lower(slug) = " + D.Str(venue.Slug.ToLowerInvariant()) + ";",
                "INSERT INTO venues (propertyid, id, slug, name, affiliation, summary, description, line1, line2, locality, region, postalcode, countrycode, addressvisibility, lat, lng, geosource, " +
                    "geocodedutc, geocodependingutc, timezone, phone, email, websiteurl, scheduletext, visitorpolicy, visitorpolicynote, isverified, verifiedutc, verifiedbyuserid, status, audience, " +
                    "instructorcount, openreportcount, moderationstate, createdbyuserid, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(venue.Id) + ", " + D.Str(venue.Slug) + ", " +
                    D.Str(venue.Name) + ", " + D.Str(venue.Affiliation) + ", " + D.Str(venue.Summary) + ", " + D.Str(venue.Description) + ", " + D.Str(venue.Line1) + ", " + D.Str(venue.Line2) + ", " +
                    D.Str(venue.Locality) + ", " + D.Str(venue.Region) + ", " + D.Str(venue.PostalCode) + ", " + D.Str(venue.CountryCode) + ", " + D.Enum(venue.AddressVisibility) + ", " +
                    DirectorySql.NullableNum(D, venue.Lat) + ", " + DirectorySql.NullableNum(D, venue.Lng) + ", " + D.Enum(venue.GeoSource) + ", " + D.Ts(venue.GeocodedUtc) + ", " + D.Ts(venue.GeocodePendingUtc) + ", " +
                    D.Str(venue.TimeZone) + ", " + D.Str(venue.Phone) + ", " + D.Str(venue.Email) + ", " + D.Str(venue.WebsiteUrl) + ", " + D.Str(venue.ScheduleText) + ", " + D.Enum(venue.VisitorPolicy) + ", " +
                    D.Str(venue.VisitorPolicyNote) + ", " + D.Bool(venue.IsVerified) + ", " + D.Ts(venue.VerifiedUtc) + ", " + D.Str(venue.VerifiedByUserId) + ", " + D.Enum(venue.Status) + ", " +
                    D.Enum(venue.Audience) + ", 0, 0, " + D.Enum(venue.ModerationState) + ", " + D.Str(venue.CreatedByUserId) + ", " + D.Ts(venue.CreatedUtc) + ", " + D.Ts(venue.UpdatedUtc) + ");"
            };
            statements.AddRange(ChildStatements(venue));
            return statements;
        }

        public List<string> UpdateStatements(Venue venue, string? previousSlug)
        {
            ArgumentNullException.ThrowIfNull(venue);
            RequireText(venue.Id, nameof(venue.Id));
            string id = D.Str(venue.Id);
            List<string> statements = new List<string>
            {
                "UPDATE venues SET slug = " + D.Str(venue.Slug) + ", name = " + D.Str(venue.Name) + ", affiliation = " + D.Str(venue.Affiliation) + ", summary = " + D.Str(venue.Summary) +
                    ", description = " + D.Str(venue.Description) + ", line1 = " + D.Str(venue.Line1) + ", line2 = " + D.Str(venue.Line2) + ", locality = " + D.Str(venue.Locality) +
                    ", region = " + D.Str(venue.Region) + ", postalcode = " + D.Str(venue.PostalCode) + ", countrycode = " + D.Str(venue.CountryCode) + ", addressvisibility = " + D.Enum(venue.AddressVisibility) +
                    ", lat = " + DirectorySql.NullableNum(D, venue.Lat) + ", lng = " + DirectorySql.NullableNum(D, venue.Lng) + ", geosource = " + D.Enum(venue.GeoSource) + ", geocodedutc = " + D.Ts(venue.GeocodedUtc) +
                    ", geocodependingutc = " + D.Ts(venue.GeocodePendingUtc) + ", timezone = " + D.Str(venue.TimeZone) + ", phone = " + D.Str(venue.Phone) + ", email = " + D.Str(venue.Email) +
                    ", websiteurl = " + D.Str(venue.WebsiteUrl) + ", scheduletext = " + D.Str(venue.ScheduleText) + ", visitorpolicy = " + D.Enum(venue.VisitorPolicy) + ", visitorpolicynote = " + D.Str(venue.VisitorPolicyNote) +
                    ", audience = " + D.Enum(venue.Audience) + ", updatedutc = " + D.Ts(venue.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + id + " AND deletedutc IS NULL;",
                "DELETE FROM venueprograms WHERE " + Scope() + " AND venueid = " + id + ";",
                "DELETE FROM venuescheduleentries WHERE " + Scope() + " AND venueid = " + id + ";",
                "DELETE FROM venuesociallinks WHERE " + Scope() + " AND venueid = " + id + ";",
                "DELETE FROM venuephotos WHERE " + Scope() + " AND venueid = " + id + ";"
            };
            if (!String.IsNullOrEmpty(previousSlug) && !String.Equals(previousSlug, venue.Slug, StringComparison.OrdinalIgnoreCase))
            {
                statements.Add("DELETE FROM venueslugs WHERE " + Scope() + " AND lower(slug) = " + D.Str(venue.Slug.ToLowerInvariant()) + ";");
                statements.Add("INSERT INTO venueslugs (propertyid, slug, venueid, createdutc) VALUES (" + Pid + ", " + D.Str(previousSlug.ToLowerInvariant()) + ", " + id + ", " + D.Ts(venue.UpdatedUtc) +
                    ") ON CONFLICT (propertyid, slug) DO UPDATE SET venueid = EXCLUDED.venueid, createdutc = EXCLUDED.createdutc;");
            }
            statements.AddRange(ChildStatements(venue));
            return statements;
        }

        public async Task<Venue> CreateAsync(Venue venue, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(venue);
            await UniqueTransactionAsync(CreateStatements(venue), "A venue with this slug", token).ConfigureAwait(false);
            return (await ReadAsync(venue.Id, token).ConfigureAwait(false))!;
        }

        public async Task<Venue?> UpdateAsync(Venue venue, string? previousSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(venue);
            await UniqueTransactionAsync(UpdateStatements(venue, previousSlug), "A venue with this slug", token).ConfigureAwait(false);
            return await ReadAsync(venue.Id, token).ConfigureAwait(false);
        }

        public async Task<Venue?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            List<Venue> venues = await LoadAsync("v.id = " + D.Str(id), null, token).ConfigureAwait(false);
            return venues.FirstOrDefault();
        }

        public async Task<Venue?> ReadBySlugAsync(string slug, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return null;
            string s = D.Str(slug.Trim().ToLowerInvariant());
            List<Venue> venues = await LoadAsync("lower(v.slug) = " + s, null, token).ConfigureAwait(false);
            if (venues.Count > 0) return venues[0];
            venues = await LoadAsync("v.id = (SELECT r.venueid FROM venueslugs r WHERE " + Scope("r") + " AND r.slug = " + s + ")", null, token).ConfigureAwait(false);
            return venues.FirstOrDefault();
        }

        public async Task<List<Venue>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = DirectorySql.Distinct(ids);
            if (list.Count == 0) return new List<Venue>();
            return await LoadAsync("v.id IN " + D.InList(list), null, token).ConfigureAwait(false);
        }

        public async Task<EnumerationResult<Venue>> EnumerateAsync(EnumerationRequest request, VenueFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = Conditions(filter);
            string select = "v.*";
            if (filter.Near != null)
            {
                select += ", " + DirectorySql.Haversine(D, filter.Near.Lat, filter.Near.Lng, "v") + " AS distancekm";
                if (String.Equals(request.Ordering.Name, "DistanceAscending", StringComparison.OrdinalIgnoreCase))
                    request.Ordering = new EnumerationOrdering { Name = "DistanceAscending", Expression = DirectorySql.DistanceKey(D, filter.Near.Lat, filter.Near.Lng, "v"), Descending = false, KeyKind = EnumerationKeyKindEnum.Number };
            }
            if (String.Equals(request.Ordering.Name, "Relevance", StringComparison.OrdinalIgnoreCase))
            {
                string search = (request.Query.Search ?? String.Empty).ToLowerInvariant();
                string expression = search.Length == 0 ? "0" : "(CASE WHEN lower(v.name) = " + D.Str(search) + " THEN 0 WHEN " + D.StartsWith("lower(v.name)", search) + " THEN 1 WHEN " +
                    D.Contains("lower(v.name)", search) + " THEN 2 ELSE 3 END)";
                request.Ordering = new EnumerationOrdering { Name = "Relevance", Expression = expression, Descending = false, KeyKind = EnumerationKeyKindEnum.Number };
            }
            EnumerationResult<Venue> page = await EnumerateAsync(request, select, "venues v", conditions, Map, token).ConfigureAwait(false);
            await FillAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public Task<long> CountMapAsync(EnumerationRequest request, VenueFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            return CountAsync("SELECT COUNT(*) AS cnt FROM venues v WHERE " + MapConditions(request, filter) + ";", token);
        }

        public async Task<List<Venue>> MapPointsAsync(EnumerationRequest request, VenueFilter filter, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            DataTable table = await QueryAsync("SELECT v.* FROM venues v WHERE " + MapConditions(request, filter) + " ORDER BY v.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            List<Venue> venues = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillAsync(venues, token).ConfigureAwait(false);
            return venues;
        }

        public async Task<List<MapCluster>> MapClustersAsync(EnumerationRequest request, VenueFilter filter, double cellDegrees, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            string approximate = "v.addressvisibility = 'Approximate'";
            DataTable table = await QueryAsync(DirectorySql.ClusterQuery(D, DirectorySql.MapCoordinate(approximate, "v.lat"), DirectorySql.MapCoordinate(approximate, "v.lng"), "v.lat", "v.lng", "v.id",
                "venues v", MapConditions(request, filter), cellDegrees, limit), token).ConfigureAwait(false);
            return DirectorySql.Clusters(table);
        }

        public async Task<bool> SetVerifiedAsync(string id, bool verified, string? byUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE venues SET isverified = " + D.Bool(verified) + ", verifiedutc = " + (verified ? D.Ts(nowUtc) : "NULL") + ", verifiedbyuserid = " + (verified ? D.Str(byUserId) : "NULL") +
                ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetStatusAsync(string id, VenueStatusEnum status, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string closed = status == VenueStatusEnum.Closed ? ", closedutc = COALESCE(closedutc, " + D.Ts(nowUtc) + "), closedreason = " + D.Str(reason) : status == VenueStatusEnum.Active ? ", closedutc = NULL, closedreason = NULL" : String.Empty;
            DataTable table = await QueryAsync("UPDATE venues SET status = " + D.Enum(status) + closed + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public string CloseStatement(string id, string? reason, DateTime nowUtc)
        {
            RequireText(id, nameof(id));
            return "UPDATE venues SET status = " + D.Enum(VenueStatusEnum.Closed) + ", closedutc = COALESCE(closedutc, " + D.Ts(nowUtc) + "), closedreason = " + D.Str(reason) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL;";
        }

        public async Task<bool> SetModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE venues SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(byUserId) + ", moderationreason = " + D.Str(reason) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task SetOpenReportCountAsync(string id, int count, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE venues SET openreportcount = " + D.Num(Math.Max(0, count)) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string vid = D.Str(id);
            DataTable linked = await QueryAsync("SELECT instructorid FROM venueinstructors WHERE " + Scope() + " AND venueid = " + vid + " ORDER BY instructorid LIMIT 1000;", token).ConfigureAwait(false);
            List<string> instructors = linked.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "instructorid")).ToList();
            List<string> statements = new List<string>
            {
                "UPDATE venues SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + ", geocodependingutc = NULL WHERE " + Scope() + " AND id = " + vid + " AND deletedutc IS NULL;",
                "DELETE FROM venueinstructors WHERE " + Scope() + " AND venueid = " + vid + ";",
                "DELETE FROM venueslugs WHERE " + Scope() + " AND venueid = " + vid + ";"
            };
            foreach (string instructor in instructors) statements.Add(InstructorCountStatement(instructor));
            statements.Add("SELECT id FROM venues WHERE " + Scope() + " AND id = " + vid + " AND deletedutc = " + D.Ts(nowUtc) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetGeocodedAsync(string id, double? lat, double? lng, string? timeZone, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string set = lat != null && lng != null
                ? "lat = " + D.Num(lat.Value) + ", lng = " + D.Num(lng.Value) + ", geosource = " + D.Enum(GeoSourceEnum.Geocoder) + ", geocodedutc = " + D.Ts(nowUtc) + ", timezone = COALESCE(timezone, " + D.Str(timeZone) + ")"
                : "lat = NULL, lng = NULL, geosource = " + D.Enum(GeoSourceEnum.None) + ", geocodedutc = NULL";
            DataTable table = await QueryAsync("UPDATE venues SET " + set + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL AND geosource <> " + D.Enum(GeoSourceEnum.Manual) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> QueueGeocodeAsync(List<string>? ids, bool onlyMissing, DateTime nowUtc, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { Scope(), "deletedutc IS NULL", "geosource <> " + D.Enum(GeoSourceEnum.Manual), "countrycode IS NOT NULL" };
            if (ids != null)
            {
                List<string> list = DirectorySql.Distinct(ids);
                if (list.Count == 0) return 0;
                conditions.Add("id IN " + D.InList(list));
            }
            if (onlyMissing) conditions.Add("(lat IS NULL OR lng IS NULL)");
            DataTable table = await QueryAsync("UPDATE venues SET geocodependingutc = " + D.Ts(nowUtc) + Where(conditions) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<List<Venue>> ClaimGeocodeAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("UPDATE venues SET geocodependingutc = NULL WHERE " + Scope() + " AND id IN (SELECT q.id FROM venues q WHERE " + Scope("q") +
                " AND q.geocodependingutc IS NOT NULL AND q.deletedutc IS NULL ORDER BY q.geocodependingutc, q.id LIMIT " + D.Num(Math.Max(1, limit)) + ") AND geocodependingutc IS NOT NULL RETURNING id;", token).ConfigureAwait(false);
            List<string> ids = table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
            return ids.Count == 0 ? new List<Venue>() : await ReadManyAsync(ids, token).ConfigureAwait(false);
        }

        public async Task<VenueInstructorLink> LinkAsync(string venueId, string instructorId, VenueInstructorRoleEnum role, DateTime nowUtc, CancellationToken token = default)
        {
            List<string> statements = LinkStatements(venueId, instructorId, role, nowUtc);
            statements.Add("SELECT * FROM venueinstructors WHERE " + Scope() + " AND venueid = " + D.Str(venueId) + " AND instructorid = " + D.Str(instructorId) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            DataRow row = table.Rows[0];
            return new VenueInstructorLink
            {
                Id = RowReader.GetString(row, "id"),
                VenueId = RowReader.GetString(row, "venueid"),
                InstructorId = RowReader.GetString(row, "instructorid"),
                Role = RowReader.GetEnum(row, "role", VenueInstructorRoleEnum.Regular),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        public List<string> LinkStatements(string venueId, string instructorId, VenueInstructorRoleEnum role, DateTime nowUtc)
        {
            RequireText(venueId, nameof(venueId));
            RequireText(instructorId, nameof(instructorId));
            return new List<string>
            {
                "INSERT INTO venueinstructors (propertyid, id, venueid, instructorid, role, createdutc) VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate(DirectoryIdPrefixes.VenueInstructor)) + ", " +
                    D.Str(venueId) + ", " + D.Str(instructorId) + ", " + D.Enum(role) + ", " + D.Ts(nowUtc) + ") ON CONFLICT (propertyid, venueid, instructorid) DO UPDATE SET role = EXCLUDED.role;",
                VenueCountStatement(venueId),
                InstructorCountStatement(instructorId)
            };
        }

        public async Task<bool> UnlinkAsync(string venueId, string instructorId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(venueId, nameof(venueId));
            RequireText(instructorId, nameof(instructorId));
            List<string> statements = new List<string>
            {
                "CREATE TEMP TABLE IF NOT EXISTS venueunlinkstate (removed INTEGER) ON COMMIT DROP;",
                "WITH d AS (DELETE FROM venueinstructors WHERE " + Scope() + " AND venueid = " + D.Str(venueId) + " AND instructorid = " + D.Str(instructorId) + " RETURNING id) INSERT INTO venueunlinkstate (removed) SELECT COUNT(*) FROM d;",
                VenueCountStatement(venueId),
                InstructorCountStatement(instructorId),
                "SELECT removed FROM venueunlinkstate;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0 && RowReader.GetInt(table.Rows[0], "removed") > 0;
        }

        public async Task<Dictionary<string, VenueInstructorRoleEnum>> RolesAsync(string venueId, IEnumerable<string> instructorIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(instructorIds);
            Dictionary<string, VenueInstructorRoleEnum> result = new Dictionary<string, VenueInstructorRoleEnum>(StringComparer.Ordinal);
            List<string> ids = DirectorySql.Distinct(instructorIds);
            if (String.IsNullOrEmpty(venueId) || ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT instructorid, role FROM venueinstructors WHERE " + Scope() + " AND venueid = " + D.Str(venueId) + " AND instructorid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "instructorid")] = RowReader.GetEnum(row, "role", VenueInstructorRoleEnum.Regular);
            return result;
        }

        public async Task<List<Venue>> DuplicateCandidatesAsync(IEnumerable<string> nameWords, GeoBox? box, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(nameWords);
            List<string> any = nameWords.Where(w => !String.IsNullOrWhiteSpace(w)).Distinct(StringComparer.Ordinal).Take(8).Select(w => D.Contains("lower(v.name)", w.ToLowerInvariant())).ToList();
            if (box != null) any.Add(DirectorySql.Box(D, box, "v.lat", "v.lng"));
            if (any.Count == 0) return new List<Venue>();
            DataTable table = await QueryAsync("SELECT v.* FROM venues v WHERE " + Scope("v") + " AND v.deletedutc IS NULL AND v.status <> " + D.Enum(VenueStatusEnum.Archived) + " AND (" + String.Join(" OR ", any) +
                ") ORDER BY v.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<Venue>> ListPageAsync(string? afterId, int limit, CancellationToken token = default)
        {
            List<Venue> venues = await LoadAsync(afterId == null ? "1 = 1" : "v.id > " + D.Str(afterId), Math.Max(1, limit), token).ConfigureAwait(false);
            return venues;
        }

        public async Task<DirectoryMetricsView> MetricsAsync(DateTime nowUtc, CancellationToken token = default)
        {
            string live = Scope() + " AND deletedutc IS NULL";
            DataTable venues = await QueryAsync("SELECT COUNT(*) FILTER (WHERE status = 'Active') AS active, COUNT(*) FILTER (WHERE status = 'Closed') AS closed, COUNT(*) FILTER (WHERE status = 'Archived') AS archived, " +
                "COUNT(*) FILTER (WHERE status = 'Active' AND isverified) AS verified, COUNT(*) FILTER (WHERE geosource = 'None') AS needspin FROM venues WHERE " + live + ";", token).ConfigureAwait(false);
            DataTable instructors = await QueryAsync("SELECT COUNT(*) FILTER (WHERE status = 'Active') AS active, COUNT(*) FILTER (WHERE linkeduserid IS NOT NULL) AS claimed FROM instructors WHERE " + live + ";", token).ConfigureAwait(false);
            DateTime since = nowUtc.AddDays(-30);
            DataTable submissions = await QueryAsync("SELECT COUNT(*) FILTER (WHERE status = 'Pending') AS pending, COUNT(*) FILTER (WHERE status = 'Pending' AND dueutc <= " + D.Ts(nowUtc) + ") AS overdue, " +
                "COUNT(*) FILTER (WHERE createdutc >= " + D.Ts(since) + ") AS recent, COUNT(*) FILTER (WHERE status = 'Approved' AND decidedutc >= " + D.Ts(since) + ") AS approved FROM directorysubmissions WHERE " + Scope() + ";", token).ConfigureAwait(false);
            DataRow v = venues.Rows[0];
            DataRow i = instructors.Rows[0];
            DataRow s = submissions.Rows[0];
            return new DirectoryMetricsView
            {
                ActiveVenues = RowReader.GetLong(v, "active"),
                ClosedVenues = RowReader.GetLong(v, "closed"),
                ArchivedVenues = RowReader.GetLong(v, "archived"),
                VerifiedVenues = RowReader.GetLong(v, "verified"),
                NeedsPin = RowReader.GetLong(v, "needspin"),
                Instructors = RowReader.GetLong(i, "active"),
                ClaimedInstructors = RowReader.GetLong(i, "claimed"),
                PendingSubmissions = RowReader.GetLong(s, "pending"),
                OverdueSubmissions = RowReader.GetLong(s, "overdue"),
                SubmissionsLast30Days = RowReader.GetLong(s, "recent"),
                ApprovedLast30Days = RowReader.GetLong(s, "approved")
            };
        }

        public async Task<List<DailyCount>> DailyAsync(string series, DateTime fromUtc, int days, CancellationToken token = default)
        {
            string from = D.Ts(fromUtc);
            string sql;
            switch (series)
            {
                case "submissions":
                    sql = "SELECT " + D.DayOf("s.createdutc") + " AS day, COUNT(*) AS cnt FROM directorysubmissions s WHERE " + Scope("s") + " AND s.createdutc >= " + from + " GROUP BY 1;";
                    break;
                case "approvals":
                    sql = "SELECT " + D.DayOf("s.decidedutc") + " AS day, COUNT(*) AS cnt FROM directorysubmissions s WHERE " + Scope("s") + " AND s.status = 'Approved' AND s.decidedutc >= " + from + " GROUP BY 1;";
                    break;
                case "venues":
                    sql = "SELECT " + D.DayOf("v.createdutc") + " AS day, COUNT(*) AS cnt FROM venues v WHERE " + Scope("v") + " AND v.deletedutc IS NULL AND v.createdutc >= " + from + " GROUP BY 1;";
                    break;
                default:
                    throw new ArgumentException("Unknown daily series " + series + ".", nameof(series));
            }
            DataTable rows = await QueryAsync(sql, token).ConfigureAwait(false);
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DataRow row in rows.Rows) counts[RowReader.GetString(row, "day")] = RowReader.GetLong(row, "cnt");
            List<DailyCount> result = new List<DailyCount>();
            for (int i = 0; i < days; i++)
            {
                string key = fromUtc.Date.AddDays(i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                result.Add(new DailyCount { Date = key, Count = counts.TryGetValue(key, out long c) ? c : 0 });
            }
            return result;
        }

        private string VenueCountStatement(string venueId)
        {
            return "UPDATE venues SET instructorcount = (SELECT COUNT(*) FROM venueinstructors c JOIN instructors ci ON " + Scope("ci") + " AND ci.id = c.instructorid AND ci.deletedutc IS NULL WHERE " + Scope("c") +
                " AND c.venueid = " + D.Str(venueId) + ") WHERE " + Scope() + " AND id = " + D.Str(venueId) + ";";
        }

        private string InstructorCountStatement(string instructorId)
        {
            return "UPDATE instructors SET venuecount = (SELECT COUNT(*) FROM venueinstructors c JOIN venues cv ON " + Scope("cv") + " AND cv.id = c.venueid AND cv.deletedutc IS NULL WHERE " + Scope("c") +
                " AND c.instructorid = " + D.Str(instructorId) + ") WHERE " + Scope() + " AND id = " + D.Str(instructorId) + ";";
        }

        private List<string> ChildStatements(Venue venue)
        {
            List<string> statements = new List<string>();
            string id = D.Str(venue.Id);
            List<string> programs = venue.Programs.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (programs.Count > 0)
                statements.Add("INSERT INTO venueprograms (propertyid, venueid, programkey) VALUES " + String.Join(", ", programs.Select(p => "(" + Pid + ", " + id + ", " + D.Str(p) + ")")) + " ON CONFLICT DO NOTHING;");
            if (venue.SocialLinks.Count > 0)
                statements.Add("INSERT INTO venuesociallinks (propertyid, id, venueid, kind, url, position) VALUES " + String.Join(", ", venue.SocialLinks.Select((l, i) => "(" + Pid + ", " +
                    D.Str(IdGenerator.Generate(DirectoryIdPrefixes.SocialLink)) + ", " + id + ", " + D.Enum(l.Kind) + ", " + D.Str(l.Url) + ", " + D.Num(i) + ")")) + ";");
            statements.Add("UPDATE venues SET hasschedule = " + D.Bool(venue.Schedule != null) + ", scheduletimezone = " + D.Str(venue.Schedule?.TimeZone) + ", schedulenotes = " + D.Str(venue.Schedule?.Notes) + " WHERE " + Scope() + " AND id = " + id + ";");
            if (venue.Schedule != null)
            {
                void Entries(string kind, List<VenueScheduleEntry> entries)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        VenueScheduleEntry e = entries[i];
                        statements.Add("INSERT INTO venuescheduleentries (propertyid, venueid, kind, position, dayofweek, starttime, endtime, endsnextday, name, instructor) VALUES (" + Pid + ", " + id + ", " + D.Str(kind) + ", " + D.Num(i) + ", " + D.Num(e.DayOfWeek) + ", " + D.Str(e.StartTime) + ", " + D.Str(e.EndTime) + ", " + D.Bool(e.EndsNextDay) + ", " + D.Str(e.Name) + ", " + D.Str(e.Instructor) + ");");
                    }
                }
                Entries("Hours", venue.Schedule.OpeningHours);
                Entries("Class", venue.Schedule.Classes);
            }
            List<string> photos = DirectorySql.Distinct(venue.PhotoMediaIds);
            if (photos.Count > 0)
                statements.Add("INSERT INTO venuephotos (propertyid, venueid, mediaid, position) VALUES " + String.Join(", ", photos.Select((m, i) => "(" + Pid + ", " + id + ", " + D.Str(m) + ", " + D.Num(i) + ")")) + " ON CONFLICT DO NOTHING;");
            return statements;
        }

        private string MapConditions(EnumerationRequest request, VenueFilter filter)
        {
            List<string> all = Conditions(filter);
            all.AddRange(EnumerationConditions(request));
            return String.Join(" AND ", all);
        }

        private List<string> Conditions(VenueFilter filter)
        {
            List<string> conditions = new List<string> { "v.deletedutc IS NULL" };
            if (filter.Statuses != null) conditions.Add(filter.Statuses.Count == 0 ? "1 = 0" : "v.status IN " + D.InList(filter.Statuses.Select(s => s.ToString())));
            else if (!filter.Staff) conditions.Add(filter.IncludeClosed ? "v.status IN ('Active', 'Closed')" : "v.status = 'Active'");
            else if (!filter.IncludeClosed) conditions.Add("v.status <> 'Closed'");
            if (filter.LiveOnly) conditions.Add("v.moderationstate = 'None'");
            if (filter.Audiences != null) conditions.Add(filter.Audiences.Count == 0 ? "1 = 0" : "v.audience IN " + D.InList(filter.Audiences.Select(a => a.ToString())));
            if (filter.AudienceFilter.Count > 0) conditions.Add("v.audience IN " + D.InList(filter.AudienceFilter.Select(a => a.ToString())));
            if (filter.Near != null)
            {
                List<GeoBox> boxes = GeoMath.NearBoxes(filter.Near);
                conditions.Add("v.lat IS NOT NULL AND v.lng IS NOT NULL");
                conditions.Add("(" + String.Join(" OR ", boxes.Select(b => DirectorySql.Box(D, b, "v.lat", "v.lng"))) + ")");
                conditions.Add(DirectorySql.Haversine(D, filter.Near.Lat, filter.Near.Lng, "v") + " <= " + D.Num(filter.Near.RadiusKm));
            }
            if (filter.Box != null)
            {
                conditions.Add("v.lat IS NOT NULL AND v.lng IS NOT NULL");
                conditions.Add(DirectorySql.Box(D, filter.Box, "v.lat", "v.lng"));
            }
            if (filter.ExcludeLocalityOnly) conditions.Add("v.addressvisibility <> 'LocalityOnly'");
            if (filter.Programs.Count > 0)
                conditions.Add("EXISTS (SELECT 1 FROM venueprograms vp WHERE " + Scope("vp") + " AND vp.venueid = v.id AND vp.programkey IN " + D.InList(filter.Programs) + ")");
            if (!String.IsNullOrWhiteSpace(filter.CountryCode)) conditions.Add("lower(v.countrycode) = " + D.Str(filter.CountryCode.Trim().ToLowerInvariant()));
            if (!String.IsNullOrWhiteSpace(filter.Region)) conditions.Add("lower(v.region) = " + D.Str(filter.Region.Trim().ToLowerInvariant()));
            if (!String.IsNullOrWhiteSpace(filter.Locality)) conditions.Add("lower(v.locality) = " + D.Str(filter.Locality.Trim().ToLowerInvariant()));
            if (filter.VisitorPolicies.Count > 0) conditions.Add("v.visitorpolicy IN " + D.InList(filter.VisitorPolicies.Select(p => p.ToString())));
            if (filter.VerifiedOnly) conditions.Add("v.isverified = " + D.Bool(true));
            if (filter.Verified != null) conditions.Add("v.isverified = " + D.Bool(filter.Verified.Value));
            if (filter.GeoSource != null) conditions.Add("v.geosource = " + D.Enum(filter.GeoSource.Value));
            if (!String.IsNullOrEmpty(filter.InstructorId))
                conditions.Add("EXISTS (SELECT 1 FROM venueinstructors fvi WHERE " + Scope("fvi") + " AND fvi.venueid = v.id AND fvi.instructorid = " + D.Str(filter.InstructorId) + ")");
            return conditions;
        }

        private async Task<List<Venue>> LoadAsync(string condition, int? limit, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT v.* FROM venues v WHERE " + Scope("v") + " AND v.deletedutc IS NULL AND " + condition + " ORDER BY v.id" +
                (limit == null ? " LIMIT 1000" : " LIMIT " + D.Num(limit.Value)) + ";", token).ConfigureAwait(false);
            List<Venue> venues = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillAsync(venues, token).ConfigureAwait(false);
            return venues;
        }

        private async Task FillAsync(List<Venue> venues, CancellationToken token)
        {
            if (venues.Count == 0) return;
            Dictionary<string, Venue> byId = venues.GroupBy(v => v.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            string ids = D.InList(byId.Keys);
            DataTable programs = await QueryAsync("SELECT venueid, programkey FROM venueprograms WHERE " + Scope() + " AND venueid IN " + ids + " ORDER BY venueid, programkey;", token).ConfigureAwait(false);
            foreach (DataRow row in programs.Rows)
                if (byId.TryGetValue(RowReader.GetString(row, "venueid"), out Venue? v)) v.Programs.Add(RowReader.GetString(row, "programkey"));
            DataTable links = await QueryAsync("SELECT * FROM venuesociallinks WHERE " + Scope() + " AND venueid IN " + ids + " ORDER BY venueid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in links.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "venueid"), out Venue? v)) continue;
                v.SocialLinks.Add(new DirectorySocialLink
                {
                    Id = RowReader.GetString(row, "id"),
                    Kind = RowReader.GetEnum(row, "kind", SocialLinkKindEnum.Other),
                    Url = RowReader.GetString(row, "url"),
                    Position = RowReader.GetInt(row, "position")
                });
            }
            DataTable scheduleRows = await QueryAsync("SELECT * FROM venuescheduleentries WHERE " + Scope() + " AND venueid IN " + ids + " ORDER BY venueid, kind, position;", token).ConfigureAwait(false);
            foreach (DataRow row in scheduleRows.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "venueid"), out Venue? v) || v.Schedule == null) continue;
                VenueScheduleEntry entry = new VenueScheduleEntry { DayOfWeek = RowReader.GetInt(row, "dayofweek"), StartTime = RowReader.GetString(row, "starttime"), EndTime = RowReader.GetString(row, "endtime"), EndsNextDay = RowReader.GetBool(row, "endsnextday"), Name = RowReader.GetNullableString(row, "name"), Instructor = RowReader.GetNullableString(row, "instructor") };
                (RowReader.GetString(row, "kind") == "Class" ? v.Schedule.Classes : v.Schedule.OpeningHours).Add(entry);
            }
            DataTable photos = await QueryAsync("SELECT venueid, mediaid FROM venuephotos WHERE " + Scope() + " AND venueid IN " + ids + " ORDER BY venueid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in photos.Rows)
                if (byId.TryGetValue(RowReader.GetString(row, "venueid"), out Venue? v)) v.PhotoMediaIds.Add(RowReader.GetString(row, "mediaid"));
        }

        private static Venue Map(DataRow row)
        {
            return new Venue
            {
                Id = RowReader.GetString(row, "id"),
                Slug = RowReader.GetString(row, "slug"),
                Name = RowReader.GetString(row, "name"),
                Affiliation = RowReader.GetNullableString(row, "affiliation"),
                Summary = RowReader.GetNullableString(row, "summary"),
                Description = RowReader.GetNullableString(row, "description"),
                Line1 = RowReader.GetNullableString(row, "line1"),
                Line2 = RowReader.GetNullableString(row, "line2"),
                Locality = RowReader.GetNullableString(row, "locality"),
                Region = RowReader.GetNullableString(row, "region"),
                PostalCode = RowReader.GetNullableString(row, "postalcode"),
                CountryCode = RowReader.GetNullableString(row, "countrycode"),
                AddressVisibility = RowReader.GetEnum(row, "addressvisibility", AddressVisibilityEnum.Exact),
                Lat = DirectorySql.NullableDouble(row, "lat"),
                Lng = DirectorySql.NullableDouble(row, "lng"),
                GeoSource = RowReader.GetEnum(row, "geosource", GeoSourceEnum.None),
                GeocodedUtc = RowReader.GetNullableDateTime(row, "geocodedutc"),
                GeocodePendingUtc = RowReader.GetNullableDateTime(row, "geocodependingutc"),
                TimeZone = RowReader.GetNullableString(row, "timezone"),
                Phone = RowReader.GetNullableString(row, "phone"),
                Email = RowReader.GetNullableString(row, "email"),
                WebsiteUrl = RowReader.GetNullableString(row, "websiteurl"),
                ScheduleText = RowReader.GetNullableString(row, "scheduletext"),
                Schedule = RowReader.GetBool(row, "hasschedule") ? new VenueSchedule { TimeZone = RowReader.GetNullableString(row, "scheduletimezone"), Notes = RowReader.GetNullableString(row, "schedulenotes") } : null,
                VisitorPolicy = RowReader.GetEnum(row, "visitorpolicy", VisitorPolicyEnum.Unknown),
                VisitorPolicyNote = RowReader.GetNullableString(row, "visitorpolicynote"),
                IsVerified = RowReader.GetBool(row, "isverified"),
                VerifiedUtc = RowReader.GetNullableDateTime(row, "verifiedutc"),
                VerifiedByUserId = RowReader.GetNullableString(row, "verifiedbyuserid"),
                Status = RowReader.GetEnum(row, "status", VenueStatusEnum.Active),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                InstructorCount = RowReader.GetInt(row, "instructorcount"),
                RatingCount = RowReader.GetInt(row, "ratingcount"),
                RatingTotal = RowReader.GetInt(row, "ratingtotal"),
                MediaCount = RowReader.GetInt(row, "mediacount"),
                OpenReportCount = RowReader.GetInt(row, "openreportcount"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                ClosedUtc = RowReader.GetNullableDateTime(row, "closedutc"),
                ClosedReason = RowReader.GetNullableString(row, "closedreason"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc"),
                DistanceKm = DirectorySql.NullableDouble(row, "distancekm")
            };
        }
    }
}
