namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared public landing page methods (docs/PHASE4_CONTRACT.md section 18).
    /// </summary>
    internal abstract class PublicPageMethodsBase : MethodsBase, IPublicPageMethods
    {
        protected PublicPageMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<ProfilePage?> ReadProfileAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(userId)) return null;
            DataTable table = await QueryAsync("SELECT * FROM profilepages WHERE " + Scope() + " AND userid = " + D.Str(userId.Trim()) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<ProfilePage?> ReadProfileBySlugAsync(string slug, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return null;
            string s = D.Str(slug.Trim().ToLowerInvariant());
            DataTable table = await QueryAsync("SELECT * FROM profilepages WHERE " + Scope() + " AND slug IS NOT NULL AND lower(slug) = " + s + ";", token).ConfigureAwait(false);
            if (table.Rows.Count > 0) return Map(table.Rows[0]);
            table = await QueryAsync("SELECT p.* FROM profilepages p WHERE " + Scope("p") + " AND p.slug IS NOT NULL AND p.userid = (SELECT r.userid FROM profileslugs r WHERE " + Scope("r") +
                " AND r.slug = " + s + ");", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task SaveProfileAsync(ProfilePage page, string? previousSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(page);
            RequireText(page.UserId, nameof(page.UserId));
            string? slug = String.IsNullOrWhiteSpace(page.Slug) ? null : page.Slug.Trim().ToLowerInvariant();
            List<string> statements = new List<string>();
            if (slug != null) statements.Add("DELETE FROM profileslugs WHERE " + Scope() + " AND slug = " + D.Str(slug) + ";");
            if (!String.IsNullOrWhiteSpace(previousSlug) && !String.Equals(previousSlug, slug, StringComparison.OrdinalIgnoreCase))
            {
                statements.Add("INSERT INTO profileslugs (propertyid, slug, userid, createdutc) VALUES (" + Pid + ", " + D.Str(previousSlug.Trim().ToLowerInvariant()) + ", " + D.Str(page.UserId) + ", " +
                    D.Ts(page.UpdatedUtc) + ") ON CONFLICT (propertyid, slug) DO UPDATE SET userid = EXCLUDED.userid, createdutc = EXCLUDED.createdutc;");
            }
            statements.Add("INSERT INTO profilepages (propertyid, userid, enabled, slug, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(page.UserId) + ", " + D.Bool(page.Enabled) + ", " +
                D.Str(slug) + ", " + D.Ts(page.CreatedUtc) + ", " + D.Ts(page.UpdatedUtc) + ") ON CONFLICT (propertyid, userid) DO UPDATE SET enabled = EXCLUDED.enabled, slug = EXCLUDED.slug, " +
                "updatedutc = EXCLUDED.updatedutc;");
            await UniqueTransactionAsync(statements, "A profile page with this slug", token).ConfigureAwait(false);
        }

        public async Task<bool> SlugTakenAsync(PublicPageKindEnum kind, string slug, string? exceptId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return false;
            string s = D.Str(slug.Trim().ToLowerInvariant());
            string except = String.IsNullOrWhiteSpace(exceptId) ? String.Empty : D.Str(exceptId.Trim());
            string sql;
            switch (kind)
            {
                case PublicPageKindEnum.Venue:
                    sql = "SELECT COUNT(*) AS cnt FROM (SELECT id FROM venues WHERE " + Scope() + " AND deletedutc IS NULL AND lower(slug) = " + s + (except.Length > 0 ? " AND id <> " + except : String.Empty) +
                        " UNION ALL SELECT venueid FROM venueslugs WHERE " + Scope() + " AND slug = " + s + (except.Length > 0 ? " AND venueid <> " + except : String.Empty) + ") t;";
                    break;
                case PublicPageKindEnum.Catalog:
                    sql = "SELECT COUNT(*) AS cnt FROM catalogentries WHERE " + Scope() + " AND deletedutc IS NULL AND lower(slug) = " + s + (except.Length > 0 ? " AND id <> " + except : String.Empty) + ";";
                    break;
                case PublicPageKindEnum.Event:
                    sql = "SELECT COUNT(*) AS cnt FROM (SELECT id FROM events WHERE " + Scope() + " AND deletedutc IS NULL AND lower(slug) = " + s + (except.Length > 0 ? " AND id <> " + except : String.Empty) +
                        " UNION ALL SELECT eventid FROM eventslugs WHERE " + Scope() + " AND slug = " + s + (except.Length > 0 ? " AND eventid <> " + except : String.Empty) + ") t;";
                    break;
                default:
                    sql = "SELECT COUNT(*) AS cnt FROM (SELECT userid FROM profilepages WHERE " + Scope() + " AND slug IS NOT NULL AND lower(slug) = " + s + (except.Length > 0 ? " AND userid <> " + except : String.Empty) +
                        " UNION ALL SELECT userid FROM profileslugs WHERE " + Scope() + " AND slug = " + s + (except.Length > 0 ? " AND userid <> " + except : String.Empty) + ") t;";
                    break;
            }
            return await CountAsync(sql, token).ConfigureAwait(false) > 0;
        }

        public Task<long> CountSitemapAsync(PublicPageKindEnum kind, DateTime nowUtc, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt " + SitemapFrom(kind, nowUtc) + ";", token);
        }

        public async Task<List<PublicPageEntry>> SitemapPageAsync(PublicPageKindEnum kind, DateTime nowUtc, long skip, int take, CancellationToken token = default)
        {
            if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
            if (take < 1 || take > IPublicPageMethods.MaxSitemapPage) throw new ArgumentOutOfRangeException(nameof(take));
            string select = kind == PublicPageKindEnum.Profile
                ? "SELECT p.slug AS slug, GREATEST(p.updatedutc, u.updatedutc) AS lastmod "
                : "SELECT x.slug AS slug, x.updatedutc AS lastmod ";
            string order = kind == PublicPageKindEnum.Profile ? " ORDER BY p.userid" : " ORDER BY x.id";
            DataTable table = await QueryAsync(select + SitemapFrom(kind, nowUtc) + order + " LIMIT " + D.Num(take) + " OFFSET " + D.Num(skip) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new PublicPageEntry
            {
                Slug = RowReader.GetString(row, "slug"),
                LastModifiedUtc = RowReader.GetDateTime(row, "lastmod")
            }).ToList();
        }

        private string SitemapFrom(PublicPageKindEnum kind, DateTime nowUtc)
        {
            switch (kind)
            {
                case PublicPageKindEnum.Venue:
                    return "FROM venues x WHERE " + Scope("x") + " AND x.deletedutc IS NULL AND x.status = 'Active' AND x.moderationstate = 'None'";
                case PublicPageKindEnum.Catalog:
                    return "FROM catalogentries x WHERE " + Scope("x") + " AND x.deletedutc IS NULL AND x.status = 'Published' AND x.moderationstate = 'None' AND x.slug <> ''";
                case PublicPageKindEnum.Event:
                    return "FROM events x WHERE " + Scope("x") + " AND x.deletedutc IS NULL AND x.status = 'Published' AND x.audience = 'Public' AND x.moderationstate = 'None' AND x.endutc >= " + D.Ts(nowUtc);
                default:
                    return "FROM profilepages p JOIN users u ON u.propertyid = p.propertyid AND u.id = p.userid WHERE " + Scope("p") + " AND p.enabled AND p.slug IS NOT NULL AND u.isdeleted = FALSE AND u.isbanned = FALSE";
            }
        }

        private static ProfilePage Map(DataRow row)
        {
            return new ProfilePage
            {
                UserId = RowReader.GetString(row, "userid"),
                Enabled = RowReader.GetBool(row, "enabled"),
                Slug = RowReader.GetNullableString(row, "slug"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
