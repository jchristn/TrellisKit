namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Builds a property's <see cref="EmailBrand"/> from its configuration: name, locale, brand colors (light and dark),
    /// the logo (email/layout.json logo, else brand.logo) with its displayed size, the support address, privacy and
    /// terms links from the property's documents, the footer note (else the tagline), the ownership line (else built
    /// from owner), and the postal address (the email.postalAddress setting, else layout.json). Engine code: every value
    /// comes from the property.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class EmailBrandFactory
    {
        #region Public-Members

        /// <summary>
        /// Tallest displayed logo when the size is derived from the image, in CSS pixels.
        /// </summary>
        public const int DefaultLogoHeight = 48;

        /// <summary>
        /// Widest displayed logo when the size is derived from the image, in CSS pixels.
        /// </summary>
        public const int MaxLogoWidth = 200;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the brand.
        /// </summary>
        /// <param name="property">Active property context.</param>
        /// <param name="layout">The property's email layout options (or empty options).</param>
        /// <param name="postalAddressSetting">The email.postalAddress setting, or null or empty.</param>
        /// <param name="nowUtc">Current time (the ownership line's year).</param>
        /// <returns>Brand.</returns>
        /// <exception cref="ArgumentNullException">property or layout is null.</exception>
        public static EmailBrand Create(PropertyContext property, EmailLayoutOptions layout, string? postalAddressSetting, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(property);
            ArgumentNullException.ThrowIfNull(layout);
            PropertyConfiguration p = property.Property;
            PropertyBrandColors colors = property.BrandColors;
            string apiOrigin = property.ApiOrigin;
            string logoPath = layout.Logo ?? p.Brand.Logo;
            string? logoFile = PropertyLoader.AssetFileName(logoPath);
            string? darkFile = PropertyLoader.AssetFileName(layout.LogoDark);
            int width = 0;
            int height = 0;
            if (layout.LogoWidth != null && layout.LogoHeight != null)
            {
                width = layout.LogoWidth.Value;
                height = layout.LogoHeight.Value;
            }
            else if (logoFile != null)
            {
                ImageSize? pixels = ImageSize.FromFile(PropertyLoader.ResolveAsset(p, logoFile));
                if (pixels != null)
                {
                    ImageSize fitted = pixels.FitWithin(MaxLogoWidth, DefaultLogoHeight);
                    width = fitted.Width;
                    height = fitted.Height;
                }
            }

            string year = nowUtc.Year.ToString(CultureInfo.InvariantCulture);
            string holder = String.IsNullOrWhiteSpace(p.Owner?.CopyrightHolder) ? (p.Owner?.Name ?? String.Empty).Trim() : p.Owner!.CopyrightHolder!.Trim();
            string copyright = layout.Copyright != null
                ? layout.Copyright.Replace("{year}", year, StringComparison.Ordinal)
                : holder.Length == 0 ? String.Empty : "(c) " + year + " " + holder.TrimEnd('.') + ". All rights reserved.";
            PropertyDocument? privacy = p.Documents?.FirstOrDefault(d => d.Kind == DocumentKindEnum.Privacy && !String.IsNullOrWhiteSpace(d.Slug));
            PropertyDocument? terms = p.Documents?.FirstOrDefault(d => d.Kind == DocumentKindEnum.Terms && !String.IsNullOrWhiteSpace(d.Slug));
            return new EmailBrand
            {
                Name = property.Name,
                Language = String.IsNullOrWhiteSpace(p.Locale) ? "en" : p.Locale.Trim(),
                LogoUrl = logoFile == null ? null : apiOrigin + Constants.ApiPrefix + "/property/assets/" + Uri.EscapeDataString(logoFile),
                DarkLogoUrl = darkFile == null ? null : apiOrigin + Constants.ApiPrefix + "/property/assets/" + Uri.EscapeDataString(darkFile),
                LogoWidth = width,
                LogoHeight = height,
                PrimaryColor = Pick(colors.Light?.Primary, "#111111"),
                OnPrimaryColor = Pick(colors.Light?.OnPrimary, "#FFFFFF"),
                DarkPrimaryColor = Null(colors.Dark?.Primary),
                DarkOnPrimaryColor = Null(colors.Dark?.OnPrimary),
                HeadingColor = layout.Heading ?? Null(colors.Light?.Accent),
                CanvasColor = layout.Canvas,
                CardColor = layout.Card,
                DarkCanvasColor = layout.CanvasDark,
                DarkCardColor = layout.CardDark,
                AccentBarColor = layout.AccentBar,
                SupportEmail = p.Contact?.SupportEmail ?? String.Empty,
                WebUrl = property.PublicBaseUrl,
                PrivacyUrl = property.Link("/" + (privacy?.Slug.Trim('/') ?? "privacy")),
                TermsUrl = terms == null ? null : property.Link("/" + terms.Slug.Trim('/')),
                PreferencesUrl = property.Link("/app/settings/notifications"),
                PostalAddress = !String.IsNullOrWhiteSpace(postalAddressSetting) ? postalAddressSetting.Trim() : layout.PostalAddress ?? String.Empty,
                FooterNote = layout.FooterNote ?? (p.Tagline ?? String.Empty).Trim(),
                Copyright = copyright
            };
        }

        /// <summary>
        /// Read a PNG file's pixel size from its header.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <param name="width">Width in pixels (0 when unknown).</param>
        /// <param name="height">Height in pixels (0 when unknown).</param>
        /// <returns>True when the file is a PNG with a positive size.</returns>
        public static bool TryPngSize(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
                byte[] header = new byte[24];
                using (FileStream stream = File.OpenRead(path))
                {
                    int read = 0;
                    while (read < header.Length)
                    {
                        int n = stream.Read(header, read, header.Length - read);
                        if (n == 0) return false;
                        read += n;
                    }
                }
                if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47) return false;
                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private static string Pick(string? value, string fallback)
        {
            return String.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string? Null(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
