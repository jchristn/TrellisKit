namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.CDK.AWS.EC2;
    using Amazon.CDK.AWS.Logs;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;
    using Constructs;

    /// <summary>
    /// VPC across two AZs with public (ALB, NAT), private with egress (Fargate), and isolated (RDS) subnets,
    /// plus the security groups that connect the tiers. Security groups live here so the API and database
    /// stacks can reference each other's group without a dependency cycle.
    /// </summary>
    public class NetworkStack : Stack
    {
        /// <summary>
        /// The VPC.
        /// </summary>
        public Vpc Vpc { get; }

        /// <summary>
        /// Security group of the internet-facing load balancer (HTTP and HTTPS from anywhere).
        /// </summary>
        public SecurityGroup LoadBalancerSecurityGroup { get; }

        /// <summary>
        /// Second security group of the load balancer: its CloudFront origin listener port, only from the CloudFront
        /// origin-facing managed prefix list. The website distributions reach the API through it.
        /// </summary>
        public SecurityGroup CloudFrontOriginSecurityGroup { get; }

        /// <summary>
        /// Security group of the Fargate tasks (container port from the load balancer only).
        /// </summary>
        public SecurityGroup ServiceSecurityGroup { get; }

        /// <summary>
        /// Security group of the database (PostgreSQL port from the tasks only).
        /// </summary>
        public SecurityGroup DatabaseSecurityGroup { get; }

        /// <summary>
        /// Security group of the RecallDB tasks (reachable only from the API tasks), or null when RecallDB is off.
        /// </summary>
        public SecurityGroup? RecallDbSecurityGroup { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public NetworkStack(Construct scope, string id, EnvironmentConfig config, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);

            LogGroup flowLogGroup = new LogGroup(this, "FlowLogs", new LogGroupProps
            {
                LogGroupName = "/" + config.ResourcePath("vpc/flow-logs"),
                Retention = config.Observability.LogRetention,
                RemovalPolicy = config.StatefulRemovalPolicy
            });

            Vpc = new Vpc(this, "Vpc", new VpcProps
            {
                VpcName = config.ResourceName("vpc"),
                IpAddresses = IpAddresses.Cidr(config.Network.CidrBlock),
                AvailabilityZones = config.Network.AvailabilityZones.ToArray(),
                NatGateways = config.Network.NatGateways,
                RestrictDefaultSecurityGroup = false,
                SubnetConfiguration = new ISubnetConfiguration[]
                {
                    new SubnetConfiguration { Name = "public", SubnetType = SubnetType.PUBLIC, CidrMask = config.Network.PublicSubnetMask },
                    new SubnetConfiguration { Name = "private", SubnetType = SubnetType.PRIVATE_WITH_EGRESS, CidrMask = config.Network.PrivateSubnetMask },
                    new SubnetConfiguration { Name = "isolated", SubnetType = SubnetType.PRIVATE_ISOLATED, CidrMask = config.Network.IsolatedSubnetMask }
                },
                FlowLogs = new Dictionary<string, IFlowLogOptions>
                {
                    ["All"] = new FlowLogOptions
                    {
                        Destination = FlowLogDestination.ToCloudWatchLogs(flowLogGroup),
                        TrafficType = FlowLogTrafficType.ALL
                    }
                }
            });

            LoadBalancerSecurityGroup = new SecurityGroup(this, "LoadBalancerSecurityGroup", new SecurityGroupProps
            {
                Vpc = Vpc,
                Description = "Internet-facing API load balancer",
                AllowAllOutbound = false
            });
            LoadBalancerSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(config.Api.HttpsPort), "HTTPS from the internet");
            LoadBalancerSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(config.Api.HttpPort), "HTTP from the internet (redirected to HTTPS)");

            if (config.Api.CloudFrontOriginHttpsPort == config.Api.HttpsPort || config.Api.CloudFrontOriginHttpsPort == config.Api.HttpPort)
            {
                throw new ArgumentException("ApiSettings.CloudFrontOriginHttpsPort must differ from the public HTTPS and HTTP listener ports.", nameof(config));
            }

            // A separate group because the managed prefix list counts as its maximum size (about 55 entries) against
            // the rules-per-group quota of 60.
            CloudFrontOriginSecurityGroup = new SecurityGroup(this, "CloudFrontOriginSecurityGroup", new SecurityGroupProps
            {
                Vpc = Vpc,
                Description = "API load balancer CloudFront origin listener (CloudFront origin-facing ranges only)",
                AllowAllOutbound = false
            });
            CloudFrontOriginSecurityGroup.AddIngressRule(Peer.PrefixList(config.Network.CloudFrontOriginFacingPrefixListId),
                Port.Tcp(config.Api.CloudFrontOriginHttpsPort), "HTTPS from CloudFront origin-facing servers");

            // Outbound stays open: the server calls Mailgun (api.mailgun.net, api.eu.mailgun.net), the DNS-over-HTTPS
            // domain verification resolver (cloudflare-dns.com by default), AI model providers, stores, payment, and
            // sign-in providers over HTTPS through the NAT gateways. There are no network ACLs or firewalls besides.
            ServiceSecurityGroup = new SecurityGroup(this, "ServiceSecurityGroup", new SecurityGroupProps
            {
                Vpc = Vpc,
                Description = "API Fargate tasks",
                AllowAllOutbound = true
            });
            ServiceSecurityGroup.AddIngressRule(LoadBalancerSecurityGroup, Port.Tcp(config.Api.ContainerPort), "API traffic from the load balancer");
            LoadBalancerSecurityGroup.AddEgressRule(ServiceSecurityGroup, Port.Tcp(config.Api.ContainerPort), "Forward to API tasks");

            DatabaseSecurityGroup = new SecurityGroup(this, "DatabaseSecurityGroup", new SecurityGroupProps
            {
                Vpc = Vpc,
                Description = "PostgreSQL",
                AllowAllOutbound = false
            });
            DatabaseSecurityGroup.AddIngressRule(ServiceSecurityGroup, Port.Tcp(config.Database.Port), "PostgreSQL from API tasks");

            if (config.Ai.RecallDbEnabled)
            {
                // Outbound stays open for the image pull from Docker Hub through the NAT gateways; inbound is the API
                // tasks only, and the database admits RecallDB on its port.
                RecallDbSecurityGroup = new SecurityGroup(this, "RecallDbSecurityGroup", new SecurityGroupProps
                {
                    Vpc = Vpc,
                    Description = "RecallDB Fargate tasks",
                    AllowAllOutbound = true
                });
                RecallDbSecurityGroup.AddIngressRule(ServiceSecurityGroup, Port.Tcp(config.Ai.RecallDbPort), "RecallDB from API tasks");
                DatabaseSecurityGroup.AddIngressRule(RecallDbSecurityGroup, Port.Tcp(config.Database.Port), "PostgreSQL from RecallDB tasks");
            }

            NagHelper.Suppress(LoadBalancerSecurityGroup, "AwsSolutions-EC23",
                "The API load balancer is the public entry point for the mobile apps and the website, so HTTP (redirect only) and HTTPS are open to the internet.");

            new CfnOutput(this, "VpcId", new CfnOutputProps { Value = Vpc.VpcId });
        }
    }
}
