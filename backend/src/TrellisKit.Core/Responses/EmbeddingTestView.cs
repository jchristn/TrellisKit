namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Embedding test output.
    /// </summary>
    public class EmbeddingTestView
    {
        #region Public-Members

        /// <summary>
        /// Dimensions.
        /// </summary>
        public int Dimensions { get; set; } = 0;

        /// <summary>
        /// First 8 values.
        /// </summary>
        public List<float> Preview { get; set; } = new List<float>();

        #endregion
    }
}
