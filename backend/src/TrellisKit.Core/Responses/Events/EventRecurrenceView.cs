namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The recurrence an occurrence belongs to (resolved decision 11).
    /// </summary>
    public class EventRecurrenceView
    {
        #region Public-Members

        /// <summary>
        /// Recurrence identifier (erc_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Rule.
        /// </summary>
        public EventRecurrenceRuleView Rule { get; set; } = new EventRecurrenceRuleView();

        /// <summary>
        /// Plain English summary, for example Every week on Tuesday and Thursday, 12 times.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Position of this occurrence (0 based).
        /// </summary>
        public int? OccurrenceIndex { get; set; } = null;

        /// <summary>
        /// Whether this occurrence was edited or canceled on its own.
        /// </summary>
        public bool IsException { get; set; } = false;

        /// <summary>
        /// Start the rule produced for this occurrence.
        /// </summary>
        public DateTime? OriginalStartUtc { get; set; } = null;

        #endregion
    }
}
