namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// PUT and DELETE /api/v1/ideas/{id}/follow: { isFollowed }.
    /// </summary>
    public class IdeaFollowResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the caller follows the idea.
        /// </summary>
        public bool IsFollowed { get; set; } = false;

        #endregion
    }
}
