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

    internal abstract class VenueReviewMethodsBase : MethodsBase, IVenueReviewMethods
    {
        protected VenueReviewMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<VenueReview> CreateAsync(VenueReview review, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(review);
            RequireText(review.Id, nameof(review.Id));
            RequireText(review.VenueId, nameof(review.VenueId));
            RequireText(review.AuthorUserId, nameof(review.AuthorUserId));
            List<string> statements = new List<string>
            {
                "INSERT INTO venuereviews (propertyid, id, venueid, authoruserid, rating, body, openreportcount, moderationstate, createdutc, updatedutc) VALUES (" + Pid + ", " +
                    D.Str(review.Id) + ", " + D.Str(review.VenueId) + ", " + D.Str(review.AuthorUserId) + ", " + D.Num(review.Rating) + ", " + D.Str(review.Body) + ", 0, " +
                    D.Enum(review.ModerationState) + ", " + D.Ts(review.CreatedUtc) + ", " + D.Ts(review.UpdatedUtc) + ");",
                RecomputeStatement(review.VenueId)
            };
            await UniqueTransactionAsync(statements, "A review of this venue by this member", token).ConfigureAwait(false);
            return (await ReadAsync(review.Id, token).ConfigureAwait(false))!;
        }

        public async Task<VenueReview?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            List<VenueReview> rows = await LoadAsync("r.id = " + D.Str(id), token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        public async Task<List<VenueReview>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = DirectorySql.Distinct(ids);
            if (list.Count == 0) return new List<VenueReview>();
            return await LoadAsync("r.id IN " + D.InList(list), token).ConfigureAwait(false);
        }

        public async Task<VenueReview?> ReadByAuthorAsync(string venueId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(venueId) || String.IsNullOrEmpty(userId)) return null;
            List<VenueReview> rows = await LoadAsync("r.venueid = " + D.Str(venueId) + " AND r.authoruserid = " + D.Str(userId) + " AND r.deletedutc IS NULL", token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        public async Task<VenueReview?> UpdateAsync(VenueReview review, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(review);
            RequireText(review.Id, nameof(review.Id));
            List<string> statements = new List<string>
            {
                "UPDATE venuereviews SET rating = " + D.Num(review.Rating) + ", body = " + D.Str(review.Body) + ", editedutc = " + D.Ts(review.EditedUtc) + ", updatedutc = " + D.Ts(review.UpdatedUtc) +
                    " WHERE " + Scope() + " AND id = " + D.Str(review.Id) + " AND deletedutc IS NULL;",
                RecomputeStatement(review.VenueId)
            };
            await TransactionAsync(statements, token).ConfigureAwait(false);
            VenueReview? stored = await ReadAsync(review.Id, token).ConfigureAwait(false);
            return stored == null || stored.DeletedUtc != null ? null : stored;
        }

        public async Task<bool> SetReplyAsync(string id, string body, string userId, string instructorId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(userId, nameof(userId));
            string now = D.Ts(nowUtc);
            DataTable table = await QueryAsync("UPDATE venuereviews SET replybody = " + D.Str(body) + ", replyuserid = " + D.Str(userId) + ", replyinstructorid = " + D.Str(instructorId) +
                ", replyeditedutc = CASE WHEN replyutc IS NULL THEN NULL ELSE " + now + " END, replyutc = COALESCE(replyutc, " + now + "), updatedutc = " + now +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> ClearReplyAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE venuereviews SET replybody = NULL, replyuserid = NULL, replyinstructorid = NULL, replyutc = NULL, replyeditedutc = NULL, updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND replybody IS NOT NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            VenueReview? review = await ReadAsync(id, token).ConfigureAwait(false);
            if (review == null || review.DeletedUtc != null) return false;
            List<string> statements = new List<string>
            {
                "UPDATE venuereviews SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL;",
                RecomputeStatement(review.VenueId),
                "SELECT id FROM venuereviews WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc = " + D.Ts(nowUtc) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            VenueReview? review = await ReadAsync(id, token).ConfigureAwait(false);
            if (review == null || review.DeletedUtc != null) return false;
            List<string> statements = new List<string>
            {
                "UPDATE venuereviews SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(byUserId) + ", moderationreason = " + D.Str(reason) +
                    " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL;",
                RecomputeStatement(review.VenueId)
            };
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return true;
        }

        public async Task SetOpenReportCountAsync(string id, int count, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE venuereviews SET openreportcount = " + D.Num(Math.Max(0, count)) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<EnumerationResult<VenueReview>> EnumerateAsync(EnumerationRequest request, VenueContributionFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            RequireText(filter.VenueId, nameof(filter.VenueId));
            List<string> conditions = new List<string> { "r.venueid = " + D.Str(filter.VenueId), "r.deletedutc IS NULL" };
            if (!filter.IncludeModerated) conditions.Add("r.moderationstate = 'None'");
            List<string> hidden = DirectorySql.Distinct(filter.HiddenAuthorIds);
            if (hidden.Count > 0) conditions.Add("r.authoruserid NOT IN " + D.InList(hidden));
            if (!String.IsNullOrEmpty(filter.AuthorUserId)) conditions.Add("r.authoruserid = " + D.Str(filter.AuthorUserId));
            List<int> ratings = filter.Ratings.Where(r => r >= 1 && r <= 5).Distinct().ToList();
            if (ratings.Count > 0) conditions.Add("r.rating IN (" + String.Join(", ", ratings.Select(r => D.Num(r))) + ")");
            if (filter.HasReply != null) conditions.Add(filter.HasReply.Value ? "r.replybody IS NOT NULL" : "r.replybody IS NULL");
            return await EnumerateAsync(request, "r.*", "venuereviews r", conditions, Map, token).ConfigureAwait(false);
        }

        public async Task<Dictionary<int, int>> DistributionAsync(string venueId, CancellationToken token = default)
        {
            Dictionary<int, int> result = new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 } };
            if (String.IsNullOrEmpty(venueId)) return result;
            DataTable table = await QueryAsync("SELECT rating, COUNT(*) AS cnt FROM venuereviews WHERE " + Scope() + " AND venueid = " + D.Str(venueId) +
                " AND deletedutc IS NULL AND moderationstate = 'None' GROUP BY rating;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                int rating = RowReader.GetInt(row, "rating");
                if (result.ContainsKey(rating)) result[rating] = (int)Math.Min(Int32.MaxValue, RowReader.GetLong(row, "cnt"));
            }
            return result;
        }

        public async Task<int> EraseForUserAsync(string userId, DateTime nowUtc, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            int bound = Math.Clamp(limit, 1, 1000);
            string user = D.Str(userId);
            DataTable own = await QueryAsync("SELECT id, venueid FROM venuereviews WHERE " + Scope() + " AND authoruserid = " + user + " AND deletedutc IS NULL ORDER BY id LIMIT " + D.Num(bound) + ";", token).ConfigureAwait(false);
            DataTable replies = await QueryAsync("SELECT id, venueid FROM venuereviews WHERE " + Scope() + " AND replyuserid = " + user + " ORDER BY id LIMIT " + D.Num(bound) + ";", token).ConfigureAwait(false);
            List<string> ownIds = own.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
            List<string> replyIds = replies.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
            if (ownIds.Count == 0 && replyIds.Count == 0) return 0;
            List<string> venues = own.Rows.Cast<DataRow>().Concat(replies.Rows.Cast<DataRow>()).Select(r => RowReader.GetString(r, "venueid")).Distinct(StringComparer.Ordinal).ToList();
            List<string> statements = new List<string>();
            if (ownIds.Count > 0)
                statements.Add("UPDATE venuereviews SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN " + D.InList(ownIds) + " AND deletedutc IS NULL;");
            if (replyIds.Count > 0)
                statements.Add("UPDATE venuereviews SET replybody = NULL, replyuserid = NULL, replyinstructorid = NULL, replyutc = NULL, replyeditedutc = NULL, updatedutc = " + D.Ts(nowUtc) +
                    " WHERE " + Scope() + " AND id IN " + D.InList(replyIds) + ";");
            foreach (string venue in venues) statements.Add(RecomputeStatement(venue));
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return ownIds.Count + replyIds.Count;
        }

        private string RecomputeStatement(string venueId)
        {
            RequireText(venueId, nameof(venueId));
            string live = " FROM venuereviews c WHERE " + Scope("c") + " AND c.venueid = " + D.Str(venueId) + " AND c.deletedutc IS NULL AND c.moderationstate = 'None'";
            return "UPDATE venues SET ratingcount = (SELECT COUNT(*)" + live + "), ratingtotal = (SELECT COALESCE(SUM(c.rating), 0)" + live + ") WHERE " + Scope() + " AND id = " + D.Str(venueId) + ";";
        }

        private async Task<List<VenueReview>> LoadAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT r.* FROM venuereviews r WHERE " + Scope("r") + " AND " + condition + " ORDER BY r.id LIMIT 1000;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        private static VenueReview Map(DataRow row)
        {
            return new VenueReview
            {
                Id = RowReader.GetString(row, "id"),
                VenueId = RowReader.GetString(row, "venueid"),
                AuthorUserId = RowReader.GetString(row, "authoruserid"),
                Rating = RowReader.GetInt(row, "rating"),
                Body = RowReader.GetString(row, "body"),
                ReplyBody = RowReader.GetNullableString(row, "replybody"),
                ReplyUserId = RowReader.GetNullableString(row, "replyuserid"),
                ReplyInstructorId = RowReader.GetNullableString(row, "replyinstructorid"),
                ReplyUtc = RowReader.GetNullableDateTime(row, "replyutc"),
                ReplyEditedUtc = RowReader.GetNullableDateTime(row, "replyeditedutc"),
                EditedUtc = RowReader.GetNullableDateTime(row, "editedutc"),
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
