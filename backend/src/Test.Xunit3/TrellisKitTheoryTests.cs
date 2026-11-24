namespace Test.Xunit3
{
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Xunit3Adapter;
    using Xunit;

    /// <summary>
    /// Runs each TrellisKit case as its own xUnit v3 theory row.
    /// </summary>
    public sealed class TrellisKitTheoryTests
    {
        /// <summary>
        /// One row per case.
        /// </summary>
        /// <returns>Theory data.</returns>
        public static TheoryData<TouchstoneXunit3Case> Cases()
        {
            return TouchstoneXunit3Cases.From<TrellisKitSuites>();
        }

        /// <summary>
        /// Execute one case.
        /// </summary>
        /// <param name="testCase">The case.</param>
        /// <returns>A task that completes when the case passes.</returns>
        [Theory]
        [MemberData(nameof(Cases))]
        public Task Run(TouchstoneXunit3Case testCase)
        {
            return TouchstoneXunit3.ExecuteAsync(testCase, TestContext.Current.CancellationToken);
        }
    }
}
