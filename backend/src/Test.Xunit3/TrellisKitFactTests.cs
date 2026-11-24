namespace Test.Xunit3
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.Xunit3Adapter;
    using Xunit;

    /// <summary>
    /// Runs every TrellisKit case, with suite hooks, inside one xUnit v3 fact.
    /// </summary>
    public sealed class TrellisKitFactTests : TouchstoneXunit3FactBase
    {
        /// <inheritdoc />
        protected override IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return TrellisKitSuites.All; }
        }

        /// <summary>
        /// Run every suite.
        /// </summary>
        /// <returns>A task that completes when every case passed.</returns>
        [Fact]
        public Task RunAll()
        {
            return RunAllAsync(TestContext.Current.CancellationToken);
        }
    }
}
