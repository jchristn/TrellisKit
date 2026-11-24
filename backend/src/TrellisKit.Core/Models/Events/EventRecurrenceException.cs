namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An occurrence of a recurrence that was edited, canceled, or deleted on its own.
    /// </summary>
    public class EventRecurrenceException
    {
        #region Public-Members

        /// <summary>
        /// Identifier (erx_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Recurrence.
        /// </summary>
        public string RecurrenceId { get; set; } = String.Empty;

        /// <summary>
        /// Original start the rule produced.
        /// </summary>
        public DateTime OriginalStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Kind.
        /// </summary>
        public EventRecurrenceExceptionKindEnum Kind { get; set; } = EventRecurrenceExceptionKindEnum.Edited;

        /// <summary>
        /// Occurrence row, or null.
        /// </summary>
        public string? EventId { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
