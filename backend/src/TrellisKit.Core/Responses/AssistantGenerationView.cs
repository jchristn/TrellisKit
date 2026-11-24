namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Generation overrides.
    /// </summary>
    public class AssistantGenerationView
    {
        #region Public-Members

        /// <summary>
        /// Temperature.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Top p.
        /// </summary>
        public double? TopP { get; set; } = null;

        #endregion
    }
}
