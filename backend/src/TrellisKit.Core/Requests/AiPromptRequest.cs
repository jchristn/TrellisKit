namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Save the text of an administrator-editable prompt.
    /// </summary>
    public class AiPromptRequest
    {
        #region Public-Members

        /// <summary>
        /// Prompt text within the prompt's length limits; required.
        /// </summary>
        public string? Content { get; set; } = null;

        #endregion
    }
}
