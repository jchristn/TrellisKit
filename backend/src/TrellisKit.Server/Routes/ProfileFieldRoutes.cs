namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Configurable profile field routes (docs/MULTIPROPERTY_CONTRACT.md sections 9.5 and 9.12): the admin definition
    /// routes, staff writes of a member's profile values, and the member enumeration with profile filters.
    /// </summary>
    public class ProfileFieldRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/profile-fields";
        private const string _Tag = "Admin: profile fields";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Field = "ProfileField { id, key, builtIn, label, helpText, placeholder, type, options[], rank, subFields[], validation, collectAt, editableBy, visibility, showOnProfile, profileOrder, showOnCard, searchable, filterable, enabled, valueCount, labels, tags, createdUtc, updatedUtc }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public ProfileFieldRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, ProfileCollections.Fields, "ProfileField",
                () => Doc("List profile fields", _Tag, "Every definition of the property, built-ins included (at most 104). Search over key and label; filters type, collectAt, visibility, enabled, builtIn, filterable, showOnCard." + _Admin),
                async (ctx, query) => await _Directory.For(ctx).ProfileFields.EnumerateAsync(query, ctx.Token).ConfigureAwait(false));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a profile field", _Tag, "Body: a ProfileField without id, builtIn, valueCount, and timestamps (options and sub-fields without ids). Appended at the end of profile order unless profileOrder is given. Audited profileField.create." + _Admin)
                    .Body(_Field).Returns("ProfileField", 201).Resp(400, "ValidationFailed").Resp(409, "FieldKeyInUse or FieldLimitReached"));
            post.Static.Add(HttpMethod.POST, _Base + "/reorder", ReorderAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reorder profile fields", _Tag, "Body { order: [field ids] }: every field id of the property exactly once (at most 104); profileOrder becomes index times 10. Audited profileField.reorder." + _Admin)
                    .Body("ProfileReorderRequest").Returns("{ order }").Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Base + "/preview", PreviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Preview profile fields", _Tag, "Body { target: Onboarding | ProfilePage | Card | Editor, viewer: Anonymous | SignedIn | Member | Self | Staff, fields?: [drafts], values?: { key: value } }. Without fields the saved definitions are used; with fields, each draft replaces the saved field with the same key and drafts with new keys are added (the other saved fields stay)." + _Admin)
                    .Body("ProfilePreviewRequest").Returns("{ target, viewer, items: [{ key, builtIn, label, type, required, helpText, placeholder, control, display, value, formattedValue, options }] }")
                    .Bounded("ProfilePreview", "the property's profile fields (at most 104)").Resp(400, "ValidationFailed"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a profile field", _Tag, "One definition with labels and tags." + _Admin).PathParam("id", "Field identifier")
                    .Returns("ProfileField").Bounded("ProfileField", "options (at most 200) and sub-fields (at most 10)").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a profile field", _Tag, "Any attributes; options and subFields replace their lists (entries with an existing id are updated, entries without one are new, missing entries are removed unless members hold values: 409 OptionInUse, retire them instead); validation and rank replace as whole objects. key and type change only while valueCount is 0. Built-ins accept label, helpText, placeholder (avatar and bio also collectAt and enabled; bio also visibility, showOnProfile, profileOrder). Audited profileField.update." + _Admin)
                    .PathParam("id", "Field identifier").Body("Partial ProfileField").Returns("ProfileField").Bounded("ProfileField", "options (at most 200) and sub-fields (at most 10)")
                    .Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "FieldHasValues, OptionInUse, BuiltInField, or FieldKeyInUse"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a profile field", _Tag, "A field with values needs ?deleteValues=true and { confirm: <key> } (or ?confirm=<key>); its values are deleted with it (202). 204 when it had none. Audited profileField.delete." + _Admin)
                    .PathParam("id", "Field identifier").QueryParam("deleteValues", "true to delete the field's values").QueryParam("confirm", "The field key (alternative to the body)")
                    .Resp(202, "Deleted with its values").Resp(204, "Deleted").Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "FieldHasValues or BuiltInField"));
            EnumerationRoutes.Add(post, _Base + "/{id}/stats", AdministrativeCatalogs.ProfileOptions, "ProfileOptionCount",
                () => Doc("Enumerate profile field option statistics", _Tag, "Option counts with scalar valueCount, fillRate and updated30d context. Immutable option timestamps use Unix epoch." + _Admin),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    string fieldId = RouteHelper.Param(ctx, "id");
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.ProfileOptions, true, fieldId);
                    ProfileFieldStats stats = await services.ProfileFields.StatsAsync(fieldId, ctx.Token).ConfigureAwait(false);
                    return CatalogEnumerationResponse<ProfileOptionCount>.From(CatalogEnumeration.Page(request, stats.OptionCounts, item => item.Key),
                        new Dictionary<string, object?> { ["valueCount"] = stats.ValueCount, ["fillRate"] = stats.FillRate, ["updated30d"] = stats.Updated30d });
                }, "id");
            post.Parameter.Add(HttpMethod.PUT, Constants.ApiPrefix + "/admin/members/{id}/profile", MemberProfileAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Set a member's profile values", "Admin: members", "Body { profile: { <key>: <value> | null } }: any configurable field, AdminOnly and Staff visibility included, with the member validation rules. Audited member.profile (key names only)." + _Admin)
                    .PathParam("id", "Member identifier").Body("MemberProfileUpdateRequest").Returns("AdminMemberDetail").Bounded("AdminMemberDetail", "previews of at most 5 entries each, profile fields of the property")
                    .Resp(400, "ValidationFailed or ContentFiltered").Resp(404, "NotFound"));
            // GET /api/v1/members and POST /api/v1/members/enumerate are the Phase 2 mention search, so the member
            // directory with profile filters lives under /members/directory.
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/members/directory", ProfileCollections.Members, "UserSummary (with labels and tags)",
                () => Doc("Enumerate members", "Members", "Active accounts (not banned or deleted) that finished their profile (handle set); when the access model requires a membership for the directory (FullyGated, or directory or members gated), only accounts with an active membership or a staff role. search matches display names, handles, and searchable profile fields the caller can see. filters.profile.<key>.<operator> on enabled, filterable fields the caller can see (eq, prefix, gte, lte, in, any, all, minRank, maxRank, minLevel, hasValue; at most profileFields.maxFiltersPerQuery); GET form profile.<key>.<operator>=<value> (repeatable for in, any, all). Unknown, hidden, or invalid filters are 400 with fieldErrors filters.profile.<key> = \"Unknown filter.\". Members and staff."),
                async (ctx, query) =>
                {
                    ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
                    return await _Directory.For(ctx).Members.EnumerateAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            ProfileField body = RouteHelper.ReadJson<ProfileField>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).ProfileFields.CreateAsync(actor, body, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).ProfileFields.GetAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            ProfileField body = RouteHelper.ReadJson<ProfileField>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).ProfileFields.UpdateAsync(actor, RouteHelper.Param(ctx, "id"), body, present, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool deleteValues = RouteHelper.QueryBool(ctx, "deleteValues", errors) ?? false;
            if (errors.Count > 0) throw TrellisKit.Core.Exceptions.ApiErrorException.Validation(errors);
            ConfirmRequest body = RouteHelper.ReadJson<ConfirmRequest>(ctx);
            string? confirm = body.Confirm ?? RouteHelper.Query(ctx, "confirm");
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            bool hadValues = await _Directory.For(ctx).ProfileFields.DeleteAsync(actor, RouteHelper.Param(ctx, "id"), deleteValues, confirm, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, hadValues ? 202 : 204).ConfigureAwait(false);
        }

        private async Task ReorderAsync(HttpContextBase ctx)
        {
            ProfileReorderRequest body = RouteHelper.ReadJson<ProfileReorderRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            List<string> order = await _Directory.For(ctx).ProfileFields.ReorderAsync(actor, body.Order, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new ProfileReorderResponse { Order = order }).ConfigureAwait(false);
        }

        private async Task PreviewAsync(HttpContextBase ctx)
        {
            ProfilePreviewRequest body = RouteHelper.ReadJson<ProfilePreviewRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).ProfileValues.PreviewAsync(body, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task StatsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).ProfileFields.StatsAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MemberProfileAsync(HttpContextBase ctx)
        {
            MemberProfileUpdateRequest body = RouteHelper.ReadJson<MemberProfileUpdateRequest>(ctx);
            ApplicationServices services = _Directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            User user = await services.Profiles.UpdateByStaffAsync(actor, RouteHelper.Param(ctx, "id"), body.Profile, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.AdminMembers.DetailAsync(user.Id, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
