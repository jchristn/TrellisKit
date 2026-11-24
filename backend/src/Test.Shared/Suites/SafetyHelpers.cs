namespace Test.Shared.Suites
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared helpers for the safety and text suites (blocks, word filter, member search, link previews).
    /// </summary>
    internal static class SafetyHelpers
    {
        /// <summary>
        /// Register a member with a membership, a display name, and optionally a handle.
        /// </summary>
        internal static async Task<TestMember> MemberAsync(HttpFixture f, CancellationToken token, string displayName = "Test Member", string? handle = null)
        {
            TestMember member = await Api.RegisterAsync(f, token, null, displayName).ConfigureAwait(false);
            await Api.GrantCompAsync(f, member.Id, token).ConfigureAwait(false);
            if (handle != null) await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { handle }, HttpStatusCode.OK, token).ConfigureAwait(false);
            return member;
        }

        /// <summary>
        /// Grant a role (global scope; Feed scope for FeedModerator).
        /// </summary>
        internal static async Task GrantRoleAsync(HttpFixture f, string userId, RoleEnum role, CancellationToken token)
        {
            await f.Database.RoleAssignments.AddAsync(new RoleAssignment
            {
                Id = IdGenerator.GenerateRoleAssignmentId(),
                UserId = userId,
                Role = role,
                ResourceType = role == RoleEnum.FeedModerator ? ResourceTypeEnum.Feed : ResourceTypeEnum.Global,
                ResourceId = "*"
            }, token).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(userId);
        }

        /// <summary>
        /// Change a stored user directly.
        /// </summary>
        internal static async Task UpdateUserAsync(HttpFixture f, string userId, Action<User> change, CancellationToken token)
        {
            User user = await f.Database.Users.ReadAsync(userId, token).ConfigureAwait(false) ?? throw new InvalidOperationException("No user " + userId);
            change(user);
            await f.Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(userId);
        }

        /// <summary>
        /// Insert a feed post row directly (the feed slice is not part of this copy).
        /// </summary>
        internal static async Task<string> InsertFeedPostAsync(HttpFixture f, string authorUserId, string? linkPreviewId, CancellationToken token)
        {
            string id = IdGenerator.Generate("fpo_");
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
            await f.Raw.ExecuteQueryAsync("INSERT INTO feedposts (propertyid, id, authoruserid, kind, body, excerpt, visibility, linkpreviewid, createdutc, updatedutc) VALUES ('" + TestProperty.SampleId + "', '" + id + "', '" +
                authorUserId + "', 'Original', 'body', 'body', 'Members', " + (linkPreviewId == null ? "NULL" : "'" + linkPreviewId + "'") + ", '" + now + "', '" + now + "');", false, token).ConfigureAwait(false);
            return id;
        }

        /// <summary>
        /// Insert a comment row directly on a feed post.
        /// </summary>
        internal static async Task<string> InsertCommentAsync(HttpFixture f, string feedPostId, string authorUserId, string? linkPreviewId, CancellationToken token)
        {
            string id = IdGenerator.Generate("cmt_");
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
            await f.Raw.ExecuteQueryAsync("INSERT INTO comments (propertyid, id, targettype, targetid, rootcommentid, path, depth, authoruserid, body, linkpreviewid, createdutc, updatedutc) VALUES ('" + TestProperty.SampleId + "', '" + id +
                "', 'FeedPost', '" + feedPostId + "', '" + id + "', '" + id + "', 0, '" + authorUserId + "', 'hi', " + (linkPreviewId == null ? "NULL" : "'" + linkPreviewId + "'") +
                ", '" + now + "', '" + now + "');", false, token).ConfigureAwait(false);
            return id;
        }
    }
}
