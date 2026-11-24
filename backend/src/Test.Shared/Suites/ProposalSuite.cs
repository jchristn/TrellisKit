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

    /// <summary>
    /// Member event proposals (resolved decision 10): propose, the staff queue and email, approve (the proposer
    /// becomes the host), reject with a note, withdraw, limits, and the switches.
    /// </summary>
    public static class ProposalSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "EventProposals";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Event proposals (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Proposals.Approve", "A member proposes an event (status Proposed, hidden from others, staff emailed), event staff see it in the queue and approve it: published with the proposer as host, who gets EventProposalDecision and the email", ApproveAsync),
                Case("Proposals.RejectWithdrawLimits", "Rejection needs a note and tells the proposer; the proposer withdraws a pending proposal; reviewed proposals cannot be reviewed again; the pending limit, hosts, and the switches apply", RejectAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                EventTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static Dictionary<string, object?> Proposal(string title, object? extra = null)
        {
            Dictionary<string, object?> body = EventTestKit.Body(title, EventTestKit.Local(20, 11), EventTestKit.Local(20, 14), extra);
            body["proposalNote"] = "I can bring snacks.";
            return body;
        }

        private static async Task ApproveAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember manager = await EventTestKit.ManagerAsync(f, ct).ConfigureAwait(false);
            TestMember proposer = await SafetyHelpers.MemberAsync(f, ct, "Picnic Planner").ConfigureAwait(false);
            TestMember other = await SafetyHelpers.MemberAsync(f, ct, "Other Member").ConfigureAwait(false);
            int mails = f.Fakes.Email.Messages.Count;
            JsonElement proposed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/proposals", proposer.AccessToken, Proposal("Community picnic"), HttpStatusCode.Created, ct).ConfigureAwait(false);
            string id = Api.Str(proposed, "id");
            Check.Equal("Proposed", Api.Str(proposed, "status"), "Proposed");
            Check.Equal("I can bring snacks.", Api.Str(proposed, "proposalNote"), "Note kept");
            Check.True(f.Fakes.Email.Messages.Skip(mails).Any(m => m.Subject.Contains("Community picnic", StringComparison.Ordinal)), "staff_event_proposal sent");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/events/" + id, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, proposer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("I can bring snacks.", Api.Str(mine, "proposalNote"), "The proposer sees the note in the Event payload");
            JsonElement myList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/events/proposals/enumerate", proposer.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(myList).Contains(id), "In my proposals");
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/proposals/enumerate", manager.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(queue).Contains(id), "In the staff queue");
            Check.Equal(proposer.Id, Api.Str(queue.GetProperty("objects")[0].GetProperty("proposedBy"), "id"), "Proposer shown");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/proposals/enumerate", other.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/approve", other.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            mails = f.Fakes.Email.Messages.Count;
            JsonElement approved = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/approve", manager.AccessToken, new { reviewNote = "Thanks for organizing!" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(approved, "status"), "Published by default");
            Check.True(approved.GetProperty("hostUserIds").EnumerateArray().Any(h => h.GetString() == proposer.Id), "The proposer is the host");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<Notification> decision = await EventTestKit.NotificationsAsync(f, proposer.Id, NotificationTypeEnum.EventProposalDecision, ct).ConfigureAwait(false);
            Check.True(decision.Count == 1 && decision[0].Title.Contains("approved", StringComparison.Ordinal), "EventProposalDecision");
            Check.Equal(NotificationCategoryEnum.Account, decision[0].Category, "Account category");
            Check.True(f.Fakes.Email.Messages.Skip(mails).Any(m => m.To == proposer.Email), "event_proposal_decision email");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/approve", manager.AccessToken, new { }, HttpStatusCode.Conflict, "NotPending", ct).ConfigureAwait(false);
            JsonElement hostEdit = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, proposer.AccessToken, new { description = "Bring a blanket." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Bring a blanket.", Api.Str(hostEdit, "description"), "The new host edits the description");
            JsonElement draftOnly = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/proposals", proposer.AccessToken, Proposal("Second picnic"), HttpStatusCode.Created, ct).ConfigureAwait(false);
            JsonElement asDraft = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + Api.Str(draftOnly, "id") + "/approve", manager.AccessToken, new { publish = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Draft", Api.Str(asDraft, "status"), "publish false leaves a Draft");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "event.approve", id, ct).ConfigureAwait(false) >= 1, "Audited");
        }

        private static async Task RejectAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember proposer = await SafetyHelpers.MemberAsync(f, ct, "Proposer").ConfigureAwait(false);
            string id = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/proposals", proposer.AccessToken, Proposal("Late night jam"), HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            int mailsBefore = f.Fakes.Email.Messages.Count;
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/reject", admin.AccessToken, new { reviewNote = "no" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement rejected = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/reject", admin.AccessToken, new { reviewNote = "The hall closes at ten." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Rejected", Api.Str(rejected, "status"), "Rejected");
            List<Notification> decision = await EventTestKit.NotificationsAsync(f, proposer.Id, NotificationTypeEnum.EventProposalDecision, ct).ConfigureAwait(false);
            Check.True(decision.Count == 1 && decision[0].Title.Contains("not approved", StringComparison.Ordinal), "Told");
            // The decline email links the website's proposals page (/app/events/proposals), never the API-style /app/me path.
            EmailMessage? declined = f.Fakes.Email.Messages.Skip(mailsBefore).FirstOrDefault(m => m.Subject.Contains("Late night jam", StringComparison.Ordinal) && m.To == proposer.Email);
            Check.True(declined != null && declined.TextBody.Contains("/app/events/proposals", StringComparison.Ordinal) && !declined.TextBody.Contains("/app/me/", StringComparison.Ordinal), "Decline email links /app/events/proposals");
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/events/proposals/enumerate", proposer.AccessToken, new { filters = new { status = "Rejected" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("The hall closes at ten.", Api.Str(mine.GetProperty("objects")[0], "reviewNote"), "The proposer sees the note");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/events/proposals/" + id + "/withdraw", proposer.AccessToken, null, HttpStatusCode.Conflict, "NotPending", ct).ConfigureAwait(false);
            string second = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/proposals", proposer.AccessToken, Proposal("Morning walk"), HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            JsonElement withdrawn = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/proposals/" + second + "/withdraw", proposer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Withdrawn", Api.Str(withdrawn, "status"), "Withdrawn");
            ApiResult hosts = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/events/proposals", proposer.AccessToken, Proposal("With hosts", new { hostUserIds = new[] { admin.Id } }), ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, hosts.Status, "Proposals cannot name hosts");
            f.Settings.Events.MaxPendingProposalsPerMember = 1;
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/proposals", proposer.AccessToken, Proposal("First pending"), HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/events/proposals", proposer.AccessToken, Proposal("Second pending"), HttpStatusCode.Conflict, "ProposalLimitReached", ct).ConfigureAwait(false);
            f.Settings.Events.ProposalsEnabled = false;
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/events/proposals", proposer.AccessToken, Proposal("Disabled"), HttpStatusCode.Forbidden, "ProposalsDisabled", ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!property.GetProperty("events").GetProperty("proposals").GetBoolean(), "The property document reports proposals off");
        }

        #endregion
    }
}
