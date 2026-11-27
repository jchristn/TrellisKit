namespace TrellisKit.Server.Services.Email
{
    using System;

    /// <summary>
    /// Sample <see cref="StaffEmailDetails.Token"/> values for the staff templates' previews (admin console and Mailpit,
    /// docs/EMAIL.md). Neutral values only: no property, brand, or real person. Thread safety: immutable.
    /// </summary>
    public static class StaffEmailSamples
    {
        #region Public-Members

        /// <summary>
        /// staff_new_member.
        /// </summary>
        public static readonly string NewMember = Build(
            Member("Member", "Pat Example", "pat", "usr_sample", "Email and password"),
            new StaffEmailSection("Sign-up").Row("Accounts from this IP in 24 hours", "1 (this one)").Row("Documents accepted", "Terms of Service 2026-01, Privacy Policy 2026-01"),
            Request());

        /// <summary>
        /// staff_new_report.
        /// </summary>
        public static readonly string NewReport = Build(
            new StaffEmailSection("Report").Row("Reason", "Harassment").Row("Details", "Repeated insults in the replies.").Row("Scope", "Feed").Row("Review due", "Thu Nov 26, 2026 1:04 PM America/Los_Angeles (21:04 UTC)"),
            Content(),
            Author(),
            Member("Reporter", "Sam Rivera", "sam", "usr_reporter", "Google").Row("Reports filed in 90 days", "2"),
            Request());

        /// <summary>
        /// staff_content_auto_hidden.
        /// </summary>
        public static readonly string AutoHidden = Build(
            Content(),
            new StaffEmailSection("Reports").Row("Distinct reporters", "3").Row("Hide threshold", "3").Row("Reasons", "Harassment 2, Spam 1"),
            Author(),
            Origin("Automatic hide after member reports"));

        /// <summary>
        /// staff_moderation_overdue.
        /// </summary>
        public static readonly string ModerationOverdue = Build(
            new StaffEmailSection("Overdue reports").Row("Oldest report", "2 days old").Row("By scope", "Feed 3, Forum 1").Row("By reason", "Harassment 2, Spam 2"),
            Origin("Overdue report check (scheduled)"));

        /// <summary>
        /// staff_new_subscription, staff_cancellation, staff_payment_failure, and staff_refund.
        /// </summary>
        public static readonly string Subscription = Build(
            new StaffEmailSection("Subscription").Row("Plan", "Example Membership").Row("Product id", "example.membership.annual").Row("List price", "99.00 USD per year")
                .Row("Store", "Apple").Row("Store subscription id", "2000000123456789").Row("Latest transaction id", "2000000987654321").Row("Status", "Active")
                .Row("Auto-renew", "On").Row("Current period", "Wed Nov 25, 2026 to Thu Nov 25, 2027").Row("Subscription id", "sub_sample"),
            Member("Member", "Pat Example", "pat", "usr_sample", "Apple"),
            Origin("App Store notification or verification"));

        /// <summary>
        /// staff_ticket_opened.
        /// </summary>
        public static readonly string TicketOpened = Build(
            new StaffEmailSection("Ticket").Row("Number", "#1042").Row("Type", "Billing").Row("Priority", "Normal").Row("Status", "Open").Row("Body", "I was charged twice for my membership this month. Can you refund one of the charges?"),
            Member("Requester", "Sam Rivera", "sam", "usr_requester", "Email and password"),
            new StaffEmailSection("Client (from the ticket)").Row("Client", "mobile").Row("App version", "0.1.0").Row("Platform", "ios").Row("Operating system", "18.1").Row("Device", "iPhone15,2"),
            Request());

        /// <summary>
        /// staff_ticket_overdue.
        /// </summary>
        public static readonly string TicketOverdue = Build(
            new StaffEmailSection("Ticket").Row("Number", "#1042").Row("Type", "billing").Row("Priority", "High").Row("Status", "Open").Row("Age", "2 days")
                .Row("First response due", "Wed Nov 25, 2026 9:00 AM America/Los_Angeles (17:00 UTC)").Row("Assigned to", "Unassigned"),
            Member("Requester", "Sam Rivera", "sam", "usr_requester", "Email and password"),
            Origin("First response check (scheduled)"));

        /// <summary>
        /// staff_directory_submission.
        /// </summary>
        public static readonly string DirectorySubmission = Build(
            new StaffEmailSection("Submission").Row("Type", "NewVenue").Row("Target", "Example Training Studio").Row("Proposed map point", "37.77493, -122.41942")
                .Row("address", "100 Example Street, Example City").Row("categories", "Gym, Studio"),
            Member("Submitter", "Pat Example", "pat", "usr_sample", "Email and password").Row("Submissions in 90 days", "4 (Approved 3, Pending 1), 75% approved"),
            Request());

        /// <summary>
        /// staff_event_proposal.
        /// </summary>
        public static readonly string EventProposal = Build(
            new StaffEmailSection("Proposal").Row("Title", "Saturday open session").Row("When", "Sat Dec 5, 2026 10:00 AM (America/Los_Angeles)").Row("Format", "InPerson")
                .Row("Where", "Example Training Studio, Example City, CA, US").Row("Category", "workshop").Row("Audience", "Members"),
            Member("Proposer", "Pat Example", "pat", "usr_sample", "Email and password").Row("Proposals in 90 days", "2 (Accepted 1, Pending 1), 50% accepted"),
            Request());

        /// <summary>
        /// System-originated templates (integration failure, metric alert, digests, activity summary).
        /// </summary>
        /// <param name="source">Origin source text.</param>
        /// <returns>Serialized sample.</returns>
        public static string System(string source)
        {
            return Build(Origin(source));
        }

        #endregion

        #region Private-Methods

        private static string Build(params StaffEmailSection[] sections)
        {
            StaffEmailDetails details = new StaffEmailDetails();
            foreach (StaffEmailSection section in sections) details.Add(section);
            return details.Serialize();
        }

        private static StaffEmailSection Member(string title, string name, string handle, string id, string method)
        {
            return new StaffEmailSection(title)
                .Row("Name", name)
                .Row("Handle", "@" + handle)
                .Row("Email", handle + "@example.test (verified)")
                .Row("Account id", id)
                .Row("Sign-up method", method)
                .Row("Created", "Wed Nov 25, 2026 1:04 PM America/Los_Angeles (21:04 UTC), 3 minutes ago")
                .Row("Location", "Example City, CA, US")
                .Row("Age attested", "Yes")
                .Row("Roles", "Member")
                .Row("Membership", "None")
                .Row("Admin page", "https://example.test/admin/members/" + id);
        }

        private static StaffEmailSection Author()
        {
            return Member("Author", "Jordan Sample", "jordan", "usr_author", "Email and password").Row("History (90 days)", "1 removal, 2 hides, 5 reports against");
        }

        private static StaffEmailSection Content()
        {
            return new StaffEmailSection("Content").Row("Type", "post").Row("Id", "fp_sample").Row("Scope", "Feed").Row("Excerpt", "Sample post text that was reported by members.")
                .Row("State", "None").Row("Open reports", "3");
        }

        private static StaffEmailSection Request()
        {
            return new StaffEmailSection("Request")
                .Row("IP address", "203.0.113.24")
                .Row("Client", "Web")
                .Row("Browser", "Chrome 131 on macOS")
                .Row("Time", "Wed Nov 25, 2026 1:04 PM America/Los_Angeles (21:04 UTC)")
                .Row("Request id", "req_sample (https://example.test/admin/requests?id=req_sample)");
        }

        private static StaffEmailSection Origin(string source)
        {
            return new StaffEmailSection("Origin").Row("Source", source).Row("Site", "https://example.test").Row("Server version", "0.1.0-alpha");
        }

        #endregion
    }
}
