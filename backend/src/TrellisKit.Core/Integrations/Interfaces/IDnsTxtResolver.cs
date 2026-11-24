namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads DNS TXT records (domain ownership verification). Implementations are safe to call concurrently.
    /// </summary>
    public interface IDnsTxtResolver
    {
        /// <summary>
        /// The TXT strings of a name (each record's character strings joined).
        /// </summary>
        /// <param name="name">Fully qualified name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>TXT values (empty when none).</returns>
        /// <exception cref="InvalidOperationException">The lookup failed (network or resolver error).</exception>
        Task<List<string>> ResolveTxtAsync(string name, CancellationToken token = default);
    }
}
