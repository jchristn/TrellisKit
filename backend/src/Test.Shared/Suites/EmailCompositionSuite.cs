namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Blocks;

    /// <summary>
    /// Email composition (docs/TRELLISKIT.md section 12.2): body blocks (fields, code, callout, list, subheading,
    /// divider, button), code templates that override Compose, typed templates, custom layouts, host registration, and
    /// property JSON templates (replace and add, with validation).
    /// </summary>
    public static class EmailCompositionSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "EmailComposition";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Email composition", new[]
            {
                SuiteCases.Unit(Id, "Blocks.RenderAndEncode", "Fields, code, callout, notice, list, subheading, divider, and button blocks render matching HTML and text; values are encoded; empty rows and non-http buttons render nothing", ct =>
                {
                    EmailStyle style = new EmailStyle(new EmailBrand { PrimaryColor = "#2E7D32" });
                    EmailFieldsBlock fields = new EmailFieldsBlock(new[] { new EmailField("Plan", "Monthly <b>"), new EmailField("Coupon", "  "), new EmailField("Note", "one\ntwo") });
                    string html = fields.RenderHtml(style);
                    Check.True(html.Contains("<table", StringComparison.Ordinal) && html.Contains("Monthly &lt;b&gt;", StringComparison.Ordinal), "Fields encode values");
                    Check.True(!html.Contains("Coupon", StringComparison.Ordinal) && html.Contains("one<br>two", StringComparison.Ordinal), "Empty rows skipped, line breaks kept");
                    Check.Equal("Plan: Monthly <b>\nNote: one\ntwo", fields.RenderText(), "Fields text");
                    Check.Equal(String.Empty, new EmailFieldsBlock(new[] { new EmailField("Only", "") }).RenderHtml(style), "No rows renders nothing");

                    EmailCodeBlock code = new EmailCodeBlock("{\"error\": \"<x>\"}\nline 2");
                    Check.True(code.RenderHtml(style).Contains("<pre", StringComparison.Ordinal) && code.RenderHtml(style).Contains("&lt;x&gt;", StringComparison.Ordinal), "Code is preformatted and encoded");
                    Check.True(code.RenderText().StartsWith("    {", StringComparison.Ordinal) && code.RenderText().Contains("\n    line 2", StringComparison.Ordinal), "Code text is indented");
                    Check.True(new EmailCodeBlock(new string('a', EmailCodeBlock.MaxLength + 10)).Text.EndsWith("(truncated)", StringComparison.Ordinal), "Long code truncated");

                    Check.True(new EmailCalloutBlock("Heads up", EmailCalloutToneEnum.Danger).RenderHtml(style).Contains("#cf222e", StringComparison.Ordinal), "Danger tone");
                    Check.True(new EmailCalloutBlock("Info").RenderHtml(style).Contains("#2E7D32", StringComparison.Ordinal), "Info uses the brand color");
                    Check.Equal("> a\n> b", new EmailCalloutBlock("a\nb").RenderText(), "Callout text");

                    EmailListBlock list = new EmailListBlock(new[] { "First", "", "Second & more" }, true);
                    Check.True(list.RenderHtml(style).StartsWith("<ol", StringComparison.Ordinal) && list.RenderHtml(style).Contains("Second &amp; more", StringComparison.Ordinal), "Ordered list");
                    Check.Equal("1. First\n2. Second & more", list.RenderText(), "List text skips empty items");

                    Check.True(new EmailSubheadingBlock("Details").RenderHtml(style).Contains("<h2", StringComparison.Ordinal), "Subheading");
                    Check.True(new EmailDividerBlock().RenderHtml(style).Contains("<hr", StringComparison.Ordinal), "Divider");
                    Check.Equal(String.Empty, new EmailButtonBlock("Go", "javascript:alert(1)").RenderHtml(style), "Non-http button renders nothing");
                    Check.Equal("Go: https://example.test/x", new EmailButtonBlock("Go", "https://example.test/x").RenderText(), "Button text");
                    EmailNoticeBlock notice = new EmailNoticeBlock("Why you get this.", "Unsubscribe", "https://example.test/u?a=1&b=2");
                    Check.True(notice.RenderHtml(style).Contains("class=\"muted\"", StringComparison.Ordinal) && notice.RenderHtml(style).Contains("href=\"https://example.test/u?a=1&amp;b=2\"", StringComparison.Ordinal), "Notice link encoded once");
                    Check.Equal("Why you get this. Unsubscribe: https://example.test/u?a=1&b=2", notice.RenderText(), "Notice text");
                    Check.True(!new EmailNoticeBlock("Only text", "Bad", "mailto:x@example.test").RenderHtml(style).Contains("<a", StringComparison.Ordinal), "Non-http notice link dropped");
                    return Task.CompletedTask;
                }),

                SuiteCases.Unit(Id, "Templates.ComposeTypedAndLayout", "A code template's Compose adds blocks around the overridable body; typed templates flatten their context into tokens; a custom layout replaces the standard one; registration rejects duplicates and bad keys", ct =>
                {
                    SampleAlertEmail alert = new SampleAlertEmail();
                    Dictionary<string, string> tokens = alert.TokensFor(new SampleAlert { Service = "Payments <api>", Detail = "HTTP 500\nretrying", Region = "" });
                    tokens["name"] = "Example Community";
                    EmailBrand brand = new EmailBrand { Name = "Example Community", PrimaryColor = "#1F5FBF" };
                    EmailRendered rendered = EmailRenderer.Render(alert, null, tokens, brand);
                    Check.Equal("Example Community: Payments <api> is failing", rendered.Subject, "Subject tokens");
                    Check.True(rendered.Html.Contains("<table", StringComparison.Ordinal) && rendered.Html.Contains("Payments &lt;api&gt;", StringComparison.Ordinal), "Fields block rendered and encoded");
                    Check.True(!rendered.Html.Contains(">Region<", StringComparison.Ordinal), "Empty field skipped");
                    Check.True(rendered.Html.Contains("<pre", StringComparison.Ordinal) && rendered.Text.Contains("    HTTP 500", StringComparison.Ordinal), "Code block in both parts");
                    Check.True(rendered.Html.Contains("prefers-color-scheme: dark", StringComparison.Ordinal) && rendered.Html.Contains(".code,.callout", StringComparison.Ordinal), "Dark styles cover the new blocks");

                    EmailRendered overridden = EmailRenderer.Render(alert, new EmailTemplateOverride { Body = "Custom body for {service}." }, tokens, brand);
                    Check.True(overridden.Text.Contains("Custom body for Payments <api>.", StringComparison.Ordinal) && overridden.Html.Contains("<table", StringComparison.Ordinal), "Body override applies inside the composition");

                    EmailTemplate feed = EmailTemplateCatalog.Get("feed_post");
                    Dictionary<string, string> feedTokens = new Dictionary<string, string>(feed.Samples, StringComparer.Ordinal) { ["name"] = "Example Community", ["memberName"] = "Pat" };
                    EmailRendered feedMail = EmailRenderer.Render(feed, new EmailTemplateOverride { Body = "Short body." }, feedTokens, brand);
                    Check.True(feedMail.Text.Contains("Unsubscribe from new post emails: https://example.test/api/v1/email/unsubscribe?token=sample", StringComparison.Ordinal), "Feed post unsubscribe survives a body override");
                    Check.True(feedMail.Text.Contains("Read the post: https://example.test/app/feed/pst_sample", StringComparison.Ordinal), "Feed post button");

                    EmailBrand logoBrand = new EmailBrand { Name = "Example Community", LogoUrl = "https://example.test/api/v1/property/assets/logo.png", LogoSize = new ImageSize(744, 763) };
                    EmailRendered withLogo = EmailRenderer.Render(alert, null, tokens, logoBrand);
                    Check.True(withLogo.Html.Contains("width:47px;height:48px", StringComparison.Ordinal), "Logo gets explicit dimensions that keep its aspect ratio");
                    EmailRendered wideLogo = EmailRenderer.Render(alert, null, tokens, new EmailBrand { Name = "Example Community", LogoUrl = logoBrand.LogoUrl, LogoSize = new ImageSize(1000, 100) });
                    Check.True(wideLogo.Html.Contains("width:200px;height:20px", StringComparison.Ordinal), "Wide logo fits the width limit");
                    EmailRendered unknownLogo = EmailRenderer.Render(alert, null, tokens, new EmailBrand { Name = "Example Community", LogoUrl = logoBrand.LogoUrl });
                    Check.True(unknownLogo.Html.Contains("height:48px;width:auto", StringComparison.Ordinal), "Logo of unknown size keeps its ratio");
                    byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0x02, 0xE8, 0, 0, 0x02, 0xFB };
                    ImageSize? pngSize = ImageSize.FromBytes(png);
                    Check.True(pngSize != null && pngSize.Width == 744 && pngSize.Height == 763, "PNG header size read");
                    Check.True(ImageSize.FromBytes(new byte[] { 1, 2, 3 }) == null, "Unknown format has no size");

                    EmailRendered custom = EmailRenderer.Render(new CustomLayoutEmail(), null, new Dictionary<string, string> { ["name"] = "X" }, brand);
                    Check.Equal("<custom>Plain heading</custom>", custom.Html, "Custom layout used");

                    Check.Throws<ArgumentException>(() => EmailTemplateCatalog.Register(new DuplicateKeyEmail()), "Duplicate of an engine key rejected");
                    Check.Throws<ArgumentException>(() => EmailTemplateCatalog.Register(new BadKeyEmail()), "Invalid key rejected");
                    try
                    {
                        EmailTemplateCatalog.Register(alert);
                        Check.True(EmailTemplateCatalog.Find(alert.Key) == alert, "Registered template is found");
                        Check.True(EmailTemplateCatalog.All.Count == EmailTemplateCatalog.Engine.Count + 1, "All includes registered templates");
                        Check.Throws<ArgumentException>(() => EmailTemplateCatalog.Register(new SampleAlertEmail()), "Registering twice rejected");
                        Check.True(new EmailTemplateSet().Find(alert.Key) == alert, "Template sets see registered templates");
                    }
                    finally
                    {
                        EmailTemplateCatalog.Unregister(alert.Key);
                    }
                    Check.True(EmailTemplateCatalog.Find(alert.Key) == null && !EmailTemplateCatalog.Unregister("welcome"), "Unregister removes only registered templates");
                    return Task.CompletedTask;
                }),

                SuiteCases.Unit(Id, "PropertyTemplates.ReplaceAddValidate", "Property JSON templates replace an engine template's text and blocks (keeping category and tokens) or add new ones; invalid files are reported and skipped", ct =>
                {
                    string root = Path.Combine(Path.GetTempPath(), "tk-email-" + Guid.NewGuid().ToString("N"));
                    string folder = Path.Combine(root, "email", "templates");
                    Directory.CreateDirectory(folder);
                    try
                    {
                        File.WriteAllText(Path.Combine(folder, "welcome.json"), "{\n  // comments allowed\n  \"subject\": \"Welcome aboard, {memberName}\",\n" +
                            "  \"blocks\": [ { \"type\": \"body\" }, { \"type\": \"callout\", \"tone\": \"success\", \"text\": \"You are in, {memberName}.\" }, { \"type\": \"list\", \"items\": [\"Finish your profile\", \"Say hello\"] }, { \"type\": \"button\" } ]\n}");
                        File.WriteAllText(Path.Combine(folder, "gym_spotlight.json"), "{ \"category\": \"Optional\", \"recipient\": \"Member\", \"subject\": \"Spotlight: {gym}\", \"heading\": \"{gym}\"," +
                            " \"body\": \"Meet {gym}.\", \"buttonLabel\": \"Visit\", \"samples\": { \"gym\": \"Example Gym\", \"link\": \"https://example.test/gyms/x\" }," +
                            " \"blocks\": [ { \"type\": \"body\" }, { \"type\": \"fields\", \"fields\": [ { \"label\": \"Gym\", \"value\": \"{gym}\" } ] }, { \"type\": \"button\" } ] }");
                        File.WriteAllText(Path.Combine(folder, "bad_token.json"), "{ \"category\": \"Optional\", \"recipient\": \"Member\", \"subject\": \"Hi {nope}\", \"heading\": \"x\" }");
                        File.WriteAllText(Path.Combine(folder, "bad_block.json"), "{ \"category\": \"Optional\", \"recipient\": \"Member\", \"subject\": \"Hi\", \"heading\": \"x\", \"blocks\": [ { \"type\": \"iframe\" } ] }");
                        File.WriteAllText(Path.Combine(folder, "platform_admin_invite.json"), "{ \"subject\": \"Hi\" }");
                        File.WriteAllText(Path.Combine(folder, "Bad-Name.json"), "{ \"subject\": \"Hi\" }");
                        File.WriteAllText(Path.Combine(folder, "broken.json"), "{ not json");
                        File.WriteAllText(Path.Combine(folder, "no_category.json"), "{ \"subject\": \"Hi\", \"heading\": \"x\" }");

                        EmailTemplateSet set = EmailTemplateSet.Load(root);
                        Check.Equal(2, set.PropertyTemplates.Count, "Two valid templates");
                        Check.Equal(6, set.Problems.Select(p => p.Substring(0, p.IndexOf(": ", StringComparison.Ordinal))).Distinct().Count(), "Six files with problems: " + String.Join(" | ", set.Problems));
                        Check.True(set.Problems.Any(p => p.Contains("bad_token.json", StringComparison.Ordinal) && p.Contains("{nope}", StringComparison.Ordinal)), "Unknown token named");
                        Check.True(set.Problems.Any(p => p.Contains("bad_block.json", StringComparison.Ordinal) && p.Contains("iframe", StringComparison.Ordinal)), "Unknown block type named");
                        Check.True(set.Problems.Any(p => p.Contains("platform_admin_invite.json", StringComparison.Ordinal)), "Platform templates cannot be replaced");

                        EmailTemplate welcome = set.Get("welcome");
                        Check.True(welcome is PropertyEmailTemplate { Replaces: not null }, "Welcome replaced");
                        Check.Equal(EmailTemplateCatalog.Get("welcome").Category, welcome.Category, "Category kept");
                        Check.Equal(EmailTemplateCatalog.Get("welcome").DefaultBody, welcome.DefaultBody, "Unset fields keep the engine default");
                        Check.True(set.All.Count(t => t.Key == "welcome") == 1 && set.All.Count == EmailTemplateCatalog.All.Count + 1, "All replaces and adds");

                        Dictionary<string, string> tokens = new Dictionary<string, string>(welcome.Samples, StringComparer.Ordinal) { ["name"] = "Example Community", ["memberName"] = "Pat" };
                        EmailRendered rendered = EmailRenderer.Render(welcome, null, tokens, new EmailBrand { Name = "Example Community" });
                        Check.Equal("Welcome aboard, Pat", rendered.Subject, "Property subject");
                        Check.True(rendered.Html.Contains("#1a7f37", StringComparison.Ordinal) && rendered.Html.Contains("<ul", StringComparison.Ordinal) && rendered.Text.Contains("- Say hello", StringComparison.Ordinal), "Callout and list rendered");
                        Check.True(rendered.Text.Contains("Your Example Community account is ready.", StringComparison.Ordinal), "Engine body kept through the body block");

                        EmailTemplate spotlight = set.Get("gym_spotlight");
                        Check.Equal(EmailCategoryEnum.Optional, spotlight.Category, "New template category");
                        Dictionary<string, string> sample = new Dictionary<string, string>(spotlight.Samples, StringComparer.Ordinal) { ["name"] = "Example Community" };
                        EmailRendered added = EmailRenderer.Render(spotlight, null, sample, new EmailBrand { Name = "Example Community" });
                        Check.True(added.Subject == "Spotlight: Example Gym" && added.Html.Contains("<table", StringComparison.Ordinal) && added.Text.Contains("Visit: https://example.test/gyms/x", StringComparison.Ordinal), "New template renders");
                        Check.Equal(0, EmailRenderer.Validate(spotlight, new EmailTemplateOverride { Body = "About {gym}" }).Count, "Admins can override a property template with its tokens");
                    }
                    finally
                    {
                        Directory.Delete(root, true);
                    }
                    Check.Equal(0, EmailTemplateSet.Load(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N"))).PropertyTemplates.Count, "A missing folder loads none");
                    return Task.CompletedTask;
                })
            });
        }

        #endregion

        #region Private-Classes

        private sealed class SampleAlert
        {
            public string Service { get; set; } = String.Empty;

            public string Detail { get; set; } = String.Empty;

            public string Region { get; set; } = String.Empty;
        }

        private sealed class SampleAlertEmail : TypedEmailTemplate<SampleAlert>
        {
            public override string Key
            {
                get { return "test_sample_alert"; }
            }

            public override EmailCategoryEnum Category
            {
                get { return EmailCategoryEnum.Staff; }
            }

            public override EmailRecipientEnum Recipient
            {
                get { return EmailRecipientEnum.Staff; }
            }

            public override string Description
            {
                get { return "Test alert."; }
            }

            public override string DefaultSubject
            {
                get { return "{name}: {service} is failing"; }
            }

            public override string DefaultPreheader
            {
                get { return "{service} is failing."; }
            }

            public override string DefaultHeading
            {
                get { return "{service} is failing"; }
            }

            public override string DefaultBody
            {
                get { return "{service} reported a problem."; }
            }

            public override IReadOnlyDictionary<string, string> Samples
            {
                get { return new Dictionary<string, string> { ["service"] = "Payments", ["detail"] = "HTTP 500", ["region"] = "us" }; }
            }

            public override Dictionary<string, string> TokensFor(SampleAlert context)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal) { ["service"] = context.Service, ["detail"] = context.Detail, ["region"] = context.Region };
            }

            public override void Compose(EmailComposer email)
            {
                email.BodyParagraphs()
                    .Fields(new EmailField("Service", "{service}"), new EmailField("Region", "{region}"))
                    .Code(email.Token("detail"))
                    .DefaultButton();
            }
        }

        private sealed class CustomLayout : EmailLayout
        {
            public override EmailRendered Render(EmailDocument document, EmailBrand brand)
            {
                return new EmailRendered { Subject = document.Subject, Html = "<custom>" + document.Heading + "</custom>", Text = document.Heading };
            }
        }

        private sealed class CustomLayoutEmail : EmailTemplate
        {
            public override string Key
            {
                get { return "test_custom_layout"; }
            }

            public override EmailCategoryEnum Category
            {
                get { return EmailCategoryEnum.Optional; }
            }

            public override EmailRecipientEnum Recipient
            {
                get { return EmailRecipientEnum.Member; }
            }

            public override string Description
            {
                get { return "Test layout."; }
            }

            public override string DefaultSubject
            {
                get { return "Subject"; }
            }

            public override string DefaultPreheader
            {
                get { return String.Empty; }
            }

            public override string DefaultHeading
            {
                get { return "Plain heading"; }
            }

            public override string DefaultBody
            {
                get { return String.Empty; }
            }

            public override IReadOnlyDictionary<string, string> Samples
            {
                get { return new Dictionary<string, string>(); }
            }

            public override EmailLayout Layout
            {
                get { return new CustomLayout(); }
            }
        }

        private sealed class DuplicateKeyEmail : EmailTemplate
        {
            public override string Key
            {
                get { return "welcome"; }
            }

            public override EmailCategoryEnum Category
            {
                get { return EmailCategoryEnum.Essential; }
            }

            public override EmailRecipientEnum Recipient
            {
                get { return EmailRecipientEnum.Member; }
            }

            public override string Description
            {
                get { return "Duplicate."; }
            }

            public override string DefaultSubject
            {
                get { return "x"; }
            }

            public override string DefaultPreheader
            {
                get { return "x"; }
            }

            public override string DefaultHeading
            {
                get { return "x"; }
            }

            public override string DefaultBody
            {
                get { return "x"; }
            }

            public override IReadOnlyDictionary<string, string> Samples
            {
                get { return new Dictionary<string, string>(); }
            }
        }

        private sealed class BadKeyEmail : EmailTemplate
        {
            public override string Key
            {
                get { return "Bad Key"; }
            }

            public override EmailCategoryEnum Category
            {
                get { return EmailCategoryEnum.Essential; }
            }

            public override EmailRecipientEnum Recipient
            {
                get { return EmailRecipientEnum.Member; }
            }

            public override string Description
            {
                get { return "Bad."; }
            }

            public override string DefaultSubject
            {
                get { return "x"; }
            }

            public override string DefaultPreheader
            {
                get { return "x"; }
            }

            public override string DefaultHeading
            {
                get { return "x"; }
            }

            public override string DefaultBody
            {
                get { return "x"; }
            }

            public override IReadOnlyDictionary<string, string> Samples
            {
                get { return new Dictionary<string, string>(); }
            }
        }

        #endregion
    }
}
