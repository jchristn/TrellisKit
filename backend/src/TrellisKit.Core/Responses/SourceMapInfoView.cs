namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The private source map used for symbolication (never served).
    /// </summary>
    public class SourceMapInfoView
    {
        /// <summary>
        /// Source map key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Key of the bundle it maps.
        /// </summary>
        public string BundleKey { get; set; } = String.Empty;

        /// <summary>
        /// Size.
        /// </summary>
        public long SizeBytes { get; set; } = 0;
    }
}
