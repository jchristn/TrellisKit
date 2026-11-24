namespace TrellisKit.Infra
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// CDK context the app needs regardless of how it is launched (CLI or tests): feature flags, and the
    /// availability zone list of each target account and region so synthesis never calls AWS.
    /// Values already present in context (for example from -c on the command line) are not overwritten.
    /// </summary>
    public static class CdkContextDefaults
    {
        /// <summary>
        /// Feature flags applied to every synthesis.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, object> FeatureFlags = new Dictionary<string, object>
        {
            ["@aws-cdk/core:defaultCrossStackReferences"] = "strong",
            ["@aws-cdk/core:checkSecretUsage"] = true,
            ["@aws-cdk/core:target-partitions"] = new string[] { "aws" },
            ["@aws-cdk/aws-iam:minimizePolicies"] = true,
            ["@aws-cdk/aws-iam:standardizedServicePrincipals"] = true,
            ["@aws-cdk/aws-ecs:arnFormatIncludesClusterName"] = true,
            ["@aws-cdk/aws-ecs:disableExplicitDeploymentControllerForCircuitBreaker"] = true,
            ["@aws-cdk/aws-s3:createDefaultLoggingPolicy"] = true,
            ["@aws-cdk/aws-s3:serverAccessLogsUseBucketPolicy"] = true,
            ["@aws-cdk/aws-kms:aliasNameRef"] = true,
            ["@aws-cdk/aws-kms:reduceCrossAccountRegionPolicyScope"] = true,
            ["@aws-cdk/aws-ec2:restrictDefaultSecurityGroup"] = true,
            ["@aws-cdk/aws-cloudfront:defaultSecurityPolicyTLSv1.2_2021"] = true,
            ["@aws-cdk/aws-route53-patters:useCertificate"] = true
        };

        /// <summary>
        /// Apply the defaults. Call before any stack is added to the app.
        /// </summary>
        /// <param name="app">CDK app.</param>
        /// <param name="config">Environment configuration.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public static void Apply(App app, EnvironmentConfig config)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(config);

            foreach (KeyValuePair<string, object> flag in FeatureFlags)
            {
                SetIfMissing(app, flag.Key, flag.Value);
            }

            SetIfMissing(app, AvailabilityZonesKey(config.Account, config.Region), config.Network.AvailabilityZones.ToArray());
        }

        /// <summary>
        /// The context key CDK uses to cache the availability zones of an account and region.
        /// </summary>
        /// <param name="account">Account id.</param>
        /// <param name="region">Region.</param>
        /// <returns>Context key.</returns>
        public static string AvailabilityZonesKey(string account, string region)
        {
            return "availability-zones:account=" + account + ":region=" + region;
        }

        private static void SetIfMissing(App app, string key, object value)
        {
            if (app.Node.TryGetContext(key) == null) app.Node.SetContext(key, value);
        }
    }
}
