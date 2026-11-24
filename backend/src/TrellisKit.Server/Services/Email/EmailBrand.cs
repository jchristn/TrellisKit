namespace TrellisKit.Server.Services.Email
{
    using System;

    /// <summary>
    /// Brand values of the email layout, all from the property configuration (property.json brand, contact, owner, and
    /// documents; the optional email/layout.json; and per-property settings) or the platform for platform mail. Every
    /// color is #RRGGBB; the layout checks text pairs for WCAG AA contrast and substitutes a readable color when a pair
    /// falls short.
    /// </summary>
    public class EmailBrand
    {
        #region Public-Members

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// HTML language of the message (BCP 47, for example en or en-US). Default en.
        /// </summary>
        public string Language { get; set; } = "en";

        /// <summary>
        /// Absolute logo URL for light backgrounds, or null (no logo; the name is the header).
        /// </summary>
        public string? LogoUrl { get; set; } = null;

        /// <summary>
        /// Original pixel size for callers that supply a logo directly. When present, the layout fits it inside
        /// 200 by 48 pixels without enlarging it. Property layouts instead supply LogoWidth and LogoHeight.
        /// </summary>
        public ImageSize? LogoSize { get; set; } = null;

        /// <summary>
        /// Absolute logo URL for dark mail clients, or null to keep <see cref="LogoUrl"/> in dark mode.
        /// </summary>
        public string? DarkLogoUrl { get; set; } = null;

        /// <summary>
        /// Displayed logo width in CSS pixels. Default 0 (unknown; preserve the image ratio). Positive values are capped at 240.
        /// </summary>
        public int LogoWidth { get; set; } = 0;

        /// <summary>
        /// Displayed logo height in CSS pixels. Default 0 (unknown; preserve the image ratio). Positive values are capped at 96.
        /// </summary>
        public int LogoHeight { get; set; } = 0;

        /// <summary>
        /// Primary color (#RRGGBB): buttons and links on light backgrounds.
        /// </summary>
        public string PrimaryColor { get; set; } = "#111111";

        /// <summary>
        /// Text color on the primary color.
        /// </summary>
        public string OnPrimaryColor { get; set; } = "#FFFFFF";

        /// <summary>
        /// Primary color in dark mail clients, or null to derive a readable one.
        /// </summary>
        public string? DarkPrimaryColor { get; set; } = null;

        /// <summary>
        /// Text color on the dark-mode primary color, or null to derive a readable one.
        /// </summary>
        public string? DarkOnPrimaryColor { get; set; } = null;

        /// <summary>
        /// Heading color on the card (light), or null for the engine's text color.
        /// </summary>
        public string? HeadingColor { get; set; } = null;

        /// <summary>
        /// Page background behind the card (light), or null for the engine default.
        /// </summary>
        public string? CanvasColor { get; set; } = null;

        /// <summary>
        /// Card background (light), or null for white.
        /// </summary>
        public string? CardColor { get; set; } = null;

        /// <summary>
        /// Page background in dark mail clients, or null for the engine default.
        /// </summary>
        public string? DarkCanvasColor { get; set; } = null;

        /// <summary>
        /// Card background in dark mail clients, or null for the engine default.
        /// </summary>
        public string? DarkCardColor { get; set; } = null;

        /// <summary>
        /// Decorative bar across the top of the card, or null for none (not text, so no contrast requirement).
        /// </summary>
        public string? AccentBarColor { get; set; } = null;

        /// <summary>
        /// Support email, or empty.
        /// </summary>
        public string SupportEmail { get; set; } = String.Empty;

        /// <summary>
        /// Website base URL.
        /// </summary>
        public string WebUrl { get; set; } = String.Empty;

        /// <summary>
        /// Privacy policy URL, or null.
        /// </summary>
        public string? PrivacyUrl { get; set; } = null;

        /// <summary>
        /// Terms of service URL, or null.
        /// </summary>
        public string? TermsUrl { get; set; } = null;

        /// <summary>
        /// Email preferences URL, or null.
        /// </summary>
        public string? PreferencesUrl { get; set; } = null;

        /// <summary>
        /// Postal address for the footer, or empty (no address line is shown).
        /// </summary>
        public string PostalAddress { get; set; } = String.Empty;

        /// <summary>
        /// One line of property copy under the name in the footer (for example the tagline), or empty.
        /// </summary>
        public string FooterNote { get; set; } = String.Empty;

        /// <summary>
        /// Ownership line at the bottom of the footer, for example "(c) 2026 Example Owner. All rights reserved.", or empty.
        /// </summary>
        public string Copyright { get; set; } = String.Empty;

        #endregion
    }
}
