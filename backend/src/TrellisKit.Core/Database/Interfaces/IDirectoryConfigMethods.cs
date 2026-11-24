namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The directory configuration row and the program options of a property.
    /// </summary>
    public interface IDirectoryConfigMethods
    {
        /// <summary>
        /// Read the configuration row.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Configuration, or null before the first seed.</returns>
        Task<DirectoryConfig?> ReadAsync(CancellationToken token = default);

        /// <summary>
        /// Insert or replace the configuration row.
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        Task WriteAsync(DirectoryConfig config, CancellationToken token = default);

        /// <summary>
        /// Every program option in sort order (bounded by the fixed maximum of 200).
        /// </summary>
        /// <param name="includeRetired">Include retired options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Programs.</returns>
        Task<List<DirectoryProgram>> ProgramsAsync(bool includeRetired, CancellationToken token = default);

        /// <summary>
        /// Read one program option.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Program, or null.</returns>
        Task<DirectoryProgram?> ReadProgramAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Create a program option.
        /// </summary>
        /// <param name="program">Program.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored program.</returns>
        /// <exception cref="ArgumentNullException">The program is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The key exists.</exception>
        Task<DirectoryProgram> CreateProgramAsync(DirectoryProgram program, CancellationToken token = default);

        /// <summary>
        /// Update a program's label, sort order, and retirement.
        /// </summary>
        /// <param name="program">Program.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored program, or null when it does not exist.</returns>
        /// <exception cref="ArgumentNullException">The program is null.</exception>
        Task<DirectoryProgram?> UpdateProgramAsync(DirectoryProgram program, CancellationToken token = default);

        /// <summary>
        /// Delete a program option that no venue uses.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteProgramAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Whether any live venue uses a program key.
        /// </summary>
        /// <param name="key">Program key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when in use.</returns>
        Task<bool> ProgramInUseAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Reorder program options (sort order 10, 20, ...).
        /// </summary>
        /// <param name="ids">Program identifiers in order.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Programs updated.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task<int> ReorderProgramsAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate program options.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="includeRetired">Include retired options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        Task<EnumerationResult<DirectoryProgram>> EnumerateProgramsAsync(EnumerationRequest request, bool includeRetired, CancellationToken token = default);
    }
}
