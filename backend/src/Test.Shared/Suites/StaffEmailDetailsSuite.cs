namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Email;

    /// <summary>
    /// Staff notification email details (docs/EMAIL.md, "Staff email details"): request and member details on staff
    /// emails, per-template sections, the email.staffRequestDetails setting, and the staff-only rule. Runs on the neutral
    /// sample property.
    /// </summary>
    public static class StaffEmailDetailsSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite id.
        /// </summary>
        public const string Id = "StaffEmailDetails";

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, string> _Client = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "X-Client-Platform", "ios" },
            { "X-Client-Version", "9.8.7" },
            { "User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Staff email details (" + TestEnvironment.Provider + ")", new[]
            {
                Case("NewMember.RequestAndMember", "A sign-up's staff email shows the IP address, client, platform, app version, browser, request id, member details, documents, and the same-IP count", NewMemberAsync),
                Case("NewMember.SettingOff", "email.staffRequestDetails false drops the request details and keeps the member details", SettingOffAsync),
                Case("Report.ReporterAuthorContent", "A report's staff email shows the report, the content excerpt, the author with history, the reporter with history, and the request", ReportAsync),
                Case("Subscription.NoIp", "Subscription staff emails show the plan, store, transaction, and member with an Origin and never an IP address", SubscriptionAsync),
                Case("Templates.StaffOnly", "Every staff template accepts and renders the details sections; member templates never render them and the queue drops the token", StaffOnlyAsync),
                Case("Details.ParseAndLimits", "Malformed details render nothing, limits are enforced, braces never fill placeholders, and the user agent summary reads common agents", ParseAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static async Task<TestMember> RegisterWithClientAsync(HttpFixture f, string displayName, CancellationToken ct)
        {
            TestMember member = new TestMember { Email = Api.UniqueEmail("staffdetails"), Password = Api.Password };
            ApiResult result = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                new { email = member.Email, password = member.Password, displayName, dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, ct, "mobile", _Client).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, result.Status, "Registered (body: " + result.Text + ")");
            member.Id = result.Body.GetProperty("user").GetProperty("id").GetString()!;
            member.AccessToken = result.Body.GetProperty("accessToken").GetString()!;
            return member;
        }

        private static EmailMessage Latest(HttpFixture f, string to, string template, string contains)
        {
            EmailMessage? message = f.Fakes.Email.To(to, template).LastOrDefault(m => m.TextBody.Contains(contains, StringComparison.Ordinal));
            Check.True(message != null, template + " to " + to + " mentioning " + contains);
            return message!;
        }

        private static async Task NewMemberAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember first = await RegisterWithClientAsync(f, "Detail First", ct).ConfigureAwait(false);
            TestMember member = await RegisterWithClientAsync(f, "Detail Second", ct).ConfigureAwait(false);
            EmailMessage email = Latest(f, admin.Email, "staff_new_member", member.Email);
            string text = email.TextBody;
            foreach (string expected in new[] { "REQUEST", "IP address", "Mobile", "ios", "9.8.7", "Safari 18.1 on iOS 18.1", "req_", "/admin/requests?id=req_",
                "MEMBER", member.Id, member.Email + " (not verified)", "Email and password", "Age attested", "Yes", "Roles", "Membership", "/admin/members/" + member.Id,
                "SIGN-UP", "Accounts from this IP in 24 hours", "Documents accepted" })
                Check.True(text.Contains(expected, StringComparison.Ordinal), "staff_new_member shows " + expected + " (text: " + text + ")");
            Check.True(email.HtmlBody != null && email.HtmlBody.Contains("IP address", StringComparison.Ordinal), "The HTML part carries the details table");
            Check.True(!text.Contains(first.Email, StringComparison.Ordinal), "Another member's address never appears");

            List<AuditRecord> audit = await f.Database.Audit.EnumerateByUserAsync(member.Id, 20, ct).ConfigureAwait(false);
            AuditRecord? signup = audit.FirstOrDefault(a => a.Action == StaffEmailDetailsService.SignupAuditAction);
            Check.True(signup != null && !String.IsNullOrEmpty(signup.SourceIp), "Each sign-up writes an audit record with its source IP");
            int same = await f.Database.Audit.CountByActionAndIpSinceAsync(StaffEmailDetailsService.SignupAuditAction, signup!.SourceIp!, DateTime.UtcNow.AddHours(-24), 1000, ct).ConfigureAwait(false);
            Check.True(same >= 2, "Both sign-ups from the same IP are counted (" + same + ")");
            Check.Equal(1, await f.Database.Audit.CountByActionAndIpSinceAsync(StaffEmailDetailsService.SignupAuditAction, signup.SourceIp!, DateTime.UtcNow.AddHours(-24), 1, ct).ConfigureAwait(false), "The count is capped");
            Check.Equal(0, await f.Database.Audit.CountByActionAndIpSinceAsync(StaffEmailDetailsService.SignupAuditAction, "198.51.100.77", DateTime.UtcNow.AddHours(-24), 1000, ct).ConfigureAwait(false), "Another IP counts zero");

            EmailMessage verify = f.Fakes.Email.Messages.Last(m => String.Equals(m.To, member.Email, StringComparison.OrdinalIgnoreCase));
            Check.True(!verify.TextBody.Contains("IP address", StringComparison.Ordinal), "The member's own email never carries request details");
        }

        private static async Task SettingOffAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken,
                new { values = new Dictionary<string, object> { [StaffEmailDetailsService.RequestDetailsSetting] = false } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            try
            {
                TestMember member = await RegisterWithClientAsync(f, "Detail Quiet", ct).ConfigureAwait(false);
                string text = Latest(f, admin.Email, "staff_new_member", member.Email).TextBody;
                Check.True(!text.Contains("IP address", StringComparison.Ordinal), "No IP address with the setting off");
                Check.True(!text.Contains("9.8.7", StringComparison.Ordinal), "No app version with the setting off");
                Check.True(text.Contains(member.Id, StringComparison.Ordinal) && text.Contains("Sign-up method", StringComparison.Ordinal), "Member details stay");
            }
            finally
            {
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken,
                    new { clear = new[] { StaffEmailDetailsService.RequestDetailsSetting } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            }
        }

        private static async Task ReportAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.PosterAsync(f, ct, "Detail Author").ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Detail Reporter").ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, author.AccessToken, "Reported text for the staff details check", ct).ConfigureAwait(false);
            ApiResult created = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/reports", reporter.AccessToken,
                new { targetType = "FeedPost", targetId = postId, reason = "Spam", details = "Selling things in the feed" }, ct, "web", _Client).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, created.Status, "Report filed (body: " + created.Text + ")");
            string text = Latest(f, admin.Email, "staff_new_report", postId).TextBody;
            foreach (string expected in new[] { "REPORT", "Spam", "Selling things in the feed", "Review due", "CONTENT", "Reported text for the staff details check", "Open reports",
                "AUTHOR", author.Id, "History (90 days)", "REPORTER", reporter.Id, "Reports filed in 90 days", "REQUEST", "IP address", "Web", "req_" })
                Check.True(text.Contains(expected, StringComparison.Ordinal), "staff_new_report shows " + expected + " (text: " + text + ")");
            Check.Equal(1L, await f.Database.Reports.CountByReporterAsync(reporter.Id, DateTime.UtcNow.AddDays(-90), ct).ConfigureAwait(false), "Reporter history counts the report");
            Dictionary<string, long> breakdown = await f.Database.Reports.OpenBreakdownOnTargetAsync(TargetTypeEnum.FeedPost, postId, ct).ConfigureAwait(false);
            Check.Equal(1L, breakdown["Spam"], "Open reasons breakdown");
            Check.Equal(1L, breakdown["reporters"], "Distinct reporters");
        }

        private static async Task SubscriptionAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            Subscription subscription = new Subscription
            {
                Id = "sub_" + Guid.NewGuid().ToString("N").Substring(0, 20),
                UserId = member.Id,
                Store = StoreEnum.Apple,
                StoreSubscriptionId = "orig-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                ProductId = f.Property.Membership.StoreProductId,
                Status = SubscriptionStatusEnum.Active,
                CurrentPeriodStartUtc = now,
                CurrentPeriodEndUtc = now.AddDays(365),
                AutoRenew = true,
                LatestTransactionId = "txn-detail-1",
                Environment = StoreEnvironmentEnum.Sandbox,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            string details = await PurchaseDetailsAsync(f, subscription, member.Id, ct).ConfigureAwait(false);
            Check.True(details.Contains("txn-detail-1", StringComparison.Ordinal), "Transaction id");
            Check.True(details.Contains("Apple (Sandbox)", StringComparison.Ordinal), "Store and environment");
            Check.True(details.Contains("App Store notification", StringComparison.Ordinal), "Origin names the store");
            Check.True(details.Contains(member.Id, StringComparison.Ordinal), "Member block");
            Check.True(!details.Contains("IP address", StringComparison.Ordinal), "Never an IP address on a store-originated email");
            Check.True(admin.Id.Length > 0, "Admin registered");
        }

        private static async Task<string> PurchaseDetailsAsync(HttpFixture f, Subscription subscription, string userId, CancellationToken ct)
        {
            // The subscription staff emails build their sections the same way; render them through the template to check the result.
            StaffEmailDetailsService builder = f.Services.StaffEmailDetails;
            StaffEmailDetails sections = new StaffEmailDetails();
            sections.Add(new StaffEmailSection("Subscription").Row("Store", subscription.Store + " (" + subscription.Environment + ")").Row("Latest transaction id", subscription.LatestTransactionId));
            sections.Add(await builder.MemberAsync(userId, "Member", ct).ConfigureAwait(false));
            sections.Add(builder.Origin("App Store notification or verification"));
            EmailTemplate template = f.Services.Email.Templates.Get("staff_new_subscription");
            Dictionary<string, string> tokens = template.Samples.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            tokens[StaffEmailDetails.Token] = sections.Serialize();
            EmailRendered rendered = EmailRenderer.Render(template, null, tokens, await f.Services.Email.BrandAsync(ct).ConfigureAwait(false));
            return rendered.Text;
        }

        private static async Task StaffOnlyAsync(HttpFixture f, CancellationToken ct)
        {
            EmailBrand brand = await f.Services.Email.BrandAsync(ct).ConfigureAwait(false);
            string sample = new StaffEmailDetails().Add(new StaffEmailSection("Request").Row("IP address", "203.0.113.9")).Serialize();
            int staff = 0;
            foreach (EmailTemplate template in f.Services.Email.Templates.All)
            {
                Dictionary<string, string> tokens = template.Samples.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                if (template.Category == EmailCategoryEnum.Staff)
                {
                    staff++;
                    Check.True(template.Tokens.Contains(StaffEmailDetails.Token), template.Key + " accepts the details token");
                    Check.True(template.Samples.ContainsKey(StaffEmailDetails.Token), template.Key + " has a details sample for previews");
                    EmailRendered preview = EmailRenderer.Render(template, null, tokens, brand);
                    Check.True(preview.Text.Contains("REQUEST", StringComparison.Ordinal) || preview.Text.Contains("ORIGIN", StringComparison.Ordinal), template.Key + " sample renders a Request or Origin section");
                    tokens[StaffEmailDetails.Token] = sample;
                    EmailRendered rendered = EmailRenderer.Render(template, null, tokens, brand);
                    Check.True(rendered.Text.Contains("203.0.113.9", StringComparison.Ordinal), template.Key + " renders details");
                }
                else
                {
                    Check.True(!template.Tokens.Contains(StaffEmailDetails.Token), template.Key + " does not accept the details token");
                    tokens[StaffEmailDetails.Token] = sample;
                    EmailRendered rendered = EmailRenderer.Render(template, null, tokens, brand);
                    Check.True(!rendered.Text.Contains("203.0.113.9", StringComparison.Ordinal) || rendered.Html.Contains("203.0.113.9", StringComparison.Ordinal), template.Key + " never renders details");
                }
            }
            Check.True(staff >= 17, "Every staff template checked (" + staff + ")");

            await f.Services.Email.QueueAsync(new EmailRequest { Template = "welcome", To = "detail-member@example.test", Tokens = { [StaffEmailDetails.Token] = sample } }, ct).ConfigureAwait(false);
            EmailMessage? member = f.Fakes.Email.To("detail-member@example.test", "welcome").LastOrDefault();
            Check.True(member != null && !member.TextBody.Contains("203.0.113.9", StringComparison.Ordinal), "A member email given a details token never shows it");

            await f.Services.Email.QueueAsync(new EmailRequest { Template = "staff_integration_failure", To = "detail-ops@example.test", Tokens = { ["integration"] = "Test", ["detail"] = "x" } }, ct).ConfigureAwait(false);
            EmailMessage? system = f.Fakes.Email.To("detail-ops@example.test", "staff_integration_failure").LastOrDefault();
            Check.True(system != null && system.TextBody.Contains("Integration monitor", StringComparison.Ordinal), "A system staff email gets an Origin section");
            Check.True(system != null && !system.TextBody.Contains("IP address", StringComparison.Ordinal), "A system staff email has no IP address");
        }

        private static Task ParseAsync(HttpFixture f, CancellationToken ct)
        {
            Check.Equal(0, StaffEmailDetails.Parse("not json").Count, "Malformed JSON");
            Check.Equal(0, StaffEmailDetails.Parse("{\"title\":\"x\"}").Count, "Not an array");
            Check.Equal(0, StaffEmailDetails.Parse("[{\"title\":\"Empty\",\"rows\":[]}]").Count, "A section without rows is dropped");
            StaffEmailDetails many = new StaffEmailDetails();
            for (int i = 0; i < 12; i++)
            {
                StaffEmailSection section = new StaffEmailSection("S" + i);
                for (int r = 0; r < 30; r++) section.Row("L" + r, new string('v', 1000));
                many.Add(section);
            }
            List<StaffEmailSection> parsed = StaffEmailDetails.Parse(many.Serialize());
            Check.Equal(StaffEmailDetails.MaxSections, parsed.Count, "At most 8 sections");
            Check.Equal(StaffEmailDetails.MaxRows, parsed[0].Rows.Count, "At most 20 rows");
            Check.Equal(StaffEmailDetails.MaxValue, parsed[0].Rows[0].Value.Length, "Values are clipped");
            Check.Equal("(name)", StaffEmailDetails.Literal("{name}"), "Braces never fill placeholders");
            Check.Equal(0, new StaffEmailSection("x").Row("Blank", " ").Row("Null", null).Rows.Count, "Blank values are left out");
            Check.Equal("Chrome 131 on macOS", UserAgentSummary.Describe("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"), "Chrome on macOS");
            Check.Equal("Edge 131 on Windows", UserAgentSummary.Describe("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"), "Edge on Windows");
            Check.Equal("Firefox 133 on Linux", UserAgentSummary.Describe("Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0"), "Firefox on Linux");
            Check.Equal("Android app (okhttp)", UserAgentSummary.Describe("okhttp/4.12.0"), "Android app");
            Check.True(UserAgentSummary.Describe(null) == null, "No agent");
            Check.Equal("5 minutes", StaffEmailDetailsService.Age(TimeSpan.FromMinutes(5)), "Age in minutes");
            Check.Equal("1 day", StaffEmailDetailsService.Age(TimeSpan.FromHours(30)), "Age in days");
            Check.Equal("4 (Approved 3, Pending 1), 75% approved", StaffEmailDetailsService.History(new Dictionary<string, long> { ["Approved"] = 3, ["Pending"] = 1 }, "Approved"), "Submission history");
            Check.Equal("None", StaffEmailDetailsService.History(new Dictionary<string, long>(), "Approved"), "No history");
            Check.Equal("abcdefghij...", StaffEmailDetailsService.Excerpt("abcdefghijklmnop", 10), "Excerpt clips with an ellipsis");
            return Task.CompletedTask;
        }

        #endregion
    }
}
