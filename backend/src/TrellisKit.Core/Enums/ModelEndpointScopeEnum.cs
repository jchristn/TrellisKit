namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who owns a model endpoint.
    /// </summary>
    public enum ModelEndpointScopeEnum
    {
        /// <summary>
        /// Owned by one property.
        /// </summary>
        Property,

        /// <summary>
        /// Deployment-wide (multi-property; reserved).
        /// </summary>
        Deployment
    }
}
