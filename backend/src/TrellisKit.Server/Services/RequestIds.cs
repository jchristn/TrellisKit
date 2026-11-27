namespace TrellisKit.Server.Services
{
    using System;
    using System.Runtime.CompilerServices;
    using TrellisKit.Core.Helpers;
    using WatsonWebserver.Core;

    /// <summary>
    /// The request history id of an HTTP request, assigned on first use and shared by <see cref="RequestInfo.From"/> and
    /// <see cref="RequestHistoryCaptureService.Build"/>, so a staff email queued during a request can link to the request
    /// history entry written after it completes (docs/EMAIL.md, "Staff email details"). Thread safety: safe to call concurrently; entries
    /// are released with their context, and a pooled context reused for a new request gets a new id.
    /// </summary>
    public static class RequestIds
    {
        #region Private-Members

        private static readonly ConditionalWeakTable<HttpContextBase, string> _Ids = new ConditionalWeakTable<HttpContextBase, string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The request history id (prefix "req_") of a request, generated on first use.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Identifier.</returns>
        /// <exception cref="ArgumentNullException">ctx is null.</exception>
        public static string Of(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            // Watson pools contexts and clears Guid between requests, so the stored value is "guid|id" and a context
            // reused for a new request (new Guid) gets a new id.
            string guid = ctx.Guid.ToString("N");
            lock (_Ids)
            {
                if (_Ids.TryGetValue(ctx, out string? stored) && stored.StartsWith(guid + "|", StringComparison.Ordinal)) return stored.Substring(guid.Length + 1);
                string id = IdGenerator.GenerateRequestHistoryId();
                _Ids.AddOrUpdate(ctx, guid + "|" + id);
                return id;
            }
        }

        #endregion
    }
}
