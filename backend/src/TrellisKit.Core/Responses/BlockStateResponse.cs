namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// Result of POST and DELETE /api/v1/users/{id}/block.
    /// </summary>
    public class BlockStateResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the caller now blocks the member.
        /// </summary>
        public bool Blocked { get; set; } = false;

        #endregion
    }
}
