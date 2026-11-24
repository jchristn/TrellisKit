namespace Test.Shared.Suites
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Case factories shared by the Phase 1 suites.
    /// </summary>
    internal static class SuiteCases
    {
        internal static TestCaseDescriptor Http(string suiteId, string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId, caseId, name, async ct =>
            {
                HttpFixture fixture = await HttpFixture.StartAsync(ct).ConfigureAwait(false);
                await using (fixture.ConfigureAwait(false))
                {
                    await body(fixture, ct).ConfigureAwait(false);
                }
            }, new[] { "http", "integration", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        internal static TestCaseDescriptor Http(string suiteId, string caseId, string name, Action<PropertyConfiguration> customizeProperty, Func<HttpFixture, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId, caseId, name, async ct =>
            {
                HttpFixture fixture = await HttpFixture.StartAsync(ct, customizeProperty).ConfigureAwait(false);
                await using (fixture.ConfigureAwait(false))
                {
                    await body(fixture, ct).ConfigureAwait(false);
                }
            }, new[] { "http", "integration", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        internal static TestCaseDescriptor Http(string suiteId, string caseId, string name, Func<HttpFixture, CancellationToken, Task> body, Action<PropertyConfiguration> customizeProperty)
        {
            return Http(suiteId, caseId, name, customizeProperty, body);
        }

        internal static TestCaseDescriptor Http(string suiteId, string caseId, string name, Action<AppSettings> customizeSettings, Func<HttpFixture, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId, caseId, name, async ct =>
            {
                HttpFixture fixture = await HttpFixture.StartAsync(ct, null, customizeSettings).ConfigureAwait(false);
                await using (fixture.ConfigureAwait(false))
                {
                    await body(fixture, ct).ConfigureAwait(false);
                }
            }, new[] { "http", "integration", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        internal static TestCaseDescriptor Database(string suiteId, string caseId, string name, Func<TestDatabase, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId, caseId, name, async ct =>
            {
                TestDatabase db = await TestDatabase.CreateAsync(ct).ConfigureAwait(false);
                await using (db.ConfigureAwait(false))
                {
                    await body(db, ct).ConfigureAwait(false);
                }
            }, new[] { "database", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        internal static TestCaseDescriptor Unit(string suiteId, string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId, caseId, name, body, new[] { "fast", "unit" });
        }
    }
}
