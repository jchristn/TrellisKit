namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CloudFront;
    using TrellisKit.Infra.Nag;

    /// <summary>
    /// cdk-nag suppressions shared by every CloudFront distribution in the app.
    /// </summary>
    public static class CloudFrontNag
    {
        /// <summary>
        /// Suppress the CloudFront findings that are accepted platform-wide.
        /// </summary>
        /// <param name="distribution">Distribution to annotate.</param>
        /// <exception cref="ArgumentNullException">The distribution is null.</exception>
        public static void SuppressCommon(Distribution distribution)
        {
            SuppressCommon(distribution, false);
        }

        /// <summary>
        /// Suppress the CloudFront findings that are accepted platform-wide, and optionally acknowledge the CDK
        /// warning about Origin Access Control on an imported bucket.
        /// </summary>
        /// <param name="distribution">Distribution to annotate.</param>
        /// <param name="importedOacBucket">True when the S3 origin is an imported bucket whose policy is managed in the storage stack.</param>
        /// <exception cref="ArgumentNullException">The distribution is null.</exception>
        public static void SuppressCommon(Distribution distribution, bool importedOacBucket)
        {
            ArgumentNullException.ThrowIfNull(distribution);
            NagHelper.Suppress(distribution, "AwsSolutions-CFR1",
                "The service is offered worldwide, so no geo restriction is applied.");
            NagHelper.Suppress(distribution, "AwsSolutions-CFR2",
                "AWS WAF is deferred at launch for cost (about 10 USD a month per web ACL plus requests). The API enforces rate limits and authentication; add a us-east-1 web ACL when traffic warrants it.");
            if (importedOacBucket)
            {
                Annotations.Of(distribution).AcknowledgeWarning(
                    "@aws-cdk/aws-cloudfront-origins:updateImportedBucketPolicyOac",
                    "StorageStack grants s3:GetObject to CloudFront distributions of this account; a per-distribution grant would create a cycle between the storage and CDN stacks.");
            }
        }
    }
}
