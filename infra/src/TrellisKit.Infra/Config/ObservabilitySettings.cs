namespace TrellisKit.Infra.Config
{
    using System;
    using Amazon.CDK.AWS.Logs;

    /// <summary>
    /// Logging, metrics, and alarm settings.
    /// </summary>
    public class ObservabilitySettings
    {
        private string _AlarmEmail = "alerts@example.test";
        private int _AlarmPeriodSeconds = 300;
        private int _AlarmEvaluationPeriods = 2;
        private double _Http5xxRatePercent = 5;
        private double _RdsCpuPercent = 80;
        private int _RdsFreeStorageGiB = 5;
        private int _CollectorHealthPort = 13133;
        private double _RealtimeBusQueueUsageThreshold = 0.5;
        private string _MetricsNamespacePrefix = "TrellisKit";

        /// <summary>
        /// Address subscribed to the alarm topic. The subscription must be confirmed from the email.
        /// Default alerts@example.test. Set from the property (deploy/infra.json alarmEmail, else contact.supportEmail); the alarmEmail context value overrides both.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string AlarmEmail
        {
            get { return _AlarmEmail; }
            set { _AlarmEmail = Guard.NotEmpty(value, nameof(AlarmEmail)); }
        }

        /// <summary>
        /// Retention of every CloudWatch log group. Default ONE_MONTH.
        /// </summary>
        public RetentionDays LogRetention { get; set; } = RetentionDays.ONE_MONTH;

        /// <summary>
        /// Alarm metric period. Default 300. Minimum 60, maximum 86400.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int AlarmPeriodSeconds
        {
            get { return _AlarmPeriodSeconds; }
            set { _AlarmPeriodSeconds = Guard.Range(value, 60, 86400, nameof(AlarmPeriodSeconds)); }
        }

        /// <summary>
        /// Consecutive breaching periods before an alarm fires. Default 2. Minimum 1, maximum 100.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int AlarmEvaluationPeriods
        {
            get { return _AlarmEvaluationPeriods; }
            set { _AlarmEvaluationPeriods = Guard.Range(value, 1, 100, nameof(AlarmEvaluationPeriods)); }
        }

        /// <summary>
        /// 5xx responses (target plus load balancer) as a percent of requests that fires the 5xx alarm.
        /// Default 5. Minimum 0.1, maximum 100.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public double Http5xxRatePercent
        {
            get { return _Http5xxRatePercent; }
            set { _Http5xxRatePercent = Guard.Range(value, 0.1, 100, nameof(Http5xxRatePercent)); }
        }

        /// <summary>
        /// RDS CPU utilization percent that fires the CPU alarm. Default 80. Minimum 1, maximum 100.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public double RdsCpuPercent
        {
            get { return _RdsCpuPercent; }
            set { _RdsCpuPercent = Guard.Range(value, 1, 100, nameof(RdsCpuPercent)); }
        }

        /// <summary>
        /// Free RDS storage in GiB below which the storage alarm fires. Default 5. Minimum 1, maximum 65536.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int RdsFreeStorageGiB
        {
            get { return _RdsFreeStorageGiB; }
            set { _RdsFreeStorageGiB = Guard.Range(value, 1, 65536, nameof(RdsFreeStorageGiB)); }
        }

        /// <summary>
        /// Port of the collector's health_check extension. Default 13133. Minimum 1024, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int CollectorHealthPort
        {
            get { return _CollectorHealthPort; }
            set { _CollectorHealthPort = Guard.Range(value, 1024, 65535, nameof(CollectorHealthPort)); }
        }

        /// <summary>
        /// Server metrics the collector also sends to CloudWatch (embedded metric format) so CloudWatch alarms can read
        /// them; every other metric goes to Managed Prometheus only.
        /// </summary>
        public static readonly string RealtimeBusQueueUsageMetric = "trelliskit.realtime.bus.queue_usage";

        /// <summary>
        /// PostgreSQL notification queue usage (trelliskit.realtime.bus.queue_usage, pg_notification_queue_usage() as a
        /// fraction from 0 to 1) above which the realtime bus alarm fires. Default 0.5. Minimum 0.01, maximum 1.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public double RealtimeBusQueueUsageThreshold
        {
            get { return _RealtimeBusQueueUsageThreshold; }
            set { _RealtimeBusQueueUsageThreshold = Guard.Range(value, 0.01, 1, nameof(RealtimeBusQueueUsageThreshold)); }
        }

        /// <summary>
        /// Prefix of the CloudWatch namespace of server metrics: the namespace is {prefix}/{resource prefix}, for example
        /// TrellisKit/example-prod. Default TrellisKit.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string MetricsNamespacePrefix
        {
            get { return _MetricsNamespacePrefix; }
            set { _MetricsNamespacePrefix = Guard.NotEmpty(value, nameof(MetricsNamespacePrefix)); }
        }
    }
}
