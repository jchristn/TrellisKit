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
    /// ACM certificates for CloudFront. CloudFront only accepts certificates from us-east-1, so this stack is
    /// deployed there. In Route 53 DNS mode it validates through the hosted zones of <see cref="DnsStack"/> (CDK
    /// cross-region references); in external DNS mode it requests the certificates through
    /// <see cref="CertificateRequester"/> and lists their validation records as outputs (DnsValidation*).
    /// </summary>
    public class EdgeCertificateStack : Stack
    {
        /// <summary>
        /// Certificate for the website apex and www host.
        /// </summary>
        public ICertificate WebCertificate { get; }

        /// <summary>
        /// Certificate for the media host (which also serves live updates).
        /// </summary>
        public ICertificate MediaCertificate { get; }

        /// <summary>
        /// Certificate for every vanity apex and its www host, or null when the property has no vanity domains.
        /// </summary>
        public ICertificate? RedirectCertificate { get; }

        /// <summary>
        /// Requests certificates in external DNS mode, or null in Route 53 mode.
        /// </summary>
        public CertificateRequester? Requester { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="dns">Stack that owns the hosted zones (Route 53 mode).</param>
        /// <param name="props">Stack properties (must target us-east-1 with cross-region references enabled).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public EdgeCertificateStack(Construct scope, string id, EnvironmentConfig config, DnsStack dns, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(dns);

            List<string> redirectNames = new List<string>();
            foreach (string domain in config.Domains.RedirectDomains)
            {
                redirectNames.Add(domain);
                redirectNames.Add(config.Domains.RedirectWwwHost(domain));
            }

            if (config.Dns.IsExternal)
            {
                Requester = new CertificateRequester(this, "Certificates", config, "edge-certificate-requests");
                WebCertificate = Requester.Request("Web", new List<string> { config.Domains.PrimaryDomain, config.Domains.WwwHost }).Certificate;
                MediaCertificate = Requester.Request("Media", new List<string> { config.Domains.MediaHost }).Certificate;
                if (redirectNames.Count > 0) RedirectCertificate = Requester.Request("Redirect", redirectNames).Certificate;
                return;
            }

            WebCertificate = new Certificate(this, "WebCertificate", new CertificateProps
            {
                DomainName = config.Domains.PrimaryDomain,
                SubjectAlternativeNames = new string[] { config.Domains.WwwHost },
                Validation = CertificateValidation.FromDns(dns.PrimaryZone!)
            });

            MediaCertificate = new Certificate(this, "MediaCertificate", new CertificateProps
            {
                DomainName = config.Domains.MediaHost,
                Validation = CertificateValidation.FromDns(dns.PrimaryZone!)
            });

            if (redirectNames.Count > 0)
            {
                Dictionary<string, IHostedZone> zones = new Dictionary<string, IHostedZone>();
                foreach (string domain in config.Domains.RedirectDomains)
                {
                    zones[domain] = dns.RedirectZones[domain];
                    zones[config.Domains.RedirectWwwHost(domain)] = dns.RedirectZones[domain];
                }

                RedirectCertificate = new Certificate(this, "RedirectCertificate", new CertificateProps
                {
                    DomainName = redirectNames[0],
                    SubjectAlternativeNames = redirectNames.GetRange(1, redirectNames.Count - 1).ToArray(),
                    Validation = CertificateValidation.FromDnsMultiZone(zones)
                });
            }
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
    }
}
