namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// A property's email layout choices, read from the optional email/layout.json in the property directory
    /// (docs/EMAIL.md): the header logo for light and dark mail clients and its displayed size, the page and card
    /// colors, a decorative accent bar, one line of footer copy, the ownership line, and a default postal address. Every
    /// field is optional; an absent file keeps the engine defaults (the brand logo, neutral surfaces, and an ownership
    /// line built from property.json owner). Invalid fields are reported in <see cref="Problems"/> and ignored.
    /// Thread safety: immutable after loading.
    /// </summary>
    public class EmailLayoutOptions
    {
        #region Public-Members

        /// <summary>
        /// Relative path of the file inside the property directory.
        /// </summary>
        public const string RelativePath = "email/layout.json";

        /// <summary>
        /// Header logo for light backgrounds (a file directly under brand/, for example brand/email-logo.png), or null for
        /// property.json brand.logo.
        /// </summary>
        [JsonPropertyName("logo")]
        public string? Logo { get; set; } = null;

        /// <summary>
        /// Header logo for dark mail clients (a file directly under brand/), or null to keep the light logo.
        /// </summary>
        [JsonPropertyName("logoDark")]
        public string? LogoDark { get; set; } = null;

        /// <summary>
        /// Displayed logo width in CSS pixels, or null to derive it from the image (at most 48 px tall). Minimum 16, maximum 240.
        /// </summary>
        [JsonPropertyName("logoWidth")]
        public int? LogoWidth { get; set; } = null;

        /// <summary>
        /// Displayed logo height in CSS pixels, or null to derive it from the image. Minimum 16, maximum 96.
        /// </summary>
        [JsonPropertyName("logoHeight")]
        public int? LogoHeight { get; set; } = null;

        /// <summary>
        /// Page background behind the card (#RRGGBB), or null for the engine default.
        /// </summary>
        [JsonPropertyName("canvas")]
        public string? Canvas { get; set; } = null;

        /// <summary>
        /// Card background (#RRGGBB), or null for white.
        /// </summary>
        [JsonPropertyName("card")]
        public string? Card { get; set; } = null;

        /// <summary>
        /// Heading color (#RRGGBB), or null for brand.colors.light.accent.
        /// </summary>
        [JsonPropertyName("heading")]
        public string? Heading { get; set; } = null;

        /// <summary>
        /// Page background in dark mail clients (#RRGGBB), or null for the engine default.
        /// </summary>
        [JsonPropertyName("canvasDark")]
        public string? CanvasDark { get; set; } = null;

        /// <summary>
        /// Card background in dark mail clients (#RRGGBB), or null for the engine default.
        /// </summary>
        [JsonPropertyName("cardDark")]
        public string? CardDark { get; set; } = null;

        /// <summary>
        /// Decorative bar across the top of the card (#RRGGBB), or null for none.
        /// </summary>
        [JsonPropertyName("accentBar")]
        public string? AccentBar { get; set; } = null;

        /// <summary>
        /// One line of footer copy under the property name (at most 200 characters), or null for the tagline.
        /// </summary>
        [JsonPropertyName("footerNote")]
        public string? FooterNote { get; set; } = null;

        /// <summary>
        /// Ownership line (at most 200 characters; {year} is replaced with the current year), or null for
        /// "(c) {year} {owner.copyrightHolder or owner.name}. All rights reserved."
        /// </summary>
        [JsonPropertyName("copyright")]
        public string? Copyright { get; set; } = null;

        /// <summary>
        /// Postal address for the footer (at most 300 characters), or null. The per-property setting
        /// email.postalAddress takes precedence.
        /// </summary>
        [JsonPropertyName("postalAddress")]
        public string? PostalAddress { get; set; } = null;

        /// <summary>
        /// Problems found while loading (empty when the file is valid or absent). Never null.
        /// </summary>
        [JsonIgnore]
        public List<string> Problems { get; private set; } = new List<string>();

        #endregion

        #region Private-Members

        private static readonly Regex _Hex = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read email/layout.json from a property directory. A missing file or directory gives empty options; an
        /// unreadable file gives empty options with one problem; invalid fields are cleared and reported.
        /// </summary>
        /// <param name="propertyDirectory">Property directory, or null.</param>
        /// <returns>Options (never null).</returns>
        public static EmailLayoutOptions Load(string? propertyDirectory)
        {
            if (String.IsNullOrWhiteSpace(propertyDirectory)) return new EmailLayoutOptions();
            string path = Path.Combine(propertyDirectory, "email", "layout.json");
            if (!File.Exists(path)) return new EmailLayoutOptions();
            EmailLayoutOptions? options;
            try
            {
                options = JsonSerializer.Deserialize<EmailLayoutOptions>(File.ReadAllText(path), _Json);
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                EmailLayoutOptions failed = new EmailLayoutOptions();
                failed.Problems.Add(RelativePath + ": " + e.Message);
                return failed;
            }
            options ??= new EmailLayoutOptions();
            options.Problems = new List<string>();
            options.Validate(propertyDirectory);
            return options;
        }

        #endregion

        #region Private-Methods

        private void Validate(string directory)
        {
            Logo = BrandFile(Logo, "logo", directory);
            LogoDark = BrandFile(LogoDark, "logoDark", directory);
            LogoWidth = Range(LogoWidth, "logoWidth", 16, 240);
            LogoHeight = Range(LogoHeight, "logoHeight", 16, 96);
            Canvas = Hex(Canvas, "canvas");
            Card = Hex(Card, "card");
            Heading = Hex(Heading, "heading");
            CanvasDark = Hex(CanvasDark, "canvasDark");
            CardDark = Hex(CardDark, "cardDark");
            AccentBar = Hex(AccentBar, "accentBar");
            FooterNote = Line(FooterNote, "footerNote", 200);
            Copyright = Line(Copyright, "copyright", 200);
            PostalAddress = Line(PostalAddress, "postalAddress", 300);
        }

        private string? BrandFile(string? value, string field, string directory)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim().Replace('\\', '/');
            string prefix = PropertyLoader.BrandFolder + "/";
            string file = normalized.StartsWith(prefix, StringComparison.Ordinal) ? normalized.Substring(prefix.Length) : String.Empty;
            if (!PropertyLoader.IsSafeAssetName(file))
            {
                Problems.Add(RelativePath + ": " + field + " must be a file directly under " + prefix + " (found \"" + value + "\")");
                return null;
            }
            if (!File.Exists(Path.Combine(directory, PropertyLoader.BrandFolder, file)))
            {
                Problems.Add(RelativePath + ": " + field + " " + normalized + " does not exist");
                return null;
            }
            return prefix + file;
        }

        private int? Range(int? value, string field, int min, int max)
        {
            if (value == null) return null;
            if (value < min || value > max)
            {
                Problems.Add(RelativePath + ": " + field + " must be " + min + " to " + max);
                return null;
            }
            return value;
        }

        private string? Hex(string? value, string field)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!_Hex.IsMatch(value.Trim()))
            {
                Problems.Add(RelativePath + ": " + field + " must be a #RRGGBB color (found \"" + value + "\")");
                return null;
            }
            return value.Trim();
        }

        private string? Line(string? value, string field, int max)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string trimmed = value.Trim();
            if (trimmed.Length > max || trimmed.Contains('\n', StringComparison.Ordinal) || trimmed.Contains('\r', StringComparison.Ordinal))
            {
                Problems.Add(RelativePath + ": " + field + " must be one line of at most " + max + " characters");
                return null;
            }
            return trimmed;
        }

        #endregion
    }
}
