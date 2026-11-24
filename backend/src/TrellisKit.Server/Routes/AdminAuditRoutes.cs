namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin audit log. SystemAdmin or the break-glass token.
    /// </summary>
    public class AdminAuditRoutes
    {
        #region Private-Members

        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminAuditRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            EnumerationRoutes.Add(server.Routes.PostAuthentication, Constants.ApiPrefix + "/admin/audit", PhaseOneCollections.Audit, "AuditRecord",
                () => Doc("List audit records", "Admin: audit", "Search matches action, actor name, and summary. Filters: actorUserId, targetType, targetId, action; the time window is createdAfterUtc and createdBeforeUtc. SystemAdmin or the admin token."),
                async (ctx, query) => await ListAsync(ctx, query, ctx.Token).ConfigureAwait(false));
        }

        #endregion

        #region Private-Methods

        private async Task<EnumerationResult<AuditRecord>> ListAsync(HttpContextBase ctx, EnumerationQuery query, CancellationToken token)
        {
            EnumerationRequest request = _Directory.For(ctx).Enumeration.Prepare(query, PhaseOneCollections.Audit, true);
            AuditFilter filter = new AuditFilter
            {
                ActorUserId = request.Query.FilterString("actorUserId"),
                TargetType = request.Query.FilterString("targetType"),
                TargetId = request.Query.FilterString("targetId"),
                Action = request.Query.FilterString("action")
            };
            EnumerationResult<AuditRecord> page = await _Directory.For(ctx).Database.Audit.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            await _Directory.For(ctx).Metadata.AttachAsync(TargetTypeEnum.AuditRecord, page.Objects, r => r.Id, true, token).ConfigureAwait(false);
            return page;
        }

        #endregion
    }
}
