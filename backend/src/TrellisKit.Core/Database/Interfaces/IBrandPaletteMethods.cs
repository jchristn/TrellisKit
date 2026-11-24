namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The property's versioned brand palettes (tables propertybrandpalettes, propertybrandcolors, and
    /// propertybrandoverrides; docs/TRELLISKIT.md section 2.1). The current palette is the highest version.
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface IBrandPaletteMethods
    {
        /// <summary>
        /// Read the current (highest) version.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Palette, or null when none is stored yet.</returns>
        Task<BrandPalette?> ReadCurrentAsync(CancellationToken token = default);

        /// <summary>
        /// Read the highest version below a version.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Palette, or null when there is none.</returns>
        Task<BrandPalette?> ReadBeforeAsync(int version, CancellationToken token = default);

        /// <summary>
        /// Read one retained version with its colors and overrides.
        /// </summary>
        /// <param name="version">Version number.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The version, or null when it was never stored or was pruned.</returns>
        Task<BrandPalette?> ReadVersionAsync(int version, CancellationToken token = default);

        /// <summary>
        /// Enumerate the retained versions (BrandingCollections.Versions) with their colors and overrides. Versions carry
        /// no labels or tags; a query with label or tag filters (refused earlier by the normalizer) matches nothing.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="source">Optional source filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        Task<EnumerationResult<BrandPalette>> EnumerateAsync(EnumerationRequest request, string? source, CancellationToken token = default);

        /// <summary>
        /// Insert a version with its colors and overrides in one transaction.
        /// </summary>
        /// <param name="palette">Palette (its Version must be new).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The palette is null.</exception>
        /// <exception cref="DuplicateRecordException">The version already exists (a concurrent edit).</exception>
        Task CreateAsync(BrandPalette palette, CancellationToken token = default);

        /// <summary>
        /// Delete every version except the newest ones.
        /// </summary>
        /// <param name="keep">Versions to keep (at least 1).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Versions deleted.</returns>
        Task<int> PruneAsync(int keep, CancellationToken token = default);

        /// <summary>
        /// Count the stored versions.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountAsync(CancellationToken token = default);

        /// <summary>
        /// Read the stored member palette choices (table propertypalettechoices).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Choices, or null when none are stored yet (every preset is allowed).</returns>
        Task<MemberPaletteChoices?> ReadMemberChoicesAsync(CancellationToken token = default);

        /// <summary>
        /// Insert or replace the stored member palette choices.
        /// </summary>
        /// <param name="choices">Choices.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The choices are null.</exception>
        Task WriteMemberChoicesAsync(MemberPaletteChoices choices, CancellationToken token = default);
    }
}
