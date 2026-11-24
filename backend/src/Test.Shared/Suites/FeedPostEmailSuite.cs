namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 2 new feed post fan-out and emails: eligible recipients, exclusions, List-Unsubscribe headers, one-click
    /// unsubscribe (POST and GET), invalid tokens, idempotency, interval skipping, and the feature switch.
    /// </summary>
    public static class FeedPostEmailSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "FeedPostEmail";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Feed post fan-out and email (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Fanout.Recipients", "Fan-out notifies and emails every eligible member and skips the excluded ones", async (f, ct) =>
                {
                    TestMember author = await NotificationTestKit.MemberAsync(f, "Example Owner", ct).ConfigureAwait(false);
                    TestMember verified = await Verified(f, "Verified", ct).ConfigureAwait(false);
                    TestMember unverified = await NotificationTestKit.MemberAsync(f, "Unverified", ct).ConfigureAwait(false);
                    TestMember blocker = await Verified(f, "Blocker", ct).ConfigureAwait(false);
                    TestMember blockedByAuthor = await Verified(f, "Blocked by author", ct).ConfigureAwait(false);
                    TestMember banned = await Verified(f, "Banned", ct).ConfigureAwait(false);
                    TestMember deleted = await Verified(f, "Deleted", ct).ConfigureAwait(false);
                    TestMember lapsed = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await NotificationTestKit.VerifyEmailAsync(f, lapsed.Id, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    await NotificationTestKit.VerifyEmailAsync(f, admin.Id, ct).ConfigureAwait(false);
                    TestMember noInApp = await Verified(f, "No in-app", ct).ConfigureAwait(false);
                    TestMember noEmail = await Verified(f, "No email", ct).ConfigureAwait(false);
                    await NotificationTestKit.BlockAsync(f, blocker.Id, author.Id, ct).ConfigureAwait(false);
                    await NotificationTestKit.BlockAsync(f, author.Id, blockedByAuthor.Id, ct).ConfigureAwait(false);
                    User bannedUser = (await f.Database.Users.ReadAsync(banned.Id, ct).ConfigureAwait(false))!;
                    bannedUser.IsBanned = true;
                    await f.Database.Users.UpdateAsync(bannedUser, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", deleted.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", noInApp.AccessToken, new { categories = new object[] { new { category = "FeedPosts", inApp = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", noEmail.AccessToken, new { categories = new object[] { new { category = "FeedPosts", email = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    string post = await NotificationTestKit.InsertPostAsync(f, author.Id, "Welcome to the community", ct).ConfigureAwait(false);
                    Check.True(await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, post, ct).ConfigureAwait(false), "Job queued");
                    Check.True(!await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, post, ct).ConfigureAwait(false), "Second call is idempotent");
                    NotificationFanoutWorker worker = NotificationTestKit.FanoutWorker(f);
                    await using (worker.ConfigureAwait(false))
                    {
                        Check.Equal(4, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Notifications created");
                    }
                    HashSet<string> notified = await Recipients(f, post, ct).ConfigureAwait(false);
                    foreach (TestMember m in new[] { verified, unverified, admin, noEmail }) Check.True(notified.Contains(m.Id), "Notified " + m.Email);
                    foreach (TestMember m in new[] { author, blocker, blockedByAuthor, banned, deleted, lapsed, noInApp }) Check.True(!notified.Contains(m.Id), "Not notified " + m.Email);

                    HashSet<string> emailed = new HashSet<string>(f.Fakes.Email.Messages.Where(m => m.Category == FeedPostEmailService.EmailCategory).Select(m => m.To), StringComparer.OrdinalIgnoreCase);
                    foreach (TestMember m in new[] { verified, admin, noInApp }) Check.True(emailed.Contains(m.Email), "Emailed " + m.Email);
                    foreach (TestMember m in new[] { author, unverified, blocker, blockedByAuthor, banned, lapsed, noEmail }) Check.True(!emailed.Contains(m.Email), "Not emailed " + m.Email);
                    Check.Equal(3, emailed.Count, "Three emails");

                    EmailMessage message = f.Fakes.Email.To(verified.Email, FeedPostEmailService.EmailCategory).Single();
                    Check.Equal("Welcome to the community", message.Subject, "Subject is the title");
                    Check.True(message.TextBody.Contains(f.Services.Property.PublicBaseUrl + "/app/feed/" + post, StringComparison.Ordinal), "Text links to the post");
                    Check.True(message.TextBody.Contains("Body text of the post", StringComparison.Ordinal), "Excerpt");
                    Check.True(message.HtmlBody!.Contains(f.Services.Property.Name, StringComparison.Ordinal), "Property name in HTML");
                    string primary = f.Property.Brand.Colors.Light.Primary;
                    if (!String.IsNullOrEmpty(primary)) Check.True(message.HtmlBody.Contains(primary, StringComparison.OrdinalIgnoreCase), "Brand color");
                    Check.True(message.Headers["List-Unsubscribe"].StartsWith("<" + f.Services.Property.PublicBaseUrl + "/api/v1/email/unsubscribe?token=", StringComparison.Ordinal), "List-Unsubscribe");
                    Check.Equal("List-Unsubscribe=One-Click", message.Headers["List-Unsubscribe-Post"], "List-Unsubscribe-Post");
                    Notification n = (await NotificationTestKit.ListAsync(f, verified.Id, ct).ConfigureAwait(false)).Single();
                    Check.Equal("Example Owner posted: Welcome to the community", n.Title, "In-app title");
                    Check.Equal("/feed/" + post, n.Path, "Path");
                    Check.Equal(PushStateEnum.Pending, n.PushState, "Push queued");
                    FanoutDone(await f.Database.NotificationFanouts.ReadAsync(NotificationTypeEnum.NewFeedPost, post, ct).ConfigureAwait(false));
                }),
                Case("Fanout.Idempotent", "Running a job twice creates no duplicate notifications or emails; posts that cannot fan out create nothing", async (f, ct) =>
                {
                    TestMember author = await NotificationTestKit.MemberAsync(f, "Example Owner", ct).ConfigureAwait(false);
                    TestMember member = await Verified(f, "Reader", ct).ConfigureAwait(false);
                    string post = await NotificationTestKit.InsertPostAsync(f, author.Id, null, ct).ConfigureAwait(false);
                    await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, post, ct).ConfigureAwait(false);
                    NotificationFanoutWorker worker = NotificationTestKit.FanoutWorker(f);
                    await using (worker.ConfigureAwait(false))
                    {
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        await f.Raw.ExecuteQueryAsync("UPDATE notificationfanouts SET status = 'Pending', lastuserid = NULL, completedutc = NULL;", false, ct).ConfigureAwait(false);
                        Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Rerun creates nothing");
                        string hidden = await NotificationTestKit.InsertPostAsync(f, author.Id, "Hidden", ct, true, "Hidden").ConfigureAwait(false);
                        string draft = await NotificationTestKit.InsertPostAsync(f, author.Id, "Draft", ct, false).ConfigureAwait(false);
                        string gone = await NotificationTestKit.InsertPostAsync(f, author.Id, "Gone", ct, true, "None", true).ConfigureAwait(false);
                        foreach (string p in new[] { hidden, draft, gone }) await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, p, ct).ConfigureAwait(false);
                        Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Unpublished, moderated, and deleted posts do not fan out");
                        FanoutDone(await f.Database.NotificationFanouts.ReadAsync(NotificationTypeEnum.NewFeedPost, hidden, ct).ConfigureAwait(false));
                    }
                    Check.Equal(1, (await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false)).Count, "One notification");
                    List<EmailMessage> emails = f.Fakes.Email.To(member.Email, FeedPostEmailService.EmailCategory);
                    Check.Equal(1, emails.Count, "One email");
                    Check.Equal("New post from Example Owner", emails[0].Subject, "Subject without a title");
                    Check.Equal("Example Owner posted", (await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false))[0].Title, "Title without a post title");
                }),
                Case("Fanout.Resume", "A job resumes after its recorded keyset position (lastuserid), so a crashed job does not restart", async (f, ct) =>
                {
                    TestMember author = await NotificationTestKit.MemberAsync(f, "Example Owner", ct).ConfigureAwait(false);
                    List<TestMember> members = new List<TestMember>();
                    for (int i = 0; i < 4; i++) members.Add(await NotificationTestKit.MemberAsync(f, "Member " + i, ct).ConfigureAwait(false));
                    List<string> ordered = members.Select(m => m.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
                    string post = await NotificationTestKit.InsertPostAsync(f, author.Id, "Resume", ct).ConfigureAwait(false);
                    await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, post, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE notificationfanouts SET lastuserid = '" + ordered[1] + "', status = 'Running';", false, ct).ConfigureAwait(false);
                    NotificationFanoutWorker worker = NotificationTestKit.FanoutWorker(f);
                    await using (worker.ConfigureAwait(false)) await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    HashSet<string> notified = await Recipients(f, post, ct).ConfigureAwait(false);
                    Check.True(!notified.Contains(ordered[0]) && !notified.Contains(ordered[1]), "Members before the position are not notified again");
                    Check.True(notified.Contains(ordered[2]) && notified.Contains(ordered[3]), "Members after the position are notified");
                    Check.True(!notified.Contains(author.Id), "Author excluded");
                    NotificationFanout? job = await f.Database.NotificationFanouts.ReadAsync(NotificationTypeEnum.NewFeedPost, post, ct).ConfigureAwait(false);
                    FanoutDone(job);
                    Check.True(job!.CreatedCount >= 2, "Created count recorded");
                }),
                Case("Unsubscribe.Token", "The unsubscribe token turns FeedPosts email off by POST and GET; bad tokens get 400", async (f, ct) =>
                {
                    TestMember author = await NotificationTestKit.MemberAsync(f, "Example Owner", ct).ConfigureAwait(false);
                    TestMember first = await Verified(f, "First", ct).ConfigureAwait(false);
                    TestMember second = await Verified(f, "Second", ct).ConfigureAwait(false);
                    string post = await NotificationTestKit.InsertPostAsync(f, author.Id, "Hello", ct).ConfigureAwait(false);
                    await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, post, ct).ConfigureAwait(false);
                    NotificationFanoutWorker worker = NotificationTestKit.FanoutWorker(f);
                    await using (worker.ConfigureAwait(false)) await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    string firstUrl = Url(f.Fakes.Email.To(first.Email, FeedPostEmailService.EmailCategory).Single());
                    string secondUrl = Url(f.Fakes.Email.To(second.Email, FeedPostEmailService.EmailCategory).Single());
                    JsonElement ok = await Api.ExpectAsync(f, HttpMethod.Post, firstUrl, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(ok.GetProperty("unsubscribed").GetBoolean(), "Unsubscribed");
                    Check.Equal("FeedPosts", Api.Str(ok, "category"), "Category");
                    Check.True(!await EmailOn(f, first, ct).ConfigureAwait(false), "POST turned email off");
                    Check.True(await EmailOn(f, second, ct).ConfigureAwait(false), "Other member unchanged");
                    ApiResult page = await Api.SendAsync(f, HttpMethod.Get, secondUrl, null, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, page.Status, "GET page");
                    Check.True((page.ContentType ?? String.Empty).StartsWith("text/html", StringComparison.Ordinal) && page.Text.Contains(f.Services.Property.Name, StringComparison.Ordinal), "HTML with the property name");
                    Check.True(!await EmailOn(f, second, ct).ConfigureAwait(false), "GET turned email off");
                    await Api.ExpectAsync(f, HttpMethod.Post, firstUrl, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/email/unsubscribe?token=garbage", null, null, HttpStatusCode.BadRequest, "InvalidToken", ct).ConfigureAwait(false);
                    string forged = f.Services.FeedPostEmails.CreateUnsubscribeToken(first.Id, NotificationCategoryEnum.FeedPosts);
                    string tampered = forged.Substring(0, forged.IndexOf('.') + 1) + "AAAA" + forged.Substring(forged.IndexOf('.') + 5);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/email/unsubscribe?token=" + Uri.EscapeDataString(tampered), null, null, HttpStatusCode.BadRequest, "InvalidToken", ct).ConfigureAwait(false);
                    string replies = f.Services.FeedPostEmails.CreateUnsubscribeToken(first.Id, NotificationCategoryEnum.Replies);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/email/unsubscribe?token=" + Uri.EscapeDataString(replies), null, null, HttpStatusCode.BadRequest, "InvalidToken", ct).ConfigureAwait(false);
                    ApiResult badPage = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/email/unsubscribe?token=garbage", null, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, badPage.Status, "Bad GET is 400");
                    Check.True((badPage.ContentType ?? String.Empty).StartsWith("text/html", StringComparison.Ordinal), "Bad GET is HTML");

                    string later = await NotificationTestKit.InsertPostAsync(f, author.Id, "Later", ct).ConfigureAwait(false);
                    await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, later, ct).ConfigureAwait(false);
                    NotificationFanoutWorker again = NotificationTestKit.FanoutWorker(f);
                    await using (again.ConfigureAwait(false)) await again.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Email.To(first.Email, FeedPostEmailService.EmailCategory).Count, "No email after unsubscribing");
                    Check.Equal(2, (await NotificationTestKit.ListAsync(f, first.Id, ct).ConfigureAwait(false)).Count, "In-app continues");

                    f.Settings.RateLimits.UnsubscribePerHour = 1;
                    TestMember third = await Verified(f, "Third", ct).ConfigureAwait(false);
                    string third1 = "/api/v1/email/unsubscribe?token=" + Uri.EscapeDataString(f.Services.FeedPostEmails.CreateUnsubscribeToken(third.Id, NotificationCategoryEnum.FeedPosts));
                    ApiResult limited = await Api.SendAsync(f, HttpMethod.Post, third1, null, null, ct).ConfigureAwait(false);
                    Check.Equal((HttpStatusCode)429, limited.Status, "Per-IP limit");
                }),
                Case("Email.IntervalAndSwitch", "email.feedPostMinIntervalMinutes skips posts in between; email.feedPostEmailsEnabled false stops all emails", async (f, ct) =>
                {
                    TestMember author = await NotificationTestKit.MemberAsync(f, "Example Owner", ct).ConfigureAwait(false);
                    TestMember member = await Verified(f, "Reader", ct).ConfigureAwait(false);
                    f.Settings.Email.FeedPostMinIntervalMinutes = 60;
                    NotificationFanoutWorker worker = NotificationTestKit.FanoutWorker(f);
                    await using (worker.ConfigureAwait(false))
                    {
                        string p1 = await NotificationTestKit.InsertPostAsync(f, author.Id, "One", ct).ConfigureAwait(false);
                        await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, p1, ct).ConfigureAwait(false);
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        string p2 = await NotificationTestKit.InsertPostAsync(f, author.Id, "Two", ct).ConfigureAwait(false);
                        await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, p2, ct).ConfigureAwait(false);
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        Check.Equal(1, f.Fakes.Email.To(member.Email, FeedPostEmailService.EmailCategory).Count, "Second post skipped by the interval");
                        EmailSend? skipped = await f.Database.EmailSends.ReadAsync(member.Id, NotificationCategoryEnum.FeedPosts, p2, ct).ConfigureAwait(false);
                        Check.Equal(EmailSendStatusEnum.Skipped, skipped?.Status ?? EmailSendStatusEnum.Error, "Recorded as Skipped");
                        Check.Equal(2, (await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false)).Count, "Both in-app");
                        worker.Clock = () => DateTime.UtcNow.AddMinutes(61);
                        string p3 = await NotificationTestKit.InsertPostAsync(f, author.Id, "Three", ct).ConfigureAwait(false);
                        await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, p3, ct).ConfigureAwait(false);
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        Check.Equal(2, f.Fakes.Email.To(member.Email, FeedPostEmailService.EmailCategory).Count, "After the interval emails resume");
                        f.Settings.Email.FeedPostEmailsEnabled = false;
                        worker.Clock = () => DateTime.UtcNow.AddMinutes(200);
                        string p4 = await NotificationTestKit.InsertPostAsync(f, author.Id, "Four", ct).ConfigureAwait(false);
                        await f.Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, p4, ct).ConfigureAwait(false);
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        Check.Equal(2, f.Fakes.Email.To(member.Email, FeedPostEmailService.EmailCategory).Count, "Switch off stops emails");
                        Check.Equal(4, (await NotificationTestKit.ListAsync(f, member.Id, ct).ConfigureAwait(false)).Count, "In-app continues with the switch off");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<TestMember> Verified(HttpFixture f, string name, CancellationToken ct)
        {
            TestMember member = await NotificationTestKit.MemberAsync(f, name, ct).ConfigureAwait(false);
            await NotificationTestKit.VerifyEmailAsync(f, member.Id, ct).ConfigureAwait(false);
            return member;
        }

        private static async Task<HashSet<string>> Recipients(HttpFixture f, string post, CancellationToken ct)
        {
            DataTable table = await f.Raw.ExecuteQueryAsync("SELECT recipientuserid FROM notifications WHERE targetid = '" + post + "';", false, ct).ConfigureAwait(false);
            return new HashSet<string>(table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "recipientuserid")), StringComparer.Ordinal);
        }

        private static string Url(EmailMessage message)
        {
            string header = message.Headers["List-Unsubscribe"].Trim('<', '>');
            Uri uri = new Uri(header);
            return uri.PathAndQuery;
        }

        private static async Task<bool> EmailOn(HttpFixture f, TestMember member, CancellationToken ct)
        {
            JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/notification-preferences", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return doc.GetProperty("objects").EnumerateArray().Single(c => Api.Str(c, "category") == "FeedPosts").GetProperty("email").GetBoolean();
        }

        private static void FanoutDone(NotificationFanout? job)
        {
            Check.True(job != null && job.Status == FanoutStatusEnum.Done && job.CompletedUtc != null, "Job done");
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
