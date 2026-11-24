namespace TrellisKit.Infra.Config
{
    /// <summary>
    /// How the properties of one deployment map to stacks.
    /// </summary>
    public enum DeploymentTopologyEnum
    {
        /// <summary>
        /// Every property gets its own full stack set (network, database, API, storage, CDN, DNS, email). The default.
        /// </summary>
        PerProperty,

        /// <summary>
        /// One shared network, database, API, storage, and observability set serves every property (the backend runs
        /// in MultiProperty mode); each property gets its own DNS zones, certificates, website, media CDN, redirect,
        /// and email identity.
        /// </summary>
        Shared
    }
}
