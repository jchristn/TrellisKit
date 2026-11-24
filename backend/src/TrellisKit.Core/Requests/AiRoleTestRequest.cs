namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Send a test prompt through a task role's hierarchy.
    /// </summary>
    public class AiRoleTestRequest
    {
        #region Public-Members

        /// <summary>
        /// Prompt, 1 to 4000 characters; required.
        /// </summary>
        public string? Prompt { get; set; } = null;

        #endregion
    }
}
