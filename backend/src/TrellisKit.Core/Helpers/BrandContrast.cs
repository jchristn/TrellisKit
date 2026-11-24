namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// WCAG 2.1 contrast math and the brand color pairs every palette is checked on (docs/TRELLISKIT.md section 2.1).
    /// shared/src/brand.js mirrors this class exactly; change both together.
    /// Thread safety: stateless and safe to call concurrently.
    /// </summary>
    public static class BrandContrast
    {
        #region Public-Members

        /// <summary>
        /// The engine page background of the light scheme, used for pairs whose background is "background".
        /// </summary>
        public const string LightBackground = "#FFFFFF";

        /// <summary>
        /// The engine page background of the dark scheme, used for pairs whose background is "background".
        /// </summary>
        public const string DarkBackground = "#0E141D";

        /// <summary>
        /// The pseudo key of the engine page background in a pair.
        /// </summary>
        public const string BackgroundKey = "background";

        /// <summary>
        /// The pairs checked in each scheme, in display order.
        /// </summary>
        public static IReadOnlyList<BrandContrastPair> Pairs
        {
            get { return _Pairs; }
        }

        /// <summary>
        /// The color schemes, in display order.
        /// </summary>
        public static IReadOnlyList<string> Schemes
        {
            get { return _Schemes; }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _Hex = new Regex("^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _DocumentHex = new Regex("^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly List<string> _Schemes = new List<string> { "light", "dark" };
        private static readonly List<BrandContrastPair> _Pairs = new List<BrandContrastPair>
        {
            new BrandContrastPair { Foreground = "onPrimary", Background = "primary", Level = BrandContrastLevelEnum.Text, Usage = "Button labels" },
            new BrandContrastPair { Foreground = "onPrimary", Background = "primaryHover", Level = BrandContrastLevelEnum.Text, Usage = "Button labels on hover" },
            new BrandContrastPair { Foreground = "onAccent", Background = "accent", Level = BrandContrastLevelEnum.Text, Usage = "Text on accent surfaces" },
            new BrandContrastPair { Foreground = "onAccentSoft", Background = "accentSoft", Level = BrandContrastLevelEnum.Text, Usage = "Selected chips, active navigation, and avatar initials" },
            new BrandContrastPair { Foreground = "primary", Background = BackgroundKey, Level = BrandContrastLevelEnum.Text, Usage = "Links and primary text on the page" },
            new BrandContrastPair { Foreground = "primary", Background = "accentSoft", Level = BrandContrastLevelEnum.Component, Usage = "Selected chip borders on the soft accent" },
            new BrandContrastPair { Foreground = "accent", Background = BackgroundKey, Level = BrandContrastLevelEnum.Component, Usage = "Accent surfaces against the page" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize an editable color: #RGB or #RRGGBB (any case) to uppercase #RRGGBB.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Normalized color, or null when the value is not #RGB or #RRGGBB.</returns>
        public static string? NormalizeHex(string? value)
        {
            if (value == null) return null;
            string trimmed = value.Trim();
            if (!_Hex.IsMatch(trimmed)) return null;
            if (trimmed.Length == 4) trimmed = "#" + trimmed[1] + trimmed[1] + trimmed[2] + trimmed[2] + trimmed[3] + trimmed[3];
            return trimmed.ToUpperInvariant();
        }

        /// <summary>
        /// Normalize a property document color (property.json accepts 3, 6, or 8 digits): uppercase #RRGGBB, with the
        /// alpha of an 8-digit value dropped (palettes are opaque).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Normalized color, or empty when the value is missing or invalid.</returns>
        public static string NormalizeDocumentColor(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            string trimmed = value.Trim();
            if (!_DocumentHex.IsMatch(trimmed)) return String.Empty;
            if (trimmed.Length == 9) trimmed = trimmed.Substring(0, 7);
            return NormalizeHex(trimmed) ?? String.Empty;
        }

        /// <summary>
        /// WCAG 2.1 relative luminance of a #RRGGBB color.
        /// </summary>
        /// <param name="hex">Color.</param>
        /// <returns>Luminance from 0 to 1.</returns>
        /// <exception cref="ArgumentException">The color is not #RGB or #RRGGBB.</exception>
        public static double Luminance(string hex)
        {
            string normalized = NormalizeHex(hex) ?? throw new ArgumentException("Not a hex color: " + hex, nameof(hex));
            double r = Channel(normalized.Substring(1, 2));
            double g = Channel(normalized.Substring(3, 2));
            double b = Channel(normalized.Substring(5, 2));
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        /// <summary>
        /// WCAG 2.1 contrast ratio of two colors, from 1 to 21 (unrounded).
        /// </summary>
        /// <param name="foreground">Foreground color.</param>
        /// <param name="background">Background color.</param>
        /// <returns>Ratio.</returns>
        /// <exception cref="ArgumentException">A color is not #RGB or #RRGGBB.</exception>
        public static double Ratio(string foreground, string background)
        {
            double a = Luminance(foreground);
            double b = Luminance(background);
            double lighter = Math.Max(a, b);
            double darker = Math.Min(a, b);
            return (lighter + 0.05) / (darker + 0.05);
        }

        /// <summary>
        /// A ratio rounded to two decimals for display, except that a ratio below 4.5 or 3 is never rounded up to it
        /// (4.4999 shows as 4.49), so a value shown as passing always passes.
        /// </summary>
        /// <param name="ratio">Ratio.</param>
        /// <returns>Rounded ratio.</returns>
        public static double Display(double ratio)
        {
            double rounded = Math.Round(ratio, 2, MidpointRounding.AwayFromZero);
            if ((ratio < 4.5 && rounded >= 4.5) || (ratio < 3.0 && rounded >= 3.0)) return Math.Floor(ratio * 100.0) / 100.0;
            return rounded;
        }

        /// <summary>
        /// Format a ratio for messages, for example 4.29:1.
        /// </summary>
        /// <param name="ratio">Ratio.</param>
        /// <returns>Text.</returns>
        public static string Format(double ratio)
        {
            return Display(ratio).ToString("0.00", CultureInfo.InvariantCulture) + ":1";
        }

        /// <summary>
        /// The page background color of a scheme.
        /// </summary>
        /// <param name="scheme">light or dark.</param>
        /// <returns>Color.</returns>
        public static string BackgroundOf(string scheme)
        {
            return scheme == "dark" ? DarkBackground : LightBackground;
        }

        /// <summary>
        /// Find a pair by identifier.
        /// </summary>
        /// <param name="id">Identifier, for example onPrimary/primary.</param>
        /// <returns>Pair, or null.</returns>
        public static BrandContrastPair? FindPair(string? id)
        {
            return id == null ? null : _Pairs.FirstOrDefault(p => String.Equals(p.Id, id, StringComparison.Ordinal));
        }

        /// <summary>
        /// Check every pair of both schemes. Pairs with a missing or invalid color are reported failing with ratio 0.
        /// </summary>
        /// <param name="light">Light set.</param>
        /// <param name="dark">Dark set.</param>
        /// <param name="overrides">Saved overrides (marks failing pairs as overridden), or null.</param>
        /// <returns>One check per scheme and pair, light first.</returns>
        /// <exception cref="ArgumentNullException">A set is null.</exception>
        public static List<BrandContrastCheck> Check(BrandColorSet light, BrandColorSet dark, IEnumerable<BrandContrastOverride>? overrides)
        {
            ArgumentNullException.ThrowIfNull(light);
            ArgumentNullException.ThrowIfNull(dark);
            List<BrandContrastOverride> saved = overrides?.ToList() ?? new List<BrandContrastOverride>();
            List<BrandContrastCheck> checks = new List<BrandContrastCheck>();
            foreach (string scheme in _Schemes)
            {
                BrandColorSet set = scheme == "dark" ? dark : light;
                foreach (BrandContrastPair pair in _Pairs)
                {
                    string fg = set.Get(pair.Foreground);
                    string bg = pair.Background == BackgroundKey ? BackgroundOf(scheme) : set.Get(pair.Background);
                    bool valid = NormalizeHex(fg) != null && NormalizeHex(bg) != null;
                    double ratio = valid ? Ratio(fg, bg) : 0;
                    bool passes = valid && ratio >= pair.Required;
                    checks.Add(new BrandContrastCheck
                    {
                        Scheme = scheme,
                        Pair = pair.Id,
                        Foreground = pair.Foreground,
                        Background = pair.Background,
                        ForegroundColor = fg,
                        BackgroundColor = bg,
                        Ratio = Display(ratio),
                        Required = pair.Required,
                        Level = pair.Level,
                        Passes = passes,
                        Overridden = !passes && saved.Any(o => o.Scheme == scheme && o.Pair == pair.Id),
                        Usage = pair.Usage
                    });
                }
            }
            return checks;
        }

        /// <summary>
        /// A sentence describing a failing check, naming the pair and the ratio.
        /// </summary>
        /// <param name="check">Check.</param>
        /// <returns>Message.</returns>
        /// <exception cref="ArgumentNullException">The check is null.</exception>
        public static string Describe(BrandContrastCheck check)
        {
            ArgumentNullException.ThrowIfNull(check);
            string level = check.Level == BrandContrastLevelEnum.Text ? "normal text" : check.Level == BrandContrastLevelEnum.LargeText ? "large text" : "user interface components";
            return check.Scheme + " " + check.Foreground + " " + check.ForegroundColor + " on " + check.Background + " " + check.BackgroundColor + " is " +
                check.Ratio.ToString("0.00", CultureInfo.InvariantCulture) + ":1; WCAG 2.1 AA needs " + check.Required.ToString("0.0", CultureInfo.InvariantCulture) + ":1 for " + level + ".";
        }

        #endregion

        #region Private-Methods

        private static double Channel(string hex)
        {
            double c = Int32.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        #endregion
    }
}
