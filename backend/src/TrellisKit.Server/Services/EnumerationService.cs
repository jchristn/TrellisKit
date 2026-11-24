namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Prepares enumerations for services with the live enumeration settings (docs/ENUMERATION_AND_METADATA.md
    /// section 2). Continuation tokens are bound to the property: a token issued in one property is
    /// 400 InvalidContinuationToken in another. Thread safety: stateless apart from the shared settings; safe to call
    /// concurrently.
    /// </summary>
    public class EnumerationService
    {
        #region Public-Members

        /// <summary>
        /// Property id bound into every continuation token fingerprint. Default empty.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private readonly AppSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="settings">Running settings.</param>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public EnumerationService(AppSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate and clamp a query for a collection.
        /// </summary>
        /// <param name="query">Query, or null for defaults.</param>
        /// <param name="collection">Collection.</param>
        /// <param name="isStaff">Whether the caller may see and filter on staff metadata.</param>
        /// <param name="scope">Extra scope bound into continuation tokens (for example a parent id).</param>
        /// <param name="maxResultsCap">Optional lower route maximum, or null.</param>
        /// <returns>Request.</returns>
        /// <exception cref="ArgumentNullException">The collection is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 400 InvalidContinuationToken.</exception>
        public EnumerationRequest Prepare(EnumerationQuery? query, EnumerationCollection collection, bool isStaff, string scope = "", int? maxResultsCap = null)
        {
            return EnumerationNormalizer.Normalize(query, collection, _Settings.Enumeration, isStaff, PropertyId + "|" + (scope ?? String.Empty), maxResultsCap);
        }

        #endregion
    }
}
