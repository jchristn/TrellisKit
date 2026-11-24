namespace Test.Automated
{
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    internal static class Program
    {
        private static Task<int> Main(string[] args)
        {
            // Every suite runs against PostgreSQL (TK_TEST_PG_* variables); there is no provider option.
            return ConsoleRunner.RunMainAsync(TrellisKitSuites.All, args);
        }
    }
}
