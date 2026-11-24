namespace TrellisKit.Core.PropertyConfig
{
    using System.Collections.Generic;

    /// <summary>
    /// Mobile destination ordering and bottom shortcuts. Missing values retain the engine defaults.
    /// The complete drawer always appends omitted destinations, filtered by features and caller access.
    /// </summary>
    public class PropertyMobileNavigation
    {
        #region Private-Members

        private List<string>? _DrawerItems = null;
        private List<string>? _Shortcuts = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Maximum number of bottom shortcuts. Empty shortcuts explicitly selects drawer-only navigation.
        /// </summary>
        public const int MaxShortcuts = 4;

        /// <summary>
        /// Preferred drawer destination identifiers, in display order. Null uses engine order. At most 15
        /// distinct known identifiers are accepted. Omitted identifiers are appended by the client.
        /// </summary>
        public List<string>? DrawerItems
        {
            get => _DrawerItems;
            set => _DrawerItems = value;
        }

        /// <summary>
        /// Zero to four distinct known destination identifiers for the bottom bar. Null defaults to
        /// feed, forums, messages and profile. Disabled or inaccessible destinations are hidden.
        /// </summary>
        public List<string>? Shortcuts
        {
            get => _Shortcuts;
            set => _Shortcuts = value;
        }

        #endregion
    }
}
