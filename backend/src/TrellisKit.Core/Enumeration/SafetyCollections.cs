namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the safety slice: a member's blocks, the admin word filter list, and member search.
    /// Column names use the aliases of the queries in BlockMethodsBase (b, u), WordFilterMethodsBase (t), and
    /// MemberSearchMethodsBase (u).
    /// </summary>
    public static class SafetyCollections
    {
        #region Public-Members

        /// <summary>
        /// GET /api/v1/me/blocks: the caller's blocks. Label and tag filters apply to the blocked member's (User)
        /// metadata; search covers the blocked member's display name and handle. Orderings CreatedDescending (default)
        /// and CreatedAscending (block time). No filters.
        /// </summary>
        public static readonly EnumerationCollection Blocks = new EnumerationCollection("me-blocks", TargetTypeEnum.User, "b.blockeduserid", "b.createdutc", false)
            .Search("u.displayname", "u.handle");

        /// <summary>
        /// GET /api/v1/admin/moderation/word-filter: admin word filter terms (metadata type WordFilterTerm). Orderings
        /// CreatedDescending (default), CreatedAscending, TermAscending, UpdatedDescending, HitsDescending (hits
        /// retained in wordfilterhits, which are pruned after 90 days). Search over term and note. Filters action
        /// (Block, Flag) and matchType (Word, Substring).
        /// </summary>
        public static readonly EnumerationCollection WordFilterTerms = new EnumerationCollection("word-filter-terms", TargetTypeEnum.WordFilterTerm, "t.id", "t.createdutc", true)
            .Order("TermAscending", "lower(t.term)", false, EnumerationKeyKindEnum.Text)
            .Order("UpdatedDescending", "t.updatedutc", true, EnumerationKeyKindEnum.Time)
            .Order("HitsDescending", "(SELECT COUNT(*) FROM wordfilterhits hc WHERE hc.propertyid = t.propertyid AND hc.termid = t.id)", true, EnumerationKeyKindEnum.Number)
            .Search("t.term", "t.note")
            .Filters("action", "matchType");

        /// <summary>
        /// GET /api/v1/members and POST /api/v1/members/enumerate: member search (ranked keyset pages). The
        /// search field is the typed prefix (matched by the member search SQL, not by generic search expressions);
        /// filters contextType and contextId rank a feed post's author and commenters first. Label and tag filters apply
        /// to the members' User metadata; created window on the account creation time. The ordering is the relevance
        /// ranking of contract section 3.4 (Relevance, the default), or account CreatedAscending/CreatedDescending. maxResults default 8, capped at 20.
        /// </summary>
        public static readonly EnumerationCollection MemberSearch = new EnumerationCollection("member-search", TargetTypeEnum.User, "u.id", "u.createdutc", false)
            .Order("Relevance", "u.id", false, EnumerationKeyKindEnum.Text)
            .Filters("contextType", "contextId")
            .DefaultTo("Relevance");

        #endregion
    }
}
