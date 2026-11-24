namespace TrellisKit.Server.Services
{
    using System;
    using Radiant;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Owns the telemetry export pipeline. Subscribes to the Watson meter and activity source (HTTP layer) and the
    /// application's own (database, jobs, settings, auth), and exports metrics, traces, and logs over OTLP to the
    /// collector, which fans out to Prometheus, Tempo, and Loki. Failures to start are logged and telemetry is
    /// disabled; the server keeps running.
    /// </summary>
    public sealed class TelemetryHost : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Name of the Watson meter and activity source.
        /// </summary>
        public const string WatsonSourceName = "Watson";

        /// <summary>
        /// Meter and activity source name of the PolyPrompt model client library (docs/AI_CONTRACT.md section 16).
        /// </summary>
        public const string PolyPromptSourceName = "PolyPrompt";

        /// <summary>
        /// Resource attribute carrying the active property id.
        /// </summary>
        public const string PropertyResourceAttribute = "trelliskit.property";

        /// <summary>
        /// Standard OpenTelemetry environment variable of extra resource attributes, read by the SDK at startup.
        /// </summary>
        public const string ResourceAttributesVariable = "OTEL_RESOURCE_ATTRIBUTES";

        /// <summary>
        /// Whether telemetry export is running.
        /// </summary>
        public bool IsEnabled
        {
            get { return _Host != null; }
        }

        #endregion

        #region Private-Members

        private readonly RadiantHost? _Host;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private TelemetryHost(RadiantHost? host)
        {
            _Host = host;
        }

        /// <summary>
        /// Start telemetry export. Never throws; returns a disabled host when telemetry is off or cannot start.
        /// </summary>
        /// <param name="settings">Telemetry settings.</param>
        /// <param name="propertyId">Active property id, stamped as the trelliskit.property resource attribute.</param>
        /// <param name="logger">Logger; receives the telemetry log sink on success.</param>
        /// <returns>Telemetry host.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static TelemetryHost Start(TelemetrySettings settings, string propertyId, AppLogger logger)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(propertyId);
            ArgumentNullException.ThrowIfNull(logger);

            if (!settings.Enabled)
            {
                logger.Info("[TelemetryHost] telemetry export disabled by settings");
                return new TelemetryHost(null);
            }

            try
            {
                AddResourceAttribute(PropertyResourceAttribute, propertyId);
                RadiantSettings radiant = new RadiantSettings(settings.ServiceName);
                radiant.Otlp.Endpoint = settings.OtlpEndpoint;
                radiant.Otlp.Protocol = settings.OtlpProtocol == TelemetryProtocolEnum.HttpProtobuf
                    ? OtlpProtocolEnum.HttpProtobuf
                    : OtlpProtocolEnum.Grpc;
                radiant.Sources.AddMeter(WatsonSourceName);
                radiant.Sources.AddActivitySource(WatsonSourceName);
                radiant.Sources.AddMeter(Constants.TelemetrySourceName);
                radiant.Sources.AddActivitySource(Constants.TelemetrySourceName);
                // docs/TRELLISKIT.md section 12.1: SendWithMailgun's own metrics and spans next to trelliskit.email.*.
                radiant.Sources.AddMeter(SendWithMailgun.MailgunTelemetry.MeterName);
                radiant.Sources.AddActivitySource(SendWithMailgun.MailgunTelemetry.ActivitySourceName);
                radiant.Sources.AddMeter(PolyPromptSourceName);
                radiant.Sources.AddActivitySource(PolyPromptSourceName);

                RadiantHost host = RadiantHost.Start(radiant);
                logger.AttachTelemetry(host.CreateLogger("TrellisKit.Server"));
                logger.Info("[TelemetryHost] exporting telemetry (" + settings.OtlpProtocol + ") to " + settings.OtlpEndpoint + " as " + settings.ServiceName);
                return new TelemetryHost(host);
            }
            catch (Exception e)
            {
                logger.Warn("[TelemetryHost] telemetry disabled, start failed: " + e.Message);
                return new TelemetryHost(null);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Flush and stop telemetry export.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try
            {
                _Host?.ForceFlush(5000);
                _Host?.Dispose();
            }
            catch (InvalidOperationException)
            {
                // Best-effort during shutdown.
            }
        }

        #endregion

        #region Private-Methods

        private static void AddResourceAttribute(string key, string value)
        {
            string existing = Environment.GetEnvironmentVariable(ResourceAttributesVariable) ?? String.Empty;
            if (existing.Contains(key + "=", StringComparison.Ordinal)) return;
            string pair = key + "=" + Uri.EscapeDataString(value);
            Environment.SetEnvironmentVariable(ResourceAttributesVariable, existing.Length == 0 ? pair : existing.TrimEnd(',') + "," + pair);
        }

        #endregion
    }
}
