namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CertificateManager;
    using Amazon.CDK.AWS.Route53;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// The regional ACM certificate of the API load balancer and, in Route 53 DNS mode (the default), the hosted zones
    /// of the primary domain and each vanity domain of the property. In external DNS mode there are no zones: the
    /// certificate is requested with DNS validation through <see cref="CertificateRequester"/> and its validation
    /// records are outputs (DnsValidation*), so this stack deploys at once and can be deployed first. In the shared
    /// topology it also holds the certificates of platform hosts under the property's domain when DNS is external.
    /// CloudFront certificates live in <see cref="EdgeCertificateStack"/> (us-east-1).
    /// </summary>
    public class DnsStack : Stack
    {
        /// <summary>
        /// Hosted zone of the primary domain (domains.web, prefixed with dev. in development), or null in external DNS mode.
        /// </summary>
        public PublicHostedZone? PrimaryZone { get; }

        /// <summary>
        /// Hosted zones of the vanity domains (domains.redirects), keyed by apex. Empty when the property has none or
        /// DNS is external.
        /// </summary>
        public Dictionary<string, PublicHostedZone> RedirectZones { get; } = new Dictionary<string, PublicHostedZone>();

        /// <summary>
        /// Regional certificate for the API host, used by the load balancer.
        /// </summary>
        public ICertificate ApiCertificate { get; }

        /// <summary>
        /// Requests certificates in external DNS mode, or null in Route 53 mode.
        /// </summary>
        public CertificateRequester? Requester { get; }

        /// <summary>
        /// True when the property's DNS is managed outside AWS.
        /// </summary>
        public bool IsExternal
        {
            get { return Requester != null; }
        }

        /// <summary>
        /// What the operator does when a certificate of this stack is still pending (external DNS mode).
        /// </summary>
        public string PendingHint
        {
            get
            {
                return "Create the DnsValidation* CNAME records from the outputs of stack " + StackName + " (" + Region
                    + ") at your DNS provider, wait until ACM issues the certificate, and deploy again (infra/README.md, Managing DNS yourself).";
            }
        }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public DnsStack(Construct scope, string id, EnvironmentConfig config, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (config.Dns.IsExternal)
            {
                Requester = new CertificateRequester(this, "Certificates", config, "certificate-requests");
                ApiCertificate = Requester.Request("Api", new List<string> { config.Domains.ApiOriginHost }).Certificate;
                return;
            }

            PrimaryZone = new PublicHostedZone(this, "PrimaryZone", new PublicHostedZoneProps
            {
                ZoneName = config.Domains.PrimaryDomain,
                Comment = config.Property.Name + " website, API, and media (" + config.Name + ")"
            });

            for (int i = 0; i < config.Domains.RedirectDomains.Count; i++)
            {
                string domain = config.Domains.RedirectDomains[i];
                RedirectZones[domain] = new PublicHostedZone(this, i == 0 ? "RedirectZone" : "RedirectZone" + i, new PublicHostedZoneProps
                {
                    ZoneName = domain,
                    Comment = "Vanity domain that redirects to " + config.Domains.RedirectTargetUrl
                });
            }

            ApiCertificate = new Certificate(this, "ApiCertificate", new CertificateProps
            {
                DomainName = config.Domains.ApiOriginHost,
                Validation = CertificateValidation.FromDns(PrimaryZone)
            });

            new CfnOutput(this, "PrimaryZoneNameServers", new CfnOutputProps
            {
                Value = Fn.Join(",", PrimaryZone.HostedZoneNameServers!),
                Description = "Set these as the nameservers of " + config.Domains.PrimaryDomain + " at the registrar (or delegate from the parent zone)."
            });
            int index = 0;
            foreach (KeyValuePair<string, PublicHostedZone> zone in RedirectZones)
            {
                new CfnOutput(this, index == 0 ? "RedirectZoneNameServers" : "RedirectZoneNameServers" + index, new CfnOutputProps
                {
                    Value = Fn.Join(",", zone.Value.HostedZoneNameServers!),
                    Description = "Set these as the nameservers of " + zone.Key + " at the registrar (or delegate from the parent zone)."
                });
                index++;
            }
        }

        /// <summary>
        /// External DNS mode: request the regional certificate of a platform host under this property's domain (shared
        /// topology), so its validation records are outputs of this stack, which deploys before the shared API.
        /// </summary>
        /// <param name="host">Platform host of this environment.</param>
        /// <param name="constructSuffix">Unique suffix of the construct ids and output keys.</param>
        /// <returns>The certificate.</returns>
        /// <exception cref="ArgumentNullException">The host is empty.</exception>
        /// <exception cref="InvalidOperationException">DNS is not external.</exception>
        public ICertificate RequestPlatformCertificate(string host, string constructSuffix)
        {
            if (String.IsNullOrWhiteSpace(host)) throw new ArgumentNullException(nameof(host));
            if (Requester == null) throw new InvalidOperationException("Platform certificates are requested here only when DNS is external.");
            return Requester.Request("Platform" + (constructSuffix ?? String.Empty), new List<string> { host }).Certificate;
        }
    }
}
