namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Administrative email and per-property settings routes (docs/TRELLISKIT.md section 12; docs/MULTIPROPERTY_CONTRACT.md
    /// section 5.7): the property's integration settings (secrets write-only), the email integration test, templates with
    /// overrides and previews, staff notification settings, the suppression list, the message log, and the Mailgun webhook.
    /// </summary>
    public class EmailRoutes
    {
        #region Private-Members

        private const string _Email = Constants.ApiPrefix + "/admin/email";
        private const string _Tag = "Admin: email";
        private const string _Admin = " SystemAdmin or the admin token.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public EmailRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/admin/property/settings", AdministrativeCatalogs.PropertySettings, "PropertySettingField",
                () => Doc("Enumerate property settings", _Tag, "Property settings with standard paging. Immutable field declarations use Unix epoch creation time." + _Admin),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.PropertySettings, true);
                    PropertySettingsResponse result = await services.PropertySettings.ReadAsync(ctx.Token).ConfigureAwait(false);
                    EnumerationResult<PropertySettingField> page = CatalogEnumeration.Page(request, result.Fields, field => field.Key, search: field => field.Key + " " + field.Section + " " + field.Label + " " + field.Description);
                    return CatalogEnumerationResponse<PropertySettingField>.From(page, new Dictionary<string, object?> { ["propertyId"] = result.PropertyId });
                });
            post.Static.Add(HttpMethod.PUT, Constants.ApiPrefix + "/admin/property/settings", PutSettingsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update the property's integration settings", "Admin: property", "Body { values: { key: value }, clear: [keys] }. All or nothing; secrets are encrypted with the engine secrets key (TK_SECRETS_ENCRYPTION_KEY); null or empty clears. Per-property Mailgun credentials are set only here, never from per-property environment variables. Audited property.settings.update (key names only)." + _Admin)
                    .Body("PropertySettingsUpdateRequest").Returns("PropertySettings").Bounded("PropertySettings", "the property settings registry").Resp(400, "ValidationFailed"));
            post.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/property/integrations/{name}/test", IntegrationTestAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Test an integration", "Admin: property", "name email: sends the test template to the caller (or body { to }). Other integrations report ok false until their per-property settings move in. 200 { ok, message }." + _Admin)
                    .PathParam("name", "Integration: email").Body("{ to? }", false).Returns("{ ok, message }").Resp(400, "ValidationFailed"));

            EnumerationRoutes.Add(post, _Email + "/templates", AdministrativeCatalogs.EmailTemplates, "EmailTemplateView",
                () => Doc("Enumerate email templates", _Tag, "Declared templates with defaults and overrides. Catalog declaration timestamps use Unix epoch." + _Admin),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.EmailTemplates, true);
                    Dictionary<string, string> errors = new Dictionary<string, string>();
                    EmailCategoryEnum? category = request.Query.FilterEnum<EmailCategoryEnum>("category", errors);
                    if (errors.Count > 0) throw ApiErrorException.Validation(errors, "Invalid template category.");
                    List<EmailTemplateView> templates = await services.EmailAdmin.TemplatesAsync(ctx.Token).ConfigureAwait(false);
                    if (category.HasValue) templates = templates.Where(item => item.Category == category.Value).ToList();
                    return CatalogEnumeration.Page(request, templates, item => item.Key, search: item => item.Key + " " + item.Description + " " + item.Category);
                });
            post.Parameter.Add(HttpMethod.GET, _Email + "/templates/{key}", TemplateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an email template", _Tag, "Tokens, defaults, and the override." + _Admin).PathParam("key", "Template key")
                    .Returns("EmailTemplate").Bounded("EmailTemplate", "the template's tokens (fixed by the engine version)").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Email + "/templates/{key}", PutTemplateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Override an email template", _Tag, "Body { subject?, preheader?, heading?, body? } with {token} placeholders from the template's tokens; null parts use the engine default (all null resets). Audited email.template.update." + _Admin)
                    .PathParam("key", "Template key").Body("EmailTemplateOverrideRequest").Returns("EmailTemplate").Bounded("EmailTemplate", "the template's tokens").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Email + "/templates/{key}", DeleteTemplateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reset an email template", _Tag, "Removes the override." + _Admin).PathParam("key", "Template key").Resp(204, "Reset").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Email + "/templates/{key}/preview", PreviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Preview an email template", _Tag, "Body { subject?, preheader?, heading?, body?, tokens? }: renders the unsaved text (or the saved override) with sample data in the property's brand." + _Admin)
                    .PathParam("key", "Template key").Body("EmailTemplateOverrideRequest", false).Returns("{ subject, preheader, html, text }").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.POST, _Email + "/test", TestAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Send a test email", _Tag, "Body { template?, to? }: sends the template (default test) with sample data to the caller or to. Audited email.test." + _Admin)
                    .Body("EmailTestRequest", false).Returns("{ ok, message }").Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Email + "/test/catalog", TestCatalogAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Send every email template as a test", _Tag, "Body { to? }: sends every template the admin portal lists, once each with sample data, to the caller or to, through the property's provider (the local stack delivers to Mailpit), so the whole catalog can be reviewed in one inbox. Audited once as email.test_catalog." + _Admin)
                    .Body("EmailTestRequest", false).Returns("{ to, provider, sent, failed, results: [{ template, ok, message }] }").Bounded("EmailCatalogTest", "the engine's template catalog plus the property's own templates").Resp(400, "ValidationFailed"));
            EnumerationRoutes.Add(post, _Email + "/staff-events", AdministrativeCatalogs.EmailEvents, "EmailStaffEventView",
                () => Doc("Enumerate staff email events", _Tag, "Configured staff email events. Catalog declarations use Unix epoch." + _Admin),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.EmailEvents, true);
                    return CatalogEnumeration.Page(request, await services.EmailAdmin.StaffEventsAsync(ctx.Token).ConfigureAwait(false), item => item.Template, search: item => item.Template + " " + item.Description);
                });
            post.Parameter.Add(HttpMethod.PUT, _Email + "/staff-events/{template}", PutStaffEventAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a staff notification event", _Tag, "Body { enabled?, recipients? (at most 10) }. Audited email.staff_event.update." + _Admin)
                    .PathParam("template", "Staff template key").Body("EmailStaffEventRequest").Returns("EmailStaffEvent").Bounded("EmailStaffEvent", "at most 10 recipients").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Email + "/messages", EmailCollections.Messages, "EmailMessage",
                () => Doc("List email messages", _Tag, "The administrative email log (metadata only: template, category, recipient, subject, status, provider, attempts, timestamps; bodies are never stored). Search over recipient and subject; filters status, template, category, recipient, userId." + _Admin),
                async (ctx, query) => await _Directory.For(ctx).EmailAdmin.MessagesAsync(query, ctx.Token).ConfigureAwait(false));
            post.Parameter.Add(HttpMethod.GET, _Email + "/messages/{id}", MessageAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an email message record", _Tag, "Metadata only." + _Admin).PathParam("id", "Message id").Returns("EmailMessage")
                    .Bounded("EmailMessage", "the record's labels and tags (50 each)").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Email + "/suppressions", EmailCollections.Suppressions, "EmailSuppression",
                () => Doc("List suppressed addresses", _Tag, "Addresses that receive no email (Mailgun bounces, complaints, unsubscribes, and staff entries). Search over email and note; filter reason." + _Admin),
                async (ctx, query) => await _Directory.For(ctx).EmailAdmin.SuppressionsAsync(query, ctx.Token).ConfigureAwait(false));
            post.Static.Add(HttpMethod.POST, _Email + "/suppressions", AddSuppressionAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Suppress an address", _Tag, "Body { email, note?, labels?, tags? }. Audited email.suppression.add." + _Admin)
                    .Body("EmailSuppressionRequest").Returns("EmailSuppression", 201).Resp(400, "ValidationFailed"));
            post.Parameter.Add(HttpMethod.DELETE, _Email + "/suppressions/{id}", RemoveSuppressionAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a suppression", _Tag, "Audited email.suppression.remove." + _Admin).PathParam("id", "Suppression id").Resp(204, "Removed").Resp(404, "NotFound"));

            server.Routes.PreAuthentication.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/webhooks/mailgun/{propertyId}", MailgunAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mailgun webhook (per property)", "Webhooks", "delivered, failed, complained, and unsubscribed events, verified with the property's email.mailgunWebhookSigningKey (HMAC-SHA256 of timestamp and token, 15 minute window, no replays). Permanent failures, complaints, and unsubscribes suppress the address. Not called by clients.")
                    .PathParam("propertyId", "Property id").Resp(200, "Processed or ignored").Resp(400, "InvalidSignature or InvalidJson").Resp(404, "PropertyNotFound").Resp(503, "IntegrationNotConfigured"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task GetSettingsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).PropertySettings.ReadAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutSettingsAsync(HttpContextBase ctx)
        {
            PropertySettingsUpdateRequest request = RouteHelper.ReadJson<PropertySettingsUpdateRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).PropertySettings.UpdateAsync(request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task IntegrationTestAsync(HttpContextBase ctx)
        {
            string name = RouteHelper.Param(ctx, "name");
            if (!String.Equals(name, "email", StringComparison.OrdinalIgnoreCase))
            {
                await RouteHelper.SendJsonAsync(ctx, 200, new IntegrationTestResponse { Ok = false, Message = "The " + name + " integration test is not available yet; its settings are deployment settings." }).ConfigureAwait(false);
                return;
            }
            await TestAsync(ctx).ConfigureAwait(false);
        }

        private async Task TemplatesAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, new EmailTemplateList { Templates = await _Directory.For(ctx).EmailAdmin.TemplatesAsync(ctx.Token).ConfigureAwait(false) }).ConfigureAwait(false);
        }

        private async Task TemplateAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).EmailAdmin.TemplateAsync(RouteHelper.Param(ctx, "key"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutTemplateAsync(HttpContextBase ctx)
        {
            EmailTemplateOverrideRequest request = RouteHelper.ReadJson<EmailTemplateOverrideRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).EmailAdmin.PutOverrideAsync(RouteHelper.Param(ctx, "key"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteTemplateAsync(HttpContextBase ctx)
        {
            await _Directory.For(ctx).EmailAdmin.DeleteOverrideAsync(RouteHelper.Param(ctx, "key"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task PreviewAsync(HttpContextBase ctx)
        {
            EmailTemplateOverrideRequest request = RouteHelper.ReadJson<EmailTemplateOverrideRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).EmailAdmin.PreviewAsync(RouteHelper.Param(ctx, "key"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase ctx)
        {
            EmailTestRequest request = RouteHelper.ReadJson<EmailTestRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).EmailAdmin.TestAsync(request, actor.User?.Email, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TestCatalogAsync(HttpContextBase ctx)
        {
            EmailTestRequest request = RouteHelper.ReadJson<EmailTestRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).EmailAdmin.TestCatalogAsync(request, actor.User?.Email, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task StaffEventsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, new EmailStaffEventList { Events = await _Directory.For(ctx).EmailAdmin.StaffEventsAsync(ctx.Token).ConfigureAwait(false) }).ConfigureAwait(false);
        }

        private async Task PutStaffEventAsync(HttpContextBase ctx)
        {
            EmailStaffEventRequest request = RouteHelper.ReadJson<EmailStaffEventRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).EmailAdmin.PutStaffEventAsync(RouteHelper.Param(ctx, "template"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MessageAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).EmailAdmin.MessageAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AddSuppressionAsync(HttpContextBase ctx)
        {
            EmailSuppressionRequest request = RouteHelper.ReadJson<EmailSuppressionRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).EmailAdmin.AddSuppressionAsync(actor, request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RemoveSuppressionAsync(HttpContextBase ctx)
        {
            await _Directory.For(ctx).EmailAdmin.RemoveSuppressionAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task MailgunAsync(HttpContextBase ctx)
        {
            PropertyRuntime? runtime = _Directory.Find(RouteHelper.Param(ctx, "propertyId"));
            if (runtime == null || runtime.Record.Status == PropertyStatusEnum.Deleting || runtime.Record.Status == PropertyStatusEnum.Deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "PropertyNotFound", "No property matches this webhook URL.").ConfigureAwait(false);
                return;
            }
            byte[] data = await ctx.Request.ReadBodyAsync(ctx.Token).ConfigureAwait(false) ?? Array.Empty<byte>();
            string outcome = await runtime.Services.EmailAdmin.HandleMailgunAsync(Encoding.UTF8.GetString(data), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new MessageResponse { Message = outcome }).ConfigureAwait(false);
        }

        #endregion
    }
}
