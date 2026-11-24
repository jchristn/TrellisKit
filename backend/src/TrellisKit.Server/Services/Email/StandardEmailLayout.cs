namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using HtmlPageBuilder;

    /// <summary>
    /// The engine's email layout: a complete HTML document plus a matching plain-text version. The page and card are
    /// role="presentation" tables (Outlook-safe) at most 600 px wide and fluid down to 360 px, with every style inline;
    /// the document declares its language and light and dark color schemes. The header shows the property's logo (with
    /// alt text and a fixed size, swapped for a dark variant in dark mail clients when the property has one), then the
    /// heading, the body blocks, and a footer with the property name, an optional line of property copy, the support
    /// address, the postal address when one is configured, privacy, terms, and email preference links, and the
    /// ownership line. A hidden preheader sets the inbox preview, padded so body text does not leak into it. Brand
    /// colors come from <see cref="EmailBrand"/> through <see cref="EmailStyle"/>, which keeps every text pair at WCAG AA.
    /// Built with HtmlPageBuilder; every value is encoded.
    /// Thread safety: stateless; safe to share.
    /// </summary>
    public class StandardEmailLayout : EmailLayout
    {
        #region Public-Members

        /// <summary>
        /// Shared instance.
        /// </summary>
        public static readonly StandardEmailLayout Instance = new StandardEmailLayout();

        #endregion

        #region Private-Members

        private const string _PreheaderStyle = "display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EmailRendered Render(EmailDocument document, EmailBrand brand)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(brand);
            EmailStyle style = new EmailStyle(brand);
            return new EmailRendered
            {
                Subject = OneLine(document.Subject),
                Preheader = OneLine(document.Preheader),
                Html = Html(document, brand, style),
                Text = Text(document, brand)
            };
        }

        #endregion

        #region Private-Methods

        private static string OneLine(string? text)
        {
            return (text ?? String.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        }

        private static string Css(EmailStyle style, bool darkLogo)
        {
            return "body{margin:0;padding:0;background:" + style.Canvas + ";-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%}" +
                "table{border-collapse:collapse}img{border:0;outline:none;text-decoration:none}" +
                ".card{background:" + style.Card + "}a{color:" + style.Link + "}" +
                ".preheader{display:none!important;visibility:hidden;opacity:0;height:0;width:0;overflow:hidden;mso-hide:all}" +
                "@media (prefers-color-scheme: dark){body,.outer{background:" + style.DarkCanvas + "!important}.card{background:" + style.DarkCard + "!important}" +
                "h1,h2,h3,p,li,td,.text{color:" + EmailStyle.DarkTextColor + "!important}.muted{color:" + style.DarkMuted + "!important}" +
                "a,.link{color:" + style.DarkLink + "!important}td.btn{background:" + style.DarkPrimary + "!important}" +
                "a.btn{background:" + style.DarkPrimary + "!important;color:" + style.DarkOnPrimary + "!important}" +
                ".code,.callout{background:" + style.DarkCanvas + "!important;color:" + EmailStyle.DarkTextColor + "!important;border-color:#30363d!important}.fields td{border-color:#30363d!important}" +
                "hr{border-color:#30363d!important}" +
                (darkLogo ? ".logo-light{display:none!important}.logo-dark{display:inline-block!important;max-height:none!important;width:auto!important;overflow:visible!important}" : String.Empty) + "}" +
                "@media only screen and (max-width:480px){.card-cell{padding:20px!important}h1{font-size:20px!important}}";
        }

        private static string Logo(EmailBrand brand, EmailStyle style)
        {
            if (String.IsNullOrWhiteSpace(brand.LogoUrl) || !EmailStyle.IsHttpUrl(brand.LogoUrl)) return String.Empty;
            ImageSize? fitted = brand.LogoSize?.FitWithin(200, 48);
            int width = fitted?.Width ?? Math.Clamp(brand.LogoWidth, 1, 240);
            int height = fitted?.Height ?? Math.Clamp(brand.LogoHeight, 1, 96);
            bool known = fitted != null || (brand.LogoWidth > 0 && brand.LogoHeight > 0);
            string size = known ? "width=\"" + width + "\" height=\"" + height + "\"" : String.Empty;
            string dimensions = known ? "width:" + width + "px;height:" + height + "px" : "height:48px;width:auto;max-width:200px";
            string alt = EmailStyle.Encode(brand.Name);
            string light = "<img class=\"logo-light\" src=\"" + EmailStyle.Encode(brand.LogoUrl) + "\" alt=\"" + alt + "\" " + size +
                " style=\"display:inline-block;" + dimensions + ";border:0;font-family:" + EmailStyle.FontFamily + ";font-size:18px;font-weight:600;color:" + style.Heading + "\">";
            bool dark = !String.IsNullOrWhiteSpace(brand.DarkLogoUrl) && EmailStyle.IsHttpUrl(brand.DarkLogoUrl);
            string darkImage = dark
                ? "<!--[if !mso]><!--><img class=\"logo-dark\" src=\"" + EmailStyle.Encode(brand.DarkLogoUrl) + "\" alt=\"" + alt + "\" " + size +
                    " style=\"display:none;width:0;max-height:0;overflow:hidden;mso-hide:all;border:0\"><!--<![endif]-->"
                : String.Empty;
            string images = light + darkImage;
            if (EmailStyle.IsHttpUrl(brand.WebUrl)) images = "<a href=\"" + EmailStyle.Encode(brand.WebUrl) + "\" style=\"text-decoration:none\">" + images + "</a>";
            return "<p style=\"margin:0 0 24px\">" + images + "</p>";
        }

        private static string Html(EmailDocument document, EmailBrand brand, EmailStyle style)
        {
            string muted = style.Muted;
            bool darkLogo = !String.IsNullOrWhiteSpace(brand.DarkLogoUrl) && EmailStyle.IsHttpUrl(brand.DarkLogoUrl);
            using (Html builder = new Html())
            {
                HtmlBody b = builder.Body();
                StringBuilder inner = new StringBuilder();
                inner.Append(Logo(brand, style));
                string heading = OneLine(document.Heading);
                if (heading.Length > 0)
                    inner.Append(b.H1Text(heading, style: "font-family:" + EmailStyle.FontFamily + ";font-size:22px;line-height:1.3;font-weight:700;color:" + style.Heading + ";margin:0 0 16px"));
                foreach (EmailBlock block in document.Blocks.Where(x => x != null)) inner.Append(block.RenderHtml(style));

                StringBuilder footer = new StringBuilder();
                footer.Append(b.ParagraphText(brand.Name, classId: "muted", style: muted + ";font-weight:600"));
                if (!String.IsNullOrWhiteSpace(brand.FooterNote))
                    footer.Append(b.ParagraphText(OneLine(brand.FooterNote), classId: "muted", style: muted));
                if (!String.IsNullOrWhiteSpace(brand.SupportEmail))
                    footer.Append(b.ParagraphHtml(builder.Text("Questions? ").ToString() + b.LinkText(brand.SupportEmail.Trim(), "mailto:" + brand.SupportEmail.Trim(), false, classId: "link", style: "color:" + style.Link), classId: "muted", style: muted));
                if (!String.IsNullOrWhiteSpace(brand.PostalAddress))
                    footer.Append(b.ParagraphText(OneLine(brand.PostalAddress), classId: "muted", style: muted));
                List<string> links = new List<string>();
                if (brand.PrivacyUrl != null && EmailStyle.IsHttpUrl(brand.PrivacyUrl)) links.Add(b.LinkText("Privacy policy", brand.PrivacyUrl, false, classId: "link", style: "color:" + style.Link));
                if (brand.TermsUrl != null && EmailStyle.IsHttpUrl(brand.TermsUrl)) links.Add(b.LinkText("Terms of service", brand.TermsUrl, false, classId: "link", style: "color:" + style.Link));
                if (brand.PreferencesUrl != null && EmailStyle.IsHttpUrl(brand.PreferencesUrl)) links.Add(b.LinkText("Email preferences", brand.PreferencesUrl, false, classId: "link", style: "color:" + style.Link));
                if (links.Count > 0) footer.Append(b.ParagraphHtml(String.Join(builder.Text(" | ").ToString(), links), classId: "muted", style: muted));
                if (!String.IsNullOrWhiteSpace(brand.Copyright))
                    footer.Append(b.ParagraphText(OneLine(brand.Copyright), classId: "muted", style: muted + ";margin:12px 0 0"));

                string bar = style.AccentBar == null ? String.Empty : ";border-top:4px solid " + style.AccentBar;
                string card =
                    "<table role=\"presentation\" class=\"card\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"" + style.Card + "\"" +
                    " style=\"width:100%;max-width:600px;margin:0 auto;background:" + style.Card + ";border-radius:8px;border-collapse:separate" + bar + "\">" +
                    "<tr><td class=\"card-cell\" style=\"padding:32px;text-align:left;font-family:" + EmailStyle.FontFamily + "\">" +
                    inner.ToString() +
                    "<hr style=\"border:0;border-top:1px solid " + EmailStyle.BorderColor + ";margin:24px 0\">" +
                    footer.ToString() +
                    "</td></tr></table>";
                string outer =
                    "<table role=\"presentation\" class=\"outer\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"" + style.Canvas + "\" style=\"width:100%;background:" + style.Canvas + "\">" +
                    "<tr><td align=\"center\" style=\"padding:24px 12px\">" + card + "</td></tr></table>";

                string preheader = OneLine(document.Preheader);
                StringBuilder hidden = new StringBuilder();
                if (preheader.Length > 0)
                {
                    hidden.Append("<div class=\"preheader\" style=\"").Append(_PreheaderStyle).Append("\">").Append(EmailStyle.Encode(preheader)).Append("</div>");
                    hidden.Append("<div class=\"preheader\" aria-hidden=\"true\" style=\"").Append(_PreheaderStyle).Append("\">");
                    for (int i = 0; i < 40; i++) hidden.Append("&#847;&zwnj;&nbsp;");
                    hidden.Append("</div>");
                }
                return builder.Page()
                    .WithLanguage(String.IsNullOrWhiteSpace(brand.Language) ? "en" : brand.Language.Trim())
                    .WithTitle(OneLine(document.Subject))
                    .WithHead(h => h
                        .AddRawElement("<meta charset=\"utf-8\">")
                        .AddRawElement("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
                        .AddRawElement("<meta name=\"x-apple-disable-message-reformatting\">")
                        .AddRawElement("<meta name=\"format-detection\" content=\"telephone=no, date=no, address=no, email=no, url=no\">")
                        .AddRawElement("<meta name=\"color-scheme\" content=\"light dark\">")
                        .AddRawElement("<meta name=\"supported-color-schemes\" content=\"light dark\">")
                        .WithStyle(Css(style, darkLogo)))
                    .WithBody(body => body
                        .AddRaw(hidden.ToString())
                        .AddRaw(outer))
                    .ToString();
            }
        }

        private static string Text(EmailDocument document, EmailBrand brand)
        {
            StringBuilder text = new StringBuilder();
            string heading = OneLine(document.Heading);
            if (heading.Length > 0) text.Append(heading).Append("\n\n");
            foreach (EmailBlock block in document.Blocks.Where(b => b != null))
            {
                string part = block.RenderText();
                if (part.Trim().Length > 0) text.Append(part).Append("\n\n");
            }
            text.Append("-- \n").Append(brand.Name).Append('\n');
            if (!String.IsNullOrWhiteSpace(brand.FooterNote)) text.Append(OneLine(brand.FooterNote)).Append('\n');
            if (!String.IsNullOrWhiteSpace(brand.SupportEmail)) text.Append(brand.SupportEmail.Trim()).Append('\n');
            if (!String.IsNullOrWhiteSpace(brand.PostalAddress)) text.Append(OneLine(brand.PostalAddress)).Append('\n');
            if (brand.PrivacyUrl != null && EmailStyle.IsHttpUrl(brand.PrivacyUrl)) text.Append("Privacy policy: ").Append(brand.PrivacyUrl).Append('\n');
            if (brand.TermsUrl != null && EmailStyle.IsHttpUrl(brand.TermsUrl)) text.Append("Terms of service: ").Append(brand.TermsUrl).Append('\n');
            if (brand.PreferencesUrl != null) text.Append("Email preferences: ").Append(brand.PreferencesUrl).Append('\n');
            if (!String.IsNullOrWhiteSpace(brand.Copyright)) text.Append(OneLine(brand.Copyright)).Append('\n');
            return text.ToString();
        }

        #endregion
    }
}
