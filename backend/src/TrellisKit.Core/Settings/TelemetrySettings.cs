namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Telemetry export settings. Changes require a restart.
    /// </summary>
    public class TelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether metrics, traces, and logs are exported. Default true.
        /// Instrumentation is best-effort: an unreachable collector never affects request handling.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Service name stamped on all telemetry. Default "trelliskit-server".
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null or empty.</exception>
        public string ServiceName
        {
            get
            {
                return _ServiceName;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ServiceName));
                _ServiceName = value.Trim();
            }
        }

        /// <summary>
        /// OTLP collector endpoint. Default "http://127.0.0.1:4317".
        /// </summary>
        /// <exception cref="ArgumentException">The value is not an absolute http or https URI.</exception>
        public string OtlpEndpoint
        {
            get
            {
                return _OtlpEndpoint;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)
                    || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new ArgumentException("OTLP endpoint must be an absolute http or https URI.", nameof(OtlpEndpoint));
                }
                _OtlpEndpoint = value.Trim();
            }
        }

        /// <summary>
        /// OTLP protocol. Default gRPC.
        /// </summary>
        public TelemetryProtocolEnum OtlpProtocol { get; set; } = TelemetryProtocolEnum.Grpc;

        #endregion

        #region Private-Members

        private string _ServiceName = "trelliskit-server";
        private string _OtlpEndpoint = "http://127.0.0.1:4317";

        #endregion
    }
}
