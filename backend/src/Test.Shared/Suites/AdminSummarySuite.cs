namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services.AdminSummary;
    using TrellisKit.Server.Services.Email.Templates;
    using TrellisKit.Server.Services.Metrics;

    /// <summary>
    /// Daily administrator summary (docs/ADMIN_SUMMARY.md): default items reference registered metrics, recipients are
    /// normalized, the report reflects real usage data, the worker sends each recipient one message per day after the send
    /// hour, and the routes are administrator-only.
    /// </summary>
    public static class AdminSummarySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AdminSummary";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite (neutral sample property).
        /// </summary>
        /// <returns>Descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Daily administrator summary", new[]
            {
                SuiteCases.Unit(Id, "DefaultsAreRegistered", "Every default item names a registered metric with daily buckets and a declared grouping", ct =>
                {
                    MetricRegistry registry = new MetricRegistry(new PropertyMetricsConfiguration());
                    foreach (AdminSummaryItem item in AdminSummaryDefaults.Items)
                    {
                        MetricDefinition? definition = registry.Find(item.Metric);
                        Check.True(definition != null, item.Metric + " is registered");
                        Check.True(definition!.Buckets.Contains("Day"), item.Metric + " has daily buckets");
                        if (item.GroupBy != null) Check.True(definition.Groupings.Contains(item.GroupBy), item.Metric + " declares " + item.GroupBy);
                        foreach (string key in item.Filters.Keys) Check.True(definition.Groupings.Contains(key), item.Metric + " declares filter " + key);
                    }
                    Check.True(AdminSummaryDefaults.Items.Any(i => i.Metric == "users.new") && AdminSummaryDefaults.Items.Any(i => i.Metric == "users.active") && AdminSummaryDefaults.Items.Any(i => i.Metric == "users.total"), "Sign-ups, daily active, and total members are reported");
                    Check.True(AdminSummaryDefaults.Items.Any(i => i.Metric == "interactions.activity" && i.GroupBy == "kind"), "Activity is broken down by type");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "RecipientsAndFormat", "Recipients are trimmed, deduplicated, validated, and capped; values format by unit", ct =>
                {
                    AdminSummarySettings settings = new AdminSummarySettings { Recipients = new List<string> { " ops@example.test ", "OPS@example.test", "not-an-address", "", "lead@example.test" } };
                    List<string> recipients = AdminSummaryService.Recipients(settings);
                    Check.Equal(2, recipients.Count, "Two valid distinct recipients");
                    Check.Equal("ops@example.test", recipients[0], "Trimmed, first spelling kept");
                    settings.Recipients = Enumerable.Range(0, 30).Select(i => "r" + i + "@example.test").ToList();
                    Check.Equal(AdminSummarySettings.MaxRecipients, AdminSummaryService.Recipients(settings).Count, "Capped");
                    Check.Equal(23, new AdminSummarySettings { SendHour = 99 }.SendHour, "Send hour clamped");
                    Check.Equal("1,234", AdminSummaryFormat.Value(1234, "count"), "Count");
                    Check.Equal("12.5%", AdminSummaryFormat.Value(12.5, "percent"), "Percent");
                    Check.Equal("1.5 MB", AdminSummaryFormat.Value(1.5 * 1024 * 1024, "bytes"), "Bytes");
                    Check.Equal("$1.25", AdminSummaryFormat.Value(1250000, "usdMicros"), "Money");
                    Check.Equal("No data", AdminSummaryFormat.Display(new AdminSummaryRow()), "Missing values are not zero");
                    Check.Equal("10 (day before 8, +25%; 7-day average 9)", AdminSummaryFormat.Display(new AdminSummaryRow { Value = 10, PreviousValue = 8, Change = 0.25, SevenDayAverage = 9, Complete = true }), "Comparisons");
                    Check.Throws<ApiErrorException>(() => AdminSummaryService.ParseDate("11/24/2026", "date"), "Dates use yyyy-MM-dd");
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "ReportFromUsage", "The report counts members and sign-ups from real usage rows and lists unknown configured metrics as skipped", s =>
                {
                    s.AdminSummary.Items = new List<AdminSummaryItem> { new AdminSummaryItem { Metric = "not.registered" }, new AdminSummaryItem { Metric = "users.new", Section = "Custom", Label = "Joined", GroupBy = "nope" } };
                }, async (f, ct) =>
                {
                    await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    DateTime now = DateTime.UtcNow;
                    await f.Services.Usage.RunPassAsync(now, ct).ConfigureAwait(false);
                    AdminSummaryService service = f.Services.AdminSummary;
                    service.Clock = () => now.AddDays(1);
                    AdminSummaryReport report = await service.BuildAsync(null, ct).ConfigureAwait(false);
                    Check.Equal(DateOnly.FromDateTime(now).ToString("yyyy-MM-dd"), report.Date, "Yesterday relative to the clock (UTC calendar)");
                    AdminSummarySection members = report.Sections.Single(s => s.Title == AdminSummaryDefaults.Members);
                    Check.True((members.Rows.Single(r => r.Metric == "users.new").Value ?? 0) >= 2, "New sign-ups counted");
                    Check.True((members.Rows.Single(r => r.Metric == "users.total").Value ?? 0) >= 2, "Total members counted");
                    Check.True(report.Sections.Any(s => s.Title == AdminSummaryDefaults.Activity), "Activity section present");
                    Check.True(report.Skipped.Any(s => s.StartsWith("not.registered:", StringComparison.Ordinal)), "Unknown metric skipped");
                    Check.True(report.Skipped.Any(s => s.StartsWith("users.new: unsupported grouping", StringComparison.Ordinal)), "Undeclared grouping skipped");
                    Check.True(report.Sections.All(s => s.Rows.All(r => r.Display.Length > 0)), "Every line is formatted");
                    await Check.ThrowsAsync<ApiErrorException>(() => service.BuildAsync(DateOnly.FromDateTime(now).AddDays(5), ct), "Future dates are rejected").ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "WorkerSendsOncePerDay", "The worker waits for the send hour, then sends each recipient one message for the day, and stays quiet when disabled", s =>
                {
                    s.AdminSummary.Enabled = true;
                    s.AdminSummary.Recipients = new List<string> { "summary-a@example.test", "SUMMARY-A@example.test", "summary-b@example.test", "bad address" };
                    s.AdminSummary.SendHour = 13;
                }, async (f, ct) =>
                {
                    await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await f.Services.Usage.RunPassAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    AdminSummaryService service = f.Services.AdminSummary;
                    DateTime noonTomorrow = DateTime.UtcNow.Date.AddDays(1).AddHours(12);
                    service.Clock = () => noonTomorrow;
                    Check.Equal(0, await service.RunDueAsync(ct).ConfigureAwait(false), "Before the send hour");
                    f.Services.Settings.AdminSummary.SendHour = 12;
                    Check.Equal(2, await service.RunDueAsync(ct).ConfigureAwait(false), "Two distinct valid recipients");
                    Check.Equal(0, await service.RunDueAsync(ct).ConfigureAwait(false), "Each recipient gets the day once");
                    List<EmailMessage> sent = f.Fakes.Email.To("summary-a@example.test", StaffAdminSummaryEmail.TemplateKey);
                    Check.Equal(1, sent.Count, "One message for recipient a");
                    Check.Equal(1, f.Fakes.Email.To("summary-b@example.test", StaffAdminSummaryEmail.TemplateKey).Count, "One message for recipient b");
                    string day = DateTime.UtcNow.Date.ToString("dddd, MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
                    Check.True(sent[0].Subject.Contains(day, StringComparison.Ordinal), "Subject names the summarized day");
                    Check.True(sent[0].TextBody.Contains("New sign-ups", StringComparison.Ordinal) && sent[0].TextBody.Contains("Daily active members", StringComparison.Ordinal), "Text body lists the default lines");
                    Check.True((sent[0].HtmlBody ?? String.Empty).Contains("Total members", StringComparison.Ordinal), "HTML body lists the default lines");
                    f.Services.Settings.AdminSummary.Recipients.Add("summary-c@example.test");
                    Check.Equal(1, await service.RunDueAsync(ct).ConfigureAwait(false), "A recipient added later still gets the day");
                    service.Clock = () => noonTomorrow.AddDays(1);
                    f.Services.Settings.AdminSummary.Enabled = false;
                    Check.Equal(0, await service.RunDueAsync(ct).ConfigureAwait(false), "Disabled sends nothing");
                }),
                SuiteCases.Http(Id, "Routes", "Report and send routes are administrator-only, validate input, and an explicit address always gets a copy", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/summary", member.AccessToken, null, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/summary/send", member.AccessToken, new { to = "x@example.test" }, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                    JsonElement report = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/summary", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(report.GetProperty("sections").GetArrayLength() > 0, "Sections returned");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/summary?date=2026-13-40", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/summary/send", HttpFixture.AdminToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/summary/send", HttpFixture.AdminToken, new { to = "nope" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    for (int i = 0; i < 2; i++)
                    {
                        JsonElement sent = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/summary/send", HttpFixture.AdminToken, new { to = "copy@example.test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(1, sent.GetProperty("queued").GetArrayLength(), "Explicit copy queued");
                    }
                    Check.Equal(2, f.Fakes.Email.To("copy@example.test", StaffAdminSummaryEmail.TemplateKey).Count, "Explicit copies are not deduplicated");
                })
            });
        }

        #endregion
    }
}
