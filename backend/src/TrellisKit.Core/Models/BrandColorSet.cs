namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Every brand color of one color scheme (light or dark), each a #RRGGBB hex value (docs/TRELLISKIT.md section 2.1).
    /// onAccentSoft is the text and icon color on the soft accent background (chips, active navigation, avatar
    /// initials); when a property document leaves it out it defaults to primary.
    /// Thread safety: not thread safe; snapshots served to requests are never mutated.
    /// </summary>
    public class BrandColorSet
    {
        #region Public-Members

        /// <summary>
        /// Every brand color key in its wire form, in display order.
        /// </summary>
        public static IReadOnlyList<string> Keys
        {
            get { return _Keys; }
        }

        /// <summary>
        /// Primary color (buttons, links, fills).
        /// </summary>
        public string Primary { get; set; } = String.Empty;

        /// <summary>
        /// Primary color on hover.
        /// </summary>
        public string PrimaryHover { get; set; } = String.Empty;

        /// <summary>
        /// Text color on primary and primaryHover.
        /// </summary>
        public string OnPrimary { get; set; } = String.Empty;

        /// <summary>
        /// Accent color (brand surfaces).
        /// </summary>
        public string Accent { get; set; } = String.Empty;

        /// <summary>
        /// Text color on accent.
        /// </summary>
        public string OnAccent { get; set; } = String.Empty;

        /// <summary>
        /// Soft accent background (selected chips, active navigation, avatar fallbacks).
        /// </summary>
        public string AccentSoft { get; set; } = String.Empty;

        /// <summary>
        /// Text and icon color on accentSoft.
        /// </summary>
        public string OnAccentSoft { get; set; } = String.Empty;

        /// <summary>
        /// Browser theme color.
        /// </summary>
        public string ThemeColor { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private static readonly List<string> _Keys = new List<string> { "primary", "primaryHover", "onPrimary", "accent", "onAccent", "accentSoft", "onAccentSoft", "themeColor" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a wire key names a brand color.
        /// </summary>
        /// <param name="key">Key, for example onAccentSoft.</param>
        /// <returns>True when known (case-sensitive).</returns>
        public static bool IsKey(string? key)
        {
            return key != null && _Keys.Contains(key);
        }

        /// <summary>
        /// Build a set from a property document scheme: every value normalized to uppercase #RRGGBB (3-digit values are
        /// expanded, the alpha of 8-digit values is dropped), and onAccentSoft defaulting to primary.
        /// </summary>
        /// <param name="scheme">Document scheme, or null.</param>
        /// <returns>Set.</returns>
        public static BrandColorSet FromScheme(PropertyColorScheme? scheme)
        {
            BrandColorSet set = new BrandColorSet();
            if (scheme == null) return set;
            set.Primary = BrandContrast.NormalizeDocumentColor(scheme.Primary);
            set.PrimaryHover = BrandContrast.NormalizeDocumentColor(scheme.PrimaryHover);
            set.OnPrimary = BrandContrast.NormalizeDocumentColor(scheme.OnPrimary);
            set.Accent = BrandContrast.NormalizeDocumentColor(scheme.Accent);
            set.OnAccent = BrandContrast.NormalizeDocumentColor(scheme.OnAccent);
            set.AccentSoft = BrandContrast.NormalizeDocumentColor(scheme.AccentSoft);
            string onAccentSoft = BrandContrast.NormalizeDocumentColor(scheme.OnAccentSoft);
            set.OnAccentSoft = onAccentSoft.Length > 0 ? onAccentSoft : set.Primary;
            set.ThemeColor = BrandContrast.NormalizeDocumentColor(scheme.ThemeColor);
            return set;
        }

        /// <summary>
        /// The value of a key.
        /// </summary>
        /// <param name="key">Wire key.</param>
        /// <returns>Value, or empty for an unknown key.</returns>
        public string Get(string key)
        {
            switch (key)
            {
                case "primary": return Primary;
                case "primaryHover": return PrimaryHover;
                case "onPrimary": return OnPrimary;
                case "accent": return Accent;
                case "onAccent": return OnAccent;
                case "accentSoft": return AccentSoft;
                case "onAccentSoft": return OnAccentSoft;
                case "themeColor": return ThemeColor;
                default: return String.Empty;
            }
        }

        /// <summary>
        /// Set the value of a key.
        /// </summary>
        /// <param name="key">Wire key.</param>
        /// <param name="value">Value (stored as given).</param>
        /// <exception cref="ArgumentException">The key is not a brand color key.</exception>
        public void Set(string key, string value)
        {
            switch (key)
            {
                case "primary": Primary = value; break;
                case "primaryHover": PrimaryHover = value; break;
                case "onPrimary": OnPrimary = value; break;
                case "accent": Accent = value; break;
                case "onAccent": OnAccent = value; break;
                case "accentSoft": AccentSoft = value; break;
                case "onAccentSoft": OnAccentSoft = value; break;
                case "themeColor": ThemeColor = value; break;
                default: throw new ArgumentException("Unknown brand color key " + key + ".", nameof(key));
            }
        }

        /// <summary>
        /// A copy of this set.
        /// </summary>
        /// <returns>Copy.</returns>
        public BrandColorSet Clone()
        {
            BrandColorSet copy = new BrandColorSet();
            foreach (string key in _Keys) copy.Set(key, Get(key));
            return copy;
        }

        /// <summary>
        /// Whether every key has the same value as another set (ordinal, case-insensitive).
        /// </summary>
        /// <param name="other">Other set, or null.</param>
        /// <returns>True when equal.</returns>
        public bool SameAs(BrandColorSet? other)
        {
            if (other == null) return false;
            foreach (string key in _Keys)
            {
                if (!String.Equals(Get(key), other.Get(key), StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// The document form of this set (GET /api/v1/property brand.colors).
        /// </summary>
        /// <returns>Scheme.</returns>
        public PropertyColorScheme ToScheme()
        {
            return new PropertyColorScheme
            {
                Primary = Primary,
                PrimaryHover = PrimaryHover,
                OnPrimary = OnPrimary,
                Accent = Accent,
                OnAccent = OnAccent,
                AccentSoft = AccentSoft,
                OnAccentSoft = OnAccentSoft,
                ThemeColor = ThemeColor
            };
        }

        #endregion
    }
}
