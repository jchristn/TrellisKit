namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Email;

    /// <summary>
    /// Administrative email (docs/TRELLISKIT.md section 12): HtmlPageBuilder rendering and escaping, the encrypted
    /// per-property settings and secrets, the queued delivery pipeline (idempotency, suppression, retries), template
    /// overrides and previews, staff notification settings, lifecycle hooks, and the Mailgun webhook.
    /// </summary>
    public static class AdministrativeEmailSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AdministrativeEmail";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Administrative email (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "SecretBox.BoundAndRotatable", "AES-256-GCM secrets round trip, fail with other associated data or a tampered ciphertext, and decrypt with a previous key after rotation", ct =>
                {
                    string oldKey = SecretBox.GenerateKey();
                    string newKey = SecretBox.GenerateKey();
                    SecretBox box = new SecretBox(oldKey);
                    SecretCiphertext cipher = box.Encrypt("mailgun-key-123", "prop|email.mailgunApiKey");
                    Check.True(!cipher.Ciphertext.Contains("mailgun", StringComparison.Ordinal), "No plaintext in the ciphertext");
                    Check.Equal("mailgun-key-123", box.Decrypt(cipher.Ciphertext, cipher.Nonce, cipher.KeyId, "prop|email.mailgunApiKey"), "Round trip");
                    Check.True(box.Decrypt(cipher.Ciphertext, cipher.Nonce, cipher.KeyId, "other|email.mailgunApiKey") == null, "Another property's associated data fails");
                    byte[] bytes = Convert.FromBase64String(cipher.Ciphertext);
                    bytes[0] ^= 0x01;
                    Check.True(box.Decrypt(Convert.ToBase64String(bytes), cipher.Nonce, cipher.KeyId, "prop|email.mailgunApiKey") == null, "Tampering fails");
                    SecretBox rotated = new SecretBox(newKey, oldKey);
                    Check.Equal("mailgun-key-123", rotated.Decrypt(cipher.Ciphertext, cipher.Nonce, cipher.KeyId, "prop|email.mailgunApiKey"), "Previous key still decrypts");
                    Check.True(new SecretBox(newKey).Decrypt(cipher.Ciphertext, cipher.Nonce, cipher.KeyId, "prop|email.mailgunApiKey") == null, "Unknown key id fails");
                    Check.Throws<ArgumentException>(() => new SecretBox("short"), "Invalid key");
                    return Task.CompletedTask;
                }),

                SuiteCases.Unit(Id, "Renderer.EscapesAndBrands", "Templates render a complete HTML document (color-scheme, 600 px max width, brand color, footer) and a text version; token values are HTML-encoded; overrides with unknown tokens are rejected", ct =>
                {
                    EmailTemplate template = EmailTemplateCatalog.Get("content_removed");
                    Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["name"] = "Example Community", ["memberName"] = "<script>alert(1)</script>", ["contentType"] = "post", ["excerpt"] = "a & b <i>", ["reason"] = "Spam", ["supportEmail"] = "support@example.test"
                    };
                    EmailRendered rendered = EmailRenderer.Render(template, null, tokens, new EmailBrand { Name = "Example Community", PrimaryColor = "#1F5FBF", SupportEmail = "support@example.test", PrivacyUrl = "https://example.test/privacy" });
                    Check.True(rendered.Html.StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase), "Complete document");
                    Check.True(rendered.Html.Contains("color-scheme", StringComparison.Ordinal) && rendered.Html.Contains("prefers-color-scheme: dark", StringComparison.Ordinal), "Light and dark");
                    Check.True(rendered.Html.Contains("max-width:600px", StringComparison.Ordinal), "Fluid to 600 px");
                    Check.True(rendered.Html.Contains("#1F5FBF", StringComparison.Ordinal), "Brand color");
                    Check.True(!rendered.Html.Contains("<i>", StringComparison.Ordinal) && rendered.Html.Contains("a &amp; b &lt;i&gt;", StringComparison.Ordinal), "Token values are encoded");
                    Check.True(rendered.Html.Contains("Privacy policy", StringComparison.Ordinal) && rendered.Html.Contains("support@example.test", StringComparison.Ordinal), "Footer");
                    Check.True(!rendered.Html.Contains("Postal address", StringComparison.OrdinalIgnoreCase), "No postal line without an address");
                    EmailRendered withAddress = EmailRenderer.Render(template, null, tokens, new EmailBrand { Name = "Example Community", PostalAddress = "1 Example Way" });
                    Check.True(withAddress.Html.Contains("1 Example Way", StringComparison.Ordinal) && withAddress.Text.Contains("1 Example Way", StringComparison.Ordinal), "Postal line when configured");
                    Check.True(rendered.Text.Contains("a & b <i>", StringComparison.Ordinal), "Plain text keeps the raw value");
                    Check.Equal("Your post on Example Community was removed", rendered.Subject, "Subject tokens");
                    Dictionary<string, string> errors = EmailRenderer.Validate(template, new EmailTemplateOverride { Subject = "Hello {secretValue}" });
                    Check.True(errors.ContainsKey("subject"), "Unknown token rejected");
                    Check.Equal(0, EmailRenderer.Validate(template, new EmailTemplateOverride { Subject = "{name}: {contentType} removed", Body = "One\n\nTwo {reason}" }).Count, "Known tokens accepted");
                    Check.Equal("AI assistant reply", EmailLabels.TargetType(TargetTypeEnum.AssistantMessage), "Assistant reply label");
                    Check.Equal("feed post", EmailLabels.TargetType(TargetTypeEnum.FeedPost), "Feed post label");
                    Check.Equal("member profile", EmailLabels.TargetType(TargetTypeEnum.User), "Member label");
                    Check.Equal("Hate speech", EmailLabels.ReportReason(ReportReasonEnum.HateSpeech), "Reason label");
                    EmailTemplate reportTemplate = EmailTemplateCatalog.All.First(t => t.Key == "staff_new_report");
                    EmailRendered report = EmailRenderer.Render(reportTemplate, null, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "name", "Example Community" }, { "targetType", EmailLabels.TargetType(TargetTypeEnum.AssistantMessage) }, { "reason", EmailLabels.ReportReason(ReportReasonEnum.Spam) }, { "link", "https://example.test/admin/moderation" }
                    }, new EmailBrand { Name = "Example Community" });
                    Check.Equal("New report on Example Community: AI assistant reply", report.Subject, "Report subject uses the human label");
                    Check.True(!report.Text.Contains("AssistantMessage", StringComparison.Ordinal), "No enum names in the report email");
                    Check.True(EmailTemplateCatalog.All.Select(t => t.Key).Distinct().Count() == EmailTemplateCatalog.All.Count, "Unique template keys");
                    foreach (EmailTemplate t in EmailTemplateCatalog.All)
                        Check.Equal(0, EmailRenderer.Validate(t, new EmailTemplateOverride { Subject = t.DefaultSubject, Preheader = t.DefaultPreheader, Heading = t.DefaultHeading, Body = t.DefaultBody }).Count, t.Key + " defaults use declared tokens");
                    List<string> txt = DohTxtResolver.Parse("{\"Status\":0,\"Answer\":[{\"name\":\"x\",\"type\":16,\"data\":\"\\\"trelliskit-verify=abc\\\"\"},{\"name\":\"x\",\"type\":5,\"data\":\"cname\"}]}");
                    Check.True(txt.SequenceEqual(new[] { "trelliskit-verify=abc" }), "DNS-over-HTTPS TXT parsing");
                    return Task.CompletedTask;
                }),

                SuiteCases.Http(Id, "Settings.SecretsWriteOnly", "Per-property settings: secrets are encrypted at rest and never returned; invalid keys and values are 400; Mailgun becomes the provider when configured; changes are audited by key name", async (f, ct) =>
                {
                    JsonElement put = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken,
                        new { values = new Dictionary<string, object> { ["email.mailgunDomain"] = "mg.example.test", ["email.mailgunApiKey"] = "key-plaintext-value", ["email.mailgunRegion"] = "Eu", ["email.postalAddress"] = "1 Example Way" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement key = put.GetProperty("fields").EnumerateArray().First(x => Api.Str(x, "key") == "email.mailgunApiKey");
                    Check.True(key.GetProperty("value").ValueKind == JsonValueKind.Null && key.GetProperty("isSet").GetBoolean(), "Secret is write-only");
                    Check.True(!put.ToString().Contains("key-plaintext-value", StringComparison.Ordinal), "Never echoed");
                    DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT valuetext, ciphertext, keyid FROM propertysettings WHERE settingkey = 'email.mailgunApiKey';", false, ct).ConfigureAwait(false);
                    Check.True(rows.Rows[0]["valuetext"] == DBNull.Value && !rows.Rows[0]["ciphertext"]!.ToString()!.Contains("plaintext", StringComparison.Ordinal), "Ciphertext only at rest");
                    EmailProviderChoice provider = await f.Services.Email.ProviderAsync(ct).ConfigureAwait(false);
                    Check.Equal(EmailProviderEnum.Mailgun, provider.Provider, "Mailgun when configured");
                    Check.True(provider.Sender is MailgunEmailSender, "SendWithMailgun sender");
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { values = new Dictionary<string, object> { ["email.smtpPort"] = 70000, ["email.nope"] = "x" } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, bad.Status, "Invalid values");
                    Check.True(bad.Body.GetProperty("fieldErrors").TryGetProperty("values.email.smtpPort", out JsonElement _) && bad.Body.GetProperty("fieldErrors").TryGetProperty("values.email.nope", out JsonElement _), "Each invalid key named");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { clear = new[] { "email.mailgunApiKey", "email.mailgunDomain" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True((await f.Services.Email.ProviderAsync(ct).ConfigureAwait(false)).Provider != EmailProviderEnum.Mailgun, "Cleared");
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=property.settings.update", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(audit.GetProperty("objects").EnumerateArray().All(a => !(Api.Str(a, "summary") ?? String.Empty).Contains("plaintext", StringComparison.Ordinal)), "Audit has key names only");
                    Check.True(audit.GetProperty("totalRecords").GetInt64() >= 2, "Audited");
                }),

                SuiteCases.Http(Id, "Pipeline.LifecycleAndLog", "Verification sends welcome once; records are idempotent per event key, logged without bodies, and suppressed addresses get nothing", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    string token = f.Fakes.Email.LatestToken(member.Email, EmailService.VerifyEmailCategory);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/email/verify", null, new { token }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    List<EmailMessage> welcome = f.Fakes.Email.To(member.Email, "welcome");
                    Check.Equal(1, welcome.Count, "Welcome sent once");
                    Check.True(welcome[0].HtmlBody!.Contains("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase) && welcome[0].Subject.Contains(f.Property.Name, StringComparison.Ordinal), "Rendered with the property name");
                    JsonElement log = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/messages/enumerate", HttpFixture.AdminToken, new { filters = new { recipient = member.Email } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> templates = log.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "template")).ToList();
                    Check.True(templates.Contains("welcome") && templates.Contains("verify_email"), "Logged (" + String.Join(",", templates) + ")");
                    JsonElement first = log.GetProperty("objects")[0];
                    Check.Equal("Sent", Api.Str(first, "status"), "Status");
                    Check.True(!first.TryGetProperty("payload", out JsonElement _) && !log.ToString().Contains("token=", StringComparison.Ordinal), "No bodies or links in the log");

                    EmailRequest request = new EmailRequest { Template = "test", To = member.Email, IdempotencyKey = "test-event-" + member.Id };
                    Check.True(await f.Services.Email.QueueAsync(request, ct).ConfigureAwait(false) != null, "First event queued");
                    Check.True(await f.Services.Email.QueueAsync(request, ct).ConfigureAwait(false) == null, "Duplicate event ignored");

                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/suppressions", HttpFixture.AdminToken, new { email = member.Email.ToUpperInvariant(), note = "Asked to stop" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    int before = f.Fakes.Email.To(member.Email, "test").Count;
                    EmailMessageRecord? suppressed = await f.Services.Email.QueueAsync(new EmailRequest { Template = "test", To = member.Email }, ct).ConfigureAwait(false);
                    Check.Equal(EmailStatusEnum.Suppressed, suppressed!.Status, "Suppressed");
                    Check.Equal(before, f.Fakes.Email.To(member.Email, "test").Count, "Nothing delivered");
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/suppressions/enumerate", HttpFixture.AdminToken, new { search = member.Email }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string suppressionId = Api.Str(list.GetProperty("objects")[0], "id");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/email/suppressions/" + suppressionId, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                }),

                SuiteCases.Http(Id, "Pipeline.RetriesWithBackoff", "A failing provider leaves the message Queued with a backoff; the delivery pass retries it and marks it Failed after the last attempt", async (f, ct) =>
                {
                    f.Settings.Email.DeliveryMaxAttempts = 2;
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { values = new Dictionary<string, object> { ["email.smtpHost"] = "127.0.0.1", ["email.smtpPort"] = 1, ["email.smtpUseTls"] = false } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    EmailMessageRecord? record = await f.Services.Email.QueueAsync(new EmailRequest { Template = "test", To = "retry@example.test" }, ct).ConfigureAwait(false);
                    Check.Equal(EmailStatusEnum.Queued, record!.Status, "Queued after a failure");
                    Check.True(record.NextAttemptUtc > DateTime.UtcNow, "Backoff");
                    await f.Raw.ExecuteQueryAsync("UPDATE emailmessages SET nextattemptutc = now() - interval '1 minute' WHERE id = '" + record.Id + "';", false, ct).ConfigureAwait(false);
                    await f.Services.Email.DeliverDueAsync(ct).ConfigureAwait(false);
                    EmailMessageRecord? after = await f.Database.Email.ReadMessageAsync(record.Id, ct).ConfigureAwait(false);
                    Check.Equal(EmailStatusEnum.Failed, after!.Status, "Failed after the last attempt");
                    Check.Equal(2, after.Attempts, "Two attempts");
                    Check.True(after.Payload == null, "Payload removed once final");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { clear = new[] { "email.smtpHost", "email.smtpPort", "email.smtpUseTls" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),

                SuiteCases.Http(Id, "Templates.OverrideAndPreview", "Overrides are validated, change the next message, preview unsaved text with sample data, and reset", async (f, ct) =>
                {
                    JsonElement templates = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/email/templates?maxResults=1000", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(templates.GetProperty("objects").GetArrayLength() >= 30, "Catalog");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/email/templates/password_reset", HttpFixture.AdminToken, new { subject = "Reset {password}" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/email/templates/password_reset", HttpFixture.AdminToken, new { subject = "Custom reset for {name}" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/password/forgot", null, new { email = member.Email }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    EmailMessage reset = f.Fakes.Email.To(member.Email, EmailService.PasswordResetCategory).Last();
                    Check.Equal("Custom reset for " + f.Property.Name, reset.Subject, "Override applied");
                    Check.True(reset.TextBody.Contains("/reset-password?token=", StringComparison.Ordinal), "Link kept");
                    JsonElement preview = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/welcome/preview", HttpFixture.AdminToken, new { heading = "Hi {memberName}", tokens = new { memberName = "<b>Pat</b>" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Api.Str(preview, "html").Contains("Hi &lt;b&gt;Pat&lt;/b&gt;", StringComparison.Ordinal), "Preview encodes sample values");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/email/templates/password_reset", HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    JsonElement reverted = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/email/templates/password_reset", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(reverted.GetProperty("override").ValueKind == JsonValueKind.Null, "Reset");
                    JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/test", HttpFixture.AdminToken, new { to = "tester@example.test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(test.GetProperty("ok").GetBoolean(), "Test send (" + Api.Str(test, "message") + ")");
                    Check.Equal(1, f.Fakes.Email.To("tester@example.test", "test").Count, "Test delivered");
                }),

                SuiteCases.Http(Id, "Staff.Notifications", "Staff events default to the property's SystemAdmins, can be disabled, and can name recipients", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember first = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    Check.True(f.Fakes.Email.To(admin.Email, "staff_new_member").Count >= 1, "Default recipients are SystemAdmins");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/email/staff-events/staff_new_member", HttpFixture.AdminToken, new { enabled = true, recipients = new[] { "ops@example.test" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Email.To("ops@example.test", "staff_new_member").Count, "Configured recipients");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/email/staff-events/staff_new_member", HttpFixture.AdminToken, new { enabled = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Email.To("ops@example.test", "staff_new_member").Count, "Disabled");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/email/staff-events/staff_new_member", HttpFixture.AdminToken, new { recipients = new[] { "not-an-email" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement events = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/email/staff-events", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(events.GetProperty("objects").EnumerateArray().Any(e => Api.Str(e, "template") == "staff_new_report" && Api.Str(e, "defaultRecipients") == "Moderators"), "Moderator events listed");
                    Check.True(first.Id.Length > 0, "Registered");
                }),

                SuiteCases.Http(Id, "Mailgun.Webhook", "The Mailgun webhook verifies the signature, rejects replays and stale timestamps, marks deliveries, and suppresses permanent failures", async (f, ct) =>
                {
                    string key = "whsec-" + Guid.NewGuid().ToString("N");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { values = new Dictionary<string, object> { ["email.mailgunWebhookSigningKey"] = key } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    EmailMessageRecord? record = await f.Services.Email.QueueAsync(new EmailRequest { Template = "test", To = "bounce@example.test" }, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE emailmessages SET providermessageid = 'mg-message-1@example.test' WHERE id = '" + record!.Id + "';", false, ct).ConfigureAwait(false);
                    string path = "/api/v1/webhooks/mailgun/" + f.Services.PropertyId;
                    object Signed(string ev, string severity, string nonce, long? when = null)
                    {
                        string ts = (when ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(ts + nonce))).ToLowerInvariant();
                        return new Dictionary<string, object>
                        {
                            ["signature"] = new { timestamp = ts, token = nonce, signature = sig },
                            ["event-data"] = new Dictionary<string, object> { ["event"] = ev, ["severity"] = severity, ["recipient"] = "bounce@example.test", ["message"] = new { headers = new Dictionary<string, string> { ["message-id"] = "<mg-message-1@example.test>" } } }
                        };
                    }
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, path, null, new { signature = new { timestamp = "1", token = "t", signature = "00" } }, HttpStatusCode.BadRequest, "InvalidSignature", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, path, null, Signed("delivered", "", "stale", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600), HttpStatusCode.BadRequest, "InvalidSignature", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, path, null, Signed("delivered", "", "n1"), HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(EmailStatusEnum.Delivered, (await f.Database.Email.ReadMessageAsync(record.Id, ct).ConfigureAwait(false))!.Status, "Delivered");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, path, null, Signed("delivered", "", "n1"), HttpStatusCode.BadRequest, "InvalidSignature", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, path, null, Signed("failed", "temporary", "n2"), HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!await f.Database.Email.IsSuppressedAsync("bounce@example.test", ct).ConfigureAwait(false), "Temporary failures do not suppress");
                    await Api.ExpectAsync(f, HttpMethod.Post, path, null, Signed("failed", "permanent", "n3"), HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(await f.Database.Email.IsSuppressedAsync("bounce@example.test", ct).ConfigureAwait(false), "Permanent failure suppresses");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/webhooks/mailgun/app_unknown_000000000000000", null, Signed("delivered", "", "n4"), HttpStatusCode.NotFound, "PropertyNotFound", ct).ConfigureAwait(false);
                })
            });
        }

        #endregion
    }
}
