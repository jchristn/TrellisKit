namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Colors and base inline styles a layout hands to its blocks, plus the encoding and contrast helpers every block
    /// uses. Text colors are checked against their backgrounds for WCAG AA (4.5:1): a brand color that falls short is
    /// replaced with a readable one, so a property's palette can never make a message unreadable.
    /// Thread safety: immutable after construction.
    /// </summary>
    public class EmailStyle
    {
        #region Public-Members

        /// <summary>
        /// Font stack.
        /// </summary>
        public const string FontFamily = "-apple-system,'Segoe UI',Helvetica,Arial,sans-serif";

        /// <summary>
        /// Monospace font stack (code blocks).
        /// </summary>
        public const string MonospaceFamily = "SFMono-Regular,Consolas,'Liberation Mono',Menlo,monospace";

        /// <summary>
        /// Body text color (light scheme).
        /// </summary>
        public const string TextColor = "#1f2328";

        /// <summary>
        /// Secondary text color (light scheme).
        /// </summary>
        public const string MutedColor = "#59636e";

        /// <summary>
        /// Border and rule color (light scheme).
        /// </summary>
        public const string BorderColor = "#d0d7de";

        /// <summary>
        /// Subtle surface color for code blocks and callouts (light scheme).
        /// </summary>
        public const string SurfaceColor = "#f6f8fa";

        /// <summary>
        /// Body text color in dark mail clients.
        /// </summary>
        public const string DarkTextColor = "#e6edf3";

        /// <summary>
        /// Secondary text color in dark mail clients (before the contrast check).
        /// </summary>
        public const string DarkMutedColor = "#9aa4ae";

        /// <summary>
        /// Minimum contrast ratio of text against its background (WCAG 2.2 AA, normal text).
        /// </summary>
        public const double MinimumContrast = 4.5;

        /// <summary>
        /// Brand primary color (validated hex): button background and accents.
        /// </summary>
        public string Primary { get; }

        /// <summary>
        /// Text color on the primary color (validated hex; at least 4.5:1 where any candidate reaches it).
        /// </summary>
        public string OnPrimary { get; }

        /// <summary>
        /// Link color on the card: the primary color when it reaches 4.5:1, else the heading color.
        /// </summary>
        public string Link { get; }

        /// <summary>
        /// Heading color on the card (at least 4.5:1).
        /// </summary>
        public string Heading { get; }

        /// <summary>
        /// Page background behind the card.
        /// </summary>
        public string Canvas { get; }

        /// <summary>
        /// Card background (the engine's text and secondary text colors reach 4.5:1 on it).
        /// </summary>
        public string Card { get; }

        /// <summary>
        /// Button background in dark mail clients.
        /// </summary>
        public string DarkPrimary { get; }

        /// <summary>
        /// Button text in dark mail clients.
        /// </summary>
        public string DarkOnPrimary { get; }

        /// <summary>
        /// Link color in dark mail clients.
        /// </summary>
        public string DarkLink { get; }

        /// <summary>
        /// Page background in dark mail clients, also the surface of code blocks and callouts there (the dark text color
        /// reaches 4.5:1 on it).
        /// </summary>
        public string DarkCanvas { get; }

        /// <summary>
        /// Card background in dark mail clients (the dark text and secondary text colors reach 4.5:1 on it).
        /// </summary>
        public string DarkCard { get; }

        /// <summary>
        /// Secondary text color in dark mail clients (at least 4.5:1 on the dark card).
        /// </summary>
        public string DarkMuted { get; }

        /// <summary>
        /// Decorative bar across the top of the card, or null.
        /// </summary>
        public string? AccentBar { get; }

        /// <summary>
        /// Inline style of a body paragraph.
        /// </summary>
        public string Text
        {
            get { return "font-family:" + FontFamily + ";font-size:16px;line-height:1.5;color:" + TextColor + ";margin:0 0 16px"; }
        }

        /// <summary>
        /// Inline style of secondary text.
        /// </summary>
        public string Muted
        {
            get { return "font-family:" + FontFamily + ";font-size:13px;line-height:1.5;color:" + MutedColor + ";margin:0 0 8px"; }
        }

        #endregion

        #region Private-Members

        private const string _DefaultCanvas = "#f4f5f7";
        private const string _DefaultCard = "#ffffff";
        private const string _DefaultDarkCanvas = "#111315";
        private const string _DefaultDarkCard = "#1d2125";
        private static readonly Regex _Hex = new Regex("^#[0-9A-Fa-f]{3,8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate from a brand.
        /// </summary>
        /// <param name="brand">Brand.</param>
        /// <exception cref="ArgumentNullException">brand is null.</exception>
        public EmailStyle(EmailBrand brand)
        {
            ArgumentNullException.ThrowIfNull(brand);
            Primary = Color(brand.PrimaryColor, "#111111");
            OnPrimary = Readable(Color(brand.OnPrimaryColor, "#FFFFFF"), Primary, "#FFFFFF", "#111111");
            string card = Color(brand.CardColor, _DefaultCard);
            Card = ContrastRatio(MutedColor, card) >= MinimumContrast && ContrastRatio(TextColor, card) >= MinimumContrast ? card : _DefaultCard;
            Canvas = Color(brand.CanvasColor, _DefaultCanvas);
            Heading = Readable(Color(brand.HeadingColor, TextColor), Card, TextColor);
            Link = Readable(Primary, Card, Heading);

            string darkCard = Color(brand.DarkCardColor, _DefaultDarkCard);
            DarkCard = ContrastRatio(DarkMutedColor, darkCard) >= MinimumContrast && ContrastRatio(DarkTextColor, darkCard) >= MinimumContrast ? darkCard : _DefaultDarkCard;
            string darkCanvas = Color(brand.DarkCanvasColor, _DefaultDarkCanvas);
            DarkCanvas = ContrastRatio(DarkTextColor, darkCanvas) >= MinimumContrast ? darkCanvas : _DefaultDarkCanvas;
            DarkMuted = Readable(DarkMutedColor, DarkCard, DarkTextColor);
            DarkPrimary = Color(brand.DarkPrimaryColor, Primary);
            DarkOnPrimary = Readable(Color(brand.DarkOnPrimaryColor, OnPrimary), DarkPrimary, "#FFFFFF", "#111111");
            DarkLink = Readable(DarkPrimary, DarkCard, DarkTextColor);
            AccentBar = brand.AccentBarColor != null && _Hex.IsMatch(brand.AccentBarColor) ? brand.AccentBarColor : null;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// HTML-encode text (null renders empty).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Encoded text.</returns>
        public static string Encode(string? text)
        {
            return WebUtility.HtmlEncode(text ?? String.Empty);
        }

        /// <summary>
        /// HTML-encode multi-line text, turning line breaks into br elements.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Encoded HTML.</returns>
        public static string EncodeLines(string? text)
        {
            IEnumerable<string> lines = (text ?? String.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(Encode);
            return String.Join("<br>", lines);
        }

        /// <summary>
        /// Whether a value is an absolute http or https URL.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True for http(s).</returns>
        public static bool IsHttpUrl(string? value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        /// <summary>
        /// A hex color, or the fallback when the value is not one.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="fallback">Fallback.</param>
        /// <returns>Color.</returns>
        public static string Color(string? value, string fallback)
        {
            return value != null && _Hex.IsMatch(value) ? value : fallback;
        }

        /// <summary>
        /// WCAG 2.2 contrast ratio of two colors (#RGB, #RRGGBB, or #RRGGBBAA; alpha is ignored), from 1 to 21.
        /// </summary>
        /// <param name="first">First color.</param>
        /// <param name="second">Second color.</param>
        /// <returns>Ratio, or 1 when either value is not a color.</returns>
        public static double ContrastRatio(string? first, string? second)
        {
            double a = Luminance(first);
            double b = Luminance(second);
            if (a < 0 || b < 0) return 1;
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        /// <summary>
        /// The preferred text color when it reaches 4.5:1 on the background, else the first candidate that does (then
        /// white and near-black), else whichever of those has the highest contrast.
        /// </summary>
        /// <param name="preferred">Preferred color.</param>
        /// <param name="background">Background.</param>
        /// <param name="candidates">Fallbacks, in order.</param>
        /// <returns>Color.</returns>
        public static string Readable(string preferred, string background, params string[] candidates)
        {
            if (ContrastRatio(preferred, background) >= MinimumContrast) return preferred;
            List<string> all = (candidates ?? Array.Empty<string>()).Where(c => c != null && _Hex.IsMatch(c)).Concat(new[] { "#FFFFFF", "#111111" }).ToList();
            string? good = all.FirstOrDefault(c => ContrastRatio(c, background) >= MinimumContrast);
            return good ?? all.OrderByDescending(c => ContrastRatio(c, background)).First();
        }

        #endregion

        #region Private-Methods

        private static double Luminance(string? color)
        {
            if (color == null || !_Hex.IsMatch(color)) return -1;
            string hex = color.Substring(1);
            if (hex.Length == 3 || hex.Length == 4) hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            if (hex.Length != 6 && hex.Length != 8) return -1;
            return 0.2126 * Channel(hex, 0) + 0.7152 * Channel(hex, 2) + 0.0722 * Channel(hex, 4);
        }

        private static double Channel(string hex, int offset)
        {
            double c = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        #endregion
    }
}
