namespace TrellisKit.Core.Observability
{
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Public landing page metrics (docs/PHASE4_CONTRACT.md section 18.7).
    /// </summary>
    public static class PublicPageTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Public page requests, tagged kind (Venue, Event, Profile, Sitemap, Robots, None) and outcome (Ok, Redirect,
        /// NotFound, PassThrough).
        /// </summary>
        public static readonly Counter<long> Requests = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.public_pages.requests", "{request}", "Public landing page, sitemap, and robots.txt requests.");

        /// <summary>
        /// Public page slug changes, tagged kind.
        /// </summary>
        public static readonly Counter<long> SlugChanges = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.public_pages.slug_changes", "{change}", "Public landing page slug changes.");

        #endregion
    }
}
