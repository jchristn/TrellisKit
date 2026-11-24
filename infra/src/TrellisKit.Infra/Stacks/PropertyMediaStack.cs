namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CloudFront;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// One property's media host distribution in a shared deployment (see <see cref="MediaCdn"/>), serving the shared
    /// media and updates buckets and trusting the shared signing key group.
    /// </summary>
    public class PropertyMediaStack : Stack
    {
        /// <summary>
        /// The media distribution.
        /// </summary>
        public Distribution Distribution { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Property configuration.</param>
        /// <param name="dns">The property's hosted zones.</param>
        /// <param name="certificates">The property's CloudFront certificates (us-east-1).</param>
        /// <param name="storage">Shared buckets.</param>
        /// <param name="pipeline">Shared media pipeline.</param>
        /// <param name="props">Stack properties (cross-region references enabled).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public PropertyMediaStack(
            Construct scope,
            string id,
            EnvironmentConfig config,
            DnsStack dns,
            EdgeCertificateStack certificates,
            StorageStack storage,
            MediaPipeline pipeline,
            IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(pipeline);

            Distribution = MediaCdn.Create(this, config, dns, certificates, storage, pipeline.KeyGroup);
            new CfnOutput(this, "MediaDistributionDomain", new CfnOutputProps { Value = Distribution.DistributionDomainName });
        }
    }
}
