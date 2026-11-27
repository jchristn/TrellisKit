namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services.Email;

    /// <summary>
    /// Administrative email of one property (docs/TRELLISKIT.md section 12): every message is a template rendered with
    /// HtmlPageBuilder in the property's brand, queued as an EmailMessage record (bodies are never stored; the recipient
    /// and tokens are encrypted until the message is final), sent at once and retried by EmailDeliveryWorker with
    /// backoff, idempotent per event key, and never sent to a suppressed address. The provider is resolved per send from
    /// the property's settings: Mailgun (SendWithMailgun), SMTP (the property's own server, or in SingleProperty the
    /// deployment's), or the log. Sending never fails the caller.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class EmailService
    {
        #region Public-Members

        /// <summary>
        /// Category (template key) of verification emails.
        /// </summary>
        public const string VerifyEmailCategory = "verify_email";

        /// <summary>
        /// Category (template key) of password reset emails.
        /// </summary>
        public const string PasswordResetCategory = "password_reset";

        /// <summary>
        /// Category (template key) of code membership expiry reminders.
        /// </summary>
        public const string CodeReminderCategory = "code_expiry_reminder";

        /// <summary>
        /// Category (template key) of code membership revocation notices.
        /// </summary>
        public const string CodeRevokedCategory = "code_revoked";

        /// <summary>
        /// List-Unsubscribe header name (RFC 2369).
        /// </summary>
        public const string ListUnsubscribeHeader = "List-Unsubscribe";

        /// <summary>
        /// List-Unsubscribe-Post header name (RFC 8058 one-click).
        /// </summary>
        public const string ListUnsubscribePostHeader = "List-Unsubscribe-Post";

        /// <summary>
        /// Provider variable carrying the engine's message record id (echoed in Mailgun webhooks as user-variables).
        /// </summary>
        public const string MessageIdVariable = "tk-message-id";

        /// <summary>
        /// Provider variable carrying the property id.
        /// </summary>
        public const string PropertyIdVariable = "tk-property-id";

        /// <summary>
        /// Provider variable carrying the template key.
        /// </summary>
        public const string TemplateVariable = "tk-template";

        /// <summary>
        /// Called after a message is queued so the delivery worker runs soon. Set by the property runtime.
        /// </summary>
        public Action? Signal { get; set; } = null;

        /// <summary>
        /// Builder of the request, member, and origin sections of staff emails (docs/EMAIL.md, "Staff email details"). Set by the property
        /// services; null in tests that build the email service alone.
        /// </summary>
        public StaffEmailDetailsService? StaffDetails { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly IEmailSender _DeploymentSender;
        private readonly PropertyContext _Property;
        private readonly AppSettings _Settings;
        private readonly PropertySettingsService _PropertySettings;
        private readonly AppLogger _Logger;
        private readonly string _Holder = "email-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        private readonly Lazy<EmailTemplateSet> _Templates;
        private readonly Lazy<EmailLayoutOptions> _Layout;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="deploymentSender">The deployment's SMTP sender (SingleProperty default).</param>
        /// <param name="property">Active property context (links and brand).</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="propertySettings">The property's own email settings.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public EmailService(DatabaseDriverBase database, IEmailSender deploymentSender, PropertyContext property, AppSettings settings, PropertySettingsService propertySettings, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _DeploymentSender = deploymentSender ?? throw new ArgumentNullException(nameof(deploymentSender));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _PropertySettings = propertySettings ?? throw new ArgumentNullException(nameof(propertySettings));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _Templates = new Lazy<EmailTemplateSet>(LoadTemplates, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
            _Layout = new Lazy<EmailLayoutOptions>(LoadLayout, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The templates this property sends: the engine catalog and registered templates, with the property's
        /// email/templates JSON files layered on top (loaded once, on first use; problems are logged).
        /// </summary>
        public EmailTemplateSet Templates
        {
            get { return _Templates.Value; }
        }

        /// <summary>
        /// Link to a website page, for example /verify-email?token=...
        /// </summary>
        /// <param name="pathAndQuery">Path and query starting with a slash.</param>
        /// <returns>Absolute URL.</returns>
        public string Link(string pathAndQuery)
        {
            return _Property.Link(pathAndQuery);
        }

        /// <summary>
        /// Link to the website page for a member app path (a notification path such as /support/tickets/{id}). App paths are
        /// shared with the mobile app; the website serves member pages under /app (see <see cref="WebPathFor"/>).
        /// </summary>
        /// <param name="appPath">App path starting with a slash.</param>
        /// <returns>Absolute URL.</returns>
        public string MemberLink(string appPath)
        {
            return _Property.Link(WebPathFor(appPath));
        }

        /// <summary>
        /// The website path for a member app path, mirroring the website's notification path mapping: admin console paths and
        /// paths already under /app stay as they are, /account/membership and /account/notifications map to their website
        /// pages, /me/directory/submissions maps to /app/directory/submissions, and every other path is served under /app.
        /// </summary>
        /// <param name="appPath">App path starting with a slash; null or empty gives /app.</param>
        /// <returns>Website path and query.</returns>
        public static string WebPathFor(string? appPath)
        {
            string path = (appPath ?? String.Empty).Trim();
            if (path.Length == 0 || !path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return "/app";
            if (path == "/admin" || path.StartsWith("/admin/", StringComparison.Ordinal) || path.StartsWith("/admin?", StringComparison.Ordinal)) return path;
            if (path == "/app" || path.StartsWith("/app/", StringComparison.Ordinal) || path.StartsWith("/app?", StringComparison.Ordinal)) return path;
            if (path.StartsWith("/account/membership", StringComparison.Ordinal)) return "/app/membership";
            if (path.StartsWith("/account/notifications", StringComparison.Ordinal)) return "/app/settings/notifications";
            if (path.StartsWith("/me/directory/submissions", StringComparison.Ordinal)) return "/app/directory/submissions" + path.Substring("/me/directory/submissions".Length);
            return "/app" + path;
        }

        /// <summary>
        /// The property's brand for the email layout.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Brand.</returns>
        public async Task<EmailBrand> BrandAsync(CancellationToken token = default)
        {
            string? postal = await _PropertySettings.GetAsync("email.postalAddress", token).ConfigureAwait(false);
            return EmailBrandFactory.Create(_Property, _Layout.Value, postal, DateTime.UtcNow);
        }

        /// <summary>
        /// The property's email/layout.json choices (loaded once, on first use; problems are logged).
        /// </summary>
        public EmailLayoutOptions LayoutOptions
        {
            get { return _Layout.Value; }
        }

        /// <summary>
        /// Resolve the provider that sends this property's mail now (section 12.1).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Provider, sender, and sender identity.</returns>
        public async Task<EmailProviderChoice> ProviderAsync(CancellationToken token = default)
        {
            bool single = _Settings.Deployment.Mode == DeploymentModeEnum.SingleProperty;
            string chosen = await _PropertySettings.GetOrDefaultAsync("email.provider", token).ConfigureAwait(false);
            EmailProviderEnum provider = Enum.TryParse(chosen, true, out EmailProviderEnum parsed) ? parsed : EmailProviderEnum.Auto;
            if (provider == EmailProviderEnum.Auto && single && _Settings.Email.Provider == EmailProviderEnum.Log) provider = EmailProviderEnum.Log;
            string fromName = await _PropertySettings.GetAsync("email.fromName", token).ConfigureAwait(false) ?? (single && !String.IsNullOrWhiteSpace(_Settings.Email.FromName) ? _Settings.Email.FromName : _Property.EmailFromName);
            string fromAddress = await _PropertySettings.GetAsync("email.fromAddress", token).ConfigureAwait(false) ?? (single && !String.IsNullOrWhiteSpace(_Settings.Email.FromAddress) ? _Settings.Email.FromAddress : _Property.EmailFromAddress);
            string? replyTo = await _PropertySettings.GetAsync("email.replyTo", token).ConfigureAwait(false) ?? NullIfEmpty(_Property.Property.Contact.SupportEmail);
            EmailProviderChoice choice = new EmailProviderChoice { FromName = fromName, FromAddress = fromAddress, ReplyTo = replyTo };

            string? domain = await _PropertySettings.GetAsync("email.mailgunDomain", token).ConfigureAwait(false);
            string? apiKey = await _PropertySettings.GetAsync("email.mailgunApiKey", token).ConfigureAwait(false);
            bool mailgun = !String.IsNullOrWhiteSpace(domain) && !String.IsNullOrWhiteSpace(apiKey);
            if ((provider == EmailProviderEnum.Mailgun || provider == EmailProviderEnum.Auto) && mailgun)
            {
                string region = await _PropertySettings.GetOrDefaultAsync("email.mailgunRegion", token).ConfigureAwait(false);
                string baseUrl = String.Equals(region, "Eu", StringComparison.OrdinalIgnoreCase) ? "https://api.eu.mailgun.net/v3/" : EmailSettings.DefaultMailgunBaseUrl;
                choice.Provider = EmailProviderEnum.Mailgun;
                choice.Sender = new MailgunEmailSender(domain!, apiKey!, baseUrl, fromName, fromAddress);
                return choice;
            }
            if (provider == EmailProviderEnum.Mailgun || provider == EmailProviderEnum.Log) return choice;
            string? host = await _PropertySettings.GetAsync("email.smtpHost", token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(host))
            {
                EmailSettings smtp = new EmailSettings
                {
                    SmtpHost = host,
                    SmtpPort = Int32.TryParse(await _PropertySettings.GetOrDefaultAsync("email.smtpPort", token).ConfigureAwait(false), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) ? port : 587,
                    Username = await _PropertySettings.GetAsync("email.smtpUsername", token).ConfigureAwait(false) ?? String.Empty,
                    Password = await _PropertySettings.GetAsync("email.smtpPassword", token).ConfigureAwait(false) ?? String.Empty,
                    UseTls = !String.Equals(await _PropertySettings.GetOrDefaultAsync("email.smtpUseTls", token).ConfigureAwait(false), "false", StringComparison.OrdinalIgnoreCase),
                    FromName = fromName,
                    FromAddress = fromAddress
                };
                choice.Provider = EmailProviderEnum.Smtp;
                choice.Sender = new SmtpEmailSender(smtp, fromName, fromAddress);
                return choice;
            }
            // Resolved decision 14: in MultiProperty no property borrows the deployment's mail account.
            if (single && _DeploymentSender.IsConfigured)
            {
                choice.Provider = EmailProviderEnum.Smtp;
                choice.Sender = _DeploymentSender;
            }
            return choice;
        }

        /// <summary>
        /// Queue an administrative email and make the first attempt now. Never throws for delivery problems.
        /// </summary>
        /// <param name="request">Message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null when the event already had a message (idempotency) or there is no recipient.</returns>
        public async Task<EmailMessageRecord?> QueueAsync(EmailRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (String.IsNullOrWhiteSpace(request.To)) return null;
            EmailTemplate template = Templates.Get(request.Template);
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> tokens = await TokensAsync(request.Tokens, token).ConfigureAwait(false);
            // Request and member details (docs/EMAIL.md, "Staff email details") are for staff only: never stored on a member email.
            if (template.Category != EmailCategoryEnum.Staff) tokens.Remove(Email.StaffEmailDetails.Token);
            else if (template.StaffOrigin != null && StaffDetails != null && String.IsNullOrEmpty(tokens.GetValueOrDefault(Email.StaffEmailDetails.Token)))
                tokens[Email.StaffEmailDetails.Token] = new Email.StaffEmailDetails().Add(StaffDetails.Origin(template.StaffOrigin)).Serialize();
            string id = IdGenerator.Generate(Constants.EmailMessageIdPrefix);
            EmailPayload payload = new EmailPayload { Tokens = tokens, Headers = request.AddListUnsubscribe && !request.Essential ? UnsubscribeHeaders(template, tokens, request.Headers) : new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase) };
            EmailMessageRecord record = new EmailMessageRecord
            {
                Id = id,
                Template = template.Key,
                Category = request.Essential ? EmailCategoryEnum.Essential : template.Category,
                Recipient = request.To.Trim(),
                UserId = request.UserId,
                Subject = EmailRenderer.Fill(template.DefaultSubject, tokens),
                Status = EmailStatusEnum.Queued,
                IdempotencyKey = request.IdempotencyKey,
                Payload = SecretBoxProvider.For(_Settings.Secrets).Encrypt(JsonSerializer.Serialize(payload), AssociatedData(id)),
                NextAttemptUtc = now,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                if (!await _Database.Email.CreateMessageAsync(record, token).ConfigureAwait(false)) return null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[EmailService] [p:" + _Property.Property.Id + "] could not queue " + template.Key + ": " + e.Message);
                return null;
            }
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.EmailQueued, "template", template.Key, "category", template.Category.ToString());
            await AttemptAsync(record, payload, token).ConfigureAwait(false);
            return record;
        }

        /// <summary>
        /// Deliver due queued messages (retries): one pass of EmailDeliveryWorker, plus the retention prune.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages attempted.</returns>
        public async Task<int> DeliverDueAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            List<EmailMessageRecord> due = await _Database.Email.ClaimDueAsync(now, _Holder, 300, 50, token).ConfigureAwait(false);
            foreach (EmailMessageRecord record in due)
            {
                EmailPayload? payload = Decrypt(record);
                if (payload == null)
                {
                    Final(record, EmailStatusEnum.Failed, "The queued payload could not be decrypted.");
                    await _Database.Email.UpdateMessageAsync(record, true, token).ConfigureAwait(false);
                    continue;
                }
                await AttemptAsync(record, payload, token).ConfigureAwait(false);
            }
            await _Database.Email.PruneMessagesAsync(now.AddDays(-_Settings.Email.MessageRetentionDays), 1000, token).ConfigureAwait(false);
            return due.Count;
        }

        /// <summary>
        /// Render a template for a preview or a test with the property's brand and override.
        /// </summary>
        /// <param name="template">Template.</param>
        /// <param name="overrides">Override, or null for the saved one.</param>
        /// <param name="tokens">Token values (the template's samples fill the rest).</param>
        /// <param name="useSaved">Whether to use the saved override when <paramref name="overrides"/> is null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rendered email.</returns>
        public async Task<EmailRendered> RenderAsync(EmailTemplate template, EmailTemplateOverride? overrides, IReadOnlyDictionary<string, string>? tokens, bool useSaved, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(template);
            Dictionary<string, string> values = new Dictionary<string, string>(template.Samples, StringComparer.Ordinal) { ["memberName"] = "Pat Example" };
            foreach (KeyValuePair<string, string> pair in tokens ?? new Dictionary<string, string>()) values[pair.Key] = pair.Value;
            values = await TokensAsync(values, token).ConfigureAwait(false);
            EmailTemplateOverride? saved = overrides;
            if (saved == null && useSaved) saved = (await _Database.Email.ReadOverridesAsync(token).ConfigureAwait(false)).FirstOrDefault(o => o.Template == template.Key);
            return EmailRenderer.Render(template, saved, values, await BrandAsync(token).ConfigureAwait(false));
        }

        /// <summary>
        /// Send the email verification message.
        /// </summary>
        /// <param name="to">Recipient.</param>
        /// <param name="displayName">Member name.</param>
        /// <param name="rawToken">Verification token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendVerificationAsync(string to, string displayName, string rawToken, CancellationToken token = default)
        {
            return MemberAsync(VerifyEmailCategory, to, null, displayName, null, token, ("link", Link("/verify-email?token=" + Uri.EscapeDataString(rawToken))));
        }

        /// <summary>
        /// Send the password reset message.
        /// </summary>
        /// <param name="to">Recipient.</param>
        /// <param name="displayName">Member name.</param>
        /// <param name="rawToken">Reset token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendPasswordResetAsync(string to, string displayName, string rawToken, CancellationToken token = default)
        {
            return MemberAsync(PasswordResetCategory, to, null, displayName, null, token, ("link", Link("/reset-password?token=" + Uri.EscapeDataString(rawToken))));
        }

        /// <summary>
        /// Send a reminder that a free code membership is ending.
        /// </summary>
        /// <param name="to">Recipient.</param>
        /// <param name="displayName">Member name.</param>
        /// <param name="endsUtc">When the free membership ends.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendCodeReminderAsync(string to, string displayName, DateTime endsUtc, CancellationToken token = default)
        {
            return MemberAsync(CodeReminderCategory, to, null, displayName, null, token, ("link", Link("/app/membership")), ("endDate", FormatDate(endsUtc)));
        }

        /// <summary>
        /// Tell a member their code membership was revoked.
        /// </summary>
        /// <param name="to">Recipient.</param>
        /// <param name="displayName">Member name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendCodeRevokedAsync(string to, string displayName, CancellationToken token = default)
        {
            return MemberAsync(CodeRevokedCategory, to, null, displayName, null, token, ("link", Link("/app/membership")));
        }

        /// <summary>
        /// Send a member lifecycle email (welcome, security notices, billing, moderation, account; section 12.3).
        /// </summary>
        /// <param name="template">Template key.</param>
        /// <param name="user">Member (no email is sent to deleted members or members without an address).</param>
        /// <param name="idempotencyKey">Event key, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="tokens">Template tokens.</param>
        /// <returns>Task.</returns>
        public Task SendToMemberAsync(string template, User? user, string? idempotencyKey, CancellationToken token, params (string Key, string Value)[] tokens)
        {
            if (user == null || String.IsNullOrWhiteSpace(user.Email)) return Task.CompletedTask;
            return MemberAsync(template, user.Email, user.Id, user.DisplayName, idempotencyKey, token, tokens);
        }

        /// <summary>
        /// Queue a message from a typed template (see <see cref="TypedEmailTemplate{TContext}"/>): the context is flattened
        /// into tokens, which the queue stores, so retries render the same message. The template must be in
        /// <see cref="Templates"/> (register it with <see cref="EmailTemplateCatalog.Register"/>).
        /// </summary>
        /// <typeparam name="TContext">Context type.</typeparam>
        /// <param name="template">Template.</param>
        /// <param name="to">Recipient address.</param>
        /// <param name="context">Context.</param>
        /// <param name="userId">Recipient member id, or null.</param>
        /// <param name="idempotencyKey">Event key, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null (no recipient, or the event already had a message).</returns>
        /// <exception cref="ArgumentNullException">template is null.</exception>
        /// <exception cref="ArgumentException">The template is not registered for this property.</exception>
        public Task<EmailMessageRecord?> SendAsync<TContext>(TypedEmailTemplate<TContext> template, string to, TContext context, string? userId = null, string? idempotencyKey = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(template);
            EmailTemplate? found = Templates.Find(template.Key);
            bool registered = ReferenceEquals(found, template) || (found is PropertyEmailTemplate own && ReferenceEquals(own.Replaces, template));
            if (!registered)
                throw new ArgumentException("Email template " + template.Key + " is not registered; call EmailTemplateCatalog.Register at startup.", nameof(template));
            EmailRequest request = new EmailRequest { Template = template.Key, To = to ?? String.Empty, UserId = userId, IdempotencyKey = idempotencyKey };
            foreach (KeyValuePair<string, string> pair in template.TokensFor(context) ?? new Dictionary<string, string>()) request.Tokens[pair.Key] = pair.Value ?? String.Empty;
            return QueueAsync(request, token);
        }

        /// <summary>
        /// Send a staff notification (section 12.3) to the event's configured recipients, or by default the property's
        /// SystemAdmins (moderator events: SystemAdmins and feed moderators); skipped when the event is disabled.
        /// </summary>
        /// <param name="template">Staff template key.</param>
        /// <param name="idempotencyKey">Event key, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="tokens">Template tokens.</param>
        /// <returns>Task.</returns>
        public async Task NotifyStaffAsync(string template, string? idempotencyKey, CancellationToken token, params (string Key, string Value)[] tokens)
        {
            try
            {
                EmailTemplate t = Templates.Get(template);
                EmailStaffEvent? setting = (await _Database.Email.ReadStaffEventsAsync(token).ConfigureAwait(false)).FirstOrDefault(e => e.Template == t.Key);
                if (setting != null && !setting.Enabled) return;
                List<string> recipients = setting != null && setting.Recipients.Count > 0 ? setting.Recipients : await DefaultStaffAsync(t.Recipient, token).ConfigureAwait(false);
                foreach (string recipient in recipients.Distinct(StringComparer.OrdinalIgnoreCase).Take(10))
                {
                    EmailRequest request = new EmailRequest { Template = t.Key, To = recipient, IdempotencyKey = idempotencyKey == null ? null : idempotencyKey + ":" + recipient.ToLowerInvariant() };
                    foreach ((string Key, string Value) pair in tokens) request.Tokens[pair.Key] = pair.Value ?? String.Empty;
                    request.Tokens.TryAdd("memberName", "team");
                    await QueueAsync(request, token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[EmailService] [p:" + _Property.Property.Id + "] staff notification " + template + " failed: " + e.Message);
            }
        }

        /// <summary>
        /// Alert staff that an integration is failing, at most once per email.integrationAlertMinutes per kind.
        /// </summary>
        /// <param name="integration">Integration name (bounded set, for example Stripe webhook).</param>
        /// <param name="detail">Problem (no secrets).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task AlertIntegrationAsync(string integration, string detail, CancellationToken token = default)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                string kind = "integration:" + integration.ToLowerInvariant().Replace(' ', '_') + ":";
                if (await _Database.Email.ExistsSinceAsync(kind, now.AddMinutes(-_Settings.Email.IntegrationAlertMinutes), token).ConfigureAwait(false)) return;
                await NotifyStaffAsync("staff_integration_failure", kind + now.Ticks.ToString(CultureInfo.InvariantCulture), token,
                    ("integration", integration), ("detail", detail.Length > 300 ? detail.Substring(0, 300) : detail), ("link", Link("/admin"))).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[EmailService] integration alert failed: " + e.Message);
            }
        }

        /// <summary>
        /// Check a new address with Mailgun validation when the property enables it (section 12.1): risk high or result
        /// undeliverable is refused; unknown results, errors, and an unconfigured validator allow the address.
        /// </summary>
        /// <param name="email">Address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A member-facing message when refused, else null.</returns>
        public async Task<string?> ValidateAddressAsync(string? email, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(email)) return null;
            if (!String.Equals(await _PropertySettings.GetOrDefaultAsync("email.validationEnabled", token).ConfigureAwait(false), "true", StringComparison.OrdinalIgnoreCase)) return null;
            string? key = await _PropertySettings.GetAsync("email.mailgunValidationApiKey", token).ConfigureAwait(false) ?? await _PropertySettings.GetAsync("email.mailgunApiKey", token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(key)) return null;
            string region = await _PropertySettings.GetOrDefaultAsync("email.mailgunRegion", token).ConfigureAwait(false);
            string baseUrl = String.Equals(region, "Eu", StringComparison.OrdinalIgnoreCase) ? "https://api.eu.mailgun.net/v4/" : "https://api.mailgun.net/v4/";
            try
            {
                SendWithMailgun.MailgunValidator validator = new SendWithMailgun.MailgunValidator(key, baseUrl);
                SendWithMailgun.MailgunValidationResult? result = await validator.ValidateAsync(email.Trim(), token).ConfigureAwait(false);
                if (result == null) return null;
                if (result.Risk == SendWithMailgun.RiskEnum.High || result.Result == SendWithMailgun.ResultEnum.Undeliverable || result.Result == SendWithMailgun.ResultEnum.DoNotSend)
                    return "We could not deliver email to that address. Check it, or use another address.";
                return null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[EmailService] [p:" + _Property.Property.Id + "] address validation unavailable: " + e.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// The headers a message carries: the caller's, plus, for an Optional template whose unsubscribeLink token is the
        /// engine's signed one-click endpoint, List-Unsubscribe and List-Unsubscribe-Post: List-Unsubscribe=One-Click (RFC 2369
        /// and RFC 8058) when the caller did not set them. <see cref="EmailRequest.AddListUnsubscribe"/> false skips this.
        /// </summary>
        /// <param name="template">Template.</param>
        /// <param name="tokens">Resolved tokens.</param>
        /// <param name="given">Caller headers, or null.</param>
        /// <returns>Headers (a new dictionary).</returns>
        /// <exception cref="ArgumentNullException">template or tokens is null.</exception>
        public static Dictionary<string, string> UnsubscribeHeaders(EmailTemplate template, IReadOnlyDictionary<string, string> tokens, IReadOnlyDictionary<string, string>? given)
        {
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(tokens);
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in given ?? new Dictionary<string, string>()) headers[pair.Key] = pair.Value;
            if (template.Category != EmailCategoryEnum.Optional) return headers;
            // Only the engine's signed one-click endpoint can answer List-Unsubscribe-Post; a fallback settings link cannot.
            if (!headers.ContainsKey(ListUnsubscribeHeader) && tokens.TryGetValue("unsubscribeLink", out string? link) && EmailStyle.IsHttpUrl(link) &&
                link!.Contains(Constants.ApiPrefix + "/email/unsubscribe?", StringComparison.Ordinal))
                headers[ListUnsubscribeHeader] = "<" + link!.Trim() + ">";
            if (headers.ContainsKey(ListUnsubscribeHeader) && !headers.ContainsKey(ListUnsubscribePostHeader))
                headers[ListUnsubscribePostHeader] = "List-Unsubscribe=One-Click";
            return headers;
        }

        /// <summary>
        /// Long date form used in emails, for example November 11, 2026.
        /// </summary>
        /// <param name="utc">Time.</param>
        /// <returns>Text.</returns>
        public static string FormatDate(DateTime utc)
        {
            return utc.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        }

        #endregion

        #region Private-Methods

        private EmailTemplateSet LoadTemplates()
        {
            EmailTemplateSet set = EmailTemplateSet.Load(_Property.Property.Directory);
            foreach (string problem in set.Problems) _Logger.Warn("[EmailService] [p:" + _Property.Property.Id + "] email template skipped: " + problem);
            if (set.PropertyTemplates.Count > 0) _Logger.Info("[EmailService] [p:" + _Property.Property.Id + "] " + set.PropertyTemplates.Count + " property email template(s) loaded");
            return set;
        }

        private EmailLayoutOptions LoadLayout()
        {
            EmailLayoutOptions layout = EmailLayoutOptions.Load(_Property.Property.Directory);
            foreach (string problem in layout.Problems) _Logger.Warn("[EmailService] [p:" + _Property.Property.Id + "] email layout setting ignored: " + problem);
            return layout;
        }

        private async Task MemberAsync(string template, string to, string? userId, string displayName, string? idempotencyKey, CancellationToken token, params (string Key, string Value)[] tokens)
        {
            try
            {
                EmailRequest request = new EmailRequest { Template = template, To = to, UserId = userId, IdempotencyKey = idempotencyKey };
                request.Tokens["memberName"] = displayName ?? String.Empty;
                foreach ((string Key, string Value) pair in tokens) request.Tokens[pair.Key] = pair.Value ?? String.Empty;
                request.Tokens.TryAdd("link", Link("/app"));
                await QueueAsync(request, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[EmailService] [p:" + _Property.Property.Id + "] " + template + " to " + Mask(to) + " failed: " + e.Message);
            }
        }

        private async Task<Dictionary<string, string>> TokensAsync(IReadOnlyDictionary<string, string> given, CancellationToken token)
        {
            Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = _Property.Name,
                ["supportEmail"] = _Property.Property.Contact.SupportEmail ?? String.Empty,
                ["webUrl"] = _Property.PublicBaseUrl
            };
            foreach (KeyValuePair<string, string> pair in given) tokens[pair.Key] = pair.Value ?? String.Empty;
            if (!tokens.ContainsKey("provider")) tokens["provider"] = (await ProviderAsync(token).ConfigureAwait(false)).Provider.ToString();
            return tokens;
        }

        private async Task<List<string>> DefaultStaffAsync(EmailRecipientEnum recipient, CancellationToken token)
        {
            List<RoleEnum> roles = recipient == EmailRecipientEnum.Moderators ? new List<RoleEnum> { RoleEnum.SystemAdmin, RoleEnum.FeedModerator } : new List<RoleEnum> { RoleEnum.SystemAdmin };
            List<string> emails = new List<string>();
            foreach (RoleEnum role in roles)
            {
                List<RoleAssignment> assignments = await _Database.RoleAssignments.EnumerateByRoleAsync(role, 10, token).ConfigureAwait(false);
                List<User> users = await _Database.Users.ReadManyAsync(assignments.Select(a => a.UserId).Distinct(StringComparer.Ordinal).ToList(), token).ConfigureAwait(false);
                emails.AddRange(users.Where(u => !u.IsDeleted && !String.IsNullOrWhiteSpace(u.Email)).Select(u => u.Email!));
            }
            return emails.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        }

        private async Task AttemptAsync(EmailMessageRecord record, EmailPayload payload, CancellationToken token)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("email.send"))
            {
                activity?.SetTag("email.template", record.Template);
                DateTime now = DateTime.UtcNow;
                record.Attempts++;
                record.UpdatedUtc = now;
                try
                {
                    EmailTemplate template = Templates.Get(record.Template);
                    if (await _Database.Email.IsSuppressedAsync(record.Recipient, token).ConfigureAwait(false))
                    {
                        Final(record, EmailStatusEnum.Suppressed, null);
                        await _Database.Email.UpdateMessageAsync(record, true, token).ConfigureAwait(false);
                        TrellisKitTelemetry.Increment(TrellisKitTelemetry.EmailSuppressed, "template", record.Template);
                        return;
                    }
                    EmailProviderChoice provider = await ProviderAsync(token).ConfigureAwait(false);
                    EmailTemplateOverride? overrides = (await _Database.Email.ReadOverridesAsync(token).ConfigureAwait(false)).FirstOrDefault(o => o.Template == template.Key);
                    EmailRendered rendered = EmailRenderer.Render(template, overrides, payload.Tokens, await BrandAsync(token).ConfigureAwait(false));
                    record.Subject = rendered.Subject;
                    record.Provider = provider.Provider.ToString();
                    if (provider.Sender == null || !provider.Sender.IsConfigured)
                    {
                        payload.Tokens.TryGetValue("link", out string? link);
                        _Logger.Info("[EmailService] [p:" + _Property.Property.Id + "] email is not configured; " + template.Key + " message for " + Mask(record.Recipient) + ": subject \"" + rendered.Subject + "\"" + (link == null ? String.Empty : ", link " + link));
                        record.Provider = EmailProviderEnum.Log.ToString();
                        Final(record, EmailStatusEnum.Sent, null);
                        record.SentUtc = now;
                        await _Database.Email.UpdateMessageAsync(record, true, token).ConfigureAwait(false);
                        TrellisKitTelemetry.Increment(TrellisKitTelemetry.EmailSent, "template", record.Template, "provider", "Log");
                        TrellisKitTelemetry.Increment(TrellisKitTelemetry.Emails, "category", record.Template, "outcome", "logged");
                        return;
                    }
                    EmailMessage message = new EmailMessage
                    {
                        To = record.Recipient,
                        Subject = rendered.Subject,
                        TextBody = rendered.Text,
                        HtmlBody = rendered.Html,
                        Category = template.Key,
                        FromName = provider.FromName,
                        FromAddress = provider.FromAddress,
                        ReplyTo = provider.ReplyTo,
                        Headers = payload.Headers,
                        Tags = new List<string> { template.Key, record.Category.ToString().ToLowerInvariant() },
                        Variables = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [MessageIdVariable] = record.Id,
                            [PropertyIdVariable] = _Property.Property.Id,
                            [TemplateVariable] = template.Key
                        }
                    };
                    await ApplyTrackingAsync(message, record.Category, token).ConfigureAwait(false);
                    await provider.Sender.SendAsync(message, token).ConfigureAwait(false);
                    record.ProviderMessageId = message.ProviderMessageId;
                    Final(record, EmailStatusEnum.Sent, null);
                    record.SentUtc = now;
                    await _Database.Email.UpdateMessageAsync(record, true, token).ConfigureAwait(false);
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.EmailSent, "template", record.Template, "provider", record.Provider);
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.Emails, "category", record.Template, "outcome", "sent");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    bool final = record.Attempts >= _Settings.Email.DeliveryMaxAttempts;
                    record.LastError = e.Message.Length > 500 ? e.Message.Substring(0, 500) : e.Message;
                    if (final) Final(record, EmailStatusEnum.Failed, record.LastError);
                    else record.NextAttemptUtc = now.AddMinutes(Math.Min(240, Math.Pow(2, record.Attempts)));
                    activity?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.EmailFailed, 1, "template", record.Template, "provider", record.Provider ?? "unknown", "final", final ? "true" : "false");
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.Emails, "category", record.Template, "outcome", "failed");
                    _Logger.Warn("[EmailService] [p:" + _Property.Property.Id + "] " + record.Template + " to " + Mask(record.Recipient) + " failed (attempt " + record.Attempts + "): " + record.LastError);
                    try
                    {
                        await _Database.Email.UpdateMessageAsync(record, final, token).ConfigureAwait(false);
                    }
                    catch (Exception update) when (update is not OperationCanceledException)
                    {
                        _Logger.Warn("[EmailService] could not record the delivery failure: " + update.Message);
                    }
                    if (final && record.Template != "staff_integration_failure") await AlertIntegrationAsync("Email delivery", e.GetType().Name + " sending " + record.Template, token).ConfigureAwait(false);
                }
            }
        }

        private async Task ApplyTrackingAsync(EmailMessage message, EmailCategoryEnum category, CancellationToken token)
        {
            // Essential and staff mail carries sign-in, reset, and billing links: never rewrite them or add a pixel.
            bool optional = category == EmailCategoryEnum.Optional;
            bool opens = String.Equals(await _PropertySettings.GetOrDefaultAsync("email.trackOpens", token).ConfigureAwait(false), "true", StringComparison.OrdinalIgnoreCase);
            bool clicks = String.Equals(await _PropertySettings.GetOrDefaultAsync("email.trackClicks", token).ConfigureAwait(false), "true", StringComparison.OrdinalIgnoreCase);
            message.TrackOpens = optional && opens;
            message.TrackClicks = optional && clicks;
        }

        private static void Final(EmailMessageRecord record, EmailStatusEnum status, string? error)
        {
            record.Status = status;
            record.NextAttemptUtc = null;
            if (status == EmailStatusEnum.Failed) record.FailedUtc = DateTime.UtcNow;
            if (error != null) record.LastError = error;
        }

        private EmailPayload? Decrypt(EmailMessageRecord record)
        {
            if (record.Payload == null) return null;
            string? json = SecretBoxProvider.For(_Settings.Secrets).Decrypt(record.Payload.Ciphertext, record.Payload.Nonce, record.Payload.KeyId, AssociatedData(record.Id));
            if (json == null) return null;
            try
            {
                return JsonSerializer.Deserialize<EmailPayload>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private string AssociatedData(string id)
        {
            return _Property.Property.Id + "|email|" + id;
        }

        private static string? NullIfEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Mask(string email)
        {
            int at = email.IndexOf('@');
            if (at <= 1) return "***" + (at >= 0 ? email.Substring(at) : String.Empty);
            return email.Substring(0, 1) + "***" + email.Substring(at);
        }

        #endregion
    }
}
