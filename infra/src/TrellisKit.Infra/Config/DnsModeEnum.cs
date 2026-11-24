namespace TrellisKit.Infra.Config
{
    /// <summary>
    /// Who manages the DNS records of a property (dns.mode in deploy/infra.json or the deployment file).
    /// </summary>
    public enum DnsModeEnum
    {
        /// <summary>
        /// The stacks create Route 53 hosted zones for the primary and vanity domains, every alias and email record, and
        /// validate certificates through those zones. The operator only delegates the nameservers. The default.
        /// </summary>
        Route53,

        /// <summary>
        /// The operator manages DNS at another provider (for example the registrar). No hosted zone and no Route 53
        /// record is created; certificates are requested with DNS validation and their validation records, like every
        /// other record the operator must create, are stack outputs (infra/README.md, "Managing DNS yourself").
        /// </summary>
        External
    }
}
