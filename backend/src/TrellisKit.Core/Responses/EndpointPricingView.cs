namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Admin-entered prices of an endpoint in US dollars per million tokens (resolved decision 14).
    /// </summary>
    public class EndpointPricingView
    {
        #region Public-Members

        /// <summary>
        /// Price per million prompt tokens.
        /// </summary>
        public double? InputPerMillionTokens { get; set; } = null;

        /// <summary>
        /// Price per million completion tokens.
        /// </summary>
        public double? OutputPerMillionTokens { get; set; } = null;

        /// <summary>
        /// Price per million input tokens read from the provider's prompt cache, or null (billed at the input price).
        /// </summary>
        public double? CacheReadPerMillionTokens { get; set; } = null;

        /// <summary>
        /// Price per million input tokens written to the provider's prompt cache, or null (billed at the input price).
        /// </summary>
        public double? CacheWritePerMillionTokens { get; set; } = null;

        #endregion
    }
}
