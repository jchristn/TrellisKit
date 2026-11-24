namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Short reference to an assistant.
    /// </summary>
    public class AiAssistantRef
    {
        #region Public-Members

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Assistant name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Whether the assistant was deleted.
        /// </summary>
        public bool Deleted { get; set; } = false;

        #endregion
    }
}
