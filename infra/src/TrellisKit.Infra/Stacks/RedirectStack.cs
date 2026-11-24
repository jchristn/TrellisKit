namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CertificateManager;
    using Amazon.CDK.AWS.CloudFront;
    using Amazon.CDK.AWS.CloudFront.Origins;
    using Amazon.CDK.AWS.Route53;
    using Amazon.CDK.AWS.Route53.Targets;
    using TrellisKit.Infra.Config;
    using Constructs;
    using Function = Amazon.CDK.AWS.CloudFront.Function;

    /// <summary>
    /// Every vanity domain of the property (domains.redirects) and its www host: a small distribution whose viewer-request CloudFront
    /// Function answers every request with a 301 to the website, preserving path and query string. Route 53 alias
    /// records give the zone apex a valid target and TLS certificate (a CNAME is not allowed at an apex); in external
    /// DNS mode the records are outputs (DnsRecordRedirect*) for an ALIAS, ANAME, or flattened CNAME at the provider.
    /// </summary>
    public class RedirectStack : Stack
    {
        /// <summary>
        /// The redirect distribution.
        /// </summary>
        public Distribution Distribution { get; }

        /// <summary>
        /// The redirect function.
        /// </summary>
        public Function RedirectFunction { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="dns">Hosted zones.</param>
        /// <param name="certificates">CloudFront certificates (us-east-1).</param>
        /// <param name="storage">Buckets (access logs).</param>
        /// <param name="props">Stack properties (cross-region references enabled).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public RedirectStack(
            Construct scope,
            string id,
            EnvironmentConfig config,
            DnsStack dns,
            EdgeCertificateStack certificates,
            StorageStack storage,
            IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(dns);
            ArgumentNullException.ThrowIfNull(certificates);
            ArgumentNullException.ThrowIfNull(storage);
            if (config.Domains.RedirectDomains.Count == 0 || certificates.RedirectCertificate == null)
            {
                throw new ArgumentException("The property has no redirect domains; do not create a redirect stack.", nameof(config));
            }

            List<string> hosts = new List<string>();
            foreach (string domain in config.Domains.RedirectDomains)
            {
                hosts.Add(domain);
                hosts.Add(config.Domains.RedirectWwwHost(domain));
            }

            RedirectFunction = new Function(this, "RedirectFunction", new FunctionProps
            {
                FunctionName = config.ResourceName("vanity-redirect"),
                Comment = "301 to " + config.Domains.RedirectTargetUrl + " preserving path",
                Runtime = FunctionRuntime.JS_2_0,
                Code = FunctionCode.FromInline(BuildRedirectFunction(config.Domains.RedirectTargetUrl))
            });

            Distribution = new Distribution(this, "RedirectDistribution", new DistributionProps
            {
                Comment = config.Domains.RedirectDomains[0] + " redirect",
                DomainNames = hosts.ToArray(),
                Certificate = certificates.RedirectCertificate!,
                MinimumProtocolVersion = SecurityPolicyProtocol.TLS_V1_2_2021,
                HttpVersion = HttpVersion.HTTP2_AND_3,
                EnableIpv6 = true,
                PriceClass = config.Web.PriceClass,
                EnableLogging = true,
                LogBucket = storage.AccessLogBucket,
                LogFilePrefix = "cloudfront/redirect/",
                DefaultBehavior = new BehaviorOptions
                {
                    // Never reached: the viewer-request function answers every request.
                    Origin = new HttpOrigin(config.Domains.PrimaryDomain, new HttpOriginProps
                    {
                        ProtocolPolicy = OriginProtocolPolicy.HTTPS_ONLY,
                        OriginSslProtocols = new OriginSslPolicy[] { OriginSslPolicy.TLS_V1_2 }
                    }),
                    ViewerProtocolPolicy = ViewerProtocolPolicy.ALLOW_ALL,
                    AllowedMethods = AllowedMethods.ALLOW_GET_HEAD,
                    CachePolicy = CachePolicy.CACHING_DISABLED,
                    FunctionAssociations = new IFunctionAssociation[]
                    {
                        new FunctionAssociation { Function = RedirectFunction, EventType = FunctionEventType.VIEWER_REQUEST }
                    }
                }
            });
            CloudFrontNag.SuppressCommon(Distribution);

            for (int d = 0; d < config.Domains.RedirectDomains.Count; d++)
            {
                string domain = config.Domains.RedirectDomains[d];
                string index = d == 0 ? String.Empty : d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (config.Dns.IsExternal)
                {
                    DnsRecordOutputs.AddTarget(this, "RedirectApex" + index, config, domain, Distribution.DistributionDomainName,
                        "Vanity domain (301 to " + config.Domains.RedirectTargetUrl + ")");
                    DnsRecordOutputs.AddTarget(this, "RedirectWww" + index, config, config.Domains.RedirectWwwHost(domain), Distribution.DistributionDomainName,
                        "Vanity www host (301 to " + config.Domains.RedirectTargetUrl + ")");
                    continue;
                }

                AddAliases(dns.RedirectZones[domain], domain, "Apex" + index);
                AddAliases(dns.RedirectZones[domain], config.Domains.RedirectWwwHost(domain), "Www" + index);
            }

            new CfnOutput(this, "RedirectDistributionDomain", new CfnOutputProps { Value = Distribution.DistributionDomainName });

            if (config.Dns.IsExternal)
            {
                new CertificateIssuedCheck(this, "CertificateCheck", config, "certificate-check-redirect",
                    new ICertificate[] { certificates.RedirectCertificate! }, certificates.PendingHint).Gate(this);
            }
        }

        private void AddAliases(IHostedZone zone, string host, string suffix)
        {
            new ARecord(this, "RedirectAliasA" + suffix, new ARecordProps
            {
                Zone = zone,
                RecordName = host,
                Target = RecordTarget.FromAlias(new CloudFrontTarget(Distribution))
            });
            new AaaaRecord(this, "RedirectAliasAaaa" + suffix, new AaaaRecordProps
            {
                Zone = zone,
                RecordName = host,
                Target = RecordTarget.FromAlias(new CloudFrontTarget(Distribution))
            });
        }

        private static string BuildRedirectFunction(string targetUrl)
        {
            StringBuilder js = new StringBuilder();
            js.Append("var TARGET = '" + targetUrl + "';\n");
            js.Append("function handler(event) {\n");
            js.Append("  var request = event.request;\n");
            js.Append("  var parts = [];\n");
            js.Append("  for (var key in request.querystring) {\n");
            js.Append("    var item = request.querystring[key];\n");
            js.Append("    if (item.multiValue) { for (var i = 0; i < item.multiValue.length; i++) { parts.push(key + '=' + item.multiValue[i].value); } }\n");
            js.Append("    else if (item.value === '') { parts.push(key); }\n");
            js.Append("    else { parts.push(key + '=' + item.value); }\n");
            js.Append("  }\n");
            js.Append("  var query = parts.length > 0 ? '?' + parts.join('&') : '';\n");
            js.Append("  return {\n");
            js.Append("    statusCode: 301,\n");
            js.Append("    statusDescription: 'Moved Permanently',\n");
            js.Append("    headers: { location: { value: TARGET + request.uri + query }, 'cache-control': { value: 'max-age=86400' } }\n");
            js.Append("  };\n");
            js.Append("}\n");
            return js.ToString();
        }
    }
}
