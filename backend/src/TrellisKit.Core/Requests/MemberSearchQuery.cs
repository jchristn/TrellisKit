namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Enumeration query of member search (GET /api/v1/members, POST /api/v1/members/enumerate, contract section 3.4):
    /// the typed text is the standard search field (an optional leading "@" is ignored); contextType and contextId are
    /// typed filters. <see cref="Q"/> is an alias of search for the GET form.
    /// </summary>
    public class MemberSearchQuery : EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Alias of Search (GET form ?q=); used only when Search is empty.
        /// </summary>
        public string? Q { get; set; } = null;

        #endregion
    }
}
