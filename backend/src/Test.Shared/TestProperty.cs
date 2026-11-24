namespace Test.Shared
{
    using System;
    using System.IO;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Access to the engine's neutral sample property (samples/example) for tests, and temporary copies of it that
    /// tests may modify.
    /// </summary>
    public static class TestProperty
    {
        #region Public-Members

        /// <summary>
        /// Id of the sample property (samples/example/property.json).
        /// </summary>
        public const string SampleId = "app_mwczw7mg_F8RsDcNidltPWZS1seB";

        /// <summary>
        /// Id of the test-local second property used by the isolation suites (never a real property).
        /// </summary>
        public const string SecondId = "app_mwczw7mr_6deWkaOcaH4bbbyb7QD";

        /// <summary>
        /// Full path of the sample property directory, found by searching upward from the test binaries and the
        /// working directory.
        /// </summary>
        public static string SampleDirectory
        {
            get { return PropertyLoader.ResolveDirectory(null, new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }); }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load a fresh, validated instance of the sample property. Callers may modify the returned instance.
        /// </summary>
        /// <returns>Property.</returns>
        public static PropertyConfiguration LoadSample()
        {
            return PropertyLoader.Load(SampleDirectory);
        }

        /// <summary>
        /// Copy the sample property (property.json, brand/, and legal/) to a new temporary directory.
        /// </summary>
        /// <returns>Full path of the copy. The caller deletes it with <see cref="Delete"/>.</returns>
        public static string CopySample()
        {
            string source = SampleDirectory;
            string target = Path.Combine(TestEnvironment.TempRoot, "property-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(target, PropertyLoader.BrandFolder));
            File.Copy(Path.Combine(source, PropertyLoader.PropertyFileName), Path.Combine(target, PropertyLoader.PropertyFileName));
            foreach (string file in Directory.GetFiles(Path.Combine(source, PropertyLoader.BrandFolder)))
                File.Copy(file, Path.Combine(target, PropertyLoader.BrandFolder, Path.GetFileName(file)));
            string legal = Path.Combine(source, "legal");
            if (Directory.Exists(legal))
            {
                Directory.CreateDirectory(Path.Combine(target, "legal"));
                foreach (string file in Directory.GetFiles(legal)) File.Copy(file, Path.Combine(target, "legal", Path.GetFileName(file)));
            }
            return target;
        }

        /// <summary>
        /// Delete a temporary property directory (best effort).
        /// </summary>
        /// <param name="directory">Directory.</param>
        public static void Delete(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort.
            }
        }

        #endregion
    }
}
