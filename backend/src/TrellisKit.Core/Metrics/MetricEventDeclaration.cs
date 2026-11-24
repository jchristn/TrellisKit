namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Closed product-event contract, with no free-text fields.
    /// </summary>
    public sealed class MetricEventDeclaration
    {
        /// <summary>
        /// Declared event name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Allowed dimensions and their finite value sets.
        /// </summary>
        public Dictionary<string,List<string>> Dimensions { get; set; } = new Dictionary<string,List<string>>(StringComparer.Ordinal);

    }
}
