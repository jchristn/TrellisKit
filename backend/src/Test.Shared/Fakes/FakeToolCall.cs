namespace Test.Shared.Fakes
{
    using System;

    /// <summary>
    /// A tool call the <see cref="FakeModelServer"/> asks for while tools are offered.
    /// </summary>
    public sealed class FakeToolCall
    {
        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Arguments as a JSON object.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";
    }
}
