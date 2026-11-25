namespace TrellisKit.Server.Services.AdminSummary
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// The engine's default administrator summary items (docs/ADMIN_SUMMARY.md). Every item names an engine metric, so the
    /// list is the same for every property; feature-dependent items are skipped when the property turns the feature off,
    /// and optional items are left out when they have no data.
    /// </summary>
    public static class AdminSummaryDefaults
    {
        #region Public-Members

        /// <summary>
        /// Section of membership and growth items.
        /// </summary>
        public const string Members = "Members";

        /// <summary>
        /// Section of activity items.
        /// </summary>
        public const string Activity = "Activity";

        /// <summary>
        /// Section of items that may need a staff member.
        /// </summary>
        public const string Attention = "Needs attention";

        /// <summary>
        /// Section of operational items.
        /// </summary>
        public const string Platform = "Platform";

        /// <summary>
        /// The default items in display order. Returns a new list on every call.
        /// </summary>
        public static List<AdminSummaryItem> Items
        {
            get
            {
                return new List<AdminSummaryItem>
                {
                    Item(Members, "users.total", "Total members"),
                    Item(Members, "users.new", "New sign-ups"),
                    Item(Members, "users.deleted", "Deleted accounts"),
                    Item(Members, "users.active", "Daily active members"),
                    Item(Members, "users.activeWeekly", "Weekly active members"),
                    Item(Members, "users.activeMonthly", "Monthly active members"),
                    Item(Members, "members.stickiness", "Daily / monthly active"),
                    Optional(Members, "members.methods", "Sign-ups by method", "method"),
                    Optional(Members, "members.membership", "Memberships", "status"),
                    Optional(Members, "members.lapses", "Lapses and cancellations", "transition"),

                    Item(Activity, "interactions.activity", "Activity", "kind"),
                    Item(Activity, "engagement.perActive", "Interactions per active member"),
                    Optional(Activity, "pollVotes.new", "Poll votes"),
                    Optional(Activity, "forums.posts", "Forum threads", null, "forums"),
                    Optional(Activity, "forums.replies", "Forum replies", null, "forums"),
                    Optional(Activity, "messages.sent", "Direct messages", null, "messaging"),
                    Optional(Activity, "messages.conversations", "Conversations started", null, "messaging"),
                    Optional(Activity, "events.rsvps", "Event responses", "status", "events"),
                    Optional(Activity, "library.plays", "Lesson plays", null, "library"),
                    Optional(Activity, "library.completions", "Lesson completions", null, "library"),
                    Optional(Activity, "directory.submissions", "Directory submissions", "status", "directory"),
                    Optional(Activity, "assistantMessages.new", "Assistant messages", null, "ai"),
                    Optional(Activity, "images.new", "Images uploaded"),
                    Optional(Activity, "videos.new", "Videos uploaded"),
                    Optional(Activity, "requests.count", "Requests by feature", "section"),

                    Optional(Attention, "moderation.reports", "New reports"),
                    Optional(Attention, "moderation.backlog", "Open reports"),
                    Optional(Attention, "moderation.holds", "Automatic holds"),
                    Optional(Attention, "support.opened", "Support tickets opened", null, "support"),
                    Optional(Attention, "support.backlog", "Open support tickets", null, "support"),
                    Optional(Attention, "forums.unanswered", "Unanswered threads", null, "forums"),
                    Optional(Attention, "auth.problems", "Sign-in problems", "kind"),
                    Filtered(Attention, "workers.runs", "Failed background passes", "outcome", "Failed"),

                    Item(Platform, "requests.count", "API requests"),
                    Item(Platform, "requests.errorRate", "Server error rate"),
                    Item(Platform, "requests.latency", "Request latency (p95)"),
                    Optional(Platform, "email.sent", "Emails sent"),
                    Optional(Platform, "push.sent", "Push notifications sent"),
                    Optional(Platform, "storage.total", "Storage used"),
                    Optional(Platform, "ai.calls", "Model calls"),
                    Optional(Platform, "ai.cost", "Model cost")
                };
            }
        }

        #endregion

        #region Private-Methods

        private static AdminSummaryItem Item(string section, string metric, string label, string? groupBy = null)
        {
            return new AdminSummaryItem { Section = section, Metric = metric, Label = label, GroupBy = groupBy };
        }

        private static AdminSummaryItem Optional(string section, string metric, string label, string? groupBy = null, string? feature = null)
        {
            return new AdminSummaryItem { Section = section, Metric = metric, Label = label, GroupBy = groupBy, Feature = feature, HideWhenEmpty = true };
        }

        private static AdminSummaryItem Filtered(string section, string metric, string label, string dimension, string value)
        {
            AdminSummaryItem item = Optional(section, metric, label);
            item.Filters = new Dictionary<string, string>(StringComparer.Ordinal) { { dimension, value } };
            return item;
        }

        #endregion
    }
}
