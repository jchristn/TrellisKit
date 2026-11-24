namespace TrellisKit.Infra
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;
    using TrellisKit.Infra.Stacks;

    /// <summary>
    /// Adds every stack of one environment to a CDK app and applies tags and cdk-nag. Used by the CDK entry point
    /// and by the tests, so both synthesize exactly the same app.
    /// </summary>
    public static class InfraAppBuilder
    {
        /// <summary>
        /// Build the full stack set of one property (per-property topology).
        /// </summary>
        /// <param name="app">CDK app.</param>
        /// <param name="config">Environment configuration.</param>
        /// <returns>The stacks.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public static AppStacks Build(App app, EnvironmentConfig config)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(config);

            DeploymentConfig deployment = new DeploymentConfig();
            deployment.Properties.Add(config);
            return BuildDeployment(app, deployment).PerProperty[0];
        }

        /// <summary>
        /// Build every stack of a deployment: one full set per property (per-property topology), or the shared set
        /// plus each property's own stacks (shared topology).
        /// </summary>
        /// <param name="app">CDK app.</param>
        /// <param name="deployment">Deployment configuration.</param>
        /// <returns>The stacks.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">The deployment has no properties, or a shared deployment has no shared configuration.</exception>
        public static DeploymentStacks BuildDeployment(App app, DeploymentConfig deployment)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(deployment);
            if (deployment.Properties.Count == 0) throw new ArgumentException("The deployment has no properties.", nameof(deployment));

            // Context must be complete before the first stack is added.
            foreach (EnvironmentConfig config in deployment.All) CdkContextDefaults.Apply(app, config);

            DeploymentStacks stacks = new DeploymentStacks();
            stacks.Topology = deployment.Topology;

            if (deployment.Topology == DeploymentTopologyEnum.Shared)
            {
                if (deployment.Shared == null) throw new ArgumentException("A shared deployment needs the shared configuration.", nameof(deployment));
                BuildShared(app, deployment, stacks);
            }
            else
            {
                foreach (EnvironmentConfig config in deployment.Properties) stacks.PerProperty.Add(BuildPerProperty(app, config, stacks));
            }

            Validations.Of(app).AddPlugins(new AwsSolutionsNagPlugin(app));
            Aspects.Of(app).Add(new NagSuppressionMetadataAspect());
            return stacks;
        }

        private static AppStacks BuildPerProperty(App app, EnvironmentConfig config, DeploymentStacks deployment)
        {
            AppStacks stacks = new AppStacks();
            stacks.Config = config;
            stacks.GitHubOidc = GitHubOidc(app, config, deployment);

            stacks.Network = new NetworkStack(app, config.StackName("Network"), config, Primary(config, "VPC, subnets, NAT, security groups", false));
            stacks.Dns = new DnsStack(app, config.StackName("Dns"), config, Primary(config, DnsDescription(config), true));
            stacks.EdgeCertificates = new EdgeCertificateStack(app, config.StackName("EdgeCertificates"), config, stacks.Dns,
                Edge(config, "CloudFront certificates (us-east-1)", true));
            stacks.Storage = new StorageStack(app, config.StackName("Storage"), config, Primary(config, "S3 buckets", false));
            stacks.Observability = new ObservabilityStack(app, config.StackName("Observability"), config,
                Primary(config, "Managed Prometheus, log groups, alarm topic", false));
            stacks.Registry = new RegistryStack(app, config.StackName("Registry"), config, Primary(config, "ECR repository", false));
            stacks.Data = new DataStack(app, config.StackName("Data"), config, stacks.Network, Primary(config, "RDS PostgreSQL", false));
            stacks.Media = new MediaStack(app, config.StackName("Media"), config, stacks.Dns, stacks.EdgeCertificates, stacks.Storage,
                Primary(config, "Media CDN, MediaConvert role, job events", true));

            ApiStackDependencies dependencies = new ApiStackDependencies
            {
                Network = stacks.Network,
                Data = stacks.Data,
                Storage = stacks.Storage,
                Registry = stacks.Registry,
                Observability = stacks.Observability,
                Media = stacks.Media.Pipeline
            };
            dependencies.Hosts.Add(new ApiHostBinding { Dns = stacks.Dns, Property = config });
            stacks.Api = new ApiStack(app, config.StackName("Api"), config, dependencies, Primary(config, "ECS Fargate service and ALB", false));

            stacks.Web = new WebStack(app, config.StackName("Web"), config, stacks.Dns, stacks.EdgeCertificates, stacks.Storage,
                Primary(config, "Website distribution", true));
            // The website's /api/* origin is the API host name, so the API must exist first.
            stacks.Web.AddStackDependency(stacks.Api);

            // A property without vanity domains has no redirect stack.
            if (config.Domains.RedirectDomains.Count > 0)
            {
                stacks.Redirect = new RedirectStack(app, config.StackName("Redirect"), config, stacks.Dns, stacks.EdgeCertificates, stacks.Storage,
                    Primary(config, "Vanity domain redirect", true));
            }

            // The SES identity exists unless deployment mail is Mailgun or None (no SES DKIM, MX, or SPF records then).
            if (config.Mail.CreatesSesIdentity)
            {
                stacks.Email = new EmailStack(app, config.StackName("Email"), config, stacks.Dns, Primary(config, "SES identity", false));
            }
            stacks.Alarms = new AlarmsStack(app, config.StackName("Alarms"), config, stacks.Api, stacks.Data, stacks.Observability,
                Primary(config, "CloudWatch alarms", false));
            stacks.Budget = new BudgetStack(app, config.StackName("Budget"), config, Edge(config, "Monthly cost budget", false));
            stacks.GitHubDeploy = new GitHubDeployStack(app, config.StackName("GitHubDeploy"), config, stacks.Registry, stacks.Api,
                stacks.Storage, new List<WebStack> { stacks.Web }, Primary(config, "GitHub Actions OIDC deploy role", false));
            if (stacks.GitHubOidc != null) stacks.GitHubDeploy.AddStackDependency(stacks.GitHubOidc);

            foreach (Stack stack in stacks.All)
            {
                if (stack == stacks.GitHubOidc) continue;
                Tag(stack, config, config);
                TagProperty(stack, config);
            }

            return stacks;
        }

        private static void BuildShared(App app, DeploymentConfig deployment, DeploymentStacks stacks)
        {
            EnvironmentConfig shared = deployment.Shared!;
            SharedStacks set = new SharedStacks();
            set.Config = shared;
            set.GitHubOidc = GitHubOidc(app, shared, stacks);
            stacks.Shared = set;

            set.Network = new NetworkStack(app, shared.StackName("Network"), shared, Primary(shared, "Shared VPC, subnets, NAT, security groups", false));
            set.Storage = new StorageStack(app, shared.StackName("Storage"), shared, Primary(shared, "Shared media, updates, and log buckets", false));
            set.Observability = new ObservabilityStack(app, shared.StackName("Observability"), shared,
                Primary(shared, "Managed Prometheus, log groups, alarm topic", false));
            set.Registry = new RegistryStack(app, shared.StackName("Registry"), shared, Primary(shared, "ECR repository", false));
            set.Data = new DataStack(app, shared.StackName("Data"), shared, set.Network, Primary(shared, "Shared RDS PostgreSQL", false));
            set.MediaPipeline = new MediaPipelineStack(app, shared.StackName("MediaPipeline"), shared, set.Storage,
                Primary(shared, "Media signing keys, MediaConvert role, job events", false));

            foreach (EnvironmentConfig config in deployment.Properties)
            {
                PropertyStacks property = new PropertyStacks();
                property.Config = config;
                property.Dns = new DnsStack(app, config.StackName("Dns"), config, Primary(config, DnsDescription(config), true));
                property.EdgeCertificates = new EdgeCertificateStack(app, config.StackName("EdgeCertificates"), config, property.Dns,
                    Edge(config, "CloudFront certificates (us-east-1)", true));
                stacks.Properties.Add(property);
            }

            ApiStackDependencies dependencies = new ApiStackDependencies
            {
                Network = set.Network,
                Data = set.Data,
                Storage = set.Storage,
                Registry = set.Registry,
                Observability = set.Observability,
                Media = set.MediaPipeline.Pipeline
            };
            foreach (PropertyStacks property in stacks.Properties)
            {
                dependencies.Hosts.Add(new ApiHostBinding
                {
                    Dns = property.Dns!,
                    Property = property.Config,
                    ConstructSuffix = PascalCase(property.Config.Property.Slug)
                });
            }

            foreach (string host in shared.Platform.Hosts)
            {
                PropertyStacks owner = stacks.Properties.First(p => host.EndsWith("." + p.Config.Domains.PrimaryDomain, StringComparison.Ordinal));
                PlatformHostBinding binding = new PlatformHostBinding
                {
                    Dns = owner.Dns!,
                    Host = host,
                    ConstructSuffix = PascalCase(host)
                };
                // External DNS: the certificate is requested by the owner's DNS stack, so its validation records are
                // outputs of a stack that deploys before the API.
                if (owner.Dns!.IsExternal) binding.Certificate = owner.Dns.RequestPlatformCertificate(host, binding.ConstructSuffix);
                dependencies.PlatformHosts.Add(binding);
            }

            set.Api = new ApiStack(app, shared.StackName("Api"), shared, dependencies, Primary(shared, "Shared ECS Fargate service and ALB", false));

            List<WebStack> sites = new List<WebStack>();
            foreach (PropertyStacks property in stacks.Properties)
            {
                EnvironmentConfig config = property.Config;
                property.Media = new PropertyMediaStack(app, config.StackName("Media"), config, property.Dns!, property.EdgeCertificates!, set.Storage,
                    set.MediaPipeline.Pipeline, Primary(config, "Media and updates CDN", true));
                property.Web = new WebStack(app, config.StackName("Web"), config, property.Dns!, property.EdgeCertificates!, set.Storage,
                    Primary(config, "Website bucket and distribution", true));
                property.Web.AddStackDependency(set.Api);
                sites.Add(property.Web);

                if (config.Domains.RedirectDomains.Count > 0)
                {
                    property.Redirect = new RedirectStack(app, config.StackName("Redirect"), config, property.Dns!, property.EdgeCertificates!, set.Storage,
                        Primary(config, "Vanity domain redirect", true));
                }

                // The deployment file's mail section decides the SES identities of every property.
                if (shared.Mail.CreatesSesIdentity)
                {
                    property.Email = new EmailStack(app, config.StackName("Email"), config, property.Dns!, Primary(config, "SES identity", false));
                }
                foreach (Stack stack in property.All)
                {
                    // Cost allocation follows the shared budget, so property stacks carry the shared deployment tag.
                    Tag(stack, config, shared);
                    TagProperty(stack, config);
                }
            }

            set.Alarms = new AlarmsStack(app, shared.StackName("Alarms"), shared, set.Api, set.Data, set.Observability,
                Primary(shared, "CloudWatch alarms", false));
            set.Budget = new BudgetStack(app, shared.StackName("Budget"), shared, Edge(shared, "Monthly cost budget", false));
            set.GitHubDeploy = new GitHubDeployStack(app, shared.StackName("GitHubDeploy"), shared, set.Registry, set.Api, set.Storage, sites,
                Primary(shared, "GitHub Actions OIDC deploy role", false));
            if (set.GitHubOidc != null) set.GitHubDeploy.AddStackDependency(set.GitHubOidc);

            foreach (Stack stack in set.All)
            {
                if (stack == set.GitHubOidc) continue;
                Tag(stack, shared, shared);
            }
        }

        private static GitHubOidcStack? GitHubOidc(App app, EnvironmentConfig config, DeploymentStacks deployment)
        {
            // A provider created outside these stacks needs no account stack.
            if (!String.IsNullOrWhiteSpace(config.GitHub.ExistingOidcProviderArn)) return null;

            // One provider per account and issuer: every environment and property of the synthesis that deploys into
            // the account shares one stack, and other syntheses (dev and prod in one account) produce the same template.
            string name = config.GitHub.OidcProviderStackName;
            foreach (GitHubOidcStack existing in deployment.GitHubOidc)
            {
                if (existing.Account == config.Account && existing.StackName == name) return existing;
            }

            GitHubOidcStack stack = new GitHubOidcStack(app, name, config, new StackProps
            {
                Env = config.PrimaryEnvironment,
                Description = "TrellisKit: GitHub Actions OIDC provider of the account (shared by every environment deployed into it)",
                TerminationProtection = true
            });
            Tags.Of(stack).Add("Engine", "TrellisKit");
            Tags.Of(stack).Add("ManagedBy", "cdk");
            deployment.GitHubOidc.Add(stack);
            return stack;
        }

        private static string DnsDescription(EnvironmentConfig config)
        {
            return config.Dns.IsExternal
                ? "API certificate (DNS managed outside AWS; validation records are outputs)"
                : "Route 53 zones and the API certificate";
        }

        private static void Tag(Stack stack, EnvironmentConfig config, EnvironmentConfig costOwner)
        {
            Tags.Of(stack).Add("Project", config.DeploymentId);
            Tags.Of(stack).Add("Engine", "TrellisKit");
            Tags.Of(stack).Add("Environment", config.Name);
            Tags.Of(stack).Add("ManagedBy", "cdk");
            // Lets each environment's budget see only its own costs when environments share an account.
            Tags.Of(stack).Add(BudgetSettings.CostTagKey, costOwner.StackPrefix);
        }

        private static void TagProperty(Stack stack, EnvironmentConfig config)
        {
            // The opaque id (tag values allow underscores and uppercase), so resources trace back to the property's data.
            Tags.Of(stack).Add("PropertyId", config.Property.Id);
        }

        private static string PascalCase(string id)
        {
            StringBuilder builder = new StringBuilder();
            bool upper = true;
            foreach (char c in id)
            {
                if (!Char.IsLetterOrDigit(c))
                {
                    upper = true;
                    continue;
                }

                builder.Append(upper ? Char.ToUpper(c, CultureInfo.InvariantCulture) : c);
                upper = false;
            }

            return builder.ToString();
        }

        private static StackProps Primary(EnvironmentConfig config, string description, bool crossRegion)
        {
            return new StackProps
            {
                Env = config.PrimaryEnvironment,
                Description = config.DisplayName + " " + config.Name + ": " + description,
                CrossRegionReferences = crossRegion,
                TerminationProtection = config.IsProduction
            };
        }

        private static StackProps Edge(EnvironmentConfig config, string description, bool crossRegion)
        {
            return new StackProps
            {
                Env = config.EdgeEnvironment,
                Description = config.DisplayName + " " + config.Name + ": " + description,
                CrossRegionReferences = crossRegion,
                TerminationProtection = config.IsProduction
            };
        }
    }
}
