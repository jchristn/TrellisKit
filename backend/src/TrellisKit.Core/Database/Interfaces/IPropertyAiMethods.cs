namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The property's stored AI switch (table propertyai; docs/TRELLISKIT.md section 9.5). Implementations are safe to
    /// call concurrently.
    /// </summary>
    public interface IPropertyAiMethods
    {
        /// <summary>
        /// Read the stored switch.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Switch, or null when none is stored yet.</returns>
        Task<PropertyAiSwitch?> ReadAsync(CancellationToken token = default);

        /// <summary>
        /// Insert or replace the stored switch.
        /// </summary>
        /// <param name="value">Switch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The switch is null.</exception>
        Task WriteAsync(PropertyAiSwitch value, CancellationToken token = default);
    }
}
