namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An assistant that references a model endpoint (usedBy).
    /// </summary>
    public class EndpointUsageItem
    {
        #region Public-Members

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// Assistant name.
        /// </summary>
        public string AssistantName { get; set; } = String.Empty;

        /// <summary>
        /// Purpose, for example chat.
        /// </summary>
        public string Purpose { get; set; } = String.Empty;

        #endregion
    }
}
