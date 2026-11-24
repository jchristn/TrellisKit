namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Per-property setting rows (propertysettings; docs/MULTIPROPERTY_CONTRACT.md section 5). Implementations are safe
    /// to call concurrently.
    /// </summary>
    public interface IPropertySettingMethods
    {
        /// <summary>
        /// Every stored setting of the property (bounded by the property settings registry).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<PropertySettingRow>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Upsert and delete rows in one transaction.
        /// </summary>
        /// <param name="upserts">Rows to write.</param>
        /// <param name="deletes">Keys to remove.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task WriteAsync(List<PropertySettingRow> upserts, List<string> deletes, CancellationToken token = default);
    }
}
