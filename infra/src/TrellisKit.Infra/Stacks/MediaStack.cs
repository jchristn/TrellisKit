namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CloudFront;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.SecretsManager;
    using Amazon.CDK.AWS.SNS;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// Media delivery and transcoding of one property (per-property topology): the property's media host distribution
    /// (see <see cref="MediaCdn"/>), and the media pipeline (see <see cref="MediaPipeline"/>): signing key group and
    /// private key secret, MediaConvert service role, and the job events rule, topic, and webhook subscription.
    /// </summary>
    public class MediaStack : Stack
    {
        /// <summary>
        /// Signing keys, MediaConvert role, and job events.
        /// </summary>
        public MediaPipeline Pipeline { get; }

        /// <summary>
        /// CloudFront public key that validates signed cookies.
        /// </summary>
        public PublicKey SigningPublicKey
        {
            get { return Pipeline.SigningPublicKey; }
        }

        /// <summary>
        /// Key group trusted by the protected behaviors.
        /// </summary>
        public KeyGroup KeyGroup
        {
            get { return Pipeline.KeyGroup; }
        }

        /// <summary>
        /// Secret that holds the private half of the signing key pair (filled manually).
        /// </summary>
        public Secret SigningPrivateKeySecret
        {
            get { return Pipeline.SigningPrivateKeySecret; }
        }

        /// <summary>
        /// The media distribution.
        /// </summary>
        public Distribution Distribution { get; }

        /// <summary>
        /// Role MediaConvert assumes to read originals and write renditions.
        /// </summary>
        public Role MediaConvertRole
        {
            get { return Pipeline.MediaConvertRole; }
        }

        /// <summary>
        /// Topic that receives MediaConvert job state changes.
        /// </summary>
        public Topic JobEventsTopic
        {
            get { return Pipeline.JobEventsTopic; }
        }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="dns">Hosted zones.</param>
        /// <param name="certificates">CloudFront certificates (us-east-1).</param>
        /// <param name="storage">Buckets.</param>
        /// <param name="props">Stack properties (cross-region references enabled).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public MediaStack(
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

            Pipeline = new MediaPipeline(this, config, storage);
            Distribution = MediaCdn.Create(this, config, dns, certificates, storage, Pipeline.KeyGroup);

            new CfnOutput(this, "MediaDistributionDomain", new CfnOutputProps { Value = Distribution.DistributionDomainName });
            new CfnOutput(this, "MediaSigningKeyPairId", new CfnOutputProps { Value = SigningPublicKey.PublicKeyId });
            new CfnOutput(this, "MediaConvertRoleArn", new CfnOutputProps { Value = MediaConvertRole.RoleArn });
            new CfnOutput(this, "MediaConvertQueueArn", new CfnOutputProps { Value = Pipeline.Queue.AttrArn });
            new CfnOutput(this, "MediaConvertJobTemplateName", new CfnOutputProps { Value = Pipeline.JobTemplateName });
        }
    }
}
