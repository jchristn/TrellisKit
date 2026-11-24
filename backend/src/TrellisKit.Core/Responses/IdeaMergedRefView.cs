namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The idea a merged idea points to: { id, title }.
    /// </summary>
    public class IdeaMergedRefView
    {
        #region Public-Members

        /// <summary>
        /// Target idea identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Target idea title (empty when the caller cannot read it).
        /// </summary>
        public string Title { get; set; } = String.Empty;

        #endregion
    }
}
