namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using Amazon.CDK.AWS.APS;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.KMS;
    using Amazon.CDK.AWS.Logs;
    using Amazon.CDK.AWS.SNS;
    using Amazon.CDK.AWS.SNS.Subscriptions;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// Shared observability resources deployed before the service: the Amazon Managed Service for Prometheus
    /// workspace, CloudWatch log groups with retention, and the encrypted alarm topic with its email subscription.
    /// The alarms themselves are in <see cref="AlarmsStack"/>, which deploys last because it reads metrics of the
    /// load balancer and the database.
    /// </summary>
    public class ObservabilityStack : Stack
    {
        /// <summary>
        /// Prometheus workspace that the ADOT sidecar remote-writes to.
        /// </summary>
        public CfnWorkspace PrometheusWorkspace { get; }

        /// <summary>
        /// Log group of the server container (stdout and stderr).
        /// </summary>
        public LogGroup ServerLogGroup { get; }

        /// <summary>
        /// Log group of the ADOT collector container.
        /// </summary>
        public LogGroup CollectorLogGroup { get; }

        /// <summary>
        /// Log group that receives OTLP logs from the server through the collector.
        /// </summary>
        public LogGroup TelemetryLogGroup { get; }

        /// <summary>
        /// Log group of the collector's embedded metric format records (CloudWatch extracts the metrics alarms read).
        /// </summary>
        public LogGroup MetricsLogGroup { get; }

        /// <summary>
        /// CloudWatch namespace of server metrics ({MetricsNamespacePrefix}/{resource prefix}).
        /// </summary>
        public string MetricsNamespace { get; }

        /// <summary>
        /// Topic that every alarm notifies.
        /// </summary>
        public Topic AlarmTopic { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public ObservabilityStack(Construct scope, string id, EnvironmentConfig config, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);

            PrometheusWorkspace = new CfnWorkspace(this, "PrometheusWorkspace", new CfnWorkspaceProps
            {
                Alias = config.ResourceName("metrics")
            });

            ServerLogGroup = CreateLogGroup("ServerLogGroup", "/" + config.ResourcePath("server"), config);
            CollectorLogGroup = CreateLogGroup("CollectorLogGroup", "/" + config.ResourcePath("otel-collector"), config);
            TelemetryLogGroup = CreateLogGroup("TelemetryLogGroup", "/" + config.ResourcePath("server-otlp-logs"), config);
            MetricsLogGroup = CreateLogGroup("MetricsLogGroup", "/" + config.ResourcePath("server-metrics"), config);
            MetricsNamespace = config.Observability.MetricsNamespacePrefix + "/" + config.ResourcePrefix;

            Key alarmKey = new Key(this, "AlarmTopicKey", new KeyProps
            {
                Alias = "alias/" + config.ResourceName("alarms"),
                Description = "Encrypts the alarm topic",
                EnableKeyRotation = true,
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            alarmKey.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "AllowCloudWatchAlarmsToPublish",
                Principals = new IPrincipal[] { new ServicePrincipal("cloudwatch.amazonaws.com") },
                Actions = new string[] { "kms:Decrypt", "kms:GenerateDataKey*" },
                Resources = new string[] { "*" }
            }));

            AlarmTopic = new Topic(this, "AlarmTopic", new TopicProps
            {
                TopicName = config.ResourceName("alarms"),
                DisplayName = config.DisplayName + " " + config.Name + " alarms",
                MasterKey = alarmKey,
                EnforceSSL = true
            });
            AlarmTopic.AddSubscription(new EmailSubscription(config.Observability.AlarmEmail));

            new CfnOutput(this, "PrometheusRemoteWriteUrl", new CfnOutputProps
            {
                Value = PrometheusWorkspace.AttrPrometheusEndpoint + "api/v1/remote_write"
            });
        }

        private LogGroup CreateLogGroup(string id, string name, EnvironmentConfig config)
        {
            return new LogGroup(this, id, new LogGroupProps
            {
                LogGroupName = name,
                Retention = config.Observability.LogRetention,
                RemovalPolicy = config.StatefulRemovalPolicy
            });
        }
    }
}
