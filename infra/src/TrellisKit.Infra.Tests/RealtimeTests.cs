namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;
    using Xunit;

    /// <summary>
    /// Realtime infrastructure (docs/PHASE5_CONTRACT.md section 17): WebSocket routing, idle timeout, draining, the
    /// notification queue alarm, and the database connection budget.
    /// </summary>
    public class RealtimeTests
    {
        /// <summary>
        /// Both HTTPS listeners forward /api/v1/ws to the API target group, which has no stickiness and a 30 second
        /// deregistration delay, behind a 120 second idle timeout.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void WebSocketsReachTheApiTargetGroupWithoutStickiness(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Dictionary<string, JsonElement> resources = env.Json(env.Stacks.Api).GetProperty("Resources").EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            string targetGroup = resources.Single(r => r.Value.GetProperty("Type").GetString() == "AWS::ElasticLoadBalancingV2::TargetGroup").Key;
            List<string> httpsListeners = resources
                .Where(r => r.Value.GetProperty("Type").GetString() == "AWS::ElasticLoadBalancingV2::Listener"
                    && r.Value.GetProperty("Properties").GetProperty("Protocol").GetString() == "HTTPS")
                .Select(r => r.Key).ToList();
            Assert.Equal(2, httpsListeners.Count);

            List<JsonElement> realtimeRules = resources.Values
                .Where(r => r.GetProperty("Type").GetString() == "AWS::ElasticLoadBalancingV2::ListenerRule")
                .Select(r => r.GetProperty("Properties"))
                .Where(r => r.GetProperty("Conditions").EnumerateArray().Any(c => c.GetProperty("Field").GetString() == "path-pattern"))
                .ToList();
            Assert.Equal(2, realtimeRules.Count);
            foreach (string listener in httpsListeners)
            {
                JsonElement rule = realtimeRules.Single(r => r.GetProperty("ListenerArn").GetProperty("Ref").GetString() == listener);
                JsonElement condition = Assert.Single(rule.GetProperty("Conditions").EnumerateArray());
                Assert.Equal("/api/v1/ws", Assert.Single(condition.GetProperty("PathPatternConfig").GetProperty("Values").EnumerateArray()).GetString());
                JsonElement action = Assert.Single(rule.GetProperty("Actions").EnumerateArray());
                Assert.Equal("forward", action.GetProperty("Type").GetString());
                Assert.Equal(targetGroup, action.GetProperty("TargetGroupArn").GetProperty("Ref").GetString());
            }

            Dictionary<string, string> attributes = resources[targetGroup].GetProperty("Properties").GetProperty("TargetGroupAttributes").EnumerateArray()
                .ToDictionary(a => a.GetProperty("Key").GetString() ?? String.Empty, a => a.GetProperty("Value").GetString() ?? String.Empty);
            Assert.Equal("30", attributes["deregistration_delay.timeout_seconds"]);
            Assert.False(attributes.TryGetValue("stickiness.enabled", out string? sticky) && sticky == "true");

            JsonElement balancer = resources.Values.Single(r => r.GetProperty("Type").GetString() == "AWS::ElasticLoadBalancingV2::LoadBalancer");
            Assert.Contains(balancer.GetProperty("Properties").GetProperty("LoadBalancerAttributes").EnumerateArray(),
                a => a.GetProperty("Key").GetString() == "idle_timeout.timeout_seconds" && a.GetProperty("Value").GetString() == "120");
        }

        /// <summary>
        /// The server container waits at least 30 seconds between SIGTERM and SIGKILL so the realtime drain finishes.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ServerStopTimeoutCoversTheRealtimeDrain(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement server = TemplateReader.ApiContainer(env, env.Stacks.Api, ApiStack.ServerContainerName);
            Assert.True(server.GetProperty("StopTimeout").GetInt32() >= 30);
        }

        /// <summary>
        /// Realtime settings validate their ranges.
        /// </summary>
        [Fact]
        public void RealtimeSettingsAreValidated()
        {
            ApiSettings api = new ApiSettings();
            Assert.Equal(120, api.IdleTimeoutSeconds);
            Assert.Equal(30, api.StopTimeoutSeconds);
            Assert.Equal("/api/v1/ws", api.RealtimePath);
            Assert.Throws<ArgumentOutOfRangeException>(() => api.StopTimeoutSeconds = 29);
            Assert.Throws<ArgumentOutOfRangeException>(() => api.StopTimeoutSeconds = 121);
            Assert.Throws<ArgumentOutOfRangeException>(() => api.IdleTimeoutSeconds = 30);

            ObservabilitySettings observability = new ObservabilitySettings();
            Assert.Equal(0.5, observability.RealtimeBusQueueUsageThreshold);
            Assert.Throws<ArgumentOutOfRangeException>(() => observability.RealtimeBusQueueUsageThreshold = 1.5);
        }

        /// <summary>
        /// The collector sends trelliskit.realtime.bus.queue_usage (only) to CloudWatch, the task role may write its
        /// embedded metric format records, and an alarm fires above 0.5.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void QueueUsageReachesCloudWatchAndAlarms(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            string metric = ObservabilitySettings.RealtimeBusQueueUsageMetric;
            Assert.Equal("trelliskit.realtime.bus.queue_usage", metric);
            string ns = env.Stacks.Observability!.MetricsNamespace;
            Assert.Equal("TrellisKit/" + env.Stacks.Config.ResourcePrefix, ns);

            JsonElement collector = TemplateReader.ApiContainer(env, env.Stacks.Api, ApiStack.CollectorContainerName);
            string yaml = collector.GetProperty("Environment").EnumerateArray().Single(e => e.GetProperty("Name").GetString() == "AOT_CONFIG_CONTENT").GetProperty("Value").GetRawText();
            Assert.Contains("filter/cloudwatch", yaml);
            Assert.Contains("- " + metric, yaml);
            Assert.Contains("awsemf:", yaml);
            Assert.Contains("namespace: " + ns, yaml);
            Assert.Contains("exporters: [awsemf]", yaml);

            string policies = String.Join("\n", env.Resources(env.Stacks.Api, "AWS::IAM::Policy").Select(p => p.GetRawText()));
            Assert.Contains("EmfMetricsExport", policies);
            Assert.DoesNotContain("cloudwatch:PutMetricData", policies);

            JsonElement alarm = env.Resources(env.Stacks.Alarms, "AWS::CloudWatch::Alarm")
                .Single(a => a.GetProperty("Properties").GetProperty("AlarmName").GetString() == env.Stacks.Config.ResourceName("realtime-bus-queue"))
                .GetProperty("Properties");
            Assert.Equal(0.5, alarm.GetProperty("Threshold").GetDouble());
            Assert.Equal("GreaterThanThreshold", alarm.GetProperty("ComparisonOperator").GetString());
            Assert.Equal("notBreaching", alarm.GetProperty("TreatMissingData").GetString());
            string expression = Assert.Single(alarm.GetProperty("Metrics").EnumerateArray()).GetProperty("Expression").GetString() ?? String.Empty;
            Assert.Equal("SELECT MAX(\"" + metric + "\") FROM \"" + ns + "\"", expression);
        }

        /// <summary>
        /// The Data stack reports the server connection budget: each task's pool plus one LISTEN connection, at the
        /// maximum task count.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ConnectionBudgetCountsOneListenerPerTask(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            EnvironmentConfig config = env.Stacks.Config;
            int expected = config.Api.MaxTasks * (config.Database.MaxPoolSize + 1);
            Assert.Equal(expected, config.PeakServerDatabaseConnections);
            Assert.Equal(name == "dev" ? 42 : 126, expected);
            string output = env.Json(env.Stacks.Data).GetProperty("Outputs").GetProperty("PeakServerConnections").GetProperty("Value").GetString() ?? String.Empty;
            Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), output);
        }
    }
}
