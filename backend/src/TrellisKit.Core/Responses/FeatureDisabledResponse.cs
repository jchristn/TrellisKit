namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// 503 FeatureDisabled body: the error plus the engine feature key that is switched off.
    /// </summary>
    public class FeatureDisabledResponse : ErrorResponse
    {
        /// <summary>
        /// Instantiate the body.
        /// </summary>
        /// <param name="message">Message shown to members.</param>
        /// <param name="feature">Engine feature key.</param>
        public FeatureDisabledResponse(string message, string? feature) : base("FeatureDisabled", message)
        {
            Feature = feature;
        }

        /// <summary>
        /// Engine feature key, or null.
        /// </summary>
        public string? Feature { get; set; } = null;
    }
}
