namespace TrellisKit.Core.Database.Shared
{
    using System;

    /// <summary>
    /// Counter statements shared by the forum, thread, comment, and reaction method sets (docs/PHASE3_CONTRACT.md
    /// section 17 write rules: counters are recomputed from source rows inside the same batch). Every statement carries
    /// the propertyid predicate on every table reference.
    /// </summary>
    internal static class ForumCounterSql
    {
        /// <summary>
        /// Condition of a live (Published, unmoderated, undeleted) thread on an alias.
        /// </summary>
        internal static string Live(string alias)
        {
            return alias + ".deletedutc IS NULL AND " + alias + ".moderationstate = 'None' AND " + alias + ".publishedutc IS NOT NULL";
        }

        /// <summary>
        /// Recompute a forum's thread count, comment count, last activity, and last thread.
        /// </summary>
        /// <param name="pid">Property id SQL literal.</param>
        /// <param name="forumIdExpression">SQL expression or subquery yielding the forum id (a literal or a scoped subquery).</param>
        internal static string RecomputeForum(string pid, string forumIdExpression)
        {
            string match = "c.propertyid = " + pid + " AND c.forumid = forums.id AND " + Live("c");
            return "UPDATE forums SET threadcount = (SELECT COUNT(*) FROM forumthreads c WHERE " + match + "), " +
                "commentcount = (SELECT COALESCE(SUM(c.commentcount), 0) FROM forumthreads c WHERE " + match + "), " +
                "lastactivityutc = (SELECT MAX(c.lastactivityutc) FROM forumthreads c WHERE " + match + "), " +
                "lastthreadid = (SELECT c.id FROM forumthreads c WHERE " + match + " ORDER BY c.lastactivityutc DESC, c.id DESC LIMIT 1) " +
                "WHERE propertyid = " + pid + " AND id IN (" + forumIdExpression + ");";
        }

        /// <summary>
        /// Subquery yielding the forum of a thread.
        /// </summary>
        internal static string ForumOfThread(string pid, string threadIdLiteral)
        {
            return "SELECT ft.forumid FROM forumthreads ft WHERE ft.propertyid = " + pid + " AND ft.id = " + threadIdLiteral;
        }

        /// <summary>
        /// Recompute a thread's comment counters from its visible comments (Original threads; the conversation target is
        /// the thread), refresh its last activity, and re-evaluate the accepted answer.
        /// </summary>
        internal static string RecomputeThreadComments(string pid, string threadIdLiteral)
        {
            string visible = "k.propertyid = " + pid + " AND k.targettype = 'Thread' AND k.targetid = forumthreads.id AND k.deletedutc IS NULL AND k.moderationstate = 'None'";
            string last = "(SELECT MAX(k.createdutc) FROM comments k WHERE " + visible + ")";
            return "UPDATE forumthreads SET commentcount = (SELECT COUNT(*) FROM comments k WHERE " + visible + "), " +
                "uniquecommentercount = (SELECT COUNT(DISTINCT k.authoruserid) FROM comments k WHERE " + visible + " AND k.authoruserid <> forumthreads.authoruserid), " +
                "lastcommentutc = " + last + ", " +
                "lastactivityutc = GREATEST(lastactivityutc, COALESCE(" + last + ", lastactivityutc)), " +
                "hasacceptedanswer = (acceptedcommentid IS NOT NULL AND EXISTS (SELECT 1 FROM comments k WHERE " + visible + " AND k.id = forumthreads.acceptedcommentid)) " +
                "WHERE propertyid = " + pid + " AND id = " + threadIdLiteral + ";";
        }

        /// <summary>
        /// Mirror a feed post's comment counters onto its linked discussion thread (LinkedFeedPost threads share the
        /// feed post's conversation, section 8.2).
        /// </summary>
        internal static string RecomputeLinkedThreadComments(string pid, string feedPostIdLiteral)
        {
            string visible = "k.propertyid = " + pid + " AND k.targettype = 'FeedPost' AND k.targetid = forumthreads.sourceid AND k.deletedutc IS NULL AND k.moderationstate = 'None'";
            string last = "(SELECT MAX(k.createdutc) FROM comments k WHERE " + visible + ")";
            return "UPDATE forumthreads SET commentcount = (SELECT COUNT(*) FROM comments k WHERE " + visible + "), " +
                "uniquecommentercount = (SELECT COUNT(DISTINCT k.authoruserid) FROM comments k WHERE " + visible + " AND k.authoruserid <> forumthreads.authoruserid), " +
                "lastcommentutc = " + last + ", " +
                "lastactivityutc = GREATEST(lastactivityutc, COALESCE(" + last + ", lastactivityutc)) " +
                "WHERE propertyid = " + pid + " AND sourcetype = 'FeedPost' AND sourceid = " + feedPostIdLiteral + " AND deletedutc IS NULL;";
        }

        /// <summary>
        /// Subquery yielding the forum of the linked discussion thread of a feed post.
        /// </summary>
        internal static string ForumOfLinkedThread(string pid, string feedPostIdLiteral)
        {
            return "SELECT ft.forumid FROM forumthreads ft WHERE ft.propertyid = " + pid + " AND ft.sourcetype = 'FeedPost' AND ft.sourceid = " + feedPostIdLiteral;
        }

        /// <summary>
        /// Text of an audience rank (Public 0, SignedIn 1, Members 2) for an audience column, used to narrow audiences.
        /// </summary>
        internal static string AudienceRank(string column)
        {
            if (String.IsNullOrEmpty(column)) throw new ArgumentNullException(nameof(column));
            return "(CASE " + column + " WHEN 'Public' THEN 0 WHEN 'SignedIn' THEN 1 ELSE 2 END)";
        }
    }
}
