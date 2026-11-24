namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Public landing page data access (docs/PHASE4_CONTRACT.md section 18): members' opt-in profile pages with their
    /// slug redirects, and the bounded sitemap queries over venues, events, and profile pages. Implementations are safe
    /// to call concurrently.
    /// </summary>
    public interface IPublicPageMethods
    {
        /// <summary>
        /// Hard cap of one sitemap page (the sitemap protocol's 50,000 URL limit).
        /// </summary>
        const int MaxSitemapPage = 50000;

        /// <summary>
        /// Read a member's profile page row.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row, or null when the member never configured one.</returns>
        Task<ProfilePage?> ReadProfileAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Find the profile page that uses a slug now, or used it before (a redirect).
        /// </summary>
        /// <param name="slug">Slug (case-insensitive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row (its Slug is the current one), or null.</returns>
        Task<ProfilePage?> ReadProfileBySlugAsync(string slug, CancellationToken token = default);

        /// <summary>
        /// Insert or update a member's profile page; a changed slug keeps the previous one as a redirect, and a slug taken
        /// over from another member's redirect stops redirecting.
        /// </summary>
        /// <param name="page">Row.</param>
        /// <param name="previousSlug">Slug before the change, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The row is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The slug is in use.</exception>
        Task SaveProfileAsync(ProfilePage page, string? previousSlug, CancellationToken token = default);

        /// <summary>
        /// Whether a slug is used now (or as a redirect) by an object of a kind other than the one named.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="slug">Slug (case-insensitive).</param>
        /// <param name="exceptId">The object being edited (its own current and previous slugs do not count), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when taken.</returns>
        Task<bool> SlugTakenAsync(PublicPageKindEnum kind, string slug, string? exceptId, CancellationToken token = default);

        /// <summary>
        /// Count the objects of a kind that are listed in the sitemap: live Active venues, Published Public events that have
        /// not ended by the given time, and enabled profile pages of members who are neither deleted nor banned.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="nowUtc">Current time (events that ended before it drop out).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountSitemapAsync(PublicPageKindEnum kind, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One page of sitemap rows of a kind in a stable order (identifier).
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="skip">Rows to skip (minimum 0).</param>
        /// <param name="take">Rows to return (1 to <see cref="MaxSitemapPage"/>).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ArgumentOutOfRangeException">skip or take is out of range.</exception>
        Task<List<PublicPageEntry>> SitemapPageAsync(PublicPageKindEnum kind, DateTime nowUtc, long skip, int take, CancellationToken token = default);
    }
}
