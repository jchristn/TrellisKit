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

        #endregion
    }
}
