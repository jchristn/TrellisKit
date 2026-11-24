namespace TrellisKit.Core.PropertyConfig
{
    /// <summary>
    /// Property-owned AI scope policy. Topic instructions remain in ai/prompts/platform.scopeGuardrail.md.
    /// Thread safety: configure before constructing the property runtime.
    /// </summary>
    public class PropertyAiPolicy
    {
        /// <summary>
        /// Permit code requests and code in replies when they fit the property's topic. Default false.
        /// Enabling this supports technical communities without disabling instruction-disclosure checks.
        /// </summary>
        public bool AllowCode { get; set; } = false;
    }
}
