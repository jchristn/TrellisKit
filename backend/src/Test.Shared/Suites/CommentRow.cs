namespace Test.Shared.Suites
{
    using System;

    /// <summary>
    /// A comment row read back for the counter integrity checks.
    /// </summary>
    internal sealed class CommentRow
    {
        internal string Id { get; set; } = String.Empty;

        internal string? ParentId { get; set; } = null;

        internal string Path { get; set; } = String.Empty;

        internal int ChildCount { get; set; } = 0;

        internal int DescendantCount { get; set; } = 0;

        internal int ReactionTotal { get; set; } = 0;

        internal int Score { get; set; } = 0;

        internal bool Visible { get; set; } = false;
    }
}
