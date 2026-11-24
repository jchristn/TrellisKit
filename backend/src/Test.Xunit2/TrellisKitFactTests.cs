namespace Test.Xunit2
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.Xunit2Adapter;
    using Xunit;

    /// <summary>
    /// Runs every TrellisKit case, with suite hooks, inside one xUnit v2 fact.
    /// </summary>
    public sealed class TrellisKitFactTests : TouchstoneXunit2FactBase
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
            return RunAllAsync();
        }
    }
}
