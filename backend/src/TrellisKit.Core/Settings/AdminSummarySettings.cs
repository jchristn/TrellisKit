namespace TrellisKit.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Daily administrator summary email (docs/ADMIN_SUMMARY.md): once per day, after <see cref="SendHour"/>, every
    /// running property emails <see cref="Recipients"/> a summary of the previous day (members, sign-ups, active
    /// members, activity by type, items needing attention, and platform health), built from the property's registered
    /// metrics. Days follow the property's business calendar (usage.timeZone), like the metrics dashboards. These are
    /// deployment settings: in MultiProperty mode every property sends its own summary to the same recipients.
    /// </summary>
    public class AdminSummarySettings
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of recipients.
        /// </summary>
        public const int MaxRecipients = 20;

        /// <summary>
        /// Maximum number of configured items.
        /// </summary>
        public const int MaxItems = 60;

        /// <summary>
        /// Whether summaries are sent. Default false. Nothing is sent without at least one valid recipient.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Recipient email addresses (at most 20; blanks, duplicates, and invalid addresses are ignored).
        /// </summary>
        public List<string> Recipients
        {
            get { return _Recipients; }
            set { _Recipients = value ?? new List<string>(); }
        }

        /// <summary>
        /// Local hour of day, in the property's usage.timeZone setting (UTC when it is not set), after which the previous
        /// day's summary is sent. Default 7. Minimum 0, maximum 23.
        /// </summary>
        public int SendHour
        {
            get { return _SendHour; }
            set { _SendHour = Math.Clamp(value, 0, 23); }
        }

        /// <summary>
        /// Include the engine's default items (docs/ADMIN_SUMMARY.md). Default true. When false only <see cref="Items"/> are
        /// reported.
        /// </summary>
        public bool IncludeDefaultItems { get; set; } = true;

        /// <summary>
        /// Additional items appended after the defaults (at most 60; later items are ignored), for example a metric the
        /// property declares in property.json.
        /// </summary>
        public List<AdminSummaryItem> Items
        {
            get { return _Items; }
            set { _Items = value ?? new List<AdminSummaryItem>(); }
        }

        /// <summary>
        /// Largest groups listed for a grouped item; the rest are folded into Other. Default 6. Minimum 1, maximum 20.
        /// </summary>
        public int MaxGroups
        {
            get { return _MaxGroups; }
            set { _MaxGroups = Math.Clamp(value, 1, 20); }
        }

        #endregion

        #region Private-Members

        private List<string> _Recipients = new List<string>();
        private int _SendHour = 7;
        private List<AdminSummaryItem> _Items = new List<AdminSummaryItem>();
        private int _MaxGroups = 6;

        #endregion
    }
}
