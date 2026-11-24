namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// One version of a property's brand palette (tables propertybrandpalettes, propertybrandcolors, and
    /// propertybrandoverrides; docs/TRELLISKIT.md section 2.1): both color sets, the contrast overrides it was saved
    /// with, and bookkeeping. The current palette is the highest version. Instances served to requests are immutable
    /// snapshots; edits build a new version.
    /// Thread safety: safe to read concurrently when not mutated.
    /// </summary>
    public class BrandPalette
    {
        #region Public-Members

        /// <summary>
        /// Version, 1, 2, 3, ... per property. Minimum 1.
        /// </summary>
        public int Version
        {
            get { return _Version; }
            set { _Version = Math.Max(1, value); }
        }

        /// <summary>
        /// How this version was created.
        /// </summary>
        public BrandPaletteSourceEnum Source { get; set; } = BrandPaletteSourceEnum.Seed;

        /// <summary>
        /// Administrator note, or null. At most 200 characters.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Hash of the property.json brand.colors section the palette was last seeded or synchronized from (carried
        /// forward by edits). SingleProperty compares it with the document to decide whether the folder changed.
        /// </summary>
        public string? SeedHash { get; set; } = null;

        /// <summary>
        /// Light scheme colors.
        /// </summary>
        public BrandColorSet Light
        {
            get { return _Light; }
            set { _Light = value ?? new BrandColorSet(); }
        }

        /// <summary>
        /// Dark scheme colors.
        /// </summary>
        public BrandColorSet Dark
        {
            get { return _Dark; }
            set { _Dark = value ?? new BrandColorSet(); }
        }

        /// <summary>
        /// Contrast overrides saved with this version (failing pairs only). Fixed-bound: at most one per scheme and pair.
        /// </summary>
        public List<BrandContrastOverride> Overrides
        {
            get { return _Overrides; }
            set { _Overrides = value ?? new List<BrandContrastOverride>(); }
        }

        /// <summary>
        /// Member or platform administrator who created the version, or null for seeding and the break-glass token.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// When the version was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private int _Version = 1;
        private BrandColorSet _Light = new BrandColorSet();
        private BrandColorSet _Dark = new BrandColorSet();
        private List<BrandContrastOverride> _Overrides = new List<BrandContrastOverride>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The palette a property document seeds (version 1, source Seed, SeedHash set).
        /// </summary>
        /// <param name="colors">Document brand.colors, or null.</param>
        /// <returns>Palette.</returns>
        public static BrandPalette FromDocument(PropertyBrandColors? colors)
        {
            return new BrandPalette
            {
                Version = 1,
                Source = BrandPaletteSourceEnum.Seed,
                SeedHash = DocumentHash(colors),
                Light = BrandColorSet.FromScheme(colors?.Light),
                Dark = BrandColorSet.FromScheme(colors?.Dark)
            };
        }

        /// <summary>
        /// SHA-256 of the canonical (normalized) document brand.colors section.
        /// </summary>
        /// <param name="colors">Document brand.colors, or null.</param>
        /// <returns>Lowercase hex digest.</returns>
        public static string DocumentHash(PropertyBrandColors? colors)
        {
            BrandColorSet light = BrandColorSet.FromScheme(colors?.Light);
            BrandColorSet dark = BrandColorSet.FromScheme(colors?.Dark);
            string canonical = "light:" + String.Join(",", BrandColorSet.Keys.Select(k => k + "=" + light.Get(k))) + "|dark:" + String.Join(",", BrandColorSet.Keys.Select(k => k + "=" + dark.Get(k)));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        /// <summary>
        /// The set of a scheme.
        /// </summary>
        /// <param name="scheme">light or dark.</param>
        /// <returns>Set.</returns>
        public BrandColorSet SetOf(string scheme)
        {
            return scheme == "dark" ? Dark : Light;
        }

        /// <summary>
        /// Whether both sets have the same colors as another palette.
        /// </summary>
        /// <param name="other">Other palette, or null.</param>
        /// <returns>True when equal.</returns>
        public bool SameColors(BrandPalette? other)
        {
            return other != null && Light.SameAs(other.Light) && Dark.SameAs(other.Dark);
        }

        /// <summary>
        /// The document form of both sets (GET /api/v1/property brand.colors).
        /// </summary>
        /// <returns>Colors.</returns>
        public PropertyBrandColors ToDocument()
        {
            return new PropertyBrandColors { Light = Light.ToScheme(), Dark = Dark.ToScheme() };
        }

        /// <summary>
        /// A deep copy.
        /// </summary>
        /// <returns>Copy.</returns>
        public BrandPalette Clone()
        {
            return new BrandPalette
            {
                Version = Version,
                Source = Source,
                Note = Note,
                SeedHash = SeedHash,
                Light = Light.Clone(),
                Dark = Dark.Clone(),
                Overrides = Overrides.Select(o => new BrandContrastOverride { Scheme = o.Scheme, Pair = o.Pair, Ratio = o.Ratio, Required = o.Required, Reason = o.Reason }).ToList(),
                CreatedByUserId = CreatedByUserId,
                CreatedUtc = CreatedUtc
            };
        }

        #endregion
    }
}
