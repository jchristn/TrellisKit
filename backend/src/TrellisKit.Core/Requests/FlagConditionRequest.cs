namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One targeting condition in a request.
    /// </summary>
    public class FlagConditionRequest
    {
        /// <summary>
        /// Attribute.
        /// </summary>
        public string Attribute { get; set; } = String.Empty;

        /// <summary>
        /// Operator.
        /// </summary>
        public string Operator { get; set; } = String.Empty;

        /// <summary>
        /// Tag or profile field key, or null.
        /// </summary>
        public string? Argument { get; set; } = null;

        /// <summary>
        /// Values.
        /// </summary>
        public List<string> Values { get; set; } = new List<string>();
    }
}
