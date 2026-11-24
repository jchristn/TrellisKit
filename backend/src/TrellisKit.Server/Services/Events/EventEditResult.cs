namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The validated outcome of an event create or edit before it is written.
    /// </summary>
    public class EventEditResult
    {
        #region Public-Members

        /// <summary>
        /// The event as it will be stored.
        /// </summary>
        public EventRecord Record { get; set; } = new EventRecord();

        /// <summary>
        /// What changed (edits).
        /// </summary>
        public EventChangeSet Changes { get; set; } = new EventChangeSet();

        /// <summary>
        /// Validated recurrence rule from the request, or null.
        /// </summary>
        public EventRecurrenceRule? Rule { get; set; } = null;

        /// <summary>
        /// Local start in the event zone (for recurrence anchors).
        /// </summary>
        public DateTime StartLocal { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Whether hosts, instructors, or reminder offsets changed (children are rewritten).
        /// </summary>
        public bool ChildrenChanged { get; set; } = false;

        /// <summary>
        /// Whether the cover changed (the new cover is attached after the write).
        /// </summary>
        public bool CoverChanged { get; set; } = false;

        #endregion
    }
}
