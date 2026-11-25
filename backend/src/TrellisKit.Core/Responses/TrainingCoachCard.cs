namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The "Ask your coach" card: whether an assistant is available to the member (features.ai, the property AI switch,
    /// and at least one assistant the member can use) and where clients show assistants (features.aiPlacement).
    /// </summary>
    public class TrainingCoachCard
    {
        #region Public-Members

        /// <summary>
        /// Whether an assistant is available to the member.
        /// </summary>
        public bool Available { get; set; } = false;

        /// <summary>
        /// Where the mobile app shows assistants: Menu or Tab (setting ai.mobilePlacement).
        /// </summary>
        public string Placement { get; set; } = "Menu";

        #endregion
    }
}
