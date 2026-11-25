namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Feature switches of the property.
    /// </summary>
    public class PropertyFeatures
    {
        #region Public-Members

        /// <summary>
        /// Sign-up codes.
        /// </summary>
        public bool SignupCodes { get; set; } = true;

        /// <summary>
        /// Member directory.
        /// </summary>
        public bool Directory { get; set; } = true;

        /// <summary>
        /// Events.
        /// </summary>
        public bool Events { get; set; } = true;

        /// <summary>
        /// Library.
        /// </summary>
        public bool Library { get; set; } = true;

        /// <summary>
        /// Messaging.
        /// </summary>
        public bool Messaging { get; set; } = true;

        /// <summary>
        /// Support tickets (docs/PHASE5_CONTRACT.md section 0.2). Default true. When false the ticket routes return 404
        /// NotAvailable and clients show the property support email instead.
        /// </summary>
        public bool Support { get; set; } = true;

        /// <summary>
        /// Ideas board. Default true.
        /// </summary>
        public bool Ideas { get; set; } = true;

        /// <summary>
        /// Staff broadcasts and the member announcement view. Default true.
        /// </summary>
        public bool Broadcasts { get; set; } = true;

        /// <summary>
        /// Public sample feed posts readable without signing in (the anonymous public feed). Default true when absent.
        /// </summary>
        public bool PublicSamples { get; set; } = true;

        /// <summary>
        /// Forums (docs/PHASE3_CONTRACT.md section 0.4). Default true when absent; false makes every forum route 404 NotAvailable.
        /// </summary>
        public bool Forums { get; set; } = true;

        /// <summary>
        /// Global search (POST /api/v1/search/enumerate). Default true when absent; false makes the search routes 404
        /// NotAvailable (docs/ACCESS_MODELS_CONTRACT.md section 2).
        /// </summary>
        public bool Search { get; set; } = true;

        /// <summary>
        /// AI models and assistants (docs/AI_CONTRACT.md). Default false when absent: the feature is off unless the
        /// property turns it on.
        /// </summary>
        public bool Ai { get; set; } = false;

        /// <summary>
        /// The member Training hub (the <c>training</c> navigation destination and <c>GET /api/v1/me/training</c>).
        /// Default true when absent. The hub only gathers other features; turning it off leaves the library, events,
        /// and saves reachable on their own routes.
        /// </summary>
        public bool Training { get; set; } = true;

        /// <summary>
        /// Saves and private collections (/api/v1/me/saves, /api/v1/me/save-collections). Default true when absent;
        /// false makes the saves routes 404 NotAvailable. Independent of <see cref="Training"/> and any navigation switch.
        /// </summary>
        public bool Saves { get; set; } = true;

        /// <summary>
        /// Reviewed exercise and technique catalog (docs/CATALOG_PUBLIC_PAGES.md, docs/CATALOG_IMPORTS.md). Default false
        /// when absent: catalog routes, imports, public pages, and sitemaps return 404 NotAvailable until the property
        /// turns it on. Independent of features.library.
        /// </summary>
        public bool Catalog { get; set; } = false;

        #endregion
    }
}
