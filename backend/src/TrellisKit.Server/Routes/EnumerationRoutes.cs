namespace TrellisKit.Server.Routes
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using WatsonWebserver.Core.Routing;

    /// <summary>
    /// Registers the two forms of a list (docs/ENUMERATION_AND_METADATA.md section 2.1): POST {base}/enumerate with an
    /// EnumerationQuery body and GET {base} with the same fields as query parameters, sharing one handler, with the
    /// OpenAPI EnumerationResult declaration (orderings and filters taken from the collection).
    /// </summary>
    public static class EnumerationRoutes
    {
        #region Public-Methods

        /// <summary>
        /// Register both forms.
        /// </summary>
        /// <param name="group">Routing group (usually PostAuthentication).</param>
        /// <param name="listPath">GET path, for example /api/v1/admin/members or /api/v1/admin/members/{id}/sessions.</param>
        /// <param name="collection">Collection (orderings and filters for the documentation).</param>
        /// <param name="itemName">Object type name in the result.</param>
        /// <param name="doc">Builds the route documentation (summary, tag, description); called once per form.</param>
        /// <param name="handler">Handler that runs the enumeration and returns the EnumerationResult.</param>
        /// <param name="routeParameters">Path parameter names (excluded from GET filters and documented).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void Add(RoutingGroup group, string listPath, EnumerationCollection collection, string itemName, Func<OpenApiRouteMetadata> doc,
            Func<HttpContextBase, EnumerationQuery, Task<object>> handler, params string[] routeParameters)
        {
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(collection);
            ArgumentNullException.ThrowIfNull(doc);
            ArgumentNullException.ThrowIfNull(handler);
            if (String.IsNullOrEmpty(listPath)) throw new ArgumentNullException(nameof(listPath));
            string[] parameters = routeParameters ?? Array.Empty<string>();
            string orderings = collection.OrderingNames();
            string filters = String.Join(", ", collection.FilterNames.OrderBy(f => f, StringComparer.Ordinal));

            OpenApiRouteMetadata get = doc().Enumeration(itemName, orderings, filters).Resp(400, "ValidationFailed or InvalidContinuationToken");
            OpenApiRouteMetadata post = doc().Body("EnumerationQuery { maxResults, continuationToken, skip, ordering, search, createdAfterUtc, createdBeforeUtc, labels, labelMatch, tags, filters" +
                (filters.Length == 0 ? String.Empty : " (" + filters + ")") + " }; orderings: " + orderings, false)
                .Enumeration(itemName, orderings, filters, withQueryParameters: false).Resp(400, "ValidationFailed or InvalidContinuationToken");
            foreach (string p in parameters)
            {
                get = get.PathParam(p, "Path parameter " + p);
                post = post.PathParam(p, "Path parameter " + p);
            }

            async Task GetAsync(HttpContextBase ctx)
            {
                EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx, parameters);
                await RouteHelper.SendJsonAsync(ctx, 200, await handler(ctx, query).ConfigureAwait(false)).ConfigureAwait(false);
            }

            async Task PostAsync(HttpContextBase ctx)
            {
                EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
                await RouteHelper.SendJsonAsync(ctx, 200, await handler(ctx, query).ConfigureAwait(false)).ConfigureAwait(false);
            }

            string enumeratePath = listPath + "/enumerate";
            if (parameters.Length == 0)
            {
                group.Static.Add(HttpMethod.GET, listPath, GetAsync, RouteHelper.HandleExceptionAsync, openApiMetadata: get);
                group.Static.Add(HttpMethod.POST, enumeratePath, PostAsync, RouteHelper.HandleExceptionAsync, openApiMetadata: post);
            }
            else
            {
                group.Parameter.Add(HttpMethod.GET, listPath, GetAsync, RouteHelper.HandleExceptionAsync, openApiMetadata: get);
                group.Parameter.Add(HttpMethod.POST, enumeratePath, PostAsync, RouteHelper.HandleExceptionAsync, openApiMetadata: post);
            }
        }

        #endregion
    }
}
