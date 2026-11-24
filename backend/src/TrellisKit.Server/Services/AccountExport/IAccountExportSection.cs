namespace TrellisKit.Server.Services
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One top-level section of the account data export (extension point for later slices: register an implementation
    /// with <see cref="AccountExportService.Register"/>). The export file is one JSON object; the service writes the
    /// property name (<see cref="Name"/>) and the section writes exactly one JSON value (usually an array). Sections must
    /// stream: read in bounded batches (keyset), write each batch, and flush the writer, never loading a whole table.
    /// Implementations must be safe to call concurrently for different members.
    /// </summary>
    public interface IAccountExportSection
    {
        /// <summary>
        /// Property name in the export file (camelCase, unique), for example "sessions".
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Write the section's JSON value for a member.
        /// </summary>
        /// <param name="writer">Writer positioned after the property name.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task WriteAsync(Utf8JsonWriter writer, string userId, CancellationToken token);
    }
}
