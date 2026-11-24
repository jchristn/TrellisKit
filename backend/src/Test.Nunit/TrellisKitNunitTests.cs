namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs each TrellisKit case as its own NUnit test.
    /// </summary>
    [TestFixture]
    public sealed class TrellisKitNunitTests
    {
        /// <summary>
        /// One entry per case.
        /// </summary>
        /// <returns>Test case data.</returns>
        public static IEnumerable<TestCaseData> Cases()
        {
            return TouchstoneNunitCases.From(TrellisKitSuites.All);
        }

        /// <summary>
        /// Execute one case.
        /// </summary>
        /// <param name="testCase">The case.</param>
        /// <returns>A task that completes when the case passes.</returns>
        [TestCaseSource(nameof(Cases))]
        public Task Run(TestCaseDescriptor testCase)
        {
            return TouchstoneNunit.ExecuteAsync(testCase, null, null, TestContext.CurrentContext.CancellationToken);
        }
    }
}
