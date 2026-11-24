namespace Test.Shared.Suites
{
    using System;

    /// <summary>
    /// A member created by a test, with its current tokens.
    /// </summary>
    internal sealed class TestMember
    {
        internal string Id { get; set; } = String.Empty;

        internal string Email { get; set; } = String.Empty;

        internal string Password { get; set; } = String.Empty;

        internal string AccessToken { get; set; } = String.Empty;

        internal string? RefreshToken { get; set; } = null;
    }
}
