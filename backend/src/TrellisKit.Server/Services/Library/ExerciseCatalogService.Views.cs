namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Catalog entry views for other services (lesson and catalog links, unified library search).
    /// </summary>
    public sealed partial class ExerciseCatalogService
    {
        /// <summary>
        /// Build the caller's view of a catalog entry with the same rules as the catalog routes (members never see review
        /// notes, staff labels, or inaccessible lesson references).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="entry">Entry.</param>
        /// <param name="admin">Library staff view.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public Task<CatalogEntryView> ViewForAsync(ContentActor actor, CatalogEntry entry, bool admin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(entry);
            return ViewAsync(entry, actor, admin, token);
        }
    }
}
