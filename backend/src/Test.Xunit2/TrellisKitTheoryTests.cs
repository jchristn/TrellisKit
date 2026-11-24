namespace Test.Xunit2
{
    using System;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Xunit2Adapter;
    using Xunit;
    using Xunit.Abstractions;

    /// <summary>
    /// Runs each TrellisKit case as its own xUnit v2 theory row.
    /// </summary>
    public sealed class TrellisKitTheoryTests
    {
        private readonly ITestOutputHelper _Output;

        /// <summary>
        /// Initialize the test class with xUnit's output helper.
        /// </summary>
        /// <param name="output">Output helper for the running test.</param>
        /// <exception cref="ArgumentNullException">The output helper is null.</exception>
        public TrellisKitTheoryTests(ITestOutputHelper output)
        {
            _Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>
        /// One row per case.
        /// </summary>
        /// <returns>Theory data.</returns>
        public static TheoryData<TouchstoneXunit2Case> Cases()
        {
            return TouchstoneXunit2Cases.From<TrellisKitSuites>();
        }

        /// <summary>
        /// Execute one case.
        /// </summary>
        /// <param name="testCase">The case.</param>
        /// <returns>A task that completes when the case passes.</returns>
        [Theory]
        [MemberData(nameof(Cases))]
        public Task Run(TouchstoneXunit2Case testCase)
        {
            return TouchstoneXunit2.ExecuteAsync(testCase, _Output);
        }
    }
}
