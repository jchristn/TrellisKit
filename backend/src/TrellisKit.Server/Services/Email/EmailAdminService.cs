namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The admin portal Email section of one property (docs/TRELLISKIT.md section 12.4): templates with overrides and
    /// previews, test sends, staff notification settings, the suppression list, the message log, and Mailgun webhook
    /// events (signature-verified; permanent failures, complaints, and unsubscribes suppress the address).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class EmailAdminService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly EmailService _Email;
        private readonly PropertySettingsService _PropertySettings;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private readonly AuditService _Audit;
        private readonly AppLogger _Logger;
        private readonly ConcurrentDictionary<string, DateTime> _SeenTokens = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="email">Email service.</param>
        /// <param name="propertySettings">Property settings (webhook signing key).</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public EmailAdminService(DatabaseDriverBase database, EmailService email, PropertySettingsService propertySettings, EnumerationService enumeration, MetadataService metadata, AuditService audit, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Email = email ?? throw new ArgumentNullException(nameof(email));
            _PropertySettings = propertySettings ?? throw new ArgumentNullException(nameof(propertySettings));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/admin/email/templates: the engine catalog with the property's overrides (fixed-bound).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Templates.</returns>
        public async Task<List<EmailTemplateView>> TemplatesAsync(CancellationToken token = default)
        {
            List<EmailTemplateOverride> overrides = await _Database.Email.ReadOverridesAsync(token).ConfigureAwait(false);
            return _Email.Templates.All.Where(t => t.Recipient != EmailRecipientEnum.PlatformAdmins).Select(t => View(t, overrides.FirstOrDefault(o => o.Template == t.Key))).ToList();
        }

        /// <summary>
        /// GET /api/v1/admin/email/templates/{key}.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Template.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EmailTemplateView> TemplateAsync(string key, CancellationToken token = default)
        {
            EmailTemplate template = Require(key);
            List<EmailTemplateOverride> overrides = await _Database.Email.ReadOverridesAsync(token).ConfigureAwait(false);
            return View(template, overrides.FirstOrDefault(o => o.Template == template.Key));
        }

        /// <summary>
        /// PUT /api/v1/admin/email/templates/{key}: store an override (tokens validated). Audited email.template.update.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="request">Override text.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Template.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EmailTemplateView> PutOverrideAsync(string key, EmailTemplateOverrideRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(info);
            EmailTemplate template = Require(key);
            EmailTemplateOverride value = new EmailTemplateOverride
            {
                Template = template.Key,
                Subject = Empty(request.Subject),
                Preheader = Empty(request.Preheader),
                Heading = Empty(request.Heading),
                Body = Empty(request.Body),
                UpdatedByUserId = info.Context?.UserId,
                UpdatedUtc = DateTime.UtcNow
            };
            Dictionary<string, string> errors = EmailRenderer.Validate(template, value);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            bool clear = value.Subject == null && value.Preheader == null && value.Heading == null && value.Body == null;
            await _Database.Email.WriteOverrideAsync(template.Key, clear ? null : value, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "email.template.update", "EmailTemplate", template.Key, clear ? "Reset to the default" : "Override saved", token).ConfigureAwait(false);
            return await TemplateAsync(template.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/email/templates/{key}: back to the engine default. Audited email.template.update.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task DeleteOverrideAsync(string key, RequestInfo info, CancellationToken token = default)
        {
            EmailTemplate template = Require(key);
            await _Database.Email.WriteOverrideAsync(template.Key, null, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "email.template.update", "EmailTemplate", template.Key, "Reset to the default", token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/email/templates/{key}/preview: render with sample data and the given (unsaved) override, or
        /// the saved one when the body has no text parts.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="request">Unsaved override and sample tokens.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rendered email.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EmailPreviewResponse> PreviewAsync(string key, EmailTemplateOverrideRequest? request, CancellationToken token = default)
        {
            EmailTemplate template = Require(key);
            EmailTemplateOverride? draft = null;
            if (request != null && (request.Subject != null || request.Preheader != null || request.Heading != null || request.Body != null))
            {
                draft = new EmailTemplateOverride { Template = template.Key, Subject = Empty(request.Subject), Preheader = Empty(request.Preheader), Heading = Empty(request.Heading), Body = Empty(request.Body) };
                Dictionary<string, string> errors = EmailRenderer.Validate(template, draft);
                if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            }
            Dictionary<string, string>? tokens = request?.Tokens?.Where(t => template.Tokens.Contains(t.Key)).ToDictionary(t => t.Key, t => (t.Value ?? String.Empty).Length > 500 ? t.Value!.Substring(0, 500) : t.Value ?? String.Empty, StringComparer.Ordinal);
            EmailRendered rendered = await _Email.RenderAsync(template, draft, tokens, draft == null, token).ConfigureAwait(false);
            return new EmailPreviewResponse { Subject = rendered.Subject, Preheader = rendered.Preheader, Html = rendered.Html, Text = rendered.Text };
        }

        /// <summary>
        /// POST /api/v1/admin/email/test and POST /api/v1/admin/property/integrations/email/test: send a template with
        /// sample data to the caller (or a given address) through the configured provider. Audited email.test.
        /// </summary>
        /// <param name="request">Template and recipient.</param>
        /// <param name="callerEmail">The caller's address, or null.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>{ ok, message }.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed (no recipient, unknown template).</exception>
        public async Task<IntegrationTestResponse> TestAsync(EmailTestRequest? request, string? callerEmail, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            EmailTemplate template = request?.Template == null ? _Email.Templates.Get("test") : _Email.Templates.Find(request.Template) ?? throw ApiErrorException.Field("template", "Unknown template.");
            string? to = String.IsNullOrWhiteSpace(request?.To) ? callerEmail : request!.To!.Trim();
            if (String.IsNullOrWhiteSpace(to) || Validators.Email(to) != null) throw ApiErrorException.Field("to", "Give an email address to send the test to.");
            EmailProviderChoice provider = await _Email.ProviderAsync(token).ConfigureAwait(false);
            IntegrationTestResponse result = await SendSampleAsync(template, to, provider, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "email.test", "EmailTemplate", template.Key, "Test send through " + provider.Provider, token).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// POST /api/v1/admin/email/test/catalog: send every template of the property's catalog (the templates the admin
        /// portal lists) once, with sample data, to the caller or a given address through the configured provider, so the
        /// whole catalog can be reviewed in one inbox (the local stack's Mailpit). Bounded by the catalog. Audited once as
        /// email.test_catalog.
        /// </summary>
        /// <param name="request">Recipient (template is ignored).</param>
        /// <param name="callerEmail">The caller's address, or null.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Per-template outcomes.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed (no recipient).</exception>
        public async Task<EmailCatalogTestResponse> TestCatalogAsync(EmailTestRequest? request, string? callerEmail, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            string? to = String.IsNullOrWhiteSpace(request?.To) ? callerEmail : request!.To!.Trim();
            if (String.IsNullOrWhiteSpace(to) || Validators.Email(to) != null) throw ApiErrorException.Field("to", "Give an email address to send the catalog to.");
            EmailProviderChoice provider = await _Email.ProviderAsync(token).ConfigureAwait(false);
            EmailCatalogTestResponse response = new EmailCatalogTestResponse { To = to, Provider = provider.Provider.ToString() };
            foreach (EmailTemplate template in _Email.Templates.All.Where(t => t.Recipient != EmailRecipientEnum.PlatformAdmins))
            {
                IntegrationTestResponse one = await SendSampleAsync(template, to, provider, token).ConfigureAwait(false);
                response.Results.Add(new EmailCatalogTestResult { Template = template.Key, Ok = one.Ok, Message = one.Message });
                if (one.Ok) response.Sent++;
                else response.Failed++;
            }
            await _Audit.WriteAsync(info, "email.test_catalog", "EmailTemplate", "catalog", "Catalog test send of " + response.Results.Count + " templates through " + provider.Provider, token).ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// GET /api/v1/admin/email/staff-events (fixed-bound by the catalog).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events.</returns>
        public async Task<List<EmailStaffEventView>> StaffEventsAsync(CancellationToken token = default)
        {
            List<EmailStaffEvent> stored = await _Database.Email.ReadStaffEventsAsync(token).ConfigureAwait(false);
            return _Email.Templates.All.Where(t => t.Category == EmailCategoryEnum.Staff).Select(t =>
            {
                EmailStaffEvent? s = stored.FirstOrDefault(e => e.Template == t.Key);
                return new EmailStaffEventView { Template = t.Key, Description = t.Description, DefaultRecipients = t.Recipient, Enabled = s?.Enabled ?? true, Recipients = s?.Recipients ?? new List<string>() };
            }).ToList();
        }

        /// <summary>
        /// PUT /api/v1/admin/email/staff-events/{template}. Audited email.staff_event.update.
        /// </summary>
        /// <param name="key">Staff template key.</param>
        /// <param name="request">Enabled and recipients.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The event.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EmailStaffEventView> PutStaffEventAsync(string key, EmailStaffEventRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(info);
            EmailTemplate template = Require(key);
            if (template.Category != EmailCategoryEnum.Staff) throw ApiErrorException.NotFound("Staff event");
            EmailStaffEventView current = (await StaffEventsAsync(token).ConfigureAwait(false)).First(e => e.Template == template.Key);
            List<string> recipients = request.Recipients?.Where(r => !String.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? current.Recipients;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (recipients.Count > 10) errors["recipients"] = "Use at most 10 addresses.";
            for (int i = 0; i < recipients.Count; i++) if (Validators.Email(recipients[i]) != null) errors["recipients[" + i + "]"] = "Not a valid email address.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EmailStaffEvent value = new EmailStaffEvent { Template = template.Key, Enabled = request.Enabled ?? current.Enabled, Recipients = recipients, UpdatedUtc = DateTime.UtcNow };
            await _Database.Email.WriteStaffEventAsync(value, info.Context?.UserId, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "email.staff_event.update", "EmailTemplate", template.Key, (value.Enabled ? "Enabled" : "Disabled") + ", " + recipients.Count + " recipients", token).ConfigureAwait(false);
            return (await StaffEventsAsync(token).ConfigureAwait(false)).First(e => e.Template == template.Key);
        }

        /// <summary>
        /// POST /api/v1/admin/email/messages/enumerate (metadata only; bodies are never stored).
        /// </summary>
        /// <param name="query">Enumeration query; filters status, template, category, recipient, userId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<EmailMessageRecord>> MessagesAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, EmailCollections.Messages, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            EmailMessageFilter filter = new EmailMessageFilter
            {
                Status = request.Query.FilterEnum<EmailStatusEnum>("status", errors),
                Category = request.Query.FilterEnum<EmailCategoryEnum>("category", errors),
                Template = request.Query.FilterString("template"),
                Recipient = request.Query.FilterString("recipient"),
                UserId = request.Query.FilterString("userId")
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<EmailMessageRecord> page = await _Database.Email.EnumerateMessagesAsync(request, filter, token).ConfigureAwait(false);
            await _Metadata.AttachAsync(TargetTypeEnum.EmailMessage, page.Objects, m => m.Id, true, token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// GET /api/v1/admin/email/messages/{id}.
        /// </summary>
        /// <param name="id">Message id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EmailMessageRecord> MessageAsync(string id, CancellationToken token = default)
        {
            EmailMessageRecord record = await _Database.Email.ReadMessageAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Email message");
            return await _Metadata.AttachOneAsync(TargetTypeEnum.EmailMessage, record, record.Id, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/email/suppressions/enumerate.
        /// </summary>
        /// <param name="query">Enumeration query; filter reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<EmailSuppression>> SuppressionsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, EmailCollections.Suppressions, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            EmailSuppressionReasonEnum? reason = request.Query.FilterEnum<EmailSuppressionReasonEnum>("reason", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<EmailSuppression> page = await _Database.Email.EnumerateSuppressionsAsync(request, reason, token).ConfigureAwait(false);
            await _Metadata.AttachAsync(TargetTypeEnum.EmailSuppression, page.Objects, s => s.Id, true, token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// POST /api/v1/admin/email/suppressions: suppress an address. Audited email.suppression.add.
        /// </summary>
        /// <param name="actor">Staff caller.</param>
        /// <param name="request">Address and note.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Suppression.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EmailSuppression> AddSuppressionAsync(ContentActor actor, EmailSuppressionRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (String.IsNullOrWhiteSpace(request.Email) || Validators.Email(request.Email) != null) errors["email"] = "Not a valid email address.";
            if ((request.Note ?? String.Empty).Length > 500) errors["note"] = "At most 500 characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.EmailSuppression).ConfigureAwait(false);
            EmailSuppression stored = await _Database.Email.AddSuppressionAsync(new EmailSuppression
            {
                Id = IdGenerator.Generate(Constants.EmailSuppressionIdPrefix),
                Email = request.Email!.Trim(),
                Reason = EmailSuppressionReasonEnum.Manual,
                Source = "staff",
                Note = String.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                CreatedUtc = DateTime.UtcNow
            }, token).ConfigureAwait(false);
            if (request.Labels != null || request.Tags != null) await _Metadata.ApplyAsync(actor, TargetTypeEnum.EmailSuppression, stored.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "email.suppression.add", "EmailSuppression", stored.Id, "Suppressed an address", token).ConfigureAwait(false);
            return await _Metadata.AttachOneAsync(TargetTypeEnum.EmailSuppression, stored, stored.Id, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/email/suppressions/{id}. Audited email.suppression.remove.
        /// </summary>
        /// <param name="id">Suppression id.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task RemoveSuppressionAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            EmailSuppression suppression = await _Database.Email.ReadSuppressionAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Suppression");
            await _Database.Email.DeleteSuppressionAsync(suppression.Id, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "email.suppression.remove", "EmailSuppression", suppression.Id, "Removed a suppression", token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/webhooks/mailgun/{propertyId}: verify the signature (HMAC-SHA256 of timestamp and token with the
        /// property's webhook signing key; timestamp within 15 minutes; token not replayed), then update the message and
        /// suppress the address on a permanent failure, complaint, or unsubscribe.
        /// </summary>
        /// <param name="raw">JSON body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome text.</returns>
        /// <exception cref="ApiErrorException">400 InvalidSignature or InvalidJson, 503 IntegrationNotConfigured.</exception>
        public async Task<string> HandleMailgunAsync(string raw, CancellationToken token = default)
        {
            string? key = await _PropertySettings.GetAsync("email.mailgunWebhookSigningKey", token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(key)) throw new ApiErrorException(503, "IntegrationNotConfigured", "The Mailgun webhook signing key is not configured for this property.");
            JsonElement root;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(raw) ? "{}" : raw)) root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new ApiErrorException(400, "InvalidJson", "The webhook body is not JSON.");
            }
            if (!Verify(root, key))
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.EmailWebhooks, "event", "unknown", "outcome", "invalid_signature");
                throw new ApiErrorException(400, "InvalidSignature", "The Mailgun signature is invalid or expired.");
            }
            JsonElement data = root.TryGetProperty("event-data", out JsonElement d) && d.ValueKind == JsonValueKind.Object ? d : default;
            string ev = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("event", out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : String.Empty;
            string severity = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("severity", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : String.Empty;
            string recipient = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("recipient", out JsonElement r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : String.Empty;
            string? messageId = null;
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.Object &&
                m.TryGetProperty("headers", out JsonElement h) && h.ValueKind == JsonValueKind.Object && h.TryGetProperty("message-id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
                messageId = MailgunEmailSender.NormalizeId(id.GetString()!);
            string label = ev == "delivered" || ev == "failed" || ev == "complained" || ev == "unsubscribed" ? ev : "other";
            EmailMessageRecord? record = messageId == null ? null : await _Database.Email.FindByProviderIdAsync(messageId, token).ConfigureAwait(false);
            if (record == null && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("user-variables", out JsonElement vars) && vars.ValueKind == JsonValueKind.Object &&
                vars.TryGetProperty(EmailService.MessageIdVariable, out JsonElement own) && own.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(own.GetString()))
            {
                // Correlate by the engine's own record id (sent as a Mailgun variable) when the message-id header is missing.
                EmailMessageRecord? candidate = await _Database.Email.ReadMessageAsync(own.GetString()!.Trim(), token).ConfigureAwait(false);
                if (candidate != null && !String.IsNullOrWhiteSpace(recipient) && String.Equals(candidate.Recipient, recipient.Trim(), StringComparison.OrdinalIgnoreCase)) record = candidate;
            }
            DateTime now = DateTime.UtcNow;
            bool permanent = ev == "complained" || ev == "unsubscribed" || (ev == "failed" && !String.Equals(severity, "temporary", StringComparison.OrdinalIgnoreCase));
            if (record != null)
            {
                if (ev == "delivered") { record.Status = EmailStatusEnum.Delivered; record.DeliveredUtc = now; }
                else if (ev == "failed" && permanent) { record.Status = EmailStatusEnum.Failed; record.FailedUtc = now; record.LastError = "Mailgun reported a permanent failure."; }
                record.UpdatedUtc = now;
                await _Database.Email.UpdateMessageAsync(record, true, token).ConfigureAwait(false);
            }
            if (permanent && !String.IsNullOrWhiteSpace(recipient))
            {
                EmailSuppressionReasonEnum reason = ev == "complained" ? EmailSuppressionReasonEnum.Complaint : ev == "unsubscribed" ? EmailSuppressionReasonEnum.Unsubscribed : EmailSuppressionReasonEnum.Bounce;
                await _Database.Email.AddSuppressionAsync(new EmailSuppression { Id = IdGenerator.Generate(Constants.EmailSuppressionIdPrefix), Email = recipient.Trim(), Reason = reason, Source = "mailgun:" + ev, CreatedUtc = now }, token).ConfigureAwait(false);
            }
            string outcome = record == null && !permanent ? "unknown_message" : label == "other" ? "ignored" : "ok";
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.EmailWebhooks, "event", label, "outcome", outcome);
            return outcome;
        }

        #endregion

        #region Private-Methods

        private async Task<IntegrationTestResponse> SendSampleAsync(EmailTemplate template, string to, EmailProviderChoice provider, CancellationToken token)
        {
            EmailRequest send = new EmailRequest { Template = template.Key, To = to };
            foreach (KeyValuePair<string, string> sample in template.Samples) send.Tokens[sample.Key] = sample.Value;
            send.Tokens["memberName"] = "Pat Example";
            send.Tokens["provider"] = provider.Provider.ToString();
            EmailMessageRecord? record = await _Email.QueueAsync(send, token).ConfigureAwait(false);
            if (record == null) return new IntegrationTestResponse { Ok = false, Message = "The test could not be queued." };
            if (record.Status == EmailStatusEnum.Sent && record.Provider == EmailProviderEnum.Log.ToString())
                return new IntegrationTestResponse { Ok = false, Message = "Email is not configured; the message was written to the server log." };
            if (record.Status == EmailStatusEnum.Sent) return new IntegrationTestResponse { Ok = true, Message = "Sent through " + record.Provider + " to " + to + "." };
            if (record.Status == EmailStatusEnum.Suppressed) return new IntegrationTestResponse { Ok = false, Message = to + " is on the suppression list." };
            return new IntegrationTestResponse { Ok = false, Message = record.LastError ?? "Delivery failed; it will be retried." };
        }

        private bool Verify(JsonElement root, string key)
        {
            if (!root.TryGetProperty("signature", out JsonElement signature) || signature.ValueKind != JsonValueKind.Object) return false;
            string? Field(string name) => signature.TryGetProperty(name, out JsonElement v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null) : null;
            string? timestamp = Field("timestamp");
            string? nonce = Field("token");
            string? given = Field("signature");
            if (timestamp == null || nonce == null || given == null) return false;
            if (!Int64.TryParse(timestamp, out long seconds) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > 900) return false;
            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(timestamp + nonce));
            byte[] presented;
            try
            {
                presented = Convert.FromHexString(given);
            }
            catch (FormatException)
            {
                return false;
            }
            if (!CryptographicOperations.FixedTimeEquals(expected, presented)) return false;
            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, DateTime> seen in _SeenTokens.Where(t => t.Value < now.AddMinutes(-20)).ToList()) _SeenTokens.TryRemove(seen.Key, out DateTime _);
            return _SeenTokens.TryAdd(nonce, now);
        }

        private EmailTemplate Require(string key)
        {
            EmailTemplate? template = _Email.Templates.Find(key);
            if (template == null || template.Recipient == EmailRecipientEnum.PlatformAdmins) throw ApiErrorException.NotFound("Email template");
            return template;
        }

        private static EmailTemplateView View(EmailTemplate t, EmailTemplateOverride? o)
        {
            return new EmailTemplateView
            {
                Key = t.Key,
                Category = t.Category,
                Recipient = t.Recipient,
                Description = t.Description,
                Tokens = t.Tokens.ToList(),
                DefaultSubject = t.DefaultSubject,
                DefaultPreheader = t.DefaultPreheader,
                DefaultHeading = t.DefaultHeading,
                DefaultBody = t.DefaultBody,
                Override = o
            };
        }

        private static string? Empty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
