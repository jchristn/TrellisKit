namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Property identifiers (docs/MULTIPROPERTY_CONTRACT.md, resolved decision 17). A property id is an opaque,
    /// generated, never-renamed identifier of the form app_{k-sortable}_{random} (PrettyId), for example
    /// app_mwcnf2ia_Yk3hhcUgbET5kqGV. It identifies the property in every row, storage key, token, header, and URL.
    /// The separate slug (lowercase letters, digits, hyphens) is cosmetic only (folder names, image tags,
    /// infrastructure resource names). Reserved pseudo-ids name the platform realm and unresolved requests and are
    /// never property ids.
    /// </summary>
    public static class PropertyIds
    {
        #region Public-Members

        /// <summary>
        /// Prefix of every property id.
        /// </summary>
        public const string Prefix = "app_";

        /// <summary>
        /// Pseudo-id of the platform realm (platform audit records, platform request history, platform labels and tags).
        /// </summary>
        public const string Platform = "platform";

        /// <summary>
        /// Pseudo-id stamped on request history of requests that resolved to no property.
        /// </summary>
        public const string Unresolved = "unresolved";

        /// <summary>
        /// Reserved pseudo-ids. No property may use them.
        /// </summary>
        public static IReadOnlyList<string> Reserved
        {
            get { return _Reserved; }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _Id = new Regex("^app_[a-z0-9]{6,16}_[A-Za-z0-9]{8,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Slug = new Regex("^[a-z0-9][a-z0-9-]{0,62}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly List<string> _Reserved = new List<string> { Platform, Unresolved, "none", "all", "system" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a new property id.
        /// </summary>
        /// <returns>Id, for example app_mwcnf2ia_Yk3hhcUgbET5kqGV.</returns>
        public static string Generate()
        {
            return IdGenerator.Generate(Prefix);
        }

        /// <summary>
        /// Whether a value is a well-formed property id (not a reserved pseudo-id).
        /// </summary>
        /// <param name="id">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValid(string? id)
        {
            return !String.IsNullOrEmpty(id) && _Id.IsMatch(id);
        }

        /// <summary>
        /// Whether a value is a reserved pseudo-id (platform, unresolved, none, all, system).
        /// </summary>
        /// <param name="id">Value.</param>
        /// <returns>True when reserved.</returns>
        public static bool IsReserved(string? id)
        {
            return !String.IsNullOrEmpty(id) && _Reserved.Contains(id);
        }

        /// <summary>
        /// Whether a value is a well-formed cosmetic slug: lowercase letters, digits, and hyphens, 1 to 63
        /// characters, starting with a letter or digit.
        /// </summary>
        /// <param name="slug">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidSlug(string? slug)
        {
            return !String.IsNullOrEmpty(slug) && _Slug.IsMatch(slug) && !IsReserved(slug);
        }

        #endregion
    }
}
