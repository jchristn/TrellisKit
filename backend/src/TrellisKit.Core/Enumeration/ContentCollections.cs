namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the content slice (feed posts, comments, reactions, revisions). Column names use the
    /// aliases of the queries in FeedPostMethodsBase (f, fr), CommentMethodsBase (c, cr), and ReactionMethodsBase (r).
    /// </summary>
    public static class ContentCollections
    {
        #region Public-Members

        /// <summary>
        /// Orderings of the member feed.
        /// </summary>
        public const string FeedOrderings = "Feed, PublishedDescending, CreatedDescending, CreatedAscending";

        /// <summary>
        /// Orderings of comment trees and children.
        /// </summary>
        public const string CommentOrderings = "Top, CreatedDescending, CreatedAscending";

        /// <summary>
        /// Orderings of the admin feed.
        /// </summary>
        public const string AdminFeedOrderings = "PublishedDescending, PublishedAscending, CreatedDescending, CreatedAscending, PublishAtAscending, CommentCountDescending, ReactionTotalDescending";

        /// <summary>
        /// GET /api/v1/feed: Published posts, not deleted, moderation None. Ordering Feed (default) puts every pinned post
        /// first and then the unpinned posts by COALESCE(publishedutc, createdutc) descending, with one bounded page
        /// and continuation keyset covering both groups; PublishedDescending is the same order without the pinned
        /// prefix. Search over title and body. Filters publishedAfterUtc, publishedBeforeUtc, and audience (Public,
        /// SignedIn, Members; posts whose audience the caller does not meet are never listed).
        /// </summary>
        public static readonly EnumerationCollection Feed = new EnumerationCollection("feed", TargetTypeEnum.FeedPost, "f.id", "f.createdutc", false)
            .Order("Feed", "(CASE WHEN f.ispinned THEN '1|' || to_char(COALESCE(f.pinnedutc, f.createdutc) AT TIME ZONE 'UTC', 'YYYYMMDDHH24MISSUS') ELSE '0|' || to_char(COALESCE(f.publishedutc, f.createdutc) AT TIME ZONE 'UTC', 'YYYYMMDDHH24MISSUS') END)", true, EnumerationKeyKindEnum.Text)
            .Order("PublishedDescending", "COALESCE(f.publishedutc, f.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("f.title", "f.body")
            .Filters("publishedAfterUtc", "publishedBeforeUtc", "audience")
            .DefaultTo("Feed");

        /// <summary>
        /// GET /api/v1/public/feed: Public, Published posts (anonymous), newest publishedUtc first. Same orderings and
        /// filters as the member feed except the pinned prefix (PublishedDescending is the default; Feed is accepted and
        /// behaves the same).
        /// </summary>
        public static readonly EnumerationCollection PublicFeed = new EnumerationCollection("public-feed", TargetTypeEnum.FeedPost, "f.id", "f.createdutc", false)
            .Order("PublishedDescending", "COALESCE(f.publishedutc, f.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Order("Feed", "COALESCE(f.publishedutc, f.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("f.title", "f.body")
            .Filters("publishedAfterUtc", "publishedBeforeUtc", "audience")
            .DefaultTo("PublishedDescending");

        /// <summary>
        /// GET /api/v1/me/feed-posts: the caller's posts in every state except Deleted, newest createdUtc first. Filters
        /// state (Scheduled, Processing, Published, Hidden, Removed) and audience.
        /// </summary>
        public static readonly EnumerationCollection MyFeedPosts = new EnumerationCollection("me-feed-posts", TargetTypeEnum.FeedPost, "f.id", "f.createdutc", false)
            .Search("f.title", "f.body")
            .Filters("state", "audience");

        /// <summary>
        /// GET /api/v1/admin/feed (admin collection: skip and exact totals). The standard createdAfterUtc and
        /// createdBeforeUtc window applies to createdUtc. Filters state (any state or Deleted), authorUserId, pinned,
        /// audience, and publishedAfterUtc and publishedBeforeUtc (aliases fromUtc and toUtc; on publishedUtc, or
        /// createdUtc for unpublished posts). Search over title and body.
        /// </summary>
        public static readonly EnumerationCollection AdminFeed = new EnumerationCollection("admin-feed", TargetTypeEnum.FeedPost, "f.id", "f.createdutc", true)
            .Order("PublishedDescending", "COALESCE(f.publishedutc, f.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Order("PublishedAscending", "COALESCE(f.publishedutc, f.createdutc)", false, EnumerationKeyKindEnum.Time)
            .Order("PublishAtAscending", "COALESCE(f.publishatutc, f.publishedutc, f.createdutc)", false, EnumerationKeyKindEnum.Time)
            .Order("CommentCountDescending", "f.commentcount", true, EnumerationKeyKindEnum.Number)
            .Order("ReactionTotalDescending", "f.reactiontotal", true, EnumerationKeyKindEnum.Number)
            .Search("f.title", "f.body")
            .Filters("state", "authorUserId", "pinned", "audience", "fromUtc", "toUtc", "publishedAfterUtc", "publishedBeforeUtc")
            .DefaultTo("PublishedDescending");

        /// <summary>
        /// GET /api/v1/feed/{id}/comments: top-level comments of a post with nested replies. Ordering Top (default:
        /// score descending, then createdUtc ascending, then id), CreatedDescending, CreatedAscending; the same order is
        /// used for siblings at every level. Filters depth (1 to 5, default 3) and childLimit (1 to 20, default 5); they
        /// shape the page and are not part of the continuation token. Label, tag, and search filters apply to the
        /// top-level comments.
        /// </summary>
        public static readonly EnumerationCollection CommentTree = new EnumerationCollection("comment-tree", TargetTypeEnum.Comment, "c.id", "c.createdutc", false)
            .Order("Top", "c.score", true, EnumerationKeyKindEnum.Text)
            .Search("c.body")
            .Filters("depth", "childLimit")
            .DefaultTo("Top");

        /// <summary>
        /// GET /api/v1/comments/{id}/children: the direct replies of one comment with nested replies, same orderings
        /// and filters as the tree. Its continuation token (also handed out as moreChildren.cursor) is fingerprinted
        /// on the parent and the ordering only, so it is accepted whatever depth or childLimit the client sends.
        /// </summary>
        public static readonly EnumerationCollection CommentChildren = new EnumerationCollection("comment-children", TargetTypeEnum.Comment, "c.id", "c.createdutc", false)
            .Order("Top", "c.score", true, EnumerationKeyKindEnum.Text)
            .Search("c.body")
            .Filters("depth", "childLimit")
            .DefaultTo("Top");

        /// <summary>
        /// GET /api/v1/feed/{id}/reactions and /api/v1/comments/{id}/reactions: who reacted, newest first. Filter key.
        /// Label and tag filters apply to the reactions' own metadata (type Reaction).
        /// </summary>
        public static readonly EnumerationCollection Reactions = new EnumerationCollection("reactions", TargetTypeEnum.Reaction, "r.id", "r.createdutc", false)
            .Filters("key");

        /// <summary>
        /// GET /api/v1/feed/{id}/revisions: a post's revisions, newest first. Label and tag filters apply to the
        /// revisions' own metadata (type FeedPostRevision: the labels and tags the post had in that version).
        /// </summary>
        public static readonly EnumerationCollection FeedPostRevisions = new EnumerationCollection("feed-post-revisions", TargetTypeEnum.FeedPostRevision, "fr.id", "fr.createdutc", false)
            .Search("fr.title", "fr.body");

        /// <summary>
        /// GET /api/v1/comments/{id}/revisions: a comment's revisions, newest first. Label and tag filters apply to the
        /// revisions' own metadata (type CommentRevision: the labels and tags the comment had in that version).
        /// </summary>
        public static readonly EnumerationCollection CommentRevisions = new EnumerationCollection("comment-revisions", TargetTypeEnum.CommentRevision, "cr.id", "cr.createdutc", false)
            .Search("cr.body");

        #endregion
    }
}
