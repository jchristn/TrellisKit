namespace TrellisKit.Core.PropertyConfig
{
    using System.Collections.Generic;

    /// <summary>
    /// Mobile destination placement: drawer order, bottom shortcuts, the header account menu, and
    /// destination label overrides. Missing values retain the engine defaults. The client appends to the
    /// drawer any destination placed on no surface, filtered by features and caller access.
    /// </summary>
    public class PropertyMobileNavigation
    {
        #region Private-Members

        private List<string>? _DrawerItems = null;
        private List<string>? _Shortcuts = null;
        private List<string>? _AccountMenu = null;
        private Dictionary<string, string>? _Labels = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Maximum number of bottom shortcuts. Empty shortcuts explicitly selects drawer-only navigation.
        /// </summary>
        public const int MaxShortcuts = 4;

        /// <summary>
        /// Maximum length of a destination label override, after trimming.
        /// </summary>
        public const int MaxLabelLength = 24;

        /// <summary>
        /// Preferred drawer destination identifiers, in display order. Null uses engine order. At most 15
        /// distinct known identifiers are accepted. Identifiers placed on no surface are appended by the client.
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

        /// <summary>
        /// Destination identifiers shown, in order, in the menu opened from the header account button.
        /// Null or empty hides the button. At most 15 distinct known identifiers are accepted; destinations
        /// listed here are not appended to the drawer.
        /// </summary>
        public List<string>? AccountMenu
        {
            get => _AccountMenu;
            set => _AccountMenu = value;
        }

        /// <summary>
        /// Label overrides keyed by known destination identifier, 1 to 24 characters each. Null keeps the
        /// engine labels (property terminology, then the app string catalog).
        /// </summary>
        public Dictionary<string, string>? Labels
        {
            get => _Labels;
            set => _Labels = value;
        }

        #endregion
    }
}
