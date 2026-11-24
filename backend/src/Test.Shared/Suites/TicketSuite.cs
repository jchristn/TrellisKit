namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Realtime;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 5 support tickets (docs/PHASE5_CONTRACT.md section 6, as-built note C): ticket types (seeding and SystemAdmin
    /// administration), the member workflow, per-property numbering under concurrency, internal notes hidden from members,
    /// due and auto close passes with a fixed clock, notifications and emails, the staff-only inbox search, export,
    /// metrics, deletion rules, rate limits, role boundaries, every access model, the feature flag, and the kill switch.
    /// </summary>
    public static class TicketSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Tickets";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Support tickets (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Types.SeedAndAdmin", "The engine default types are seeded once; members list enabled types; SystemAdmin creates, edits, reorders, and retires types; SupportAgent and members cannot administer types", TypesAsync),
                SuiteCases.Http(Id, "Types.PropertySeed", "property.json support.ticketTypes seeds the property's types (with staff recipients) instead of the engine defaults", p =>
                {
                    p.Support = new PropertySupport { TicketTypes = new List<PropertyTicketTypeSeed> { new PropertyTicketTypeSeed { Key = "Access", Label = "Account access", HelpText = "Tell us what you see.", StaffRecipients = new List<string> { "help@example.test" } } } };
                }, PropertySeedAsync),
                Case("Tickets.Workflow", "A never-paid account in FullyGated opens a ticket with diagnostics, reads and lists it; replies move statuses; close, reopen, and the reopen window; validation, the word filter, and other members get 404", WorkflowAsync),
                Case("Tickets.NotesHidden", "Internal notes and system lines are staff only: never in member payloads, enumerations, or email; staff see the membership snapshot and the current membership", NotesAsync),
                Case("Tickets.MessageHistoryPaging", "The staff message history pages past 20 with continuation tokens in CreatedAscending and CreatedDescending order, without gaps or repeats", MessageHistoryPagingAsync),
                Case("Tickets.Numbering", "Concurrent ticket creation assigns contiguous per-property numbers without duplicates", NumberingAsync),
                Case("Tickets.DueAndAutoClose", "With a fixed clock the due pass marks an unanswered ticket overdue once and notifies support staff; the auto close pass closes Resolved tickets without a member reply", DueAsync),
                Case("Tickets.Notifications", "TicketOpened to support staff, TicketUpdate grouped per ticket for the requester, TicketMemberReplied to the assignee, the five emails, ticket.updated on the realtime bus, and the staff-only Support preference category", NotificationsAsync),
                Case("Tickets.Inbox", "Staff inbox filters and search (number, title, requester email), the metadata CSV export, metrics, assignment rules, audit, and deletion of Closed tickets only", InboxAsync),
                Case("Tickets.RateLimits", "support.ticketsPerDay and support.repliesPerHour answer 429 with Retry-After; support.maxOpenTicketsPerMember answers 409 TicketLimitReached", LimitsAsync),
                Case("Tickets.Roles", "SupportAgent reaches the inbox, replies, updates, and metrics but no other admin page; Communicator and members cannot use staff ticket routes", RolesAsync),
                Case("Tickets.AccessModels", "Support is reachable by never-paid and lapsed accounts in FullyGated, and by signed-in accounts in PartiallyGated and Open", AccessModelsAsync),
                SuiteCases.Http(Id, "Tickets.FeatureOff", "features.support off answers 404 NotAvailable on member and staff support routes", p => p.Features.Support = false, FeatureOffAsync),
                Case("Tickets.KillSwitch", "The support kill switch answers 503 FeatureDisabled on member support routes", KillSwitchAsync),
                SuiteCases.Http(Id, "Tickets.EmailLinks", "Email links map member app paths to website pages under /app and ticket staff paths open the admin inbox drawer", EmailLinksAsync),
                Case("Tickets.Lifecycle", "The account export includes tickets with replies and no notes; account deletion leaves tickets as they are with the requester shown as Deleted member", LifecycleAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                SupportTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task TypesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            JsonElement types = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/support/ticket-types/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> keys = types.GetProperty("objects").EnumerateArray().Select(t => Api.Str(t, "key")).ToList();
            Check.True(keys.SequenceEqual(new[] { "Support", "Bug", "Enhancement", "Billing" }), "Engine default types in order (" + String.Join(",", keys) + ")");
            Check.True(types.GetProperty("objects")[0].GetProperty("staffRecipients").ValueKind == JsonValueKind.Null, "Members never see staff recipients");
            await f.Services.Support.Types.EnsureSeededAsync(ct).ConfigureAwait(false);
            Check.Equal(4L, await SupportTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM tickettypes;", ct).ConfigureAwait(false), "Seeding is idempotent");

            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/support/ticket-types", HttpFixture.AdminToken,
                new { key = "Account", label = "Account access", helpText = "Include the email you signed up with.", staffRecipients = new[] { "help@example.test" }, labels = new[] { "triage" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string typeId = Api.Str(created, "id");
            Check.Equal("help@example.test", created.GetProperty("staffRecipients")[0].GetString(), "SystemAdmin sees recipients");
            Check.Equal("triage", created.GetProperty("labels")[0].GetString(), "Type labels");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/support/ticket-types", HttpFixture.AdminToken, new { key = "account", label = "Duplicate" }, HttpStatusCode.Conflict, "KeyInUse", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/support/ticket-types", HttpFixture.AdminToken, new { key = "bad key!", label = "Nope" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/support/ticket-types", HttpFixture.AdminToken, new { key = "Mail", label = "Mail", staffRecipients = new[] { "not-an-email" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/support/ticket-types/" + typeId, HttpFixture.AdminToken, new { label = "Signing in" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Signing in", Api.Str(edited, "label"), "Edited label");
            Check.Equal("Account", Api.Str(edited, "key"), "Omitted fields stay");
            JsonElement order = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/support/ticket-types/order", HttpFixture.AdminToken, new { ticketTypeIds = new[] { typeId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(typeId, Api.Str(order.GetProperty("objects")[0], "id"), "Reordered first");
            Check.Equal(5, order.GetProperty("objects").GetArrayLength(), "Every type listed after the order");
            JsonElement memberList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/support/ticket-types/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Account", Api.Str(memberList.GetProperty("objects")[0], "key"), "Members see the new order");

            string ticketId = Api.Str(await SupportTestKit.TicketAsync(f, member.AccessToken, ct, "Cannot sign in at all", "The sign in page keeps spinning forever.", "Account").ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/support/ticket-types/" + typeId, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/support/ticket-types/" + typeId, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement afterRetire = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/support/ticket-types/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!afterRetire.GetProperty("objects").EnumerateArray().Any(t => Api.Str(t, "id") == typeId), "Retired types leave the member list");
            JsonElement adminList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/support/ticket-types/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(adminList.GetProperty("objects").EnumerateArray().Any(t => Api.Str(t, "id") == typeId && t.GetProperty("isRetired").GetBoolean()), "Admins still see retired types");
            JsonElement kept = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + ticketId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Account", Api.Str(kept.GetProperty("type"), "key"), "Existing tickets keep a retired type");
            ApiResult retiredCreate = await SupportTestKit.CreateAsync(f, member.AccessToken, new { type = "Account", title = "Another one here", body = "Still cannot sign in today." }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, retiredCreate.Status, "No new tickets of a retired type");

            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/support/ticket-types", agent.AccessToken, new { key = "Agent", label = "Agent" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/support/ticket-types/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            Check.True(await ModerationTestKit.AuditCountAsync(f, "ticket_type.create", typeId, ct).ConfigureAwait(false) >= 1, "ticket_type.create audited");
        }

        private static async Task PropertySeedAsync(HttpFixture f, CancellationToken ct)
        {
            SupportTestKit.Relax(f);
            TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement types = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/support/ticket-types/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, types.GetProperty("objects").GetArrayLength(), "Only the configured type");
            Check.Equal("Account access", Api.Str(types.GetProperty("objects")[0], "label"), "Configured label");
            Check.Equal("Tell us what you see.", Api.Str(types.GetProperty("objects")[0], "helpText"), "Configured help text");
            await SupportTestKit.TicketAsync(f, member.AccessToken, ct, "Locked out of my account", "I cannot get past the sign in page.", "Access").ConfigureAwait(false);
            Check.Equal(1L, await SupportTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM emailmessages WHERE template = 'staff_ticket_opened' AND recipient = 'help@example.test';", ct).ConfigureAwait(false), "staff_ticket_opened to the type's recipient");
        }

        private static async Task WorkflowAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.RegisterAsync(f, ct, null, "Never Paid").ConfigureAwait(false);
            TestMember other = await Api.RegisterAsync(f, ct, null, "Someone Else").ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "X-Client-Version", "1.4.0" }, { "X-Client-Platform", "Ios" }, { "X-Client-Os-Version", "18.1" }, { "X-Client-Device-Model", "iPhone15,2" }, { "X-Client-Update-Id", "upd_1" },
                { "Accept-Language", "en-US,en;q=0.9" }, { "X-Client-Time-Zone", "America/Los_Angeles" }
            };
            ApiResult created = await SupportTestKit.CreateAsync(f, member.AccessToken, new { type = "billing", title = "  Charged twice  ", body = "I was charged **twice** this month.", includeDiagnostics = true, labels = new[] { "mine" } }, ct, headers).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, created.Status, "A never-paid account opens a ticket in FullyGated (" + created.Text + ")");
            JsonElement ticket = created.Body;
            string id = Api.Str(ticket, "id");
            Check.True(id.StartsWith("tkt_", StringComparison.Ordinal), "tkt_ id");
            Check.Equal(1L, ticket.GetProperty("number").GetInt64(), "First number");
            Check.Equal("Billing", Api.Str(ticket.GetProperty("type"), "key"), "Type key matched case-insensitively");
            Check.Equal("Charged twice", Api.Str(ticket, "title"), "Title normalized");
            Check.Equal("Open", Api.Str(ticket, "status"), "Open");
            Check.Equal("Normal", Api.Str(ticket, "priority"), "Normal priority");
            JsonElement device = ticket.GetProperty("device");
            Check.Equal("mobile", Api.Str(device, "client"), "X-Client");
            Check.Equal("1.4.0", Api.Str(device, "clientVersion"), "X-Client-Version");
            Check.Equal("Ios", Api.Str(device, "platform"), "X-Client-Platform");
            Check.Equal("iPhone15,2", Api.Str(device, "deviceModel"), "X-Client-Device-Model");
            Check.Equal("en-US", Api.Str(device, "locale"), "Locale from Accept-Language");
            Check.Equal("America/Los_Angeles", Api.Str(device, "timeZone"), "Time zone");
            Check.True(ticket.GetProperty("membership").ValueKind == JsonValueKind.Null, "Members never see the membership snapshot");
            Check.True(ticket.GetProperty("firstResponseDueUtc").GetDateTime() > DateTime.UtcNow.AddHours(23), "First response due from support.firstResponseTargetHours");
            Check.Equal(1, ticket.GetProperty("messages").GetProperty("objects").GetArrayLength(), "The opening message");
            Check.Equal("mine", ticket.GetProperty("labels")[0].GetString(), "Labels on create");
            Check.True(ticket.GetProperty("viewer").GetProperty("canReply").GetBoolean() && ticket.GetProperty("viewer").GetProperty("canNote").ValueKind == JsonValueKind.Null, "Member viewer flags");

            ApiResult noDiagnostics = await SupportTestKit.CreateAsync(f, member.AccessToken, new { type = "Bug", title = "A button is broken", body = "The save button does nothing." }, ct, headers).ConfigureAwait(false);
            Check.True(noDiagnostics.Body.GetProperty("device").ValueKind == JsonValueKind.Null, "No device block without includeDiagnostics");

            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages", other.AccessToken, new { body = "Not mine" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement own = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, own.GetProperty("objects").GetArrayLength(), "Own tickets");
            Check.True(own.GetProperty("objects")[0].GetProperty("messages").ValueKind == JsonValueKind.Null, "Summaries carry no messages");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/enumerate", member.AccessToken, new { filters = new { type = "Bug" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, filtered.GetProperty("objects").GetArrayLength(), "Type filter");
            JsonElement labelled = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/enumerate", member.AccessToken, new { labels = new[] { "mine" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(id, SupportTestKit.Ids(labelled).Single(), "Label filter");
            JsonElement othersList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, othersList.GetProperty("objects").GetArrayLength(), "Nobody else's tickets");

            await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, id, "We are looking into it.", ct).ConfigureAwait(false);
            JsonElement waiting = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("WaitingOnMember", Api.Str(waiting, "status"), "A staff reply sets WaitingOnMember");
            Check.True(waiting.GetProperty("firstResponseUtc").ValueKind != JsonValueKind.Null, "First response recorded");
            JsonElement staffMessage = waiting.GetProperty("messages").GetProperty("objects")[1];
            Check.Equal("Staff", Api.Str(staffMessage, "authorRole"), "Staff author role");
            Check.True(Api.Str(staffMessage, "authorName").Contains("Support", StringComparison.Ordinal), "Team name on staff replies (" + Api.Str(staffMessage, "authorName") + ")");
            JsonElement reply = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages", member.AccessToken, new { body = "Here is the receipt number 4411." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.True(Api.Str(reply, "id").StartsWith("tkm_", StringComparison.Ordinal), "tkm_ id");
            JsonElement reopened = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Open", Api.Str(reopened, "status"), "A member reply on WaitingOnMember moves to Open");
            JsonElement replies = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, replies.GetProperty("objects").GetArrayLength(), "Three replies, oldest first");
            Check.Equal("Here is the receipt number 4411.", Api.Str(replies.GetProperty("objects")[2], "body"), "CreatedAscending");

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + id, agent.AccessToken, new { status = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement resolved = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Resolved", Api.Str(resolved, "status"), "Resolved");
            Check.True(resolved.GetProperty("viewer").GetProperty("canReopen").GetBoolean(), "canReopen on Resolved");
            JsonElement reopen = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/reopen", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Open", Api.Str(reopen, "status"), "Reopened");
            JsonElement closed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/close", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Closed", Api.Str(closed, "status"), "Closed by the requester");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/close", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages", member.AccessToken, new { body = "One more thing within the window." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            JsonElement windowReply = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Open", Api.Str(windowReply, "status"), "A reply within support.reopenDays reopens");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/close", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            f.Services.Support.Clock = () => DateTime.UtcNow.AddDays(f.Settings.Support.ReopenDays + 1);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages", member.AccessToken, new { body = "Too late now." }, HttpStatusCode.Conflict, "TicketClosed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/reopen", member.AccessToken, null, HttpStatusCode.Conflict, "TicketClosed", ct).ConfigureAwait(false);
            JsonElement late = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!late.GetProperty("viewer").GetProperty("canReply").GetBoolean() && !late.GetProperty("viewer").GetProperty("canReopen").GetBoolean(), "Viewer flags after the window");
            f.Services.Support.Clock = () => DateTime.UtcNow;

            ApiResult invalid = await SupportTestKit.CreateAsync(f, member.AccessToken, new { type = "Nope", title = "Hi", body = "short" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, invalid.Status, "Validation");
            Check.True(invalid.Body.GetProperty("fieldErrors").TryGetProperty("type", out JsonElement _) && invalid.Body.GetProperty("fieldErrors").TryGetProperty("title", out JsonElement _) && invalid.Body.GetProperty("fieldErrors").TryGetProperty("body", out JsonElement _), "type, title, and body errors");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "forbiddenword", matchType = "Word", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "flaggedword", matchType = "Word", action = "Flag" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets", member.AccessToken, new { type = "Bug", title = "Problem here", body = "This has a forbiddenword inside." }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
            await SupportTestKit.TicketAsync(f, member.AccessToken, ct, "Flag terms are fine", "This has a flaggedword inside it.", "Bug").ConfigureAwait(false);
            Check.Equal(0L, await SupportTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targettype IN ('Ticket', 'TicketMessage');", ct).ConfigureAwait(false), "Flag terms never report tickets");
        }

        private static async Task NotesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            string id = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            long repliesBefore = await SupportTestKit.EmailsAsync(f, "ticket_reply", member.Id, ct).ConfigureAwait(false);
            JsonElement note = await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, id, "SECRET-NOTE refund approved by finance", ct, "Note").ConfigureAwait(false);
            Check.Equal("Note", Api.Str(note, "kind"), "Note kind");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + id, agent.AccessToken, new { priority = "High" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult memberView = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, member.AccessToken, null, ct).ConfigureAwait(false);
            Check.True(!memberView.Text.Contains("SECRET-NOTE", StringComparison.Ordinal), "Notes never in the member ticket");
            Check.True(!memberView.Text.Contains("changed the priority", StringComparison.Ordinal), "System lines never in the member ticket");
            Check.Equal("High", Api.Str(memberView.Body, "priority"), "Members see the priority read-only");
            Check.Equal("Open", Api.Str(memberView.Body, "status"), "A note changes nothing");
            ApiResult memberMessages = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages/enumerate", member.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.True(!memberMessages.Text.Contains("SECRET-NOTE", StringComparison.Ordinal) && memberMessages.Body.GetProperty("objects").GetArrayLength() == 1, "Notes never in the member enumeration");
            ApiResult kindFilter = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages/enumerate", member.AccessToken, new { filters = new { kind = "Note" } }, ct).ConfigureAwait(false);
            Check.True(!kindFilter.Text.Contains("SECRET-NOTE", StringComparison.Ordinal), "A kind filter cannot reveal notes to members");
            Check.Equal(repliesBefore, await SupportTestKit.EmailsAsync(f, "ticket_reply", member.Id, ct).ConfigureAwait(false), "Notes are never emailed");
            Check.Equal(0, (await ForumTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.TicketUpdate, ct).ConfigureAwait(false)).Count, "Notes never notify the requester");

            JsonElement staffView = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/tickets/" + id, agent.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> kinds = staffView.GetProperty("messages").GetProperty("objects").EnumerateArray().Select(m => Api.Str(m, "kind")).ToList();
            Check.True(kinds.Contains("Note") && kinds.Contains("System") && kinds.Contains("Reply"), "Staff see replies, notes, and system lines (" + String.Join(",", kinds) + ")");
            Check.True(staffView.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Membership snapshot at creation");
            Check.True(staffView.GetProperty("currentMembership").GetProperty("isActive").GetBoolean(), "Current membership");
            Check.True(staffView.GetProperty("viewer").GetProperty("canNote").GetBoolean(), "Staff viewer flags");
            JsonElement notesOnly = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + id + "/messages/enumerate", agent.AccessToken, new { filters = new { kind = "Note" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, notesOnly.GetProperty("objects").GetArrayLength(), "Staff kind filter");
        }

        private static async Task MessageHistoryPagingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            string id = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            List<string> written = new List<string>();
            for (int i = 0; i < 25; i++) written.Add(Api.Str(await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, id, "Note number " + i, ct, "Note").ConfigureAwait(false), "id"));
            string path = "/api/v1/admin/tickets/" + id + "/messages/enumerate";
            foreach (string ordering in new[] { "CreatedAscending", "CreatedDescending" })
            {
                List<string> seen = new List<string>();
                List<DateTime> times = new List<DateTime>();
                string? token = null;
                int pages = 0;
                do
                {
                    JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, path, agent.AccessToken, new { maxResults = 10, ordering, continuationToken = token, filters = new { kind = "Note" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    seen.AddRange(page.GetProperty("objects").EnumerateArray().Select(m => Api.Str(m, "id")));
                    times.AddRange(page.GetProperty("objects").EnumerateArray().Select(m => m.GetProperty("createdUtc").GetDateTime()));
                    token = page.GetProperty("endOfResults").GetBoolean() ? null : Api.Str(page, "continuationToken");
                    pages++;
                }
                while (token != null && pages < 10);
                Check.Equal(3, pages, ordering + ": three pages of at most 10");
                Check.Equal(25, seen.Distinct().Count(), ordering + ": every note once");
                Check.True(seen.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(written.OrderBy(x => x, StringComparer.Ordinal)), ordering + ": exactly the written notes");
                bool ordered = true;
                for (int i = 1; i < times.Count; i++) ordered &= ordering == "CreatedAscending" ? times[i] >= times[i - 1] : times[i] <= times[i - 1];
                Check.True(ordered, ordering + ": in order");
            }
            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, path, agent.AccessToken, new { maxResults = 10 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!all.GetProperty("endOfResults").GetBoolean() && all.GetProperty("continuationToken").ValueKind == JsonValueKind.String, "The unfiltered history continues past the first page");
        }

        private static async Task NumberingAsync(HttpFixture f, CancellationToken ct)
        {
            List<TestMember> members = new List<TestMember>();
            for (int i = 0; i < 4; i++) members.Add(await Api.RegisterAsync(f, ct, null, "Member " + i).ConfigureAwait(false));
            List<Task<ApiResult>> creates = new List<Task<ApiResult>>();
            for (int i = 0; i < 16; i++)
            {
                TestMember m = members[i % members.Count];
                creates.Add(SupportTestKit.CreateAsync(f, m.AccessToken, new { type = "Support", title = "Concurrent ticket " + i, body = "Created at the same time as others." }, ct));
            }
            ApiResult[] results = await Task.WhenAll(creates).ConfigureAwait(false);
            Check.True(results.All(r => r.Status == HttpStatusCode.Created), "Every concurrent create succeeded: " + String.Join(",", results.Select(r => (int)r.Status)));
            List<long> numbers = results.Select(r => r.Body.GetProperty("number").GetInt64()).OrderBy(n => n).ToList();
            Check.True(numbers.SequenceEqual(Enumerable.Range(1, 16).Select(n => (long)n)), "Contiguous numbers 1 to 16 (" + String.Join(",", numbers) + ")");
            Check.Equal(16L, await SupportTestKit.CountAsync(f, "SELECT nextnumber AS cnt FROM ticketcounters;", ct).ConfigureAwait(false), "Counter matches");
        }

        private static async Task DueAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            string id = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            string answered = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "Answered quickly here").ConfigureAwait(false);
            await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, answered, "Answered.", ct).ConfigureAwait(false);
            DateTime start = DateTime.UtcNow;
            Check.Equal(0, await f.Services.Support.Workflow.RunDueAsync(start.AddHours(1), ct).ConfigureAwait(false), "Nothing due before the target");
            DateTime late = start.AddHours(f.Settings.Support.FirstResponseTargetHours + 1);
            Check.Equal(1, await f.Services.Support.Workflow.RunDueAsync(late, ct).ConfigureAwait(false), "One ticket overdue (the answered one is not)");
            Check.Equal(0, await f.Services.Support.Workflow.RunDueAsync(late.AddHours(1), ct).ConfigureAwait(false), "Overdue is sent once");
            List<Notification> overdue = await ForumTestKit.NotificationsAsync(f, agent.Id, NotificationTypeEnum.TicketOverdue, ct).ConfigureAwait(false);
            Check.Equal(1, overdue.Count, "TicketOverdue to every support agent when unassigned");
            Check.Equal(NotificationCategoryEnum.Support, overdue[0].Category, "Support category");
            Check.Equal(1L, await SupportTestKit.EmailsAsync(f, "staff_ticket_overdue", agent.Id, ct).ConfigureAwait(false), "staff_ticket_overdue to the agent");
            f.Services.Support.Clock = () => late;
            JsonElement staffList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/enumerate", agent.AccessToken, new { filters = new { overdue = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(id, SupportTestKit.Ids(staffList).Single(), "Overdue filter with the fixed clock");
            Check.True(staffList.GetProperty("objects")[0].GetProperty("isOverdue").GetBoolean(), "isOverdue");
            f.Services.Support.Clock = () => DateTime.UtcNow;

            TestMember second = await SupportTestKit.AgentAsync(f, ct, "Assigned Agent").ConfigureAwait(false);
            string assigned = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "Assigned and overdue").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + assigned, agent.AccessToken, new { assigneeUserId = second.Id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, await f.Services.Support.Workflow.RunDueAsync(DateTime.UtcNow.AddHours(f.Settings.Support.FirstResponseTargetHours + 2), ct).ConfigureAwait(false), "The assigned ticket becomes overdue");
            Check.Equal(1, (await ForumTestKit.NotificationsAsync(f, second.Id, NotificationTypeEnum.TicketOverdue, ct).ConfigureAwait(false)).Count, "TicketOverdue to the assignee");
            Check.Equal(1, (await ForumTestKit.NotificationsAsync(f, agent.Id, NotificationTypeEnum.TicketOverdue, ct).ConfigureAwait(false)).Count, "Not to the other agents");

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + answered, agent.AccessToken, new { status = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            Check.Equal(0, await f.Services.Support.Workflow.RunAutoCloseAsync(now.AddDays(f.Settings.Support.AutoCloseDays - 1), ct).ConfigureAwait(false), "Not before support.autoCloseDays");
            Check.Equal(1, await f.Services.Support.Workflow.RunAutoCloseAsync(now.AddDays(f.Settings.Support.AutoCloseDays + 1), ct).ConfigureAwait(false), "Closed after support.autoCloseDays");
            JsonElement closed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + answered, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Closed", Api.Str(closed, "status"), "Auto closed");
            Check.True(await SupportTestKit.EmailsAsync(f, "ticket_resolved", member.Id, ct).ConfigureAwait(false) >= 2, "ticket_resolved for Resolved and for the auto close");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + id, agent.AccessToken, new { status = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages", member.AccessToken, new { body = "Wait, still broken." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(0, await f.Services.Support.Workflow.RunAutoCloseAsync(now.AddDays(f.Settings.Support.AutoCloseDays + 2), ct).ConfigureAwait(false), "A member reply keeps the ticket open");
        }

        private static async Task NotificationsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            ConcurrentQueue<RealtimeEvent> events = new ConcurrentQueue<RealtimeEvent>();
            Func<RealtimeEvent, Task> handler = e =>
            {
                if (e.Kind == RealtimeEventKinds.TicketUpdated) events.Enqueue(e);
                return Task.CompletedTask;
            };
            f.Services.RealtimeBus.EventReceived += handler;
            try
            {
                string id = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                Check.Equal(1L, await SupportTestKit.EmailsAsync(f, "ticket_created", member.Id, ct).ConfigureAwait(false), "ticket_created to the requester");
                List<Notification> opened = await ForumTestKit.NotificationsAsync(f, agent.Id, NotificationTypeEnum.TicketOpened, ct).ConfigureAwait(false);
                Check.Equal(1, opened.Count, "TicketOpened to support staff");
                Check.True(opened[0].Title.Contains("#1", StringComparison.Ordinal), "Title names the ticket number (" + opened[0].Title + ")");
                Check.Equal("/admin/support?ticket=" + id, opened[0].Path, "Admin path (the inbox with the ticket drawer open)");
                Check.Equal(0, (await ForumTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.TicketOpened, ct).ConfigureAwait(false)).Count, "Members do not get staff notifications");

                await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, id, "First reply with **details**.", ct).ConfigureAwait(false);
                await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, id, "Second reply.", ct).ConfigureAwait(false);
                List<Notification> updates = await ForumTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.TicketUpdate, ct).ConfigureAwait(false);
                Check.Equal(1, updates.Count, "TicketUpdate grouped per ticket while unread");
                Check.Equal(NotificationCategoryEnum.Account, updates[0].Category, "Account category");
                Check.Equal("/support/tickets/" + id, updates[0].Path, "Member path");
                Check.Equal(2L, await SupportTestKit.EmailsAsync(f, "ticket_reply", member.Id, ct).ConfigureAwait(false), "ticket_reply per staff reply");
                for (int i = 0; i < 50 && events.Count < 2; i++) await Task.Delay(40, ct).ConfigureAwait(false);
                Check.True(events.Count >= 2, "ticket.updated published after staff replies (" + events.Count + ")");
                Check.True(events.All(e => e.TargetId == id && e.UserIds.SequenceEqual(new[] { member.Id }) && e.State == "WaitingOnMember" && e.PropertyId == f.Property.Id), "Event carries the ticket, the requester, and the status");

                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + id, agent.AccessToken, new { assigneeUserId = agent.Id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages", member.AccessToken, new { body = "Thanks, one question." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                Check.Equal(1, (await ForumTestKit.NotificationsAsync(f, agent.Id, NotificationTypeEnum.TicketMemberReplied, ct).ConfigureAwait(false)).Count, "TicketMemberReplied to the assignee");
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + id, agent.AccessToken, new { status = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1L, await SupportTestKit.EmailsAsync(f, "ticket_resolved", member.Id, ct).ConfigureAwait(false), "ticket_resolved on Resolved");
                Check.Equal(1, (await ForumTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.TicketUpdate, ct).ConfigureAwait(false)).Count, "Still one unread row per ticket");

                JsonElement memberPrefs = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/notification-preferences", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(!memberPrefs.GetProperty("objects").EnumerateArray().Any(c => Api.Str(c, "category") == "Support"), "Members never see the Support category");
                await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", member.AccessToken, new { categories = new object[] { new { category = "Support", email = false } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                JsonElement agentPrefs = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/notification-preferences", agent.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                JsonElement support = agentPrefs.GetProperty("objects").EnumerateArray().First(c => Api.Str(c, "category") == "Support");
                Check.True(support.GetProperty("inApp").GetBoolean() && support.GetProperty("push").GetBoolean() && support.GetProperty("email").GetBoolean(), "Support defaults on");
                Check.True(support.GetProperty("editable").GetProperty("email").GetBoolean(), "Support email is editable");
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", agent.AccessToken, new { categories = new object[] { new { category = "Support", inApp = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "Second ticket here").ConfigureAwait(false);
                Check.Equal(1, (await ForumTestKit.NotificationsAsync(f, agent.Id, NotificationTypeEnum.TicketOpened, ct).ConfigureAwait(false)).Count, "Support in-app off stops staff notifications");
            }
            finally
            {
                f.Services.RealtimeBus.EventReceived -= handler;
            }
        }

        private static async Task InboxAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember alice = await Api.RegisterAsync(f, ct, Api.UniqueEmail("alice.inbox"), "Alice Inbox").ConfigureAwait(false);
            TestMember bob = await Api.RegisterAsync(f, ct, null, "Bob Requester").ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            string a1 = Api.Str(await SupportTestKit.TicketAsync(f, alice.AccessToken, ct, "Payment page error", "The payment page shows ZEBRA-CODE 77.", "Billing").ConfigureAwait(false), "id");
            string b1 = Api.Str(await SupportTestKit.TicketAsync(f, bob.AccessToken, ct, "Video will not play", "Videos stay black on my tablet.", "Bug").ConfigureAwait(false), "id");
            string b2 = Api.Str(await SupportTestKit.TicketAsync(f, bob.AccessToken, ct, "Feature idea for later", "Please add a dark theme option.", "Enhancement").ConfigureAwait(false), "id");

            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/enumerate", agent.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, all.GetProperty("objects").GetArrayLength(), "Inbox lists every open ticket");
            Check.True(all.GetProperty("objects")[0].GetProperty("membership").ValueKind == JsonValueKind.Object, "AdminTicketSummary carries the membership snapshot");
            Check.Equal(3L, all.GetProperty("totalRecords").GetInt64(), "Exact totals");
            async Task<List<string>> Search(object body)
            {
                JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/enumerate", agent.AccessToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
                return SupportTestKit.Ids(page);
            }
            Check.Equal(a1, (await Search(new { search = "zebra-code" }).ConfigureAwait(false)).Single(), "Search covers the body");
            Check.Equal(b1, (await Search(new { search = "will not play" }).ConfigureAwait(false)).Single(), "Search covers the title");
            Check.Equal(a1, (await Search(new { search = alice.Email.Substring(0, 12) }).ConfigureAwait(false)).Single(), "Search covers the requester email");
            Check.Equal(2, (await Search(new { search = "Bob Requester" }).ConfigureAwait(false)).Count, "Search covers the requester name");
            JsonElement b2Ticket = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/tickets/" + b2, agent.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(b2, (await Search(new { search = b2Ticket.GetProperty("number").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) }).ConfigureAwait(false)).First(), "Search covers the number");
            Check.Equal(b1, (await Search(new { filters = new { type = "Bug" } }).ConfigureAwait(false)).Single(), "Type filter");
            Check.Equal(2, (await Search(new { filters = new { requesterUserId = bob.Id } }).ConfigureAwait(false)).Count, "Requester filter");
            TestMember searcher = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            JsonElement globalSearch = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", searcher.AccessToken, new { search = "zebra" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!globalSearch.ToString().Contains(a1, StringComparison.Ordinal), "Tickets are never in global search");

            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + b1, agent.AccessToken, new { assigneeUserId = bob.Id }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement assigned = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + b1, agent.AccessToken, new { assigneeUserId = agent.Id, priority = "Urgent", labels = new[] { "staff:escalated", "video" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(agent.Id, Api.Str(assigned.GetProperty("assignee"), "id"), "Assigned to a SupportAgent");
            Check.True(assigned.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "staff:escalated"), "Staff labels for staff");
            JsonElement memberSees = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + b1, bob.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!memberSees.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "staff:escalated"), "Members never see staff labels");
            Check.Equal(b1, (await Search(new { filters = new { assigneeUserId = agent.Id } }).ConfigureAwait(false)).Single(), "Assignee filter");
            Check.Equal(2, (await Search(new { filters = new { unassigned = true } }).ConfigureAwait(false)).Count, "Unassigned filter");
            Check.Equal(b1, (await Search(new { filters = new { priority = "Urgent" } }).ConfigureAwait(false)).Single(), "Priority filter");
            Check.Equal(b1, (await Search(new { ordering = "PriorityDescending" }).ConfigureAwait(false)).First(), "PriorityDescending");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "ticket.update", b1, ct).ConfigureAwait(false) >= 1, "ticket.update audited");

            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/tickets/" + a1, agent.AccessToken, null, HttpStatusCode.Conflict, "TicketNotClosed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + a1, agent.AccessToken, new { status = "Closed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, (await Search(new { }).ConfigureAwait(false)).Count, "The inbox excludes Closed by default");
            Check.Equal(a1, (await Search(new { filters = new { status = "Closed" } }).ConfigureAwait(false)).Single(), "Status filter");
            Check.Equal(3, (await Search(new { filters = new { status = new[] { "Open", "Closed" } } }).ConfigureAwait(false)).Count, "Status list filter");

            ApiResult csv = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/admin/tickets/export.csv?status=Open", agent.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, csv.Status, "CSV export (" + csv.Text + ")");
            Check.True(csv.Text.StartsWith("id,number,type,status,priority,title", StringComparison.Ordinal), "CSV header");
            Check.True(csv.Text.Contains(b1, StringComparison.Ordinal) && !csv.Text.Contains(a1, StringComparison.Ordinal), "CSV bounded by the filters");
            Check.True(!csv.Text.Contains("dark theme", StringComparison.Ordinal) && !csv.Text.Contains("ZEBRA", StringComparison.Ordinal), "CSV carries no bodies");

            JsonElement metrics = await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/metrics/support?days=7", agent.AccessToken, ct).ConfigureAwait(false);
            Check.Equal(7, metrics.GetProperty("openedByDay").GetArrayLength(), "One entry per day");
            Check.Equal(3L, metrics.GetProperty("openedByDay").EnumerateArray().Sum(d => d.GetProperty("count").GetInt64()), "Opened in the window");
            Check.Equal(2L, metrics.GetProperty("openNow").GetInt64(), "Open now");
            Check.Equal(3, metrics.GetProperty("byType").GetProperty("objects").GetArrayLength(), "Per type");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/support?days=400", agent.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

            await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, a1, "Internal note before delete", ct, "Note").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/tickets/" + a1, agent.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(0L, await SupportTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM tickets WHERE id = '" + a1 + "';", ct).ConfigureAwait(false), "Ticket deleted");
            Check.Equal(0L, await SupportTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM ticketmessages WHERE ticketid = '" + a1 + "';", ct).ConfigureAwait(false), "Messages deleted");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "ticket.delete", a1, ct).ConfigureAwait(false) >= 1, "ticket.delete audited");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/tickets/" + a1, alice.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task LimitsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            f.Settings.Support.TicketsPerDay = 2;
            string id = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "First ticket today").ConfigureAwait(false);
            await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "Second ticket today").ConfigureAwait(false);
            ApiResult third = await SupportTestKit.CreateAsync(f, member.AccessToken, new { type = "Support", title = "Third ticket today", body = "This one should be limited." }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.TooManyRequests, third.Status, "support.ticketsPerDay");
            Check.Equal("RateLimited", third.Error, "RateLimited");
            Check.True(!String.IsNullOrEmpty(third.RetryAfter), "Retry-After");
            f.Settings.Support.TicketsPerDay = 1000;
            f.Settings.Support.MaxOpenTicketsPerMember = 2;
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets", member.AccessToken, new { type = "Support", title = "Over the open limit", body = "Too many open tickets now." }, HttpStatusCode.Conflict, "TicketLimitReached", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/close", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "After closing one").ConfigureAwait(false);
            f.Settings.Support.RepliesPerHour = 1;
            string open = await SupportTestKit.TicketIdAsync(f, (await Api.RegisterAsync(f, ct).ConfigureAwait(false)).AccessToken, ct).ConfigureAwait(false);
            TestMember owner = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string mine = await SupportTestKit.TicketIdAsync(f, owner.AccessToken, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + mine + "/messages", owner.AccessToken, new { body = "First reply." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            ApiResult second = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/tickets/" + mine + "/messages", owner.AccessToken, new { body = "Second reply." }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.TooManyRequests, second.Status, "support.repliesPerHour");
            Check.True(!String.IsNullOrEmpty(second.RetryAfter), "Retry-After on replies");
            Check.True(open.Length > 0, "Other member unaffected");
        }

        private static Task EmailLinksAsync(HttpFixture f, CancellationToken ct)
        {
            Check.Equal("/app/support/tickets/tkt_1", EmailService.WebPathFor("/support/tickets/tkt_1"), "Ticket member path");
            Check.Equal("/app/venues/ven_1", EmailService.WebPathFor("/venues/ven_1"), "Venue path");
            Check.Equal("/app/directory/submissions", EmailService.WebPathFor("/me/directory/submissions"), "Own submissions");
            Check.Equal("/app/membership", EmailService.WebPathFor("/account/membership"), "Membership");
            Check.Equal("/app/settings/notifications", EmailService.WebPathFor("/account/notifications"), "Notification settings");
            Check.Equal("/admin/support?ticket=tkt_1", EmailService.WebPathFor("/admin/support?ticket=tkt_1"), "Admin paths stay");
            Check.Equal("/app/feed", EmailService.WebPathFor("/app/feed"), "Website paths stay");
            Check.Equal("/app", EmailService.WebPathFor(null), "Empty path");
            Check.Equal("/app", EmailService.WebPathFor("//evil.example"), "Protocol-relative paths are refused");
            Ticket ticket = new Ticket { Id = "tkt_1" };
            Check.Equal("/support/tickets/tkt_1", TicketNotifier.MemberPath(ticket), "Notification path stays the shared app path");
            Check.Equal("/admin/support?ticket=tkt_1", TicketNotifier.AdminPath(ticket), "Staff path opens the inbox drawer");
            Check.True(f.Services.Email.MemberLink(TicketNotifier.MemberPath(ticket)).EndsWith("/app/support/tickets/tkt_1", StringComparison.Ordinal), "Member email link is a website page");
            return Task.CompletedTask;
        }

        private static async Task RolesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            TestMember communicator = await Api.RegisterAsync(f, ct, Api.UniqueEmail("comms"), "Communicator").ConfigureAwait(false);
            await SafetyHelpers.GrantRoleAsync(f, communicator.Id, RoleEnum.Communicator, ct).ConfigureAwait(false);
            string id = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/enumerate", agent.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/tickets/" + id, agent.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, id, "Agent reply.", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/tickets/" + id, agent.AccessToken, new { status = "InProgress" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/metrics/support", agent.AccessToken, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/tickets/export.csv", agent.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/members/enumerate", agent.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/property/access", agent.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/support/ticket-types/order", agent.AccessToken, new { ticketTypeIds = new[] { "tty_x" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            foreach (TestMember outsider in new[] { communicator, member })
            {
                await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/enumerate", outsider.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/tickets/" + id, outsider.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + id + "/messages", outsider.AccessToken, new { body = "Nope" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            }
            JsonElement adminView = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/tickets/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("InProgress", Api.Str(adminView, "status"), "SystemAdmin reads the ticket");
            // Status lines read as words ("Waiting on member", "In progress"), never enum names.
            JsonElement timeline = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/" + id + "/messages/enumerate", HttpFixture.AdminToken, new { maxResults = 50 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> bodies = timeline.GetProperty("objects").EnumerateArray().Select(m => Api.Str(m, "body")).ToList();
            Check.True(bodies.Any(b => b.Contains("to In progress", StringComparison.Ordinal)), "Status line names the status in words: " + String.Join(" | ", bodies));
            Check.True(!bodies.Any(b => b.Contains("WaitingOnMember", StringComparison.Ordinal) || b.Contains("InProgress", StringComparison.Ordinal)), "No enum names in status lines");
            JsonElement user = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + agent.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(user.ToString().Contains("\"isStaff\":true", StringComparison.Ordinal), "SupportAgent counts as staff");
        }

        private static async Task AccessModelsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember neverPaid = await Api.RegisterAsync(f, ct, null, "Never Paid").ConfigureAwait(false);
            TestMember lapsed = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE entitlements SET endsutc = now() - interval '1 day', startsutc = now() - interval '30 days' WHERE userid = '" + lapsed.Id + "';", false, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(lapsed.Id);
            lapsed = await Api.LoginAsync(f, lapsed, ct).ConfigureAwait(false);
            ApiResult feed = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", neverPaid.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PaymentRequired, feed.Status, "FullyGated member routes need a membership");
            foreach (TestMember account in new[] { neverPaid, lapsed })
            {
                string id = await SupportTestKit.TicketIdAsync(f, account.AccessToken, ct, "Billing help please").ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, account.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/support/ticket-types/enumerate", account.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                string upload = await SupportTestKit.UploadAsync(f, account.AccessToken, ct, id).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/" + id + "/messages", account.AccessToken, new { body = "Screenshot attached.", mediaIds = new[] { upload } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            }
            foreach (string model in new[] { "PartiallyGated", "Open" })
            {
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                await SupportTestKit.TicketIdAsync(f, neverPaid.AccessToken, ct, "Help in " + model).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/tickets/enumerate", neverPaid.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            }
            ApiResult anonymous = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/tickets/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, anonymous.Status, "No anonymous ticket API");
        }

        private static async Task FeatureOffAsync(HttpFixture f, CancellationToken ct)
        {
            SupportTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets", member.AccessToken, new { type = "Support", title = "Is anyone there", body = "Support is turned off here." }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/support/ticket-types/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/tickets/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!property.GetProperty("features").GetProperty("support").GetBoolean(), "features.support reported off");
        }

        private static async Task KillSwitchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/support", HttpFixture.AdminToken, new { killed = true, message = "Support is paused.", reason = "Incident 7" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tickets", member.AccessToken, new { type = "Support", title = "During the incident", body = "Support is paused right now." }, HttpStatusCode.ServiceUnavailable, "FeatureDisabled", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, member.AccessToken, null, HttpStatusCode.ServiceUnavailable, "FeatureDisabled", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/support/ticket-types/enumerate", member.AccessToken, new { }, HttpStatusCode.ServiceUnavailable, "FeatureDisabled", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/support", HttpFixture.AdminToken, new { killed = false, reason = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/tickets/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember agent = await SupportTestKit.AgentAsync(f, ct).ConfigureAwait(false);
            string id = await SupportTestKit.TicketIdAsync(f, member.AccessToken, ct, "Export me please").ConfigureAwait(false);
            await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, id, "Visible staff answer.", ct).ConfigureAwait(false);
            await SupportTestKit.StaffReplyAsync(f, agent.AccessToken, id, "PRIVATE-NOTE-TEXT", ct, "Note").ConfigureAwait(false);
            string exportId = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/export", member.AccessToken, null, HttpStatusCode.Accepted, ct).ConfigureAwait(false), "exportId");
            AccountExportWorker worker = new AccountExportWorker(f.Services.AccountExports, f.Services.Logger);
            await using (worker.ConfigureAwait(false))
            {
                await worker.RunOnceAsync(ct).ConfigureAwait(false);
            }
            JsonElement status = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/export/" + exportId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult file = await Api.SendAsync(f, HttpMethod.Get, Api.Str(status, "downloadUrl"), null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, file.Status, "Export download");
            JsonElement tickets = file.Body.GetProperty("tickets");
            Check.Equal(1, tickets.GetArrayLength(), "Export tickets");
            Check.Equal(2, tickets[0].GetProperty("replies").GetArrayLength(), "Opening message and the staff reply");
            Check.True(!file.Text.Contains("PRIVATE-NOTE-TEXT", StringComparison.Ordinal), "Internal notes are never exported");

            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", member.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            Ticket? kept = await f.Database.Support.ReadTicketAsync(id, ct).ConfigureAwait(false);
            Check.True(kept != null && kept.Status == TicketStatusEnum.WaitingOnMember && kept.Body.Length > 0, "Account deletion leaves the ticket as it is");
            JsonElement staff = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/tickets/" + id, agent.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(staff.GetProperty("requester").GetProperty("isDeleted").GetBoolean(), "The requester renders as Deleted member");
            Check.Equal(3, staff.GetProperty("messages").GetProperty("objects").EnumerateArray().Count(m => Api.Str(m, "kind") != "System"), "Messages kept");
        }

        #endregion
    }
}
