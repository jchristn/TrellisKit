namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Admin member list filter.
    /// </summary>
    public class UserFilter
    {
        #region Public-Members

        /// <summary>
        /// Membership filter, or null.
        /// </summary>
        public MembershipFilterEnum? Membership { get; set; } = null;

        /// <summary>
        /// Rank, or null.
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Only members with this role, or null.
        /// </summary>
        public RoleEnum? Role { get; set; } = null;

        /// <summary>
        /// Only banned (true) or not banned (false) members, or null.
        /// </summary>
        public bool? Banned { get; set; } = null;

        /// <summary>
        /// Whether deleted accounts are included. Default false.
        /// </summary>
        public bool IncludeDeleted { get; set; } = false;

        /// <summary>
        /// Compiled profile filters (docs/MULTIPROPERTY_CONTRACT.md section 9.12); each becomes a scoped EXISTS subquery.
        /// </summary>
        public List<TrellisKit.Core.Models.ProfileFilterClause> Profile
        {
            get { return _Profile; }
            set { _Profile = value ?? new List<TrellisKit.Core.Models.ProfileFilterClause>(); }
        }

        /// <summary>
        /// Only accounts with a handle (a finished profile). Default false. The member directory sets it.
        /// </summary>
        public bool RequireHandle { get; set; } = false;

        /// <summary>
        /// Only accounts with an active membership or a staff role. Default false. The member directory sets it when
        /// the access model requires a membership to use the directory.
        /// </summary>
        public bool MembersOrStaffOnly { get; set; } = false;

        /// <summary>
        /// Member search text: display name and handle contain it, or a searchable field word starts with it. Null
        /// means no search (the collection search, if any, still applies).
        /// </summary>
        public string? MemberSearch { get; set; } = null;

        /// <summary>
        /// Searchable field ids the caller may see (member search text over profilesearchwords).
        /// </summary>
        public List<string> SearchFieldIds
        {
            get { return _SearchFieldIds; }
            set { _SearchFieldIds = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<TrellisKit.Core.Models.ProfileFilterClause> _Profile = new List<TrellisKit.Core.Models.ProfileFilterClause>();
        private List<string> _SearchFieldIds = new List<string>();

        #endregion
    }
}
