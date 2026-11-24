namespace TrellisKit.Infra
{
    using System.Collections.Generic;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;

    /// <summary>
    /// The stacks one property owns in a shared deployment, in deployment order: hosted zones and API certificate,
    /// CloudFront certificates, media distribution, website (with its own bucket), vanity redirect, and SES identity
    /// (unless the deployment's mail.provider is Mailgun or None).
    /// </summary>
    public class PropertyStacks
    {
        /// <summary>
        /// Property configuration the stacks were built from.
        /// </summary>
        public EnvironmentConfig Config { get; set; } = new EnvironmentConfig();

        /// <summary>
        /// Hosted zones and the API certificate.
        /// </summary>
        public DnsStack? Dns { get; set; }

        /// <summary>
        /// CloudFront certificates (us-east-1).
        /// </summary>
        public EdgeCertificateStack? EdgeCertificates { get; set; }

        /// <summary>
        /// Media host distribution.
        /// </summary>
        public PropertyMediaStack? Media { get; set; }

        /// <summary>
        /// Website bucket and distribution.
        /// </summary>
        public WebStack? Web { get; set; }

        /// <summary>
        /// Vanity domain redirect, or null when the property has none.
        /// </summary>
        public RedirectStack? Redirect { get; set; }

        /// <summary>
        /// SES identity, or null when the deployment's mail.provider is Mailgun or None.
        /// </summary>
        public EmailStack? Email { get; set; }

        /// <summary>
        /// All stacks of the property in deployment order.
        /// </summary>
        public List<Stack> All
        {
            get
            {
                List<Stack> stacks = new List<Stack>();
                Stack?[] ordered = new Stack?[] { Dns, EdgeCertificates, Media, Web, Redirect, Email };
                foreach (Stack? stack in ordered)
                {
                    if (stack != null) stacks.Add(stack);
                }

                return stacks;
            }
        }
    }
}
