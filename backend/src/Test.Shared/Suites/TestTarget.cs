namespace Test.Shared.Suites
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One in-memory object served by <see cref="TestTargetProvider"/>.
    /// </summary>
    internal sealed class TestTarget
    {
        internal TargetTypeEnum Type { get; set; } = TargetTypeEnum.Lesson;

        internal string Id { get; set; } = String.Empty;

        internal string Title { get; set; } = String.Empty;

        internal string Text { get; set; } = String.Empty;

        internal string AuthorUserId { get; set; } = String.Empty;

        internal ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        internal bool StaffOnly { get; set; } = false;

        internal bool Deleted { get; set; } = false;

        internal bool CommentsEnabled { get; set; } = true;

        internal ModerationStateEnum State { get; set; } = ModerationStateEnum.None;

        internal string? PromotedFeedPostId { get; set; } = null;

        internal int CommentCount { get; set; } = 0;

        internal DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
