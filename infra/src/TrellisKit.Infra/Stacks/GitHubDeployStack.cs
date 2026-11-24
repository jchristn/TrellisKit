namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.S3;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;
    using Constructs;

    /// <summary>
    /// The deploy role trusted only by jobs of one GitHub deployment environment
    /// of the repository configured by the property (github.owner and github.repository in deploy/infra.json), or by
    /// the deployment file in the shared topology, through the account's GitHub OIDC provider (<see cref="GitHubOidcStack"/>,
    /// or GitHubSettings.ExistingOidcProviderArn). The role can push the server image, roll the ECS service, sync the
    /// website buckets, and invalidate the website distributions of the deployment. Nothing else.
    /// </summary>
    public class GitHubDeployStack : Stack
    {
        /// <summary>
        /// The deploy role.
        /// </summary>
        public Role DeployRole { get; }

        /// <summary>
        /// ARN of the OIDC provider the role trusts.
        /// </summary>
        public string OidcProviderArn { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="registry">Image repository.</param>
        /// <param name="api">ECS service.</param>
        /// <param name="storage">Storage stack (the website bucket in the per-property topology).</param>
        /// <param name="sites">Website stacks the role deploys (one per property).</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public GitHubDeployStack(
            Construct scope,
            string id,
            EnvironmentConfig config,
            RegistryStack registry,
            ApiStack api,
            StorageStack storage,
            IReadOnlyList<WebStack> sites,
            IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(sites);
            if (sites.Count == 0) throw new ArgumentException("The deploy role needs at least one website.", nameof(sites));

            List<string> bucketArns = new List<string>();
            List<string> objectArns = new List<string>();
            List<string> distributionArns = new List<string>();
            foreach (WebStack site in sites)
            {
                Bucket bucket = site.OwnedWebsiteBucket ?? storage.WebsiteBucket
                    ?? throw new ArgumentException("A website stack has no website bucket.", nameof(sites));
                bucketArns.Add(bucket.BucketArn);
                objectArns.Add(bucket.ArnForObjects("*"));
                distributionArns.Add("arn:" + Aws.PARTITION + ":cloudfront::" + config.Account + ":distribution/" + site.Distribution.DistributionId);
            }

            GitHubSettings github = config.GitHub;

            // The provider is an account singleton: it lives in the account-level GitHubOidcStack (or outside these stacks),
            // never here, so dev and prod can share an account.
            OidcProviderArn = String.IsNullOrWhiteSpace(github.ExistingOidcProviderArn)
                ? github.OidcProviderArnIn(config.Account)
                : github.ExistingOidcProviderArn;

            DeployRole = new Role(this, "DeployRole", new RoleProps
            {
                RoleName = config.ResourceName("github-deploy"),
                Description = "GitHub Actions deploy role for " + github.Subject,
                MaxSessionDuration = Duration.Minutes(github.MaxSessionMinutes),
                AssumedBy = new FederatedPrincipal(OidcProviderArn, new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        [github.OidcProviderHost + ":aud"] = github.Audience,
                        [github.OidcProviderHost + ":sub"] = github.Subject
                    }
                }, "sts:AssumeRoleWithWebIdentity")
            });

            DeployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "EcrLogin",
                Actions = new string[] { "ecr:GetAuthorizationToken" },
                Resources = new string[] { "*" }
            }));
            DeployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "EcrPushServerImage",
                Actions = new string[]
                {
                    "ecr:BatchCheckLayerAvailability",
                    "ecr:BatchGetImage",
                    "ecr:CompleteLayerUpload",
                    "ecr:DescribeImages",
                    "ecr:GetDownloadUrlForLayer",
                    "ecr:InitiateLayerUpload",
                    "ecr:PutImage",
                    "ecr:UploadLayerPart"
                },
                Resources = new string[] { registry.Repository.RepositoryArn }
            }));
            DeployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "EcsRollService",
                Actions = new string[] { "ecs:UpdateService", "ecs:DescribeServices" },
                Resources = new string[] { api.Service.ServiceArn }
            }));
            DeployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "WebsiteBucketList",
                Actions = new string[] { "s3:ListBucket" },
                Resources = bucketArns.ToArray()
            }));
            DeployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "WebsiteBucketSync",
                Actions = new string[] { "s3:GetObject", "s3:PutObject", "s3:DeleteObject" },
                Resources = objectArns.ToArray()
            }));
            DeployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "WebsiteInvalidate",
                Actions = new string[] { "cloudfront:CreateInvalidation", "cloudfront:GetInvalidation", "cloudfront:ListInvalidations" },
                Resources = distributionArns.ToArray()
            }));

            NagHelper.SuppressWildcards(DeployRole,
                "ecr:GetAuthorizationToken only supports Resource *. The object wildcards are limited to the website buckets, which aws s3 sync --delete must be able to write and delete in full.",
                NagHelper.AnyResourcePattern,
                NagHelper.ResourcePattern("WebsiteBucket", "Arn", "\\/\\*"));

            new CfnOutput(this, "DeployRoleArn", new CfnOutputProps
            {
                Value = DeployRole.RoleArn,
                Description = "Set as the AWS_DEPLOY_ROLE_ARN secret of the GitHub environment " + github.DeploymentEnvironment
            });
        }
    }
}
