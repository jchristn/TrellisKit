namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Public listing images for signed-out visitors: event covers, series covers, and venue photos of content visitors may
    /// see are copied to the public media paths (as public sample post media already is), served without a media grant,
    /// and withdrawn when the content stops being public; members-only media never reaches the public paths.
    /// </summary>
    public static class PublicMediaSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PublicMedia";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Public listing images (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "PublicMedia.EventCovers", "A Public event's cover is copied to the public paths on publication (also when it finishes processing later), loads without credentials from the public listing and the anonymous member route, and is withdrawn when the event becomes members-only; a members-only event's cover never becomes public", EventCoversAsync),
                SuiteCases.Http(Id, "PublicMedia.SeriesAndVenues", "Covers of Public published series and photos of Public live venues are served from the public paths to signed-out visitors and withdrawn on archive (venues: when no longer listed, since every listed venue has a public landing page); members-only series covers stay private", SeriesAndVenuesAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task EventCoversAsync(HttpFixture f, CancellationToken ct)
        {
            EventTestKit.Relax(f);
            f.Settings.Events.PublicListingsEnabled = true;
            await EventTestKit.SetAccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>() }, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string publicCover = await ReadyImageAsync(f, admin, ct).ConfigureAwait(false);
            string privateCover = await ReadyImageAsync(f, admin, ct).ConfigureAwait(false);

            string draft = Api.Str(await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Open day", EventTestKit.Local(10, 18), EventTestKit.Local(10, 20), new { audience = "Public", coverMediaId = publicCover }), ct).ConfigureAwait(false), "id");
            Check.True(!(await MediaAsync(f, publicCover, ct).ConfigureAwait(false)).IsPublic, "A draft's cover is not public yet");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + draft + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True((await MediaAsync(f, publicCover, ct).ConfigureAwait(false)).IsPublic, "Published Public event: cover copied to the public paths");
            string members = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Members night", ct, new { audience = "Members", coverMediaId = privateCover }).ConfigureAwait(false);
            Check.True(!(await MediaAsync(f, privateCover, ct).ConfigureAwait(false)).IsPublic, "Members event: the cover never becomes public");

            JsonElement one = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/events/" + draft, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> urls = Urls(one.GetProperty("cover"));
            Check.True(urls.Count > 0 && urls.All(u => u.Contains("/media/files/public/" + publicCover + "/", StringComparison.Ordinal)), "Public event cover URLs are public paths (" + String.Join(",", urls) + ")");
            foreach (string url in urls.Take(2)) Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false)).Status, "Loads signed out: " + url);
            ApiResult listed = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/public/events/enumerate", null, new { maxResults = 20 }, ct).ConfigureAwait(false);
            JsonElement item = listed.Body.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == draft);
            Check.True(Urls(item.GetProperty("cover")).All(u => u.Contains("/media/files/public/", StringComparison.Ordinal)), "Listing cover on the public path");
            ApiResult anonymous = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events/" + draft, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, anonymous.Status, "Anonymous browsing of the event (" + anonymous.Text + ")");
            Check.True(Urls(anonymous.Body.GetProperty("cover")).All(u => u.Contains("/media/files/public/", StringComparison.Ordinal)), "Anonymous member route uses the public copy too");
            JsonElement adminView = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + members, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> privateUrls = Urls(adminView.GetProperty("cover"));
            Check.True(privateUrls.Count > 0 && privateUrls.All(u => !u.Contains("/public/", StringComparison.Ordinal)), "Members cover stays on the member path");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/public/" + privateCover + "/thumb.jpg", null, null, ct).ConfigureAwait(false)).Status, "No public copy of the members cover");
            Check.Equal(HttpStatusCode.Forbidden, (await MediaTestKit.SendAsync(f, HttpMethod.Get, privateUrls[0], null, null, ct).ConfigureAwait(false)).Status, "The members cover needs a grant");

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + draft, admin.AccessToken, new { audience = "Members" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!(await MediaAsync(f, publicCover, ct).ConfigureAwait(false)).IsPublic, "Members-only now: cover withdrawn");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, urls[0], null, null, ct).ConfigureAwait(false)).Status, "Public copy deleted");

            // A cover still processing at publication reaches the public paths when it settles.
            string late = await MediaTestKit.UploadSingleAsync(f, admin.AccessToken, "Image", "image/png", MediaTestKit.Png(80, 60), ct).ConfigureAwait(false);
            string later = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Late cover", ct, new { audience = "Public", coverMediaId = late }).ConfigureAwait(false);
            Check.True(!(await MediaAsync(f, late, ct).ConfigureAwait(false)).IsPublic, "Not public while processing");
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            Check.True((await MediaAsync(f, late, ct).ConfigureAwait(false)).IsPublic, "Public once processed (" + later + ")");
        }

        private static async Task SeriesAndVenuesAsync(HttpFixture f, CancellationToken ct)
        {
            LibraryTestKit.Relax(f);
            DirectoryTestKit.Relax(f);
            await EventTestKit.SetAccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>() }, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string publicCover = await ReadyImageAsync(f, admin, ct).ConfigureAwait(false);
            string privateCover = await ReadyImageAsync(f, admin, ct).ConfigureAwait(false);
            string publicSeries = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Open course", ct, new { audience = "Public", coverMediaId = publicCover }).ConfigureAwait(false), "id");
            string draftSeries = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Members course", ct, new { audience = "Members", coverMediaId = privateCover }).ConfigureAwait(false), "id");
            Check.True((await MediaAsync(f, publicCover, ct).ConfigureAwait(false)).IsPublic, "Public published series: cover public");
            Check.True(!(await MediaAsync(f, privateCover, ct).ConfigureAwait(false)).IsPublic, "Members series: cover private (" + draftSeries + ")");
            ApiResult anonymous = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/series/" + publicSeries, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, anonymous.Status, "Anonymous series read (" + anonymous.Text + ")");
            List<string> urls = Urls(anonymous.Body.GetProperty("cover"));
            Check.True(urls.Count > 0 && urls.All(u => u.Contains("/media/files/public/" + publicCover + "/", StringComparison.Ordinal)), "Signed-out series cover from the public path (" + String.Join(",", urls) + ")");
            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, urls[0], null, null, ct).ConfigureAwait(false)).Status, "Series cover loads signed out");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + publicSeries + "/archive", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!(await MediaAsync(f, publicCover, ct).ConfigureAwait(false)).IsPublic, "Archived series: cover withdrawn");

            string photo = await ReadyImageAsync(f, admin, ct).ConfigureAwait(false);
            string venue = Api.Str(await DirectoryTestKit.VenueAsync(f, "Open hall", ct, 40.7, -74.0, new { audience = "Public", photoMediaIds = new[] { photo } }, admin.AccessToken).ConfigureAwait(false), "id");
            Check.True((await MediaAsync(f, photo, ct).ConfigureAwait(false)).IsPublic, "Public venue: photo public");
            ApiResult anonymousVenue = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/venues/" + venue, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, anonymousVenue.Status, "Anonymous venue read (" + anonymousVenue.Text + ")");
            List<string> photoUrls = Urls(anonymousVenue.Body.GetProperty("photos"));
            Check.True(photoUrls.Count > 0 && photoUrls.All(u => u.Contains("/media/files/public/", StringComparison.Ordinal)), "Signed-out venue photos from the public path (" + String.Join(",", photoUrls) + ")");
            ApiResult publicVenue = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/public/venues/" + venue, null, null, ct).ConfigureAwait(false);
            if (publicVenue.Status == HttpStatusCode.OK) Check.True(Urls(publicVenue.Body.GetProperty("photos")).All(u => u.Contains("/media/files/public/", StringComparison.Ordinal)), "Public venue route photos");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/venues/" + venue, admin.AccessToken, new { audience = "Members" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            // A listed venue has a public landing page whatever its audience (docs/PHASE4_CONTRACT.md section 18), so its
            // staff photos stay on the public paths until it stops being listed.
            Check.True((await MediaAsync(f, photo, ct).ConfigureAwait(false)).IsPublic, "Members venue with a public page: photo stays public");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/venues/" + venue + "/archive", admin.AccessToken, new { reason = "Gone" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!(await MediaAsync(f, photo, ct).ConfigureAwait(false)).IsPublic, "Archived venue: photo withdrawn");
        }

        private static async Task<string> ReadyImageAsync(HttpFixture f, TestMember owner, CancellationToken ct)
        {
            string id = await MediaTestKit.UploadSingleAsync(f, owner.AccessToken, "Image", "image/png", MediaTestKit.Png(80, 60), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            return id;
        }

        private static async Task<MediaAttachment> MediaAsync(HttpFixture f, string id, CancellationToken ct)
        {
            return (await f.Database.Media.ReadAsync(id, ct).ConfigureAwait(false))!;
        }

        private static List<string> Urls(JsonElement e)
        {
            List<string> result = new List<string>();
            Collect(e, result);
            return result;
        }

        private static void Collect(JsonElement e, List<string> result)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty p in e.EnumerateObject()) Collect(p.Value, result);
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in e.EnumerateArray()) Collect(item, result);
                    break;
                case JsonValueKind.String:
                    string? text = e.GetString();
                    if (text != null && text.Contains("/media/files/", StringComparison.Ordinal)) result.Add(text);
                    break;
            }
        }

        #endregion
    }
}
