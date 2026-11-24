namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CloudWatch;
    using Amazon.CDK.AWS.CloudWatch.Actions;
    using Amazon.CDK.AWS.ElasticLoadBalancingV2;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// CloudWatch alarms on the API load balancer (5xx rate, target health), the database (CPU, free storage), and the
    /// realtime bus (PostgreSQL notification queue usage), all notifying the alarm topic from <see cref="ObservabilityStack"/>. Deployed last.
    /// </summary>
    public class AlarmsStack : Stack
    {
        private static readonly double _BytesPerGiB = 1024d * 1024d * 1024d;

        /// <summary>
        /// Every alarm in the stack.
        /// </summary>
        public List<Alarm> Alarms { get; } = new List<Alarm>();

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="api">API stack.</param>
        /// <param name="data">Data stack.</param>
        /// <param name="observability">Observability stack (alarm topic).</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public AlarmsStack(
            Construct scope,
            string id,
            EnvironmentConfig config,
            ApiStack api,
            DataStack data,
            ObservabilityStack observability,
            IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(observability);

            ObservabilitySettings settings = config.Observability;
            Duration period = Duration.Seconds(settings.AlarmPeriodSeconds);
            SnsAction action = new SnsAction(observability.AlarmTopic);

            MetricOptions sum = new MetricOptions { Period = period, Statistic = "Sum" };
            MathExpression errorRate = new MathExpression(new MathExpressionProps
            {
                Label = "5xx percent of requests",
                Expression = "IF(requests > 0, 100 * (target5xx + elb5xx) / requests, 0)",
                Period = period,
                UsingMetrics = new Dictionary<string, IMetric>
                {
                    ["requests"] = api.LoadBalancer.Metrics.RequestCount(sum),
                    ["target5xx"] = api.LoadBalancer.Metrics.HttpCodeTarget(HttpCodeTarget.TARGET_5XX_COUNT, sum),
                    ["elb5xx"] = api.LoadBalancer.Metrics.HttpCodeElb(HttpCodeElb.ELB_5XX_COUNT, sum)
                }
            });
            Add(errorRate.CreateAlarm(this, "Api5xxRate", new CreateAlarmOptions
            {
                AlarmName = config.ResourceName("api-5xx-rate"),
                AlarmDescription = "API 5xx responses exceed " + settings.Http5xxRatePercent + " percent of requests",
                Threshold = settings.Http5xxRatePercent,
                EvaluationPeriods = settings.AlarmEvaluationPeriods,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                TreatMissingData = TreatMissingData.NOT_BREACHING
            }), action);

            Add(api.TargetGroup.Metrics.UnhealthyHostCount(new MetricOptions { Period = period, Statistic = "Maximum" }).CreateAlarm(this, "ApiUnhealthyTargets", new CreateAlarmOptions
            {
                AlarmName = config.ResourceName("api-unhealthy-targets"),
                AlarmDescription = "At least one API task fails the readiness check",
                Threshold = 0,
                EvaluationPeriods = settings.AlarmEvaluationPeriods,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                TreatMissingData = TreatMissingData.NOT_BREACHING
            }), action);

            Add(api.TargetGroup.Metrics.HealthyHostCount(new MetricOptions { Period = period, Statistic = "Minimum" }).CreateAlarm(this, "ApiNoHealthyTargets", new CreateAlarmOptions
            {
                AlarmName = config.ResourceName("api-no-healthy-targets"),
                AlarmDescription = "The API has no healthy tasks behind the load balancer",
                Threshold = 1,
                EvaluationPeriods = settings.AlarmEvaluationPeriods,
                ComparisonOperator = ComparisonOperator.LESS_THAN_THRESHOLD,
                TreatMissingData = TreatMissingData.BREACHING
            }), action);

            Add(data.Database.MetricCPUUtilization(new MetricOptions { Period = period, Statistic = "Average" }).CreateAlarm(this, "DatabaseCpu", new CreateAlarmOptions
            {
                AlarmName = config.ResourceName("db-cpu"),
                AlarmDescription = "Database CPU above " + settings.RdsCpuPercent + " percent",
                Threshold = settings.RdsCpuPercent,
                EvaluationPeriods = settings.AlarmEvaluationPeriods,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                TreatMissingData = TreatMissingData.MISSING
            }), action);

            Add(data.Database.MetricFreeStorageSpace(new MetricOptions { Period = period, Statistic = "Minimum" }).CreateAlarm(this, "DatabaseFreeStorage", new CreateAlarmOptions
            {
                AlarmName = config.ResourceName("db-free-storage"),
                AlarmDescription = "Database free storage below " + settings.RdsFreeStorageGiB + " GiB",
                Threshold = settings.RdsFreeStorageGiB * _BytesPerGiB,
                EvaluationPeriods = settings.AlarmEvaluationPeriods,
                ComparisonOperator = ComparisonOperator.LESS_THAN_THRESHOLD,
                TreatMissingData = TreatMissingData.MISSING
            }), action);

            // The server samples pg_notification_queue_usage() into trelliskit.realtime.bus.queue_usage; the collector
            // sends that one metric to CloudWatch (embedded metric format). A Metrics Insights query takes the maximum
            // over every task, whatever dimensions the exporter attaches.
            MathExpression queueUsage = new MathExpression(new MathExpressionProps
            {
                Label = "Realtime bus notification queue usage",
                Expression = "SELECT MAX(\"" + ObservabilitySettings.RealtimeBusQueueUsageMetric + "\") FROM \"" + observability.MetricsNamespace + "\"",
                Period = period,
                UsingMetrics = new Dictionary<string, IMetric>()
            });
            Add(queueUsage.CreateAlarm(this, "RealtimeBusQueueUsage", new CreateAlarmOptions
            {
                AlarmName = config.ResourceName("realtime-bus-queue"),
                AlarmDescription = "PostgreSQL notification queue (realtime bus) above " + settings.RealtimeBusQueueUsageThreshold.ToString(CultureInfo.InvariantCulture) + " of its capacity",
                Threshold = settings.RealtimeBusQueueUsageThreshold,
                EvaluationPeriods = settings.AlarmEvaluationPeriods,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                TreatMissingData = TreatMissingData.NOT_BREACHING
            }), action);
        }

        private void Add(Alarm alarm, SnsAction action)
        {
            alarm.AddAlarmAction(action);
            alarm.AddOkAction(action);
            Alarms.Add(alarm);
        }
    }
}
