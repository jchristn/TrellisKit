namespace Test.Shared.Suites
{
    using System;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Builds normalized enumeration requests for database-level tests.
    /// </summary>
    internal static class TestEnumeration
    {
        /// <summary>
        /// A staff request for a collection with default settings.
        /// </summary>
        internal static EnumerationRequest Staff(EnumerationCollection collection, EnumerationQuery? query = null, string scope = "")
        {
            return EnumerationNormalizer.Normalize(query, collection, new EnumerationSettings(), true, scope);
        }

        /// <summary>
        /// A staff request with a page size.
        /// </summary>
        internal static EnumerationRequest Staff(EnumerationCollection collection, int maxResults, int skip = 0, string? ordering = null)
        {
            return Staff(collection, new EnumerationQuery { MaxResults = maxResults, Skip = skip, Ordering = ordering });
        }
    }
}
