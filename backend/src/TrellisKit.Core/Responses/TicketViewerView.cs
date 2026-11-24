namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What the caller may do with a ticket (docs/PHASE5_CONTRACT.md section 1.4); staff fields are null for members.
    /// </summary>
    public class TicketViewerView
    {
        #region Public-Members

        /// <summary>
        /// May reply.
        /// </summary>
        public bool CanReply { get; set; } = false;

        /// <summary>
        /// May close.
        /// </summary>
        public bool CanClose { get; set; } = false;

        /// <summary>
        /// May reopen.
        /// </summary>
        public bool CanReopen { get; set; } = false;

        /// <summary>
        /// May attach media.
        /// </summary>
        public bool CanAttach { get; set; } = false;

        /// <summary>
        /// Staff: may add internal notes.
        /// </summary>
        public bool? CanNote { get; set; } = null;

        /// <summary>
        /// Staff: may assign.
        /// </summary>
        public bool? CanAssign { get; set; } = null;

        /// <summary>
        /// Staff: may change status and priority.
        /// </summary>
        public bool? CanSetStatus { get; set; } = null;

        #endregion
    }
}
