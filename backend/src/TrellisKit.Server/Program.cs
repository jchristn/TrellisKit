namespace TrellisKit.Server
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments. args[0], if present, is the settings file path.</param>
        /// <returns>Process exit code.</returns>
        public static int Main(string[] args)
        {
            return Bootstrapper.Run(args);
        }
    }
}
