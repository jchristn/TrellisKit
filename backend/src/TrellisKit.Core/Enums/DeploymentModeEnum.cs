namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Deployment mode (docs/TRELLISKIT.md section 10).
    /// </summary>
    public enum DeploymentModeEnum
    {
        /// <summary>
        /// One backend serves one property, loaded from property.directory. Every request is implicitly for it.
        /// </summary>
        SingleProperty,

        /// <summary>
        /// One backend serves several registered properties; every request resolves to exactly one property and data is isolated per property.
        /// </summary>
        MultiProperty
    }
}
