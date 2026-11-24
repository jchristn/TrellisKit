namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One part of a multipart/mixed manifest response, parsed by the test kit the way expo-updates reads it.
    /// </summary>
    internal sealed class TestManifestPart
    {
        internal Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal string Name { get; set; } = String.Empty;

        internal byte[] Body { get; set; } = Array.Empty<byte>();
    }
}
