namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// What the caller may do with an idea (docs/PHASE5_CONTRACT.md section 1.4).
    /// </summary>
    public class IdeaViewerView
    {
        #region Public-Members

        /// <summary>
        /// May vote or unvote.
        /// </summary>
        public bool CanVote { get; set; } = false;

        /// <summary>
        /// May comment.
        /// </summary>
        public bool CanComment { get; set; } = false;

        /// <summary>
        /// May react.
        /// </summary>
        public bool CanReact { get; set; } = false;

        /// <summary>
        /// May report.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// May edit.
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// May set the status (staff).
        /// </summary>
        public bool CanSetStatus { get; set; } = false;

        /// <summary>
        /// May merge (staff).
        /// </summary>
        public bool CanMerge { get; set; } = false;

        /// <summary>
        /// May open the idea (false for a locked stub).
        /// </summary>
        public bool CanOpen { get; set; } = true;

        /// <summary>
        /// What opening needs (Membership for a locked stub), or null.
        /// </summary>
        public string? Requires { get; set; } = null;

        #endregion
    }
}
