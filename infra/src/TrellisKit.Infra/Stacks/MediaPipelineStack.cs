namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// The shared media pipeline of a shared deployment (see <see cref="MediaPipeline"/>): one signing key group
    /// trusted by every property's media distribution, the private key secret the API signs cookies with, the
    /// MediaConvert role, and the job events rule, topic, and webhook subscription.
    /// </summary>
    public class MediaPipelineStack : Stack
    {
        /// <summary>
        /// Signing keys, MediaConvert role, and job events.
        /// </summary>
        public MediaPipeline Pipeline { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Shared configuration.</param>
        /// <param name="storage">Shared buckets.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public MediaPipelineStack(Construct scope, string id, EnvironmentConfig config, StorageStack storage, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(storage);

            Pipeline = new MediaPipeline(this, config, storage);

            new CfnOutput(this, "MediaSigningKeyPairId", new CfnOutputProps { Value = Pipeline.SigningPublicKey.PublicKeyId });
            new CfnOutput(this, "MediaConvertRoleArn", new CfnOutputProps { Value = Pipeline.MediaConvertRole.RoleArn });
            new CfnOutput(this, "MediaConvertQueueArn", new CfnOutputProps { Value = Pipeline.Queue.AttrArn });
            new CfnOutput(this, "MediaConvertJobTemplateName", new CfnOutputProps { Value = Pipeline.JobTemplateName });
        }
    }
}
