namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// What the caller may do with an instructor (docs/PHASE4_CONTRACT.md section 1.3).
    /// </summary>
    public class InstructorViewer
    {
        #region Public-Members

        /// <summary>
        /// Directory staff may edit.
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// The caller may submit an EditInstructor suggestion.
        /// </summary>
        public bool CanSuggestEdit { get; set; } = false;

        /// <summary>
        /// The caller may claim the entry as themselves.
        /// </summary>
        public bool CanClaim { get; set; } = false;

        /// <summary>
        /// The caller may report it.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// Directory staff may moderate it.
        /// </summary>
        public bool CanModerate { get; set; } = false;

        /// <summary>
        /// False on a locked stub.
        /// </summary>
        public bool CanOpen { get; set; } = true;

        /// <summary>
        /// SignIn or Membership on a locked stub, else null.
        /// </summary>
        public string? Requires { get; set; } = null;

        #endregion
    }
}
