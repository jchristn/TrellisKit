namespace Test.Shared.Suites
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Services;

    /// <summary>
    /// A test-local account export section proving the extension point.
    /// </summary>
    internal sealed class TestExportSection : IAccountExportSection
    {
        public string Name
        {
            get { return "testSection"; }
        }

        public Task WriteAsync(Utf8JsonWriter writer, string userId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStartObject();
            writer.WriteString("userId", userId);
            writer.WriteEndObject();
            return Task.CompletedTask;
        }
    }
}
