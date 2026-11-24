namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using Amazon.CDK.AWS.Route53;
    using Amazon.CDK.AWS.SES;
    using TrellisKit.Infra.Config;
    using Constructs;
    using SesIdentity = Amazon.CDK.AWS.SES.Identity;

    /// <summary>
    /// SES domain identity for the primary domain with Easy DKIM (CNAME records), a custom MAIL FROM domain
    /// (MX and SPF records), and a DMARC record, created in the primary hosted zone, or listed as DnsRecordSes* and
    /// DnsRecordDmarc outputs in external DNS mode. New SES accounts start in the sandbox: request production access
    /// manually (see infra/README.md).
    /// </summary>
    public class EmailStack : Stack
    {
        /// <summary>
        /// The domain identity.
        /// </summary>
        public EmailIdentity Identity { get; }

        /// <summary>
        /// Configuration set used for sending (reputation metrics, TLS required).
        /// </summary>
        public ConfigurationSet ConfigurationSet { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="dns">Hosted zones.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public EmailStack(Construct scope, string id, EnvironmentConfig config, DnsStack dns, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(dns);

            string domain = config.Domains.PrimaryDomain;

            ConfigurationSet = new ConfigurationSet(this, "ConfigurationSet", new ConfigurationSetProps
            {
                ConfigurationSetName = config.ResourceName("email"),
                ReputationMetrics = true,
                SendingEnabled = true,
                TlsPolicy = ConfigurationSetTlsPolicy.REQUIRE
            });

            string mailFrom = config.Email.MailFromLabel + "." + domain;
            string dmarc = "v=DMARC1; p=" + config.Email.DmarcPolicy + "; rua=mailto:" + config.Email.DmarcReportLocalPart + "@" + domain + "; adkim=r; aspf=r";

            if (config.Dns.IsExternal)
            {
                // No hosted zone: SES verifies the identity once the DKIM records exist at the operator's DNS provider.
                Identity = new EmailIdentity(this, "DomainIdentity", new EmailIdentityProps
                {
                    Identity = SesIdentity.Domain(domain),
                    DkimSigning = true,
                    MailFromDomain = mailFrom,
                    MailFromBehaviorOnMxFailure = MailFromBehaviorOnMxFailure.USE_DEFAULT_VALUE,
                    ConfigurationSet = ConfigurationSet
                });

                DnsRecordOutputs.Add(this, "SesDkim1", Identity.DkimDnsTokenName1, "CNAME", Identity.DkimDnsTokenValue1, "SES DKIM (verifies " + domain + ").");
                DnsRecordOutputs.Add(this, "SesDkim2", Identity.DkimDnsTokenName2, "CNAME", Identity.DkimDnsTokenValue2, "SES DKIM (verifies " + domain + ").");
                DnsRecordOutputs.Add(this, "SesDkim3", Identity.DkimDnsTokenName3, "CNAME", Identity.DkimDnsTokenValue3, "SES DKIM (verifies " + domain + ").");
                DnsRecordOutputs.Add(this, "SesMailFromMx", mailFrom, "MX", "10 feedback-smtp." + Region + ".amazonses.com", "SES custom MAIL FROM domain (priority 10).");
                DnsRecordOutputs.Add(this, "SesMailFromSpf", mailFrom, "TXT", "v=spf1 include:amazonses.com ~all", "SPF of the SES MAIL FROM domain.");
                DnsRecordOutputs.Add(this, "Dmarc", "_dmarc." + domain, "TXT", dmarc, "DMARC policy of " + domain + ".");
                new CfnOutput(this, "SesIdentity", new CfnOutputProps { Value = domain });
                return;
            }

            Identity = new EmailIdentity(this, "DomainIdentity", new EmailIdentityProps
            {
                Identity = SesIdentity.PublicHostedZone(dns.PrimaryZone!),
                DkimSigning = true,
                MailFromDomain = mailFrom,
                MailFromBehaviorOnMxFailure = MailFromBehaviorOnMxFailure.USE_DEFAULT_VALUE,
                ConfigurationSet = ConfigurationSet
            });

            new TxtRecord(this, "DmarcRecord", new TxtRecordProps
            {
                Zone = dns.PrimaryZone!,
                RecordName = "_dmarc." + domain,
                Values = new string[] { dmarc }
            });

            new CfnOutput(this, "SesIdentity", new CfnOutputProps { Value = domain });
        }
    }
}
