namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs every TrellisKit case, with suite hooks, inside one NUnit test.
    /// </summary>
    [TestFixture]
    public sealed class TrellisKitNunitFactTests : TouchstoneNunitFactBase
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
        [Test]
        public Task RunAll()
        {
            return RunAllAsync(TestContext.CurrentContext.CancellationToken);
        }
    }
}
