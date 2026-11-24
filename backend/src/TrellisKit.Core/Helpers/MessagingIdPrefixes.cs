namespace TrellisKit.Core.Helpers
{
    /// <summary>
    /// Identifier prefixes of the Phase 5 direct messaging objects (docs/PHASE5_CONTRACT.md section 0.1).
    /// </summary>
    public static class MessagingIdPrefixes
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier prefix.
        /// </summary>
        public const string Conversation = "cnv_";

        /// <summary>
        /// Conversation member identifier prefix.
        /// </summary>
        public const string ConversationMember = "cvm_";

        /// <summary>
        /// Message identifier prefix.
        /// </summary>
        public const string Message = "msg_";

        /// <summary>
        /// Message report snapshot identifier prefix.
        /// </summary>
        public const string ReportSnapshot = "mrs_";

        /// <summary>
        /// Message report snapshot item identifier prefix.
        /// </summary>
        public const string ReportSnapshotItem = "mri_";

        #endregion
    }
}
