namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// How one harness call is launched in isolation: the extra CLI arguments and the
    /// configuration files to write under the call's throwaway directory (built by <see cref="HarnessLaunchPlanner"/>).
    /// </summary>
    public class HarnessLaunchPlan
    {
        #region Public-Members

        /// <summary>
        /// Whether the runtime can run the call (null reason). A runtime whose restricted modes cannot reach an MCP
        /// server cannot run calls with job tools.
        /// </summary>
        public bool Supported
        {
            get { return UnsupportedReason == null; }
        }

        /// <summary>
        /// Why the runtime cannot run the call, or null.
        /// </summary>
        public string? UnsupportedReason { get; set; } = null;

        /// <summary>
        /// Extra arguments appended after PolyPrompt's (flag to value, null for a bare flag), in order.
        /// </summary>
        public Dictionary<string, string?> ExtraArguments { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// Files to write: absolute path to content.
        /// </summary>
        public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
