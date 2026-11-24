namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin word filter list (Phase 2 contract section 9.4). SystemAdmin or the admin token.
    /// </summary>
    public class WordFilterRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/moderation/word-filter";
        private const string _Tag = "Admin: word filter";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Orderings = "CreatedDescending, CreatedAscending, TermAscending, UpdatedDescending, HitsDescending";
        private const string _Filters = "action, matchType";
        private const string _Item = "{ id, term, matchType, action, note, createdByName, createdUtc, updatedUtc, hitCount30d, labels, tags }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public WordFilterRoutes(PropertyDirectory directory)
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
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List word filter terms", _Tag, "Search covers term and note; hitCount30d counts matches in the last 30 days." + _Admin)
                    .Enumeration(_Item, _Orderings, _Filters).Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate word filter terms", _Tag, "EnumerationQuery body (filters: action, matchType)." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration(_Item, _Orderings, _Filters, false).Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Add a word filter term", _Tag, "matchType defaults to Word and action to Block." + _Admin)
                    .Body("{ term, matchType?: Word | Substring, action?: Block | Flag, note?, labels?, tags? }").Returns("WordFilterTerm", 201)
                    .Resp(400, "ValidationFailed").Resp(409, "Conflict (same normalized term)"));
            post.Static.Add(HttpMethod.POST, _Base + "/import", ImportAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Import word filter terms", _Tag, "Up to 1000 terms; duplicates and invalid entries are skipped." + _Admin)
                    .Body("{ terms: [], matchType?, action? }").Returns("{ added, skipped }").Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Base + "/test", TestAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Test text against the word filter", _Tag, "start and length are UTF-16 positions in the original text." + _Admin)
                    .Body("{ text }").Returns("{ matches: [{ termId, term, action, start, length }] }").Resp(400, "ValidationFailed"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a word filter term", _Tag, "Any field; absent fields are unchanged." + _Admin)
                    .PathParam("id", "Term identifier").Body("Partial term").Returns("WordFilterTerm").Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a word filter term", _Tag, _Admin.Trim()).PathParam("id", "Term identifier").Resp(204, "Deleted").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).WordFilter.ListAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).WordFilter.ListAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            WordFilterTermRequest request = RouteHelper.ReadJson<WordFilterTermRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).WordFilter.CreateAsync(actor, request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            WordFilterTermRequest request = RouteHelper.ReadJson<WordFilterTermRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).WordFilter.UpdateAsync(actor, RouteHelper.Param(ctx, "id"), request, present, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            await _Directory.For(ctx).WordFilter.DeleteAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task ImportAsync(HttpContextBase ctx)
        {
            WordFilterImportRequest request = RouteHelper.ReadJson<WordFilterImportRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).WordFilter.ImportAsync(request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase ctx)
        {
            WordFilterTestRequest request = RouteHelper.ReadJson<WordFilterTestRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).WordFilter.TestAsync(request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
