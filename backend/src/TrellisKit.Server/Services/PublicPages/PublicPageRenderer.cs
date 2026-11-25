namespace TrellisKit.Server.Services.PublicPages
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using HtmlPageBuilder;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Renders public landing pages (docs/PHASE4_CONTRACT.md section 18.4) with HtmlPageBuilder: a complete, lightweight
    /// HTML document in the property's brand (logo, name, light and dark colors), with the title, meta description,
    /// canonical URL, Open Graph and Twitter card tags, JSON-LD, a header, the page's main content, a call to action into
    /// the member app, and a footer. Every value is HTML-encoded; JSON-LD is escaped so it cannot close its script element.
    /// A hash-authorized theme script, no external styles. Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class PublicPageRenderer
    {
        #region Private-Members

        private static readonly Regex _Color = new Regex("^#[0-9A-Fa-f]{3,8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        /// <summary>Static theme bootstrap, authorized by its exact CSP hash.</summary>
        public const string ThemeScript = "(function(){var root=document.documentElement,mode;try{mode=localStorage.getItem('tk.theme')}catch(e){}if(mode!=='light'&&mode!=='dark')mode=matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light';root.dataset.theme=mode;document.addEventListener('DOMContentLoaded',function(){var button=document.getElementById('theme-toggle');if(!button)return;function update(){var dark=root.dataset.theme==='dark';button.textContent=dark?button.dataset.lightLabel:button.dataset.darkLabel;button.setAttribute('aria-label',button.textContent)}update();button.addEventListener('click',function(){root.dataset.theme=root.dataset.theme==='dark'?'light':'dark';try{localStorage.setItem('tk.theme',root.dataset.theme)}catch(e){}update()})})})();";

        /// <summary>CSP source expression for the theme bootstrap.</summary>
        public static string ThemeScriptSource => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(ThemeScript))) + "'";

        #region Public-Methods

        /// <summary>
        /// Render a page.
        /// </summary>
        /// <param name="brand">Brand.</param>
        /// <param name="page">Page.</param>
        /// <returns>HTML document.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static string Render(PublicPageBrand brand, PublicPageDocument page)
        {
            ArgumentNullException.ThrowIfNull(brand);
            ArgumentNullException.ThrowIfNull(page);
            using (Html builder = new Html())
            {
                HtmlBody b = builder.Body();
                string home = brand.SiteUrl + "/";
                StringBuilder header = new StringBuilder();
                string mark = !String.IsNullOrWhiteSpace(brand.LogoUrl)
                    ? Img(brand.LogoUrl!, brand.Name, "logo", false) + b.SpanText(brand.Name, classId: "brand-name")
                    : b.SpanText(brand.Name, classId: "brand-name");
                header.Append(b.LinkHtml(mark, home, false, classId: "brand"));
                header.Append(b.NavHtml(b.LinkText(PublicPageText.SignIn, brand.SignInUrl, false, classId: "nav-link") + b.LinkText(PublicPageText.Join(brand.Name), brand.JoinUrl, false, classId: "button"),
                    classId: "nav"));
                header.Append("<button type=\"button\" id=\"theme-toggle\" class=\"button secondary\" data-light-label=\"" + Attr(PublicPageText.LightTheme) + "\" data-dark-label=\"" + Attr(PublicPageText.DarkTheme) + "\">" + Attr(PublicPageText.DarkTheme) + "</button>");

                StringBuilder cta = new StringBuilder();
                cta.Append(b.H2Text(PublicPageText.CtaHeading(brand.Name)));
                cta.Append(b.ParagraphText(PublicPageText.CtaBody(brand.Name, brand.MemberWord)));
                List<string> actions = new List<string>();
                if (!String.IsNullOrWhiteSpace(page.AppUrl)) actions.Add(b.LinkText(page.AppLabel, page.AppUrl!, false, classId: "button"));
                actions.Add(b.LinkText(PublicPageText.Join(brand.Name), brand.JoinUrl, false, classId: page.AppUrl == null ? "button" : "button secondary"));
                actions.Add(b.LinkText(PublicPageText.SignIn, brand.SignInUrl, false, classId: "text-link"));
                cta.Append(b.DivHtml(String.Join(String.Empty, actions), classId: "actions"));

                string footer = b.ParagraphHtml(b.LinkText(brand.Name, home, false) + (String.IsNullOrWhiteSpace(brand.Tagline) ? String.Empty : builder.Text(" | " + brand.Tagline).ToString()));

                string body = b.HeaderHtml(header.ToString(), classId: "site-header") +
                    b.MainHtml(page.MainHtml + b.SectionHtml(cta.ToString(), classId: "cta card"), id: "main") +
                    b.FooterHtml(footer, classId: "site-footer");

                return builder.Page()
                    .WithLanguage(Language(brand.Locale))
                    .WithTitle(page.Title)
                    .WithHead(h =>
                    {
                        h.AddRawElement("<meta charset=\"utf-8\">");
                        h.AddRawElement("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
                        h.AddRawElement("<meta name=\"color-scheme\" content=\"light dark\">");
                        h.WithMetaDescription(page.Description);
                        if (page.NoIndex) h.AddRawElement("<meta name=\"robots\" content=\"noindex, follow\">");
                        if (!String.IsNullOrWhiteSpace(page.CanonicalUrl)) h.AddRawElement("<link rel=\"canonical\" href=\"" + Attr(page.CanonicalUrl!) + "\">");
                        if (!String.IsNullOrWhiteSpace(brand.IconUrl)) h.AddRawElement("<link rel=\"icon\" href=\"" + Attr(brand.IconUrl!) + "\">");
                        h.AddRawElement(Meta("theme-color", Color(brand.Colors?.Light?.ThemeColor, "#111111"), false) + Meta("og:site_name", brand.Name, true) + Meta("og:type", page.OgType, true) +
                            Meta("og:title", page.Title, true) + Meta("og:description", page.Description, true) +
                            (String.IsNullOrWhiteSpace(page.CanonicalUrl) ? String.Empty : Meta("og:url", page.CanonicalUrl!, true)) +
                            (String.IsNullOrWhiteSpace(page.ImageUrl) ? String.Empty : Meta("og:image", page.ImageUrl, true) + Meta("og:image:alt", page.ImageAlt, true)) +
                            Meta("og:locale", (brand.Locale ?? "en-US").Replace('-', '_'), true) +
                            Meta("twitter:card", String.IsNullOrWhiteSpace(page.ImageUrl) ? "summary" : "summary_large_image", false) + Meta("twitter:title", page.Title, false) +
                            Meta("twitter:description", page.Description, false) +
                            (String.IsNullOrWhiteSpace(page.ImageUrl) ? String.Empty : Meta("twitter:image", page.ImageUrl, false) + Meta("twitter:image:alt", page.ImageAlt, false)));
                        h.AddRawElement("<script>" + ThemeScript + "</script>");
                        h.AddRawElement("<script type=\"module\" src=\"/assets/public-page-media.js\"></script>");
                        h.WithStyle(Css(brand.Colors ?? new PropertyBrandColors()));
                        if (!String.IsNullOrWhiteSpace(page.JsonLd)) h.AddRawElement("<script type=\"application/ld+json\">" + ScriptSafe(page.JsonLd!) + "</script>");
                    })
                    .WithBody(x => x.AddRaw(b.LinkText(PublicPageText.SkipToContent, "#main", false, classId: "skip")).AddRaw(body))
                    .ToString();
            }
        }

        /// <summary>
        /// HTML-encode text for an element or an attribute.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <returns>Encoded text.</returns>
        public static string Attr(string? value)
        {
            return WebUtility.HtmlEncode(value ?? String.Empty);
        }

        /// <summary>
        /// An img element with alternative text, lazy loading off the first screen, and async decoding.
        /// </summary>
        /// <param name="url">Absolute image URL.</param>
        /// <param name="alt">Alternative text (empty for a decorative image).</param>
        /// <param name="classId">Class.</param>
        /// <param name="lazy">Whether to load lazily.</param>
        /// <returns>HTML.</returns>
        public static string Img(string url, string alt, string classId, bool lazy)
        {
            return "<img src=\"" + Attr(url) + "\" alt=\"" + Attr(alt) + "\" class=\"" + Attr(classId) + "\" decoding=\"async\"" + (lazy ? " loading=\"lazy\"" : String.Empty) + ">";
        }

        /// <summary>
        /// Make JSON safe inside a script element (no closing tags, no HTML comments).
        /// </summary>
        /// <param name="json">JSON.</param>
        /// <returns>Escaped JSON.</returns>
        public static string ScriptSafe(string json)
        {
            return (json ?? String.Empty).Replace("<", "\\u003c", StringComparison.Ordinal).Replace(">", "\\u003e", StringComparison.Ordinal).Replace("&", "\\u0026", StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static string Meta(string key, string value, bool property)
        {
            return "<meta " + (property ? "property" : "name") + "=\"" + Attr(key) + "\" content=\"" + Attr(value) + "\">";
        }

        private static string Language(string? locale)
        {
            string l = (locale ?? "en").Trim();
            return Regex.IsMatch(l, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$") ? l : "en";
        }

        private static string Color(string? value, string fallback)
        {
            return value != null && _Color.IsMatch(value) ? value : fallback;
        }

        private static string Vars(PropertyColorScheme? scheme, bool dark)
        {
            PropertyColorScheme s = scheme ?? new PropertyColorScheme();
            string primary = Color(s.Primary, dark ? "#8ab4f8" : "#1a56db");
            return "--primary:" + primary + ";--primary-hover:" + Color(s.PrimaryHover, primary) + ";--on-primary:" + Color(s.OnPrimary, dark ? "#111111" : "#ffffff") +
                ";--accent:" + Color(s.Accent, dark ? "#f3f4f6" : "#111111") + ";--accent-soft:" + Color(s.AccentSoft, dark ? "#1f2937" : "#eef2ff") +
                ";--on-accent-soft:" + Color(s.OnAccentSoft, primary) + ";" +
                (dark ? "--bg:#111315;--surface:#1d2125;--text:#e6edf3;--muted:#9aa4ae;--border:#30363d;" : "--bg:#f6f7f9;--surface:#ffffff;--text:#1f2328;--muted:#59636e;--border:#d0d7de;");
        }

        private static string Css(PropertyBrandColors colors)
        {
            return ":root{" + Vars(colors.Light, false) + "color-scheme:light dark}" +
                "@media (prefers-color-scheme: dark){:root:not([data-theme=light]){" + Vars(colors.Dark, true) + "color-scheme:dark}}" +
                ":root[data-theme=dark]{" + Vars(colors.Dark, true) + "color-scheme:dark}:root[data-theme=light]{color-scheme:light}" +
                "*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:16px/1.55 -apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif}" +
                "a{color:var(--primary)}a:hover{color:var(--primary-hover)}img{max-width:100%;height:auto}" +
                ".skip{position:absolute;left:-9999px}.skip:focus{left:16px;top:8px;background:var(--surface);padding:8px;z-index:2}" +
                ".site-header{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;max-width:960px;margin:0 auto;padding:16px}" +
                ".brand{display:flex;align-items:center;gap:10px;text-decoration:none;color:var(--text);font-weight:700}.brand .logo{height:40px;width:auto}" +
                ".nav{display:flex;align-items:center;gap:12px}.nav-link{text-decoration:none}" +
                "main{max-width:960px;margin:0 auto;padding:0 16px 32px}" +
                ".card{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:24px;margin:0 0 20px}" +
                ".hero{display:flex;gap:20px;align-items:center;flex-wrap:wrap}.hero h1{margin:0 0 4px;font-size:1.9rem;line-height:1.2}" +
                ".avatar{width:112px;height:112px;border-radius:50%;object-fit:cover;border:1px solid var(--border)}" +
                ".cover{width:100%;max-height:420px;object-fit:cover;border-radius:12px;margin:0 0 20px;display:block}" +
                ".subtitle{color:var(--muted);margin:0}.eyebrow{color:var(--on-accent-soft);background:var(--accent-soft);display:inline-block;border-radius:999px;padding:2px 10px;font-size:.85rem;font-weight:600;margin:0 0 8px}" +
                ".chips{display:flex;flex-wrap:wrap;gap:8px;list-style:none;padding:0;margin:8px 0}.chips li{background:var(--accent-soft);color:var(--on-accent-soft);border-radius:999px;padding:2px 12px;font-size:.9rem}" +
                ".photos{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:12px;list-style:none;padding:0;margin:0}.photos video{width:100%;aspect-ratio:4/3;object-fit:contain;background:#000;border-radius:8px}.photos img{width:100%;aspect-ratio:4/3;object-fit:cover;border-radius:8px}" +
                "dl.facts{display:grid;grid-template-columns:minmax(120px,max-content) 1fr;gap:6px 16px;margin:0}dl.facts dt{color:var(--muted);font-weight:600}dl.facts dd{margin:0}" +
                ".people{list-style:none;padding:0;margin:0;display:grid;gap:8px}.description{white-space:pre-line}" +
                ".status{font-weight:700;color:var(--primary)}" +
                ".button{display:inline-block;background:var(--primary);color:var(--on-primary)!important;text-decoration:none;font-weight:600;padding:10px 18px;border-radius:8px}" +
                ".button:hover{background:var(--primary-hover)}.button.secondary{background:transparent;color:var(--primary)!important;border:1px solid var(--primary)}" +
                ".actions{display:flex;flex-wrap:wrap;gap:12px;align-items:center}.cta h2{margin-top:0}" +
                ".site-footer{max-width:960px;margin:0 auto;padding:16px 16px 40px;color:var(--muted);font-size:.9rem}" +
                "h2{font-size:1.2rem;margin:0 0 12px}" +
                "@media (max-width:480px){.hero h1{font-size:1.5rem}.card{padding:16px}}";
        }

        #endregion
    }
}
