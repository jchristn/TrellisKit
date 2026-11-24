namespace Test.Mstest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.MstestAdapter;

    /// <summary>
    /// Runs each TrellisKit case as its own MSTest data row.
    /// </summary>
    [TestClass]
    public sealed class TrellisKitMstestTests
    {
        /// <summary>
        /// The running test's context, set by MSTest.
        /// </summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>
        /// One row per case.
        /// </summary>
        /// <returns>Data rows.</returns>
        public static IEnumerable<TestDataRow<TouchstoneMstestCase>> Cases()
        {
            return TouchstoneMstestCases.From<TrellisKitSuites>();
        }

        /// <summary>
        /// Execute one case.
        /// </summary>
        /// <param name="testCase">The case.</param>
        /// <returns>A task that completes when the case passes.</returns>
        [TestMethod]
        [DynamicData(nameof(Cases))]
        public Task Run(TouchstoneMstestCase testCase)
        {
            return TouchstoneMstest.ExecuteAsync(testCase, TestContext, TestContext.CancellationToken);
        }
    }
}
