namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CertificateManager;

    /// <summary>
    /// A certificate requested by <see cref="CertificateRequester"/> (external DNS mode): the ACM certificate, the
    /// custom resource that requested it, and the host names it covers in request order.
    /// </summary>
    public class ExternalCertificate
    {
        /// <summary>
        /// The certificate, for load balancer listeners and CloudFront distributions.
        /// </summary>
        public ICertificate Certificate { get; }

        /// <summary>
        /// The custom resource that requested the certificate; its Ref is the certificate ARN.
        /// </summary>
        public CustomResource Request { get; }

        /// <summary>
        /// Host names covered by the certificate (the first is the domain name, the rest subject alternative names).
        /// </summary>
        public IReadOnlyList<string> DomainNames { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="certificate">Certificate.</param>
        /// <param name="request">Custom resource.</param>
        /// <param name="domainNames">Host names.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ExternalCertificate(ICertificate certificate, CustomResource request, IReadOnlyList<string> domainNames)
        {
            Certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
            Request = request ?? throw new ArgumentNullException(nameof(request));
            DomainNames = domainNames ?? throw new ArgumentNullException(nameof(domainNames));
        }
    }
}
