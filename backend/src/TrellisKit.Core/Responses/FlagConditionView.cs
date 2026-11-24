namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A targeting condition.
    /// </summary>
    public class FlagConditionView
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
        /// Values (bounded by appConfig.maxValuesPerCondition).
        /// </summary>
        public List<string> Values { get; set; } = new List<string>();
    }
}
