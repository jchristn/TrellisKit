namespace Test.Shared.Suites
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Shared helpers of the catalog suites: every case runs with features.catalog on (the engine default is off).
    /// </summary>
    internal static class CatalogTestKit
    {
        internal static TestCaseDescriptor Http(string suiteId, string caseId, string name, Func<HttpFixture, CancellationToken, Task> body, Action<PropertyConfiguration>? customizeProperty = null)
        {
            return SuiteCases.Http(suiteId, caseId, name, body, p =>
            {
                p.Features.Catalog = true;
                customizeProperty?.Invoke(p);
            });
        }
    }
}
