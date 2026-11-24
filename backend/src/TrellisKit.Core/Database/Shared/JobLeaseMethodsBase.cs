namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;

    /// <summary>
    /// Shared singleton job lease methods.
    /// </summary>
    internal abstract class JobLeaseMethodsBase : MethodsBase, IJobLeaseMethods
    {
        protected JobLeaseMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> TryAcquireAsync(string jobName, string holder, DateTime nowUtc, TimeSpan duration, CancellationToken token = default)
        {
            RequireText(jobName, nameof(jobName));
            RequireText(holder, nameof(holder));
            jobName = LeaseName(jobName);
            if (duration < TimeSpan.FromSeconds(1)) duration = TimeSpan.FromSeconds(1);
            DateTime until = nowUtc + duration;
            DataTable table = await QueryAsync("INSERT INTO jobleases (jobname, holder, leaseuntilutc, updatedutc) VALUES (" +
                D.Str(jobName) + ", " + D.Str(holder) + ", " + D.Ts(until) + ", " + D.Ts(nowUtc) + ") " +
                "ON CONFLICT (jobname) DO UPDATE SET holder = excluded.holder, leaseuntilutc = excluded.leaseuntilutc, updatedutc = excluded.updatedutc " +
                "WHERE jobleases.leaseuntilutc < " + D.Ts(nowUtc) + " OR jobleases.holder = " + D.Str(holder) + " RETURNING jobname;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task ReleaseAsync(string jobName, string holder, CancellationToken token = default)
        {
            RequireText(jobName, nameof(jobName));
            RequireText(holder, nameof(holder));
            jobName = LeaseName(jobName);
            await QueryAsync("DELETE FROM jobleases WHERE jobname = " + D.Str(jobName) + " AND holder = " + D.Str(holder) + ";", token).ConfigureAwait(false);
        }

        private string LeaseName(string jobName)
        {
            // jobleases is deployment-level; a lease taken through a property-bound method set is per property.
            return BoundPropertyId == null ? jobName : BoundPropertyId + ":" + jobName;
        }
    }
}
