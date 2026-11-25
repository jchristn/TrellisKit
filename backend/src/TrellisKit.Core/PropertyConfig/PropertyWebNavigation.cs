namespace TrellisKit.Core.PropertyConfig
{
    using System.Collections.Generic;

    /// <summary>
    /// Website member navigation (<c>web.navigation</c>): the preferred order of member sections and label overrides.
    /// Missing values keep the engine order and labels. Sections not listed are appended in engine order, except a
    /// section covered by a listed or visible section (such as the library inside Training). Validated with the same
    /// rules as <c>mobile.navigation</c> (shared <c>validateNavigation</c>).
    /// </summary>
    public class PropertyWebNavigation
    {
        #region Private-Members

        private List<string>? _Primary = null;
        private Dictionary<string, string>? _Labels = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Maximum number of identifiers in <see cref="Primary"/>.
        /// </summary>
        public const int MaxPrimary = 8;

        /// <summary>
        /// Maximum length of a section label override, after trimming.
        /// </summary>
        public const int MaxLabelLength = 24;

        /// <summary>
        /// Member section identifiers shown first, in order. Null uses the engine order. At most 8 distinct known
        /// identifiers are accepted.
        /// </summary>
        public List<string>? Primary
        {
            get => _Primary;
            set => _Primary = value;
        }

        /// <summary>
        /// Label overrides keyed by known member section identifier, 1 to 24 characters each. Null keeps the engine
        /// labels.
        /// </summary>
        public Dictionary<string, string>? Labels
        {
            get => _Labels;
            set => _Labels = value;
        }

        #endregion
    }
}
