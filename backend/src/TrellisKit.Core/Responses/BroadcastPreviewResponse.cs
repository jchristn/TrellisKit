namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// POST /api/v1/admin/broadcasts/segments/preview response (docs/PHASE5_CONTRACT.md section 8.1).
    /// </summary>
    public class BroadcastPreviewResponse
    {
        #region Public-Members

        /// <summary>
        /// Recipients.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Recipients per channel.
        /// </summary>
        public BroadcastChannelCounts ByChannel { get; set; } = new BroadcastChannelCounts();

        /// <summary>
        /// Sample.
        /// </summary>
        public BroadcastSample Sample { get; set; } = new BroadcastSample();

        #endregion
    }
}
