namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// OTLP export protocol.
    /// </summary>
    public enum TelemetryProtocolEnum
    {
        /// <summary>
        /// OTLP over gRPC, usually port 4317.
        /// </summary>
        Grpc,

        /// <summary>
        /// OTLP over HTTP with protobuf payloads, usually port 4318.
        /// </summary>
        HttpProtobuf
    }
}
