namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Test.Shared.IntegrationTests;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 2 link previews (contract section 5.9): every blocked address range through a scripted resolver,
    /// IPv4-mapped, NAT64, 6to4, and Teredo forms, DNS rebinding and the pinned connection, redirects, blocked
    /// domains, URL refusals, type, size, and time limits against a loopback server, parsing, the cache and refresh
    /// rules, the prune worker, the per-host limit, and the route.
    /// </summary>
    public static class LinkPreviewSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "LinkPreviews";

        #endregion

        #region Private-Members

        private static readonly string[] _Blocked = new[]
        {
            "0.0.0.0", "0.1.2.3", "10.0.0.1", "10.255.255.255", "100.64.0.1", "100.127.255.254", "127.0.0.1", "127.255.0.9", "169.254.169.254", "169.254.0.1",
            "172.16.0.1", "172.31.255.255", "192.0.0.8", "192.0.2.5", "192.88.99.1", "192.168.1.1", "198.18.0.1", "198.19.255.255", "198.51.100.7", "203.0.113.9",
            "224.0.0.1", "239.255.255.250", "240.0.0.1", "250.1.2.3", "255.255.255.255",
            "::", "::1", "fe80::1", "febf::1", "fec0::1", "fc00::1", "fdff::1", "fd00:ec2::254", "ff02::1", "ff0e::1", "2001:db8::1", "2001:db8:ffff::1", "3fff::1",
            "100::1", "::ffff:10.0.0.1", "::ffff:127.0.0.1", "::ffff:169.254.169.254", "::ffff:192.168.0.1", "::ffff:0.0.0.0", "::7f00:1",
            "64:ff9b::a00:1", "64:ff9b::7f00:1", "64:ff9b::a9fe:a9fe", "64:ff9b:1::1", "2002:a00:1::1", "2002:7f00:1::1", "2002:a9fe:a9fe::1",
            "2001:0:4136:e378:8000:63bf:80ff:fffe", "2001:0:a00:1:8000:63bf:f7f7:f7f7", "2001:10::1"
        };

        private static readonly string[] _Allowed = new[]
        {
            "93.184.216.34", "8.8.8.8", "1.1.1.1", "100.63.255.255", "100.128.0.1", "172.15.255.255", "172.32.0.1", "192.0.1.1", "192.169.0.1", "198.17.255.255",
            "198.20.0.1", "223.255.255.255", "2606:4700::1111", "2a00:1450::1", "::ffff:8.8.8.8", "64:ff9b::808:808", "2002:808:808::1",
            "2001:0:4136:e378:8000:63bf:f7f7:f7f7"
        };

        private static readonly byte[] _Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4 };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Link previews (" + TestEnvironment.Provider + ")", new[]
            {
                Unit("Policy.Ranges", "Every blocked range (including mapped, NAT64, 6to4, and Teredo forms) is refused and public addresses pass", async ct =>
                {
                    Check.True(_Blocked.All(a => !PublicAddressPolicy.IsPublic(IPAddress.Parse(a))), "Blocked: " + String.Join(", ", _Blocked.Where(a => PublicAddressPolicy.IsPublic(IPAddress.Parse(a)))));
                    Check.True(_Allowed.All(a => PublicAddressPolicy.IsPublic(IPAddress.Parse(a))), "Allowed: " + String.Join(", ", _Allowed.Where(a => !PublicAddressPolicy.IsPublic(IPAddress.Parse(a)))));
                    ScriptedDnsResolver resolver = new ScriptedDnsResolver();
                    HttpLinkPreviewFetcher fetcher = new HttpLinkPreviewFetcher(new LinkPreviewSettings(), resolver);
                    for (int i = 0; i < _Blocked.Length; i++)
                    {
                        string host = "h" + i + ".example.test";
                        resolver.Script(host, _Blocked[i]);
                        LinkFetchResult result = await fetcher.FetchPageAsync(new Uri("https://" + host + "/page"), ct).ConfigureAwait(false);
                        Check.Equal(LinkFetchOutcomeEnum.BlockedAddress, result.Outcome, "Fetch refused for " + _Blocked[i]);
                        Check.Equal(1, resolver.Lookups(host), "Resolved once for " + _Blocked[i]);
                    }
                    resolver.Script("mixed.example.test", "93.184.216.34, 10.0.0.1");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedAddress, (await fetcher.FetchPageAsync(new Uri("https://mixed.example.test/"), ct).ConfigureAwait(false)).Outcome, "Any blocked address refuses");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedAddress, (await fetcher.FetchPageAsync(new Uri("http://169.254.169.254/latest/meta-data"), ct).ConfigureAwait(false)).Outcome, "IPv4 literal");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedAddress, (await fetcher.FetchPageAsync(new Uri("http://[::ffff:127.0.0.1]/"), ct).ConfigureAwait(false)).Outcome, "Mapped literal");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedAddress, (await fetcher.FetchPageAsync(new Uri("http://[fd00:ec2::254]/"), ct).ConfigureAwait(false)).Outcome, "IPv6 metadata literal");
                    Check.Equal(LinkFetchOutcomeEnum.Error, (await fetcher.FetchPageAsync(new Uri("https://nxdomain.example.test/"), ct).ConfigureAwait(false)).Outcome, "No answer is an error");
                }),
                Unit("Policy.UrlRules", "Scheme, port, credentials, and blocked domain suffixes are refused before any lookup", async ct =>
                {
                    ScriptedDnsResolver resolver = new ScriptedDnsResolver();
                    LinkPreviewSettings settings = new LinkPreviewSettings { BlockedDomains = new List<string> { "bad.test", ".worse.test" } };
                    HttpLinkPreviewFetcher fetcher = new HttpLinkPreviewFetcher(settings, resolver);
                    Check.Equal(LinkFetchOutcomeEnum.BlockedUrl, (await fetcher.FetchPageAsync(new Uri("ftp://site.test/"), ct).ConfigureAwait(false)).Outcome, "ftp");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedUrl, (await fetcher.FetchPageAsync(new Uri("file:///etc/passwd"), ct).ConfigureAwait(false)).Outcome, "file");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedUrl, (await fetcher.FetchPageAsync(new Uri("https://site.test:8443/"), ct).ConfigureAwait(false)).Outcome, "Port 8443");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedUrl, (await fetcher.FetchPageAsync(new Uri("http://site.test:22/"), ct).ConfigureAwait(false)).Outcome, "Port 22");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedUrl, (await fetcher.FetchPageAsync(new Uri("https://user:pass@site.test/"), ct).ConfigureAwait(false)).Outcome, "Credentials");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedDomain, (await fetcher.FetchPageAsync(new Uri("https://bad.test/"), ct).ConfigureAwait(false)).Outcome, "Domain");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedDomain, (await fetcher.FetchPageAsync(new Uri("https://deep.sub.BAD.test/"), ct).ConfigureAwait(false)).Outcome, "Subdomain");
                    Check.Equal(LinkFetchOutcomeEnum.BlockedDomain, (await fetcher.FetchPageAsync(new Uri("https://x.worse.test/"), ct).ConfigureAwait(false)).Outcome, "Leading dot suffix");
                    Check.Equal(0, resolver.Lookups("bad.test") + resolver.Lookups("site.test"), "No lookups for refused URLs");
                    Check.True(!HttpLinkPreviewFetcher.IsBlockedDomain("notbad.test", settings.BlockedDomains), "Suffix matches labels only");
                    Check.True(HttpLinkPreviewFetcher.IsAcceptedImage(_Png), "PNG magic");
                    Check.True(HttpLinkPreviewFetcher.IsAcceptedImage(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }), "JPEG magic");
                    Check.True(HttpLinkPreviewFetcher.IsAcceptedImage(Encoding.ASCII.GetBytes("RIFF0000WEBPVP8 ")), "WebP magic");
                    Check.True(!HttpLinkPreviewFetcher.IsAcceptedImage(Encoding.ASCII.GetBytes("GIF89a000000")), "GIF refused");
                }),
                Unit("Fetch.Pinned", "A real fetch connects to the checked address with the original Host; DNS rebinding cannot swap the address", async ct =>
                {
                    await using (LoopbackHttpServer server = new LoopbackHttpServer())
                    {
                        server.Html("/page", "<html><head><title>Loopback</title></head></html>");
                        ScriptedDnsResolver resolver = new ScriptedDnsResolver();
                        resolver.Script("rebind.example.test", "127.0.0.1", "10.0.0.1");
                        HttpLinkPreviewFetcher fetcher = new HttpLinkPreviewFetcher(new LinkPreviewSettings(), resolver, new LoopbackTestAddressPolicy());
                        Uri url = new Uri("http://rebind.example.test:" + server.Port + "/page");
                        LinkFetchResult first = await fetcher.FetchPageAsync(url, ct).ConfigureAwait(false);
                        Check.Equal(LinkFetchOutcomeEnum.Fetched, first.Outcome, "Fetched through the pinned address (" + first.Detail + ")");
                        Check.True(Encoding.UTF8.GetString(first.Body).Contains("Loopback"), "Body");
                        Check.Equal(1, resolver.Lookups("rebind.example.test"), "Resolved exactly once; the connection never resolved again");
                        Check.Equal("/page host=rebind.example.test:" + server.Port, server.Requests.Single(), "Original host name in the Host header");
                        LinkFetchResult second = await fetcher.FetchPageAsync(url, ct).ConfigureAwait(false);
                        Check.Equal(LinkFetchOutcomeEnum.BlockedAddress, second.Outcome, "The rebound private answer is refused");
                        Check.Equal(1, server.Requests.Count, "No connection for the refused answer");
                    }
                }),
                Unit("Fetch.Redirects", "Every redirect hop is checked and the redirect limit applies", async ct =>
                {
                    await using (LoopbackHttpServer server = new LoopbackHttpServer())
                    {
                        string origin = "http://site.example.test:" + server.Port;
                        server.Html("/final", "<title>Final</title>");
                        server.Redirect("/relative", "/final");
                        server.Redirect("/to-private", "http://10.0.0.1/");
                        server.Redirect("/to-metadata", "http://other.example.test/x");
                        server.Redirect("/to-domain", "http://evil.bad.test/");
                        server.Redirect("/to-port", "http://site.example.test:9/");
                        server.Redirect("/a", "/b");
                        server.Redirect("/b", "/c");
                        server.Redirect("/c", "/final");
                        ScriptedDnsResolver resolver = new ScriptedDnsResolver();
                        resolver.Script("site.example.test", "127.0.0.1");
                        resolver.Script("other.example.test", "169.254.169.254");
                        LinkPreviewSettings settings = new LinkPreviewSettings { MaxRedirects = 3, BlockedDomains = new List<string> { "bad.test" } };
                        HttpLinkPreviewFetcher fetcher = new HttpLinkPreviewFetcher(settings, resolver, new LoopbackPortPolicy(server.Port));
                        LinkFetchResult relative = await fetcher.FetchPageAsync(new Uri(origin + "/relative"), ct).ConfigureAwait(false);
                        Check.Equal(LinkFetchOutcomeEnum.Fetched, relative.Outcome, "Relative redirect followed");
                        Check.Equal(origin + "/final", relative.FinalUrl!.AbsoluteUri, "Final URL");
                        Check.Equal(LinkFetchOutcomeEnum.BlockedAddress, (await fetcher.FetchPageAsync(new Uri(origin + "/to-private"), ct).ConfigureAwait(false)).Outcome, "Redirect to a private literal");
                        Check.Equal(LinkFetchOutcomeEnum.BlockedAddress, (await fetcher.FetchPageAsync(new Uri(origin + "/to-metadata"), ct).ConfigureAwait(false)).Outcome, "Redirect to a host resolving to metadata");
                        Check.Equal(LinkFetchOutcomeEnum.BlockedDomain, (await fetcher.FetchPageAsync(new Uri(origin + "/to-domain"), ct).ConfigureAwait(false)).Outcome, "Redirect to a blocked domain");
                        Check.Equal(LinkFetchOutcomeEnum.BlockedUrl, (await fetcher.FetchPageAsync(new Uri(origin + "/to-port"), ct).ConfigureAwait(false)).Outcome, "Redirect to another port");
                        Check.Equal(LinkFetchOutcomeEnum.Fetched, (await fetcher.FetchPageAsync(new Uri(origin + "/a"), ct).ConfigureAwait(false)).Outcome, "Three redirects allowed");
                        settings.MaxRedirects = 2;
                        Check.Equal(LinkFetchOutcomeEnum.TooManyRedirects, (await fetcher.FetchPageAsync(new Uri(origin + "/a"), ct).ConfigureAwait(false)).Outcome, "Limit 2 refuses the third");
                        settings.MaxRedirects = 0;
                        Check.Equal(LinkFetchOutcomeEnum.TooManyRedirects, (await fetcher.FetchPageAsync(new Uri(origin + "/relative"), ct).ConfigureAwait(false)).Outcome, "Limit 0");
                    }
                }),
                Unit("Fetch.Limits", "Only text/html pages, the size caps, image magic bytes, the timeout, and the fixed User-Agent", async ct =>
                {
                    await using (LoopbackHttpServer server = new LoopbackHttpServer())
                    {
                        string origin = "http://site.example.test:" + server.Port;
                        server.Html("/json", "{}", "application/json");
                        server.Html("/big", "<title>x</title>" + new string('a', 70000));
                        server.Html("/slow", "<title>slow</title>");
                        server.Delay("/slow", 2500);
                        server.Raw("/png", 200, "image/png", _Png);
                        server.Raw("/fake.png", 200, "image/png", Encoding.ASCII.GetBytes("<html>not an image</html>"));
                        server.Raw("/huge.png", 200, "image/png", _Png.Concat(new byte[70000]).ToArray());
                        server.Raw("/missing", 404, "text/html", Encoding.ASCII.GetBytes("<title>404</title>"));
                        ScriptedDnsResolver resolver = new ScriptedDnsResolver();
                        resolver.Script("site.example.test", "127.0.0.1");
                        LinkPreviewSettings settings = new LinkPreviewSettings { MaxHtmlBytes = 65536, MaxImageBytes = 65536, TimeoutSeconds = 1 };
                        HttpLinkPreviewFetcher fetcher = new HttpLinkPreviewFetcher(settings, resolver, new LoopbackPortPolicy(server.Port));
                        Check.Equal(LinkFetchOutcomeEnum.NotHtml, (await fetcher.FetchPageAsync(new Uri(origin + "/json"), ct).ConfigureAwait(false)).Outcome, "JSON refused");
                        Check.Equal(LinkFetchOutcomeEnum.TooLarge, (await fetcher.FetchPageAsync(new Uri(origin + "/big"), ct).ConfigureAwait(false)).Outcome, "HTML cap");
                        Check.Equal(LinkFetchOutcomeEnum.HttpError, (await fetcher.FetchPageAsync(new Uri(origin + "/missing"), ct).ConfigureAwait(false)).Outcome, "404");
                        Check.Equal(LinkFetchOutcomeEnum.Fetched, (await fetcher.FetchImageAsync(new Uri(origin + "/png"), ct).ConfigureAwait(false)).Outcome, "PNG image");
                        Check.Equal(LinkFetchOutcomeEnum.UnsupportedImage, (await fetcher.FetchImageAsync(new Uri(origin + "/fake.png"), ct).ConfigureAwait(false)).Outcome, "Magic bytes checked");
                        Check.Equal(LinkFetchOutcomeEnum.TooLarge, (await fetcher.FetchImageAsync(new Uri(origin + "/huge.png"), ct).ConfigureAwait(false)).Outcome, "Image cap");
                        DateTime started = DateTime.UtcNow;
                        Check.Equal(LinkFetchOutcomeEnum.Timeout, (await fetcher.FetchPageAsync(new Uri(origin + "/slow"), ct).ConfigureAwait(false)).Outcome, "Timeout");
                        Check.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2.4), "Stopped at the timeout");
                        Check.True(server.UserAgents.All(a => a == "TrellisKitLinkPreview/1.0"), "Fixed User-Agent on every request");
                        Check.True(server.Cookies == 0, "No cookies sent");
                    }
                }),
                Unit("Parse.Precedence", "Open Graph, then Twitter, then title and meta description; entities decoded, markup stripped, cut to 200 and 300", ct =>
                {
                    LinkPreviewMetadata og = LinkPreviewHtmlParser.Parse("<html><head><title>Title el</title><meta name=\"description\" content=\"Meta d\"><meta name=\"twitter:title\" content=\"Tw t\">" +
                        "<meta property=\"og:title\" content=\"OG &amp; &lt;b&gt;bold&lt;/b&gt;\"><meta content='OG d' property='og:description'><meta property=\"og:site_name\" content=\"Site\">" +
                        "<meta property=\"og:image\" content=\"/img/card.png\"><meta property=\"og:url\" content=\"https://site.test/canon\"></head></html>");
                    Check.Equal("OG & bold", og.Title, "og:title decoded and stripped");
                    Check.Equal("OG d", og.Description, "og:description (single quotes, content first)");
                    Check.Equal("Site", og.SiteName, "Site name");
                    Check.Equal("/img/card.png", og.ImageUrl, "Image as found");
                    Check.Equal("https://site.test/canon", og.CanonicalUrl, "og:url");
                    LinkPreviewMetadata tw = LinkPreviewHtmlParser.Parse("<title>Title el</title><meta name=\"twitter:title\" content=\"Tw t\"><meta name=\"twitter:description\" content=\"Tw d\"><meta name=\"twitter:image\" content=\"https://cdn.test/i.png\"><link rel=\"canonical\" href=\"https://site.test/c\">");
                    Check.Equal("Tw t", tw.Title, "Twitter title before the title element");
                    Check.Equal("Tw d", tw.Description, "Twitter description");
                    Check.Equal("https://cdn.test/i.png", tw.ImageUrl, "Twitter image");
                    Check.Equal("https://site.test/c", tw.CanonicalUrl, "Canonical link");
                    LinkPreviewMetadata plain = LinkPreviewHtmlParser.Parse("<!-- <title>Hidden</title> --><TITLE>\n  Fish &#38;\n Chips &lt;3 </TITLE><META NAME=description CONTENT=\"Plain\u200B desc\">");
                    Check.Equal("Fish & Chips <3", plain.Title, "Title element decoded, whitespace normalized; a decoded lone < is text");
                    Check.Equal("Plain desc", plain.Description, "Meta description normalized like bodies");
                    string longTitle = String.Join(" ", Enumerable.Repeat("word", 80));
                    LinkPreviewMetadata cut = LinkPreviewHtmlParser.Parse("<meta property=\"og:title\" content=\"" + longTitle + "\"><meta property=\"og:description\" content=\"" + new string('d', 400) + "\">");
                    Check.Equal(200, MarkdownNormalizer.Length(cut.Title), "Title cut to 200");
                    Check.Equal(300, MarkdownNormalizer.Length(cut.Description), "Description cut to 300");
                    Check.True(cut.Title!.EndsWith("…", StringComparison.Ordinal), "Ellipsis marks the cut");
                    Check.True(LinkPreviewHtmlParser.Parse("<meta property=\"og:title\" content=\"  \">").Title == null, "Blank is absent");
                    Check.Equal("https://example.test/a?b=1", LinkPreviewService.NormalizeUrl("HTTPS://Example.TEST:443/a?b=1#frag"), "Normalized URL");
                    Check.Equal("http://example.test/", LinkPreviewService.NormalizeUrl("http://EXAMPLE.test:80"), "Default port removed");
                    Check.Equal("http://example.test:8080/x", LinkPreviewService.NormalizeUrl("http://example.test:8080/x"), "Other port kept");
                    Check.True(LinkPreviewService.NormalizeUrl("ftp://example.test/") == null && LinkPreviewService.NormalizeUrl("not a url") == null, "Refused");
                    Check.Equal(LinkPreviewService.HashUrl("https://example.test/a"), LinkPreviewService.HashUrl(LinkPreviewService.NormalizeUrl("https://EXAMPLE.test/a#x")!), "Same hash");
                    return Task.CompletedTask;
                }),
                Case("Route.Preview", "The route returns the LinkPreview shape, stores the image through the store, and uses the cache", async (f, ct) =>
                {
                    FakeLinkPreviewImageStore images = Images(f);
                    f.Fakes.LinkPreviews.Page("https://site.example.test/about", "<meta property=\"og:title\" content=\"About us\"><meta property=\"og:description\" content=\"Who we are\">" +
                        "<meta property=\"og:site_name\" content=\"Example\"><meta property=\"og:image\" content=\"../img/card.png\">", "https://site.example.test/pages/about");
                    f.Fakes.LinkPreviews.Image("https://site.example.test/img/card.png", _Png);
                    TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement preview = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/link-previews", member.AccessToken, new { url = "https://SITE.example.test/about#top" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("https://site.example.test/about", Api.Str(preview, "url"), "Normalized url");
                    Check.Equal("https://site.example.test/pages/about", Api.Str(preview, "canonicalUrl"), "Final URL as canonical");
                    Check.Equal("Example", Api.Str(preview, "siteName"), "siteName");
                    Check.Equal("About us", Api.Str(preview, "title"), "title");
                    Check.Equal("Who we are", Api.Str(preview, "description"), "description");
                    Check.True(Api.Str(preview, "imageUrl").StartsWith("/api/v1/media/files/link-previews/", StringComparison.Ordinal), "Engine image URL");
                    Check.Equal(1200, preview.GetProperty("imageWidth").GetInt32(), "imageWidth");
                    Check.Equal(630, preview.GetProperty("imageHeight").GetInt32(), "imageHeight");
                    Check.True(preview.TryGetProperty("fetchedUtc", out JsonElement _), "fetchedUtc");
                    Check.True(f.Fakes.LinkPreviews.Calls.Contains("image https://site.example.test/img/card.png"), "Relative og:image resolved against the final URL");
                    Check.Equal(1, images.Stored.Count, "Image stored through the seam");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/link-previews", member.AccessToken, new { url = "https://site.example.test/about" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.LinkPreviews.Calls.Count(c => c.StartsWith("page ", StringComparison.Ordinal)), "Cache hit: no second fetch");

                    f.Fakes.LinkPreviews.PageFailure("https://site.example.test/down", LinkFetchOutcomeEnum.Timeout);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/link-previews", member.AccessToken, new { url = "https://site.example.test/down" }, HttpStatusCode.BadRequest, "LinkPreviewUnavailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/link-previews", member.AccessToken, new { url = "https://site.example.test/down" }, HttpStatusCode.BadRequest, "LinkPreviewUnavailable", ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.LinkPreviews.Calls.Count(c => c == "page https://site.example.test/down"), "Failure cached");
                    f.Fakes.LinkPreviews.Page("https://site.example.test/empty", "<html><body>no metadata</body></html>");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/link-previews", member.AccessToken, new { url = "https://site.example.test/empty" }, HttpStatusCode.BadRequest, "LinkPreviewUnavailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/link-previews", member.AccessToken, new { url = "javascript:alert(1)" }, HttpStatusCode.BadRequest, "LinkPreviewUnavailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/link-previews", member.AccessToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    TestMember nonMember = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/link-previews", nonMember.AccessToken, new { url = "https://site.example.test/about" }, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    f.Settings.LinkPreviews.Enabled = false;
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/link-previews", member.AccessToken, new { url = "https://site.example.test/about" }, HttpStatusCode.BadRequest, "LinkPreviewUnavailable", ct).ConfigureAwait(false);
                    Check.True(await f.Services.LinkPreviews.ResolveForBodyAsync("see https://site.example.test/about", true, ct).ConfigureAwait(false) == null, "Disabled: content gets null");
                }),
                Case("Route.Limits", "linkPreviews.perHour per member and perHostPerMinute across members, with Retry-After", async (f, ct) =>
                {
                    Images(f);
                    for (int i = 0; i < 4; i++) f.Fakes.LinkPreviews.Page("https://busy.example.test/p" + i, "<title>P" + i + "</title>");
                    f.Settings.LinkPreviews.PerHour = 2;
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/link-previews", a.AccessToken, new { url = "https://busy.example.test/p0" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/link-previews", a.AccessToken, new { url = "https://busy.example.test/p0" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult limited = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/link-previews", a.AccessToken, new { url = "https://busy.example.test/p0" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.TooManyRequests, limited.Status, "Member limit");
                    Check.True(limited.RetryAfter != null, "Retry-After");
                    f.Settings.LinkPreviews.PerHour = 100;
                    f.Settings.LinkPreviews.PerHostPerMinute = 2;
                    TestMember b = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/link-previews", b.AccessToken, new { url = "https://busy.example.test/p1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult host = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/link-previews", b.AccessToken, new { url = "https://busy.example.test/p2" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.TooManyRequests, host.Status, "Per-host limit across members (p0 and p1 already fetched)");
                    Check.True(await f.Services.LinkPreviews.ResolveForBodyAsync("https://busy.example.test/p3", true, ct).ConfigureAwait(false) == null, "Content gets null, never an exception");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/link-previews", b.AccessToken, new { url = "https://busy.example.test/p0" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Content.ResolveAndRefresh", "Write-time resolution, word filtered previews, views for many ids, refresh never mutates a referenced row, and pruning", async (f, ct) =>
                {
                    Images(f);
                    LinkPreviewService service = f.Services.LinkPreviews;
                    f.Fakes.LinkPreviews.Page("https://news.example.test/story", "<meta property=\"og:title\" content=\"Version one\">");
                    string? id = await service.ResolveForBodyAsync("Read [this](https://news.example.test/story). Also https://other.example.test/", true, ct).ConfigureAwait(false);
                    Check.True(id != null && id.StartsWith("lpv_", StringComparison.Ordinal), "First URL resolved");
                    Check.True(await service.ResolveForBodyAsync("https://news.example.test/story", false, ct).ConfigureAwait(false) == null, "showLinkPreview false");
                    Check.True(await service.ResolveForBodyAsync("no links here", true, ct).ConfigureAwait(false) == null, "No URL");
                    Check.True(await service.ResolveForBodyAsync("https://unscripted.example.test/", true, ct).ConfigureAwait(false) == null, "Fetch failure gives null");

                    TestMember author = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    string post = await SafetyHelpers.InsertFeedPostAsync(f, author.Id, id, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE linkpreviews SET expiresutc = '2000-01-01T00:00:00.000Z' WHERE id = '" + id + "';", false, ct).ConfigureAwait(false);
                    f.Fakes.LinkPreviews.Page("https://news.example.test/story", "<meta property=\"og:title\" content=\"Version two\">");
                    string? refreshed = await service.ResolveForBodyAsync("https://news.example.test/story", true, ct).ConfigureAwait(false);
                    Check.True(refreshed != null && refreshed != id, "Refresh inserts a new row");
                    LinkPreview old = (await f.Database.LinkPreviews.ReadAsync(id!, ct).ConfigureAwait(false))!;
                    Check.Equal("Version one", old.Title, "Referenced row unchanged");
                    Check.Equal("old:" + id, old.UrlHash, "Old row moved off the url hash");
                    Dictionary<string, TrellisKit.Core.Responses.LinkPreviewView> views = await service.LoadViewsAsync(new[] { id, refreshed, null, "lpv_missing" }, ct).ConfigureAwait(false);
                    Check.Equal("Version one", views[id!].Title, "Post keeps its card");
                    Check.Equal("Version two", views[refreshed!].Title, "New card");
                    Check.Equal(2, views.Count, "Unknown ids skipped");

                    await f.Services.WordFilter.CreateAsync(new ContentActor { IsSystemAdmin = true, IsBreakGlass = true }, new TrellisKit.Core.Requests.WordFilterTermRequest { Term = "forbidden" },
                        new RequestInfo(), ct).ConfigureAwait(false);
                    f.Fakes.LinkPreviews.Page("https://news.example.test/bad", "<meta property=\"og:title\" content=\"A f0rbidden title\">");
                    Check.True(await service.ResolveForBodyAsync("https://news.example.test/bad", true, ct).ConfigureAwait(false) == null, "Block match drops the preview");

                    string orphan = (await service.GetOrFetchAsync("https://news.example.test/orphan", ct).ConfigureAwait(false))!.Id;
                    f.Fakes.LinkPreviews.Page("https://news.example.test/comment", "<title>Comment card</title>");
                    string commentCard = (await service.GetOrFetchAsync("https://news.example.test/comment", ct).ConfigureAwait(false))!.Id;
                    await SafetyHelpers.InsertCommentAsync(f, post, author.Id, commentCard, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE linkpreviews SET expiresutc = '2000-01-01T00:00:00.000Z';", false, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("INSERT INTO wordfilterhits (propertyid, id, termid, targettype, targetid, field, action, createdutc) VALUES ('" + TestProperty.SampleId + "', 'wfh_old', 'wft_x', 'FeedPost', NULL, 'body', 'Block', '2000-01-01T00:00:00.000Z'), " +
                        "('" + TestProperty.SampleId + "', 'wfh_new', 'wft_x', 'FeedPost', NULL, 'body', 'Block', '" + DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) + "');", false, ct).ConfigureAwait(false);
                    LinkPreviewPruneWorker worker = new LinkPreviewPruneWorker(f.Database, f.Services.Logger);
                    int pruned = await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.True(pruned >= 3, "Pruned rows (" + pruned + ")");
                    Check.True(await f.Database.LinkPreviews.ReadAsync(id!, ct).ConfigureAwait(false) != null, "Row referenced by a post kept");
                    Check.True(await f.Database.LinkPreviews.ReadAsync(commentCard, ct).ConfigureAwait(false) != null, "Row referenced by a comment kept");
                    Check.True(await f.Database.LinkPreviews.ReadAsync(refreshed!, ct).ConfigureAwait(false) == null, "Unreferenced expired row deleted");
                    Check.True(await f.Database.LinkPreviews.ReadAsync(orphan, ct).ConfigureAwait(false) == null, "Expired failure row deleted");
                    DataTable hits = await f.Raw.ExecuteQueryAsync("SELECT id FROM wordfilterhits WHERE id IN ('wfh_old', 'wfh_new');", false, ct).ConfigureAwait(false);
                    Check.True(hits.Rows.Count == 1 && hits.Rows[0]["id"].ToString() == "wfh_new", "Hits older than 90 days pruned");
                    LinkPreviewPruneWorker other = new LinkPreviewPruneWorker(f.Database, f.Services.Logger);
                    Check.Equal(0, await other.RunOnceAsync(ct).ConfigureAwait(false), "A second instance does not get the lease");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static FakeLinkPreviewImageStore Images(HttpFixture f)
        {
            FakeLinkPreviewImageStore store = new FakeLinkPreviewImageStore();
            f.Services.LinkPreviews.ImageStore = store;
            return store;
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static TestCaseDescriptor Unit(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return SuiteCases.Unit(Id, caseId, name, body);
        }

        #endregion
    }
}
