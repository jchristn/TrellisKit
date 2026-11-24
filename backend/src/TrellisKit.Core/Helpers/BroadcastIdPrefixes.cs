namespace TrellisKit.Core.Helpers
{
    /// <summary>
    /// Identifier prefixes of the Phase 5 broadcast objects (docs/PHASE5_CONTRACT.md section 0.1).
    /// </summary>
    public static class BroadcastIdPrefixes
    {
        #region Public-Members

        /// <summary>
        /// Broadcast identifier prefix.
        /// </summary>
        public const string Broadcast = "bct_";

        /// <summary>
        /// Saved segment identifier prefix.
        /// </summary>
        public const string Segment = "bsg_";

        /// <summary>
        /// Segment filter row identifier prefix.
        /// </summary>
        public const string Filter = "bsf_";

        /// <summary>
        /// Delivery identifier prefix.
        /// </summary>
        public const string Delivery = "bdl_";

        #endregion
    }
}
