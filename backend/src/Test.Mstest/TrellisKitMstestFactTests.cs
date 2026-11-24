namespace Test.Mstest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.MstestAdapter;

    /// <summary>
    /// Runs every TrellisKit case, with suite hooks, inside one MSTest test.
    /// </summary>
    [TestClass]
    public sealed class TrellisKitMstestFactTests : TouchstoneMstestFactBase
    {
        /// <summary>
        /// The running test's context, set by MSTest.
        /// </summary>
        public TestContext TestContext { get; set; } = null!;

        /// <inheritdoc />
        protected override IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return TrellisKitSuites.All; }
        }

        /// <summary>
        /// Run every suite.
        /// </summary>
        /// <returns>A task that completes when every case passed.</returns>
        [TestMethod]
        public Task RunAll()
        {
            return RunAllAsync(TestContext.CancellationToken);
        }
    }
}
