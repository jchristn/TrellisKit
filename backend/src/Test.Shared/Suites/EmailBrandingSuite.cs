namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Email;

    /// <summary>
    /// Email branding and delivery alignment (docs/EMAIL.md): every catalog message renders HTML and text in the
    /// property's brand without unresolved placeholders, for the neutral sample property;
    /// every text pair meets WCAG AA; the layout is accessible (language, presentation tables, alt text, dark mode,
    /// preheader); Mailgun receives both bodies, headers, tags, variables, and tracking choices; optional mail carries
    /// List-Unsubscribe; the preview and catalog test routes are admin-only; and webhooks correlate by the engine's
    /// message id variable.
    /// </summary>
    public static class EmailBrandingSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "EmailBranding";

        #endregion

        #region Private-Members

        private static readonly Regex _Placeholder = new Regex("\\{[A-Za-z][A-Za-z0-9]*\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Email branding (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Catalog.RendersForEveryProperty", "Every catalog message renders a complete HTML document and a text part with the property name, brand color, ownership line, and no unresolved placeholders, for the neutral sample property", ct =>
                {
                    List<string> directories = PropertyDirectories();
                    Check.True(directories.Count >= 1, "At least the sample property");
                    foreach (string directory in directories) RenderCatalog(directory);
                    return Task.CompletedTask;
                }),

                SuiteCases.Unit(Id, "Contrast.WcagAa", "Every text pair of each property's email style reaches 4.5:1 in light and dark mode, and a brand color that falls short is replaced", ct =>
                {
                    foreach (string directory in PropertyDirectories())
                    {
                        EmailBrand brand = BrandFor(directory, out PropertyConfiguration property);
                        EmailStyle style = new EmailStyle(brand);
                        string label = property.Name + ": ";
                        AssertAa(label + "heading on card", style.Heading, style.Card);
                        AssertAa(label + "body text on card", EmailStyle.TextColor, style.Card);
                        AssertAa(label + "secondary text on card", EmailStyle.MutedColor, style.Card);
                        AssertAa(label + "link on card", style.Link, style.Card);
                        AssertAa(label + "button text", style.OnPrimary, style.Primary);
                        AssertAa(label + "dark body text", EmailStyle.DarkTextColor, style.DarkCard);
                        AssertAa(label + "dark secondary text", style.DarkMuted, style.DarkCard);
                        AssertAa(label + "dark callout and code text", EmailStyle.DarkTextColor, style.DarkCanvas);
                        AssertAa(label + "dark link", style.DarkLink, style.DarkCard);
                        AssertAa(label + "dark button text", style.DarkOnPrimary, style.DarkPrimary);
                        Check.Equal(brand.PrimaryColor, style.Link, label + "the brand primary is readable as the link color");
                    }
                    EmailStyle weak = new EmailStyle(new EmailBrand { PrimaryColor = "#FFFF00", OnPrimaryColor = "#FFFFFF", HeadingColor = "#EEEEEE", CardColor = "#000000", DarkPrimaryColor = "#222222", DarkCardColor = "#FFFFFF" });
                    Check.True(weak.Link != "#FFFF00" && EmailStyle.ContrastRatio(weak.Link, weak.Card) >= 4.5, "Unreadable link color replaced");
                    Check.True(EmailStyle.ContrastRatio(weak.OnPrimary, weak.Primary) >= 4.5, "Button text adjusted");
                    Check.Equal("#ffffff", weak.Card, "A card the engine text cannot be read on falls back to white");
                    Check.True(EmailStyle.ContrastRatio(weak.Heading, weak.Card) >= 4.5, "Heading adjusted");
                    Check.True(EmailStyle.ContrastRatio(weak.DarkLink, weak.DarkCard) >= 4.5 && weak.DarkCard != "#FFFFFF", "Dark card and link stay readable");
                    Check.True(Math.Abs(EmailStyle.ContrastRatio("#000000", "#FFFFFF") - 21.0) < 0.01 && Math.Abs(EmailStyle.ContrastRatio("#fff", "#FFFFFF") - 1.0) < 0.01, "Contrast ratio bounds");
                    return Task.CompletedTask;
                }),

                SuiteCases.Unit(Id, "Layout.Accessible", "The layout declares its language, uses presentation tables, gives the logo alt text and a size, swaps a dark logo, pads the preheader, and renders the footer in both parts", ct =>
                {
                    EmailBrand brand = new EmailBrand
                    {
                        Name = "Example <Community>", Language = "en-US", LogoUrl = "https://example.test/logo.png", DarkLogoUrl = "https://example.test/logo-dark.png", LogoWidth = 144, LogoHeight = 48,
                        PrimaryColor = "#1F5FBF", DarkPrimaryColor = "#8DB4F0", CanvasColor = "#F3F4F6", AccentBarColor = "#8DB4F0", SupportEmail = "support@example.test", WebUrl = "https://example.test",
                        PrivacyUrl = "https://example.test/privacy", TermsUrl = "https://example.test/terms", PreferencesUrl = "https://example.test/app/settings/notifications",
                        FooterNote = "A sample note.", Copyright = "(c) 2026 Example Owner. All rights reserved.", PostalAddress = "1 Example Way"
                    };
                    EmailTemplate welcome = EmailTemplateCatalog.Get("welcome");
                    Dictionary<string, string> tokens = new Dictionary<string, string>(welcome.Samples, StringComparer.Ordinal) { ["name"] = "Example <Community>", ["memberName"] = "Pat" };
                    EmailRendered rendered = EmailRenderer.Render(welcome, null, tokens, brand);
                    string html = rendered.Html;
                    Check.True(html.Contains("lang=\"en-US\"", StringComparison.Ordinal), "Language");
                    Check.True(Regex.Matches(html, "role=\"presentation\"").Count >= 3, "Outer, card, and button are presentation tables");
                    Check.True(html.Contains("alt=\"Example &lt;Community&gt;\"", StringComparison.Ordinal) && html.Contains("width=\"144\" height=\"48\"", StringComparison.Ordinal), "Logo alt text and size");
                    Check.True(html.Contains("class=\"logo-dark\"", StringComparison.Ordinal) && html.Contains(".logo-dark{display:inline-block!important", StringComparison.Ordinal), "Dark logo swap");
                    Check.True(html.Contains("<!--[if !mso]><!-->", StringComparison.Ordinal), "Dark logo hidden from Outlook");
                    Check.True(html.Contains("x-apple-disable-message-reformatting", StringComparison.Ordinal) && html.Contains("supported-color-schemes", StringComparison.Ordinal), "Client hints");
                    Check.True(html.Contains("aria-hidden=\"true\"", StringComparison.Ordinal) && html.Contains("&#847;&zwnj;&nbsp;", StringComparison.Ordinal), "Preheader padding hidden from screen readers");
                    Check.True(html.Contains("a.btn{background:#8DB4F0!important", StringComparison.Ordinal) && html.Contains("border-top:4px solid #8DB4F0", StringComparison.Ordinal), "Dark button and accent bar");
                    Check.True(html.Contains("background:#F3F4F6", StringComparison.Ordinal), "Canvas color");
                    foreach (string part in new[] { "Terms of service", "Privacy policy", "Email preferences", "1 Example Way", "A sample note.", "(c) 2026 Example Owner. All rights reserved." })
                    {
                        Check.True(html.Contains(WebUtility.HtmlEncode(part), StringComparison.Ordinal), "HTML footer has " + part);
                        Check.True(rendered.Text.Contains(part, StringComparison.Ordinal), "Text footer has " + part);
                    }
                    Check.True(rendered.Text.Contains("Terms of service: https://example.test/terms", StringComparison.Ordinal), "Text links are spelled out");
                    EmailRendered bare = EmailRenderer.Render(welcome, null, tokens, new EmailBrand { Name = "Bare" });
                    Check.True(!bare.Html.Contains("<img", StringComparison.Ordinal) && !bare.Html.Contains(".logo-dark", StringComparison.Ordinal) && !bare.Html.Contains("border-top:4px", StringComparison.Ordinal), "Optional parts are omitted");

                    string root = Path.Combine(Path.GetTempPath(), "tk-email-layout-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(Path.Combine(root, "email"));
                    Directory.CreateDirectory(Path.Combine(root, "brand"));
                    try
                    {
                        File.WriteAllBytes(Path.Combine(root, "brand", "mark.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 1, 0x20, 0, 0, 0, 0x60 });
                        File.WriteAllText(Path.Combine(root, "email", "layout.json"), "{\n // comment\n \"logo\": \"brand/mark.png\", \"logoDark\": \"brand/missing.png\", \"canvas\": \"blue\", \"card\": \"#FFFFFF\"," +
                            " \"logoWidth\": 500, \"footerNote\": \"Line one\\nline two\", \"copyright\": \"(c) {year} Example\" }");
                        EmailLayoutOptions options = EmailLayoutOptions.Load(root);
                        Check.Equal("brand/mark.png", options.Logo, "Valid logo kept");
                        Check.True(options.LogoDark == null && options.Canvas == null && options.LogoWidth == null && options.FooterNote == null, "Invalid fields cleared");
                        Check.Equal(4, options.Problems.Count, "Each invalid field reported: " + String.Join(" | ", options.Problems));
                        Check.Equal("#FFFFFF", options.Card, "Valid color kept");
                        Check.True(EmailBrandFactory.TryPngSize(Path.Combine(root, "brand", "mark.png"), out int w, out int h) && w == 288 && h == 96, "PNG size read from the header");
                        File.WriteAllText(Path.Combine(root, "email", "layout.json"), "{ \"logo\": \"../property.json\" }");
                        Check.True(EmailLayoutOptions.Load(root).Logo == null && EmailLayoutOptions.Load(root).Problems.Count == 1, "Paths outside brand/ rejected");
                        File.WriteAllText(Path.Combine(root, "email", "layout.json"), "{ not json");
                        Check.Equal(1, EmailLayoutOptions.Load(root).Problems.Count, "Unreadable file reported");
                        Check.Equal(0, EmailLayoutOptions.Load(Path.Combine(root, "nowhere")).Problems.Count, "A missing file is not a problem");
                    }
                    finally
                    {
                        TestProperty.Delete(root);
                    }
                    return Task.CompletedTask;
                }),

                SuiteCases.Unit(Id, "Layout.PropertyIsolationAndLogoCompatibility", "Property metadata stays isolated, GIF dimensions and small images preserve their ratio, explicit sizes win, and runtime postal settings override the file", ct =>
                {
                    string root = TestProperty.CopySample();
                    try
                    {
                        PropertyConfiguration property = PropertyLoader.Load(root);
                        property.Name = "Another Community";
                        property.Locale = "fr-CA";
                        property.Owner.CopyrightHolder = "Another Owner";
                        File.WriteAllBytes(Path.Combine(root, "brand", "small.gif"), new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 20, 0, 10, 0 });
                        EmailLayoutOptions layout = new EmailLayoutOptions { Logo = "brand/small.gif", PostalAddress = "File address", Copyright = "(c) {year} Another Owner" };
                        PropertyContext context = new PropertyContext(property, new AppSettings());
                        EmailBrand brand = EmailBrandFactory.Create(context, layout, "Runtime address", new DateTime(2030, 1, 1));
                        Check.Equal("Another Community", brand.Name, "Property name");
                        Check.Equal("fr-CA", brand.Language, "Property language");
                        Check.Equal(20, brand.LogoWidth, "Small GIF is not enlarged");
                        Check.Equal(10, brand.LogoHeight, "Small GIF keeps its ratio");
                        Check.Equal("Runtime address", brand.PostalAddress, "Runtime setting wins");
                        Check.Equal("(c) 2030 Another Owner", brand.Copyright, "Property ownership and year");
                        layout.LogoWidth = 96;
                        layout.LogoHeight = 32;
                        EmailBrand explicitSize = EmailBrandFactory.Create(context, layout, null, DateTime.UtcNow);
                        Check.Equal(96, explicitSize.LogoWidth, "Explicit width wins");
                        Check.Equal(32, explicitSize.LogoHeight, "Explicit height wins");
                        Check.Equal("File address", explicitSize.PostalAddress, "File fallback");
                        EmailRendered mail = EmailRenderer.Render(EmailTemplateCatalog.Get("test"), null, new Dictionary<string, string> { ["name"] = brand.Name }, brand);
                        Check.True(mail.Html.Contains("width:20px;height:10px", StringComparison.Ordinal), "Layout does not distort small images");
                        Check.True(!BrandFor(TestProperty.SampleDirectory, out PropertyConfiguration sample).Name.Equals(brand.Name, StringComparison.Ordinal), "Another property keeps its own brand");
                    }
                    finally
                    {
                        TestProperty.Delete(root);
                    }
                    return Task.CompletedTask;
                }),

                SuiteCases.Unit(Id, "Mailgun.FormFields", "The Mailgun sender posts both bodies, reply-to, safe extra headers, at most three tags, variables, and tracking choices, reads the message id, and reports refusals without secrets", async ct =>
                {
                    RecordingMailgunHandler handler = new RecordingMailgunHandler();
                    MailgunEmailSender sender = new MailgunEmailSender("mg.example.test", "key-secret-value", "https://api.mailgun.test/v3", "Example \"Community\"", "no-reply@example.test", handler);
                    EmailMessage message = new EmailMessage
                    {
                        To = "pat@example.test", Subject = "Hello\r\nBcc: evil@example.test", TextBody = "Plain body", HtmlBody = "<p>HTML body</p>", ReplyTo = "support@example.test",
                        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["List-Unsubscribe"] = "<https://example.test/u?t=1>", ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click", ["Bcc"] = "evil@example.test", ["X-Bad\nName"] = "x" },
                        Tags = new List<string> { "welcome", "essential", "third", "fourth" },
                        Variables = new Dictionary<string, string>(StringComparer.Ordinal) { ["tk-message-id"] = "eml_1" },
                        TrackOpens = false,
                        TrackClicks = false
                    };
                    await sender.SendAsync(message, ct).ConfigureAwait(false);
                    Check.Equal("https://api.mailgun.test/v3/mg.example.test/messages", handler.Url, "Messages endpoint");
                    Check.Equal("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("api:key-secret-value")), handler.Authorization, "Basic api key");
                    Check.Equal("\"Example 'Community'\" <no-reply@example.test>", handler.One("from"), "From");
                    Check.Equal("Plain body", handler.One("text"), "Text part");
                    Check.Equal("<p>HTML body</p>", handler.One("html"), "HTML part");
                    Check.Equal("Hello  Bcc: evil@example.test", handler.One("subject"), "Subject is one line");
                    Check.Equal("support@example.test", handler.One("h:Reply-To"), "Reply-to");
                    Check.Equal("<https://example.test/u?t=1>", handler.One("h:List-Unsubscribe"), "List-Unsubscribe");
                    Check.Equal("List-Unsubscribe=One-Click", handler.One("h:List-Unsubscribe-Post"), "List-Unsubscribe-Post");
                    Check.True(handler.One("h:Bcc") == null && !handler.Fields.Any(f => f.Key.Contains('\n', StringComparison.Ordinal)), "Reserved and malformed headers dropped");
                    Check.True(handler.All("o:tag").SequenceEqual(new[] { "welcome", "essential", "third" }), "At most three tags");
                    Check.Equal("eml_1", handler.One("v:tk-message-id"), "Variable");
                    Check.True(handler.One("o:tracking") == "no" && handler.One("o:tracking-opens") == "no" && handler.One("o:tracking-clicks") == "no", "Tracking off");
                    Check.Equal("mg-test-1@example.test", message.ProviderMessageId, "Provider id without brackets");

                    message.TrackClicks = true;
                    await sender.SendAsync(message, ct).ConfigureAwait(false);
                    Check.True(handler.One("o:tracking") == "yes" && handler.One("o:tracking-clicks") == "htmlonly", "Click tracking on request");
                    message.TrackOpens = null;
                    message.TrackClicks = null;
                    await sender.SendAsync(message, ct).ConfigureAwait(false);
                    Check.True(handler.One("o:tracking") == null, "Domain default when unset");

                    handler.Status = HttpStatusCode.BadRequest;
                    handler.Body = "{\"message\":\"'to' parameter is not a valid address\"}";
                    InvalidOperationException? refused = null;
                    try
                    {
                        await sender.SendAsync(message, ct).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException e)
                    {
                        refused = e;
                    }
                    Check.True(refused != null && refused.Message.Contains("HTTP 400", StringComparison.Ordinal) && refused.Message.Contains("not a valid address", StringComparison.Ordinal), "Refusal reported");
                    Check.True(!refused!.Message.Contains("key-secret-value", StringComparison.Ordinal) && !refused.Message.Contains("HTML body", StringComparison.Ordinal), "No secrets or bodies in the error");
                    await Check.ThrowsAsync<TrellisKit.Core.Exceptions.IntegrationNotConfiguredException>(() => new MailgunEmailSender("", "k", "", "", "a@example.test", handler).SendAsync(message, ct), "Unconfigured").ConfigureAwait(false);
                }),

                SuiteCases.Http(Id, "Delivery.UnsubscribeTagsTracking", "Every catalog message sent through the pipeline carries tags and the message id variable; optional messages carry List-Unsubscribe and List-Unsubscribe-Post; essential and staff messages are never tracked", async (f, ct) =>
                {
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { values = new Dictionary<string, object> { ["email.trackOpens"] = true, ["email.trackClicks"] = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    int optional = 0;
                    foreach (EmailTemplate template in f.Services.Email.Templates.All.Where(t => t.Recipient != EmailRecipientEnum.PlatformAdmins))
                    {
                        string to = "catalog-" + template.Key.Replace('_', '-') + "@example.test";
                        EmailRequest request = new EmailRequest { Template = template.Key, To = to };
                        foreach (KeyValuePair<string, string> sample in template.Samples) request.Tokens[sample.Key] = sample.Value;
                        EmailMessageRecord? record = await f.Services.Email.QueueAsync(request, ct).ConfigureAwait(false);
                        Check.True(record != null && record.Status == EmailStatusEnum.Sent, template.Key + " sent");
                        EmailMessage sent = f.Fakes.Email.To(to, template.Key).Single();
                        Check.True(sent.Tags.Contains(template.Key) && sent.Tags.Count <= EmailMessage.MaxTags, template.Key + " tags");
                        Check.Equal(record!.Id, sent.Variables[EmailService.MessageIdVariable], template.Key + " message id variable");
                        Check.True(!String.IsNullOrWhiteSpace(sent.TextBody) && !String.IsNullOrWhiteSpace(sent.HtmlBody), template.Key + " has both parts");
                        Check.True(!_Placeholder.IsMatch(sent.Subject) && !_Placeholder.IsMatch(sent.TextBody), template.Key + " has no unresolved placeholders");
                        if (template.Category == EmailCategoryEnum.Optional)
                        {
                            optional++;
                            Check.True(sent.Headers.TryGetValue(EmailService.ListUnsubscribeHeader, out string? header) && header.StartsWith("<https://", StringComparison.Ordinal) && header.EndsWith(">", StringComparison.Ordinal), template.Key + " List-Unsubscribe");
                            Check.Equal("List-Unsubscribe=One-Click", sent.Headers[EmailService.ListUnsubscribePostHeader], template.Key + " one-click");
                            Check.True(sent.TrackOpens == true && sent.TrackClicks == true, template.Key + " tracked when the property allows it");
                        }
                        else
                        {
                            Check.True(!sent.Headers.ContainsKey(EmailService.ListUnsubscribeHeader), template.Key + " has no List-Unsubscribe");
                            Check.True(sent.TrackOpens == false && sent.TrackClicks == false, template.Key + " never tracked");
                        }
                    }
                    Check.True(optional >= 5, "Optional templates covered (" + optional + ")");
                    EmailRequest essential = new EmailRequest { Template = "broadcast", To = "essential@example.test", Essential = true };
                    foreach (KeyValuePair<string, string> sample in f.Services.Email.Templates.Get("broadcast").Samples) essential.Tokens[sample.Key] = sample.Value;
                    EmailMessageRecord? essentialRecord = await f.Services.Email.QueueAsync(essential, ct).ConfigureAwait(false);
                    EmailMessage essentialMessage = f.Fakes.Email.To(essential.To, "broadcast").Single();
                    Check.Equal(EmailCategoryEnum.Essential, essentialRecord!.Category, "Essential category is persisted for retries");
                    Check.True(essentialMessage.TrackOpens == false && essentialMessage.TrackClicks == false, "Essential broadcast never tracks even with an Optional template");
                    Check.True(!essentialMessage.Headers.ContainsKey(EmailService.ListUnsubscribeHeader), "Essential broadcast has no automatic unsubscribe headers");
                }),

                SuiteCases.Http(Id, "Preview.AdminOnlyAndCatalogSend", "Template previews and the catalog test send are admin-only; every listed template previews with sample data; the catalog send delivers each template once", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/email/templates?maxResults=1000", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/email/templates?maxResults=1000", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/welcome/preview", null, new { }, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/welcome/preview", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/email/test/catalog", member.AccessToken, new { to = member.Email }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/email/templates?maxResults=1000", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> keys = list.GetProperty("objects").EnumerateArray().Select(t => Api.Str(t, "key")).ToList();
                    Check.True(keys.Count >= 40 && !keys.Contains("platform_admin_invite"), "Catalog listed (" + keys.Count + ")");
                    foreach (string key in keys)
                    {
                        JsonElement preview = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/" + key + "/preview", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        string html = Api.Str(preview, "html");
                        string text = Api.Str(preview, "text");
                        Check.True(html.Contains(WebUtility.HtmlEncode(f.Property.Name), StringComparison.Ordinal) && text.Contains(f.Property.Name, StringComparison.Ordinal), key + " preview names the property");
                        Check.True(html.Contains(f.Property.Brand.Colors.Light.Primary, StringComparison.OrdinalIgnoreCase), key + " preview uses the brand color");
                        Check.True(!_Placeholder.IsMatch(Api.Str(preview, "subject")) && !_Placeholder.IsMatch(text), key + " preview has no unresolved placeholders");
                    }

                    string inbox = "catalog-review@example.test";
                    JsonElement sent = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/test/catalog", HttpFixture.AdminToken, new { to = inbox }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(keys.Count, sent.GetProperty("results").GetArrayLength(), "One result per template");
                    Check.Equal(keys.Count, sent.GetProperty("sent").GetInt32(), "All sent: " + sent.ToString());
                    Check.Equal(0, sent.GetProperty("failed").GetInt32(), "None failed");
                    foreach (string key in keys) Check.Equal(1, f.Fakes.Email.To(inbox, key).Count, key + " delivered once");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/email/test/catalog", HttpFixture.AdminToken, new { to = "not-an-address" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=email.test_catalog", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, audit.GetProperty("totalRecords").GetInt64(), "Audited once");
                }),

                SuiteCases.Http(Id, "Mailgun.WebhookVariables", "A Mailgun event without a matching message-id header is correlated by the tk-message-id variable when the recipient matches", async (f, ct) =>
                {
                    string key = "whsec-" + Guid.NewGuid().ToString("N");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { values = new Dictionary<string, object> { ["email.mailgunWebhookSigningKey"] = key } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    EmailMessageRecord? record = await f.Services.Email.QueueAsync(new EmailRequest { Template = "test", To = "vars@example.test" }, ct).ConfigureAwait(false);
                    string path = "/api/v1/webhooks/mailgun/" + f.Services.PropertyId;
                    object Signed(string ev, string recipient, string messageId, string nonce)
                    {
                        string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(ts + nonce))).ToLowerInvariant();
                        return new Dictionary<string, object>
                        {
                            ["signature"] = new { timestamp = ts, token = nonce, signature = sig },
                            ["event-data"] = new Dictionary<string, object>
                            {
                                ["event"] = ev, ["recipient"] = recipient,
                                ["user-variables"] = new Dictionary<string, string> { [EmailService.MessageIdVariable] = messageId },
                                ["message"] = new { headers = new Dictionary<string, string> { ["message-id"] = "<unknown@example.test>" } }
                            }
                        };
                    }
                    await Api.ExpectAsync(f, HttpMethod.Post, path, null, Signed("delivered", "someone-else@example.test", record!.Id, "v1"), HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(EmailStatusEnum.Sent, (await f.Database.Email.ReadMessageAsync(record.Id, ct).ConfigureAwait(false))!.Status, "A different recipient does not match");
                    await Api.ExpectAsync(f, HttpMethod.Post, path, null, Signed("delivered", "", record.Id, "v-empty"), HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(EmailStatusEnum.Sent, (await f.Database.Email.ReadMessageAsync(record.Id, ct).ConfigureAwait(false))!.Status, "A missing recipient cannot match the variable");
                    await Api.ExpectAsync(f, HttpMethod.Post, path, null, Signed("delivered", "vars@example.test", record.Id, "v2"), HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(EmailStatusEnum.Delivered, (await f.Database.Email.ReadMessageAsync(record.Id, ct).ConfigureAwait(false))!.Status, "Correlated by the variable");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static List<string> PropertyDirectories()
        {
            return new List<string> { TestProperty.SampleDirectory };
        }

        private static EmailBrand BrandFor(string directory, out PropertyConfiguration property)
        {
            property = PropertyLoader.Load(directory);
            EmailLayoutOptions layout = EmailLayoutOptions.Load(directory);
            Check.Equal(0, layout.Problems.Count, property.Name + " email/layout.json is valid: " + String.Join(" | ", layout.Problems));
            return EmailBrandFactory.Create(new PropertyContext(property, new AppSettings()), layout, null, DateTime.UtcNow);
        }

        private static void RenderCatalog(string directory)
        {
            EmailBrand brand = BrandFor(directory, out PropertyConfiguration property);
            EmailStyle style = new EmailStyle(brand);
            EmailTemplateSet set = EmailTemplateSet.Load(directory);
            Check.Equal(0, set.Problems.Count, property.Name + " email templates are valid: " + String.Join(" | ", set.Problems));
            Check.True(!String.IsNullOrWhiteSpace(brand.Copyright), property.Name + " has an ownership line");
            Check.True(brand.LogoUrl != null && brand.LogoWidth >= 16 && brand.LogoHeight >= 16, property.Name + " has a sized logo");
            string name = WebUtility.HtmlEncode(property.Name);
            foreach (EmailTemplate template in set.All)
            {
                Dictionary<string, string> values = new Dictionary<string, string>(template.Samples, StringComparer.Ordinal)
                {
                    ["name"] = property.Name,
                    ["memberName"] = "Pat Example",
                    ["supportEmail"] = property.Contact.SupportEmail,
                    ["webUrl"] = brand.WebUrl,
                    ["provider"] = "Smtp"
                };
                string label = property.Name + " " + template.Key + ": ";
                foreach (string part in new[] { template.DefaultSubject, template.DefaultPreheader, template.DefaultHeading, template.DefaultBody, template.ButtonLabel ?? String.Empty })
                    foreach (Match m in _Placeholder.Matches(part))
                        Check.True(values.ContainsKey(m.Value.Trim('{', '}')), label + m.Value + " has a value");
                EmailRendered rendered = EmailRenderer.Render(template, null, values, brand);
                Check.True(rendered.Subject.Trim().Length > 0, label + "subject");
                Check.True(rendered.Html.StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase) && rendered.Html.Contains("lang=\"" + brand.Language + "\"", StringComparison.Ordinal), label + "complete document with a language");
                Check.True(rendered.Html.Contains("role=\"presentation\"", StringComparison.Ordinal), label + "presentation tables");
                Check.True(rendered.Html.Contains(name, StringComparison.Ordinal) && rendered.Text.Contains(property.Name, StringComparison.Ordinal), label + "property name in both parts");
                Check.True(rendered.Html.Contains(style.Link, StringComparison.Ordinal) && rendered.Html.Contains(brand.PrimaryColor, StringComparison.OrdinalIgnoreCase), label + "brand color");
                Check.True(rendered.Html.Contains("alt=\"" + name + "\"", StringComparison.Ordinal), label + "logo alt text");
                Check.True(rendered.Html.Contains(WebUtility.HtmlEncode(brand.Copyright), StringComparison.Ordinal) && rendered.Text.Contains(brand.Copyright, StringComparison.Ordinal), label + "ownership line in both parts");
                Check.True(rendered.Text.Trim().Length > brand.Name.Length + 20, label + "text part has content");
                foreach (string part in new[] { rendered.Subject, rendered.Preheader, rendered.Text, rendered.Html })
                {
                    Check.True(!_Placeholder.IsMatch(part), label + "no unresolved placeholder (" + (_Placeholder.Match(part).Value) + ")");
                    Check.True(!part.Contains((char)0x2014, StringComparison.Ordinal), label + "no em-dash");
                }
            }
        }

        private static void AssertAa(string label, string foreground, string background)
        {
            double ratio = EmailStyle.ContrastRatio(foreground, background);
            Check.True(ratio >= EmailStyle.MinimumContrast, label + " " + foreground + " on " + background + " is " + ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1");
        }

        #endregion
    }
}
