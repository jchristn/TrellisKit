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

    internal abstract class InstructorMethodsBase : MethodsBase, IInstructorMethods
    {
        protected InstructorMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public List<string> CreateStatements(Instructor instructor)
        {
            ArgumentNullException.ThrowIfNull(instructor);
            RequireText(instructor.Id, nameof(instructor.Id));
            RequireText(instructor.Slug, nameof(instructor.Slug));
            List<string> statements = new List<string>
            {
                "DELETE FROM instructorslugs WHERE " + Scope() + " AND lower(slug) = " + D.Str(instructor.Slug.ToLowerInvariant()) + ";",
                "INSERT INTO instructors (propertyid, id, slug, displayname, bio, photomediaid, rankkey, ranklevel, affiliation, linkeduserid, linkedutc, websiteurl, status, audience, venuecount, " +
                    "openreportcount, moderationstate, createdbyuserid, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(instructor.Id) + ", " + D.Str(instructor.Slug) + ", " + D.Str(instructor.DisplayName) + ", " +
                    D.Str(instructor.Bio) + ", " + D.Str(instructor.PhotoMediaId) + ", " + D.Str(instructor.RankKey) + ", " + D.Num(instructor.RankLevel) + ", " + D.Str(instructor.Affiliation) + ", " +
                    D.Str(instructor.LinkedUserId) + ", " + D.Ts(instructor.LinkedUtc) + ", " + D.Str(instructor.WebsiteUrl) + ", " + D.Enum(instructor.Status) + ", " + D.Enum(instructor.Audience) + ", 0, 0, " +
                    D.Enum(instructor.ModerationState) + ", " + D.Str(instructor.CreatedByUserId) + ", " + D.Ts(instructor.CreatedUtc) + ", " + D.Ts(instructor.UpdatedUtc) + ");"
            };
            statements.AddRange(LinkRows(instructor));
            return statements;
        }

        public List<string> UpdateStatements(Instructor instructor, string? previousSlug)
        {
            ArgumentNullException.ThrowIfNull(instructor);
            RequireText(instructor.Id, nameof(instructor.Id));
            string id = D.Str(instructor.Id);
            List<string> statements = new List<string>
            {
                "UPDATE instructors SET slug = " + D.Str(instructor.Slug) + ", displayname = " + D.Str(instructor.DisplayName) + ", bio = " + D.Str(instructor.Bio) + ", photomediaid = " + D.Str(instructor.PhotoMediaId) +
                    ", rankkey = " + D.Str(instructor.RankKey) + ", ranklevel = " + D.Num(instructor.RankLevel) + ", affiliation = " + D.Str(instructor.Affiliation) + ", websiteurl = " + D.Str(instructor.WebsiteUrl) +
                    ", audience = " + D.Enum(instructor.Audience) + ", updatedutc = " + D.Ts(instructor.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + id + " AND deletedutc IS NULL;",
                "DELETE FROM instructorsociallinks WHERE " + Scope() + " AND instructorid = " + id + ";"
            };
            if (!String.IsNullOrEmpty(previousSlug) && !String.Equals(previousSlug, instructor.Slug, StringComparison.OrdinalIgnoreCase))
            {
                statements.Add("DELETE FROM instructorslugs WHERE " + Scope() + " AND lower(slug) = " + D.Str(instructor.Slug.ToLowerInvariant()) + ";");
                statements.Add("INSERT INTO instructorslugs (propertyid, slug, instructorid, createdutc) VALUES (" + Pid + ", " + D.Str(previousSlug.ToLowerInvariant()) + ", " + id + ", " + D.Ts(instructor.UpdatedUtc) +
                    ") ON CONFLICT (propertyid, slug) DO UPDATE SET instructorid = EXCLUDED.instructorid, createdutc = EXCLUDED.createdutc;");
            }
            statements.AddRange(LinkRows(instructor));
            return statements;
        }

        public async Task<Instructor> CreateAsync(Instructor instructor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(instructor);
            await UniqueTransactionAsync(CreateStatements(instructor), "An instructor with this slug or member", token).ConfigureAwait(false);
            return (await ReadAsync(instructor.Id, token).ConfigureAwait(false))!;
        }

        public async Task<Instructor?> UpdateAsync(Instructor instructor, string? previousSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(instructor);
            await UniqueTransactionAsync(UpdateStatements(instructor, previousSlug), "An instructor with this slug", token).ConfigureAwait(false);
            return await ReadAsync(instructor.Id, token).ConfigureAwait(false);
        }

        public async Task<Instructor?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            return (await LoadAsync("i.id = " + D.Str(id), null, token).ConfigureAwait(false)).FirstOrDefault();
        }

        public async Task<Instructor?> ReadBySlugAsync(string slug, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return null;
            string s = D.Str(slug.Trim().ToLowerInvariant());
            List<Instructor> found = await LoadAsync("lower(i.slug) = " + s, null, token).ConfigureAwait(false);
            if (found.Count > 0) return found[0];
            found = await LoadAsync("i.id = (SELECT r.instructorid FROM instructorslugs r WHERE " + Scope("r") + " AND r.slug = " + s + ")", null, token).ConfigureAwait(false);
            return found.FirstOrDefault();
        }

        public async Task<List<Instructor>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = DirectorySql.Distinct(ids);
            if (list.Count == 0) return new List<Instructor>();
            return await LoadAsync("i.id IN " + D.InList(list), null, token).ConfigureAwait(false);
        }

        public async Task<Instructor?> ReadByLinkedUserAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) return null;
            return (await LoadAsync("i.linkeduserid = " + D.Str(userId), 1, token).ConfigureAwait(false)).FirstOrDefault();
        }

        public async Task<EnumerationResult<Instructor>> EnumerateAsync(EnumerationRequest request, InstructorFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string> { "i.deletedutc IS NULL" };
            if (filter.Statuses != null) conditions.Add(filter.Statuses.Count == 0 ? "1 = 0" : "i.status IN " + D.InList(filter.Statuses.Select(s => s.ToString())));
            else conditions.Add("i.status = 'Active'");
            if (filter.LiveOnly) conditions.Add("i.moderationstate = 'None'");
            if (filter.Audiences != null) conditions.Add(filter.Audiences.Count == 0 ? "1 = 0" : "i.audience IN " + D.InList(filter.Audiences.Select(a => a.ToString())));
            if (filter.AudienceFilter.Count > 0) conditions.Add("i.audience IN " + D.InList(filter.AudienceFilter.Select(a => a.ToString())));
            if (!String.IsNullOrEmpty(filter.VenueId))
                conditions.Add("EXISTS (SELECT 1 FROM venueinstructors fvi WHERE " + Scope("fvi") + " AND fvi.instructorid = i.id AND fvi.venueid = " + D.Str(filter.VenueId) + ")");
            if (filter.RankKeys.Count > 0) conditions.Add("i.rankkey IN " + D.InList(filter.RankKeys));
            if (filter.Claimed != null) conditions.Add(filter.Claimed.Value ? "i.linkeduserid IS NOT NULL" : "i.linkeduserid IS NULL");
            if (!String.IsNullOrEmpty(filter.LinkedUserId)) conditions.Add("i.linkeduserid = " + D.Str(filter.LinkedUserId));
            EnumerationResult<Instructor> page = await EnumerateAsync(request, "i.*", "instructors i", conditions, Map, token).ConfigureAwait(false);
            await FillAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public async Task<bool> SetLinkedUserAsync(string id, string? userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await UniqueAsync("UPDATE instructors SET linkeduserid = " + D.Str(userId) + ", linkedutc = " + (userId == null ? "NULL" : D.Ts(nowUtc)) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", "This member's instructor link", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public string LinkUserStatement(string id, string userId, DateTime nowUtc)
        {
            RequireText(id, nameof(id));
            RequireText(userId, nameof(userId));
            return "UPDATE instructors SET linkeduserid = " + D.Str(userId) + ", linkedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND deletedutc IS NULL AND linkeduserid IS NULL;";
        }

        public async Task<bool> SetStatusAsync(string id, InstructorStatusEnum status, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE instructors SET status = " + D.Enum(status) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE instructors SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(byUserId) + ", moderationreason = " + D.Str(reason) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task SetOpenReportCountAsync(string id, int count, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE instructors SET openreportcount = " + D.Num(Math.Max(0, count)) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string iid = D.Str(id);
            List<string> venues = await VenueIdsAsync(id, 1000, token).ConfigureAwait(false);
            List<string> statements = new List<string>
            {
                "UPDATE instructors SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + ", linkeduserid = NULL, linkedutc = NULL WHERE " + Scope() + " AND id = " + iid + " AND deletedutc IS NULL;",
                "DELETE FROM venueinstructors WHERE " + Scope() + " AND instructorid = " + iid + ";",
                "DELETE FROM instructorslugs WHERE " + Scope() + " AND instructorid = " + iid + ";"
            };
            foreach (string venue in venues) statements.Add(VenueCountStatement(venue));
            statements.Add("SELECT id FROM instructors WHERE " + Scope() + " AND id = " + iid + " AND deletedutc = " + D.Ts(nowUtc) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<string>> UnlinkUserAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("UPDATE instructors SET linkeduserid = NULL, linkedutc = NULL, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND linkeduserid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<List<Instructor>> DuplicateCandidatesAsync(IEnumerable<string> nameWords, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(nameWords);
            List<string> any = nameWords.Where(w => !String.IsNullOrWhiteSpace(w)).Distinct(StringComparer.Ordinal).Take(8).Select(w => D.Contains("lower(i.displayname)", w.ToLowerInvariant())).ToList();
            if (any.Count == 0) return new List<Instructor>();
            DataTable table = await QueryAsync("SELECT i.* FROM instructors i WHERE " + Scope("i") + " AND i.deletedutc IS NULL AND (" + String.Join(" OR ", any) + ")" +
                " ORDER BY i.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<List<Instructor>> ListPageAsync(string? afterId, int limit, CancellationToken token = default)
        {
            return LoadAsync(afterId == null ? "1 = 1" : "i.id > " + D.Str(afterId), Math.Max(1, limit), token);
        }

        public async Task<List<string>> VenueIdsAsync(string id, int limit, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT x.venueid FROM venueinstructors x JOIN venues v ON " + Scope("v") + " AND v.id = x.venueid AND v.deletedutc IS NULL WHERE " + Scope("x") +
                " AND x.instructorid = " + D.Str(id) + " ORDER BY x.venueid LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "venueid")).ToList();
        }

        private string VenueCountStatement(string venueId)
        {
            return "UPDATE venues SET instructorcount = (SELECT COUNT(*) FROM venueinstructors c JOIN instructors ci ON " + Scope("ci") + " AND ci.id = c.instructorid AND ci.deletedutc IS NULL WHERE " + Scope("c") +
                " AND c.venueid = " + D.Str(venueId) + ") WHERE " + Scope() + " AND id = " + D.Str(venueId) + ";";
        }

        private List<string> LinkRows(Instructor instructor)
        {
            List<string> statements = new List<string>();
            if (instructor.SocialLinks.Count > 0)
                statements.Add("INSERT INTO instructorsociallinks (propertyid, id, instructorid, kind, url, position) VALUES " + String.Join(", ", instructor.SocialLinks.Select((l, i) => "(" + Pid + ", " +
                    D.Str(IdGenerator.Generate(DirectoryIdPrefixes.SocialLink)) + ", " + D.Str(instructor.Id) + ", " + D.Enum(l.Kind) + ", " + D.Str(l.Url) + ", " + D.Num(i) + ")")) + ";");
            return statements;
        }

        private async Task<List<Instructor>> LoadAsync(string condition, int? limit, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT i.* FROM instructors i WHERE " + Scope("i") + " AND i.deletedutc IS NULL AND " + condition + " ORDER BY i.id LIMIT " + D.Num(limit ?? 1000) + ";", token).ConfigureAwait(false);
            List<Instructor> list = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillAsync(list, token).ConfigureAwait(false);
            return list;
        }

        private async Task FillAsync(List<Instructor> instructors, CancellationToken token)
        {
            if (instructors.Count == 0) return;
            Dictionary<string, Instructor> byId = instructors.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            DataTable links = await QueryAsync("SELECT * FROM instructorsociallinks WHERE " + Scope() + " AND instructorid IN " + D.InList(byId.Keys) + " ORDER BY instructorid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in links.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "instructorid"), out Instructor? i)) continue;
                i.SocialLinks.Add(new DirectorySocialLink
                {
                    Id = RowReader.GetString(row, "id"),
                    Kind = RowReader.GetEnum(row, "kind", SocialLinkKindEnum.Other),
                    Url = RowReader.GetString(row, "url"),
                    Position = RowReader.GetInt(row, "position")
                });
            }
        }

        private static Instructor Map(DataRow row)
        {
            return new Instructor
            {
                Id = RowReader.GetString(row, "id"),
                Slug = RowReader.GetString(row, "slug"),
                DisplayName = RowReader.GetString(row, "displayname"),
                Bio = RowReader.GetNullableString(row, "bio"),
                PhotoMediaId = RowReader.GetNullableString(row, "photomediaid"),
                RankKey = RowReader.GetNullableString(row, "rankkey"),
                RankLevel = RowReader.GetNullableInt(row, "ranklevel"),
                Affiliation = RowReader.GetNullableString(row, "affiliation"),
                LinkedUserId = RowReader.GetNullableString(row, "linkeduserid"),
                LinkedUtc = RowReader.GetNullableDateTime(row, "linkedutc"),
                WebsiteUrl = RowReader.GetNullableString(row, "websiteurl"),
                Status = RowReader.GetEnum(row, "status", InstructorStatusEnum.Active),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                VenueCount = RowReader.GetInt(row, "venuecount"),
                OpenReportCount = RowReader.GetInt(row, "openreportcount"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
