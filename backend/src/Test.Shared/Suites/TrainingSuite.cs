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
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Training;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// The Training overview (docs/TRELLISKIT.md section 15) on the neutral sample property: route classification and
    /// settings registration, composition from real data with first run, every training.* setting at its bounds, feature
    /// switches and segments, the access models, provider failure isolation, the kill switch, the rank and coach cards, and
    /// property isolation.
    /// </summary>
    public static class TrainingSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Training";

        #endregion

        #region Private-Members

        private const string _Overview = TrainingTestKit.Overview;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Training overview (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Routes.Classification", "The overview is a session-only Member group route declared for training; only it follows the training kill switch; settings are registered with their bounds", ClassificationAsync),
                Case("Overview.Composition", "A new member sees first run with empty cards and no sample content; real progress, follows, saves, and RSVPs fill Continue, New for you, Recently saved, Up next, Schedule, and Progress", CompositionAsync),
                Case("Overview.SettingBounds", "Every training.* setting clamps to its range and bounds its card at the minimum; training.newForYouDays bounds the window", SettingBoundsAsync),
                SuiteCases.Http(Id, "Overview.TrainingOff", "features.training false: 404 NotAvailable", p => p.Features.Training = false, TrainingOffAsync),
                SuiteCases.Http(Id, "Overview.Segments", "Saved needs features.saves; Techniques needs features.catalog and a published entry; with every Library segment and section off the overview is 404", p =>
                {
                    p.Features.Catalog = true;
                    p.Features.Saves = false;
                }, SegmentsAsync),
                Case("Overview.AccessModels", "Signed out is 401 (also with anonymous browsing); FullyGated needs a membership (402); PartiallyGated free accounts reach it unless training is gated; Open free accounts reach it", AccessModelsAsync),
                Case("Overview.ProviderFailure", "A failing provider marks its sections NoData without failing the request, is never first run, and the hub recovers when it is removed", ProviderFailureAsync),
                Case("Overview.KillSwitch", "The training kill switch answers 503 on the overview only; the library and saves keep working", KillSwitchAsync),
                Case("Overview.RankCard", "ranks.enabled off: no rank card; on: the Your rank card { rank, next, curriculum, showVerificationLabels } follows GET /me/rank and a held rank lights Progress", RankCardAsync),
                AiTestKit.Case(Id, "Overview.CoachCard", "The coach card is available when AI is on and an assistant serves the member, with the property's placement", CoachCardAsync),
                SuiteCases.Http(Id, "Overview.PropertyIsolation", "Another property's member sees none of this property's activity, and this property's token is refused there", MultiPropertyKit.Multi, PropertyIsolationAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                TrainingTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static Task ClassificationAsync(CancellationToken ct)
        {
            Check.Equal(RouteGroupEnum.Member, AuthorizationService.Classify(_Overview), "Overview group");
            RouteAccess? access = AccessRoutes.Find("GET", _Overview);
            Check.True(access != null && access.Feature == FeatureKeyEnum.Training && !access.AnonymousEligible, "Overview declared for training, never anonymous");
            Check.True(AccessRoutes.Find("POST", _Overview) == null, "Only GET is declared");
            Check.Equal(RouteGroupEnum.SignedIn, AuthorizationService.Classify("/api/v1/me/saves"), "Other me routes stay Signed in");
            Check.True(KillSwitchCatalog.FeaturesFor("GET", _Overview).SequenceEqual(new[] { "training" }), "Overview follows the training switch");
            foreach (string path in new[] { "/api/v1/library/search", "/api/v1/lessons/les_x", "/api/v1/series/ser_x", "/api/v1/me/saves", "/api/v1/catalog/entries/cex_x/lessons" })
                Check.True(!KillSwitchCatalog.FeaturesFor("GET", path).Contains("training"), path + " ignores the training switch");
            Dictionary<string, int[]> expected = new Dictionary<string, int[]>
            {
                { "training.upNextLimit", new[] { 1, 10, 3 } },
                { "training.continueLimit", new[] { 1, 25, 10 } },
                { "training.recentSavesLimit", new[] { 1, 20, 6 } },
                { "training.newForYouLimit", new[] { 1, 20, 6 } },
                { "training.newForYouDays", new[] { 1, 90, 14 } }
            };
            AppSettings defaults = new AppSettings();
            foreach (KeyValuePair<string, int[]> pair in expected)
            {
                SettingFieldDefinition? field = SettingsRegistry.All.FirstOrDefault(d => d.Key == pair.Key);
                Check.True(field != null, pair.Key + " registered");
                Check.Equal((long)pair.Value[0], field!.Min ?? -1, pair.Key + " minimum");
                Check.Equal((long)pair.Value[1], field.Max ?? -1, pair.Key + " maximum");
                Check.Equal(pair.Value[2], Convert.ToInt32(field.GetValue(defaults)), pair.Key + " default");
            }
            return Task.CompletedTask;
        }

        private static async Task CompositionAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Followed course", ct).ConfigureAwait(false);
            string seriesId = Api.Str(series, "id");
            await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Existing lesson", ct).ConfigureAwait(false);
            await EventTestKit.PublishedAsync(f, admin.AccessToken, "Open session nobody joined", ct).ConfigureAwait(false);

            JsonElement fresh = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(fresh.GetProperty("sections").EnumerateArray().Select(s => Api.Str(s, "key")).SequenceEqual(new[] { "Overview", "Library", "Schedule", "Programs", "Progress" }), "Section order");
            Check.True(TrainingTestKit.Available(fresh, "Overview") && TrainingTestKit.Available(fresh, "Library"), "Overview and Library available");
            Check.True(TrainingTestKit.Section(fresh, "Library").GetProperty("reason").ValueKind == JsonValueKind.Null, "No reason when available");
            Check.True(TrainingTestKit.Segments(fresh).SequenceEqual(new[] { "Courses", "Saved" }), "Courses and Saved (catalog off by default)");
            foreach (string key in new[] { "Schedule", "Programs", "Progress" })
            {
                Check.True(!TrainingTestKit.Available(fresh, key), key + " unavailable for a new member");
                Check.Equal(key == "Programs" ? "FeatureOff" : "NoData", Api.Str(TrainingTestKit.Section(fresh, key), "reason"), key + " reason (Programs needs ranks.enabled)");
                Check.Equal(0, TrainingTestKit.Section(fresh, key).GetProperty("segments").GetArrayLength(), key + " has no segments");
            }
            Check.True(fresh.GetProperty("firstRun").GetBoolean(), "A new member sees the first-run state");
            foreach (string card in new[] { "upNext", "continue", "recentSaves", "newForYou" })
            {
                JsonElement page = fresh.GetProperty(card);
                Check.True(page.TryGetProperty("endOfResults", out JsonElement _) && TrainingTestKit.Count(page) == 0, card + " is an empty EnumerationResult (no sample content)");
            }
            Check.True(fresh.GetProperty("rank").ValueKind == JsonValueKind.Null, "No rank card while ranks are off");
            Check.True(!fresh.GetProperty("coach").GetProperty("available").GetBoolean(), "No coach while AI is off");
            Check.True(fresh.GetProperty("labels").ValueKind == JsonValueKind.Array && fresh.GetProperty("tags").ValueKind == JsonValueKind.Object, "Labels and tags");

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/series/" + seriesId + "/follow", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string newLesson = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Fresh lesson", ct).ConfigureAwait(false), "id");
            JsonElement followed = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(followed.GetProperty("newForYou")).Contains(newLesson), "New for you lists a new lesson in a followed series");
            Check.True(followed.GetProperty("firstRun").GetBoolean(), "Following alone is not activity");

            string watched = await LibraryTestKit.VideoLessonAsync(f, admin, seriesId, "Watched lesson", 600, ct).ConfigureAwait(false);
            await LibraryTestKit.ProgressAsync(f, member.AccessToken, watched, 60, 30, DateTime.UtcNow, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/saves/Series/" + seriesId, member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string going = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Group session", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, going, "Going", ct).ConfigureAwait(false);
            string interested = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Maybe session", ct, null, 5).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, interested, "Interested", ct).ConfigureAwait(false);

            JsonElement overview = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!overview.GetProperty("firstRun").GetBoolean(), "No longer first run");
            List<string> fresher = TrainingTestKit.Ids(overview.GetProperty("newForYou"));
            Check.True(fresher.Contains(newLesson) && !fresher.Contains(watched), "New for you leaves out a started lesson");
            Check.True(TrainingTestKit.Ids(overview.GetProperty("continue"), "lesson").Contains(watched), "Continue training lists the lesson in progress");
            Check.True(overview.GetProperty("recentSaves").GetProperty("objects").EnumerateArray().Any(s => Api.Str(s, "targetId") == seriesId), "Recently saved");
            Check.True(TrainingTestKit.Ids(overview.GetProperty("upNext")).SequenceEqual(new[] { going }), "Up next lists Going events only");
            JsonElement next = overview.GetProperty("upNext").GetProperty("objects")[0];
            Check.Equal("Event", Api.Str(next, "kind"), "Up next kind");
            Check.Equal("Group session", Api.Str(next, "title"), "Up next title");
            Check.Equal("/events/" + going, Api.Str(next, "path"), "Up next path");
            Check.True(next.TryGetProperty("startUtc", out JsonElement _) && next.TryGetProperty("timeZone", out JsonElement _) && next.TryGetProperty("locationSummary", out JsonElement _), "Up next fields");
            Check.True(TrainingTestKit.Available(overview, "Schedule"), "Schedule with an upcoming RSVP");
            Check.True(TrainingTestKit.Available(overview, "Progress"), "Progress with lesson progress");
            Check.True(!TrainingTestKit.Available(overview, "Programs"), "Programs needs a curriculum (ranks off)");

            await EventTestKit.PartiallyGatedAsync(f, ct, "library").ConfigureAwait(false);
            TestMember unpaid = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement free = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, unpaid.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(free.GetProperty("firstRun").GetBoolean(), "Other members' activity is not shared");
        }

        private static async Task SettingBoundsAsync(HttpFixture f, CancellationToken ct)
        {
            TrainingSettings s = f.Settings.Training;
            s.UpNextLimit = 0;
            s.ContinueLimit = -4;
            s.RecentSavesLimit = 0;
            s.NewForYouLimit = 0;
            s.NewForYouDays = 0;
            Check.True(s.UpNextLimit == 1 && s.ContinueLimit == 1 && s.RecentSavesLimit == 1 && s.NewForYouLimit == 1 && s.NewForYouDays == 1, "Values below the minimum clamp to 1");
            s.UpNextLimit = 11;
            s.ContinueLimit = 26;
            s.RecentSavesLimit = 21;
            s.NewForYouLimit = 21;
            s.NewForYouDays = 91;
            Check.True(s.UpNextLimit == 10 && s.ContinueLimit == 25 && s.RecentSavesLimit == 20 && s.NewForYouLimit == 20 && s.NewForYouDays == 90, "Values above the maximum clamp to the maximum");

            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Bounds course", ct).ConfigureAwait(false);
            string seriesId = Api.Str(series, "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/series/" + seriesId + "/follow", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string oldLesson = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Older new lesson", ct).ConfigureAwait(false), "id");
            await LibraryTestKit.SqlAsync(f, "UPDATE lessons SET publishedutc = now() - interval '3 days' WHERE propertyid = '" + LibraryTestKit.Pid(f) + "' AND id = '" + oldLesson + "';", ct).ConfigureAwait(false);
            await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Newest lesson", ct).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
            {
                string video = await LibraryTestKit.VideoLessonAsync(f, admin, seriesId, "Watched " + i, 600, ct).ConfigureAwait(false);
                await LibraryTestKit.ProgressAsync(f, member.AccessToken, video, 60, 30, DateTime.UtcNow.AddSeconds(i), ct).ConfigureAwait(false);
                string saved = await TrainingTestKit.LessonAsync(f, admin, "Saved " + i, ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/saves/Lesson/" + saved, member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                string eventId = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Session " + i, ct, null, 6 + i).ConfigureAwait(false);
                await EventTestKit.RsvpAsync(f, member.AccessToken, eventId, "Going", ct).ConfigureAwait(false);
            }

            JsonElement wide = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, TrainingTestKit.Count(wide.GetProperty("upNext")), "Up next lists both at the maximum");
            Check.Equal(2, TrainingTestKit.Count(wide.GetProperty("continue")), "Continue lists both at the maximum");
            Check.Equal(2, TrainingTestKit.Count(wide.GetProperty("recentSaves")), "Recently saved lists both at the maximum");
            Check.True(TrainingTestKit.Ids(wide.GetProperty("newForYou")).Contains(oldLesson), "A lesson published 3 days ago is new within 90 days");
            Check.True(TrainingTestKit.Count(wide.GetProperty("newForYou")) >= 2, "New for you lists both new lessons");
            Check.Equal(10, wide.GetProperty("upNext").GetProperty("maxResults").GetInt32(), "Up next page size is training.upNextLimit");

            s.UpNextLimit = 1;
            s.ContinueLimit = 1;
            s.RecentSavesLimit = 1;
            s.NewForYouLimit = 1;
            s.NewForYouDays = 1;
            JsonElement narrow = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            foreach (string card in new[] { "upNext", "continue", "recentSaves", "newForYou" })
            {
                JsonElement page = narrow.GetProperty(card);
                Check.Equal(1, TrainingTestKit.Count(page), card + " bounded by its setting at the minimum");
            }
            Check.True(!TrainingTestKit.Ids(narrow.GetProperty("newForYou")).Contains(oldLesson), "training.newForYouDays 1 leaves out a lesson published 3 days ago");
        }

        private static async Task TrainingOffAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!property.GetProperty("features").GetProperty("training").GetBoolean(), "features.training reaches clients");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/saves", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task SegmentsAsync(HttpFixture f, CancellationToken ct)
        {
            TrainingTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement overview = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Segments(overview).SequenceEqual(new[] { "Courses" }), "Courses only: saves off and no published entry");
            Check.Equal(0, TrainingTestKit.Count(overview.GetProperty("recentSaves")), "No recent saves while saves are off");
            string draft = await TrainingTestKit.DraftEntryAsync(f, "Unpublished movement", ct).ConfigureAwait(false);
            overview = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!TrainingTestKit.Segments(overview).Contains("Techniques"), "A draft entry does not show Techniques");
            await TrainingTestKit.SetEntryStatusAsync(f, draft, "Published", ct).ConfigureAwait(false);
            overview = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Segments(overview).SequenceEqual(new[] { "Courses", "Techniques" }), "A published entry shows Techniques");

            f.Property.Features.Library = false;
            f.Property.Features.Catalog = false;
            f.Property.Features.Events = false;
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            f.Property.Features.Catalog = true;
            overview = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Segments(overview).SequenceEqual(new[] { "Techniques" }), "Techniques alone keeps the Library available");
            Check.Equal("FeatureOff", Api.Str(TrainingTestKit.Section(overview, "Schedule"), "reason"), "Schedule FeatureOff with events and ranks off");
        }

        private static async Task AccessModelsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            ApiResult anonymous = await Api.SendAsync(f, HttpMethod.Get, _Overview, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, anonymous.Status, "Signed out is 401");
            await EventTestKit.PartiallyGatedAsync(f, ct, "library").ConfigureAwait(false);
            anonymous = await Api.SendAsync(f, HttpMethod.Get, _Overview, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, anonymous.Status, "Signed out is 401 even with anonymous browsing");
            await Api.ExpectAsync(f, HttpMethod.Get, _Overview, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await EventTestKit.PartiallyGatedAsync(f, ct, "library", "training").ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Overview, free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await EventTestKit.SetAccessAsync(f, new { model = "FullyGated" }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Overview, free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await EventTestKit.SetAccessAsync(f, new { model = "Open" }, ct).ConfigureAwait(false);
            JsonElement open = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Available(open, "Library"), "Open: free accounts see the Library");
        }

        private static async Task ProviderFailureAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await TrainingTestKit.LessonAsync(f, admin, "Failure lesson", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/saves/Lesson/" + lesson, member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            TrainingHubService hub = f.Services.Training.Hub;
            Check.True(hub.Providers.Count >= 5 && hub.Providers.Select(p => p.Name).Distinct().Count() == hub.Providers.Count, "Engine providers registered once each");
            FailingTrainingProvider failing = new FailingTrainingProvider();
            lock (hub.Providers) hub.Providers.Insert(0, failing);
            try
            {
                JsonElement overview = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                JsonElement library = TrainingTestKit.Section(overview, "Library");
                Check.True(!library.GetProperty("available").GetBoolean(), "The failing provider's section is unavailable");
                Check.Equal("NoData", Api.Str(library, "reason"), "Reason NoData");
                Check.Equal(0, library.GetProperty("segments").GetArrayLength(), "No segments on an unavailable section");
                Check.Equal(1, TrainingTestKit.Count(overview.GetProperty("recentSaves")), "Other providers still answer");
                Check.True(!overview.GetProperty("firstRun").GetBoolean(), "Never first run while a provider failed");
                lock (hub.Providers) hub.Providers.RemoveAll(p => p is LibraryTrainingProvider || p is EventsTrainingProvider || p is RankTrainingProvider);
                overview = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(!TrainingTestKit.Available(overview, "Overview"), "Nothing available, yet a failure keeps the request successful");
                lock (hub.Providers) hub.Providers.Remove(failing);
                await Api.ExpectErrorAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            }
            finally
            {
                lock (hub.Providers)
                {
                    hub.Providers.Remove(failing);
                    if (!hub.Providers.Any(p => p is LibraryTrainingProvider)) hub.Providers.Insert(0, new LibraryTrainingProvider(f.Services));
                    if (!hub.Providers.Any(p => p is EventsTrainingProvider)) hub.Providers.Add(new EventsTrainingProvider(f.Services));
                    if (!hub.Providers.Any(p => p is RankTrainingProvider)) hub.Providers.Add(new RankTrainingProvider(f.Services));
                }
            }
            JsonElement recovered = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Available(recovered, "Library"), "Recovered");
        }

        private static async Task KillSwitchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/training", HttpFixture.AdminToken, new { killed = true, reason = "Incident" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult blocked = await Api.SendAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)503, blocked.Status, "Kill switch (" + blocked.Text + ")");
            Check.Equal("FeatureDisabled", blocked.Error, "FeatureDisabled");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/saves", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/training", HttpFixture.AdminToken, new { killed = false, reason = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task RankCardAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            f.Settings.Ranks.Enabled = false;
            JsonElement off = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(off.GetProperty("rank").ValueKind == JsonValueKind.Null, "No rank card while ranks.enabled is off");
            RankTestKit.Enable(f);
            JsonElement none = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(none.GetProperty("rank").ValueKind == JsonValueKind.Object, "The card is present for a Rank field");
            Check.True(none.GetProperty("rank").GetProperty("rank").GetProperty("optionKey").ValueKind == JsonValueKind.Null, "No rank held yet");
            Check.True(!TrainingTestKit.Available(none, "Progress"), "No Progress without a rank");
            await RankTestKit.SetProfileRankAsync(f, member, "Beginner", 1, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/rank", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement overview = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement card = overview.GetProperty("rank");
            Check.True(card.ValueKind == JsonValueKind.Object, "Rank card present");
            Check.Equal("Beginner", Api.Str(card.GetProperty("rank"), "optionKey"), "Effective rank");
            Check.Equal(Api.Str(mine.GetProperty("fields")[0].GetProperty("next"), "optionKey"), Api.Str(card.GetProperty("next"), "optionKey"), "Next requirements follow GET /me/rank");
            Check.True(card.TryGetProperty("curriculum", out JsonElement curriculum) && curriculum.ValueKind == JsonValueKind.Null, "No curriculum published");
            Check.Equal(mine.GetProperty("showVerificationLabels").GetBoolean(), card.GetProperty("showVerificationLabels").GetBoolean(), "Verification labels setting");
            Check.True(TrainingTestKit.Available(overview, "Progress"), "A rank lights Progress");
        }

        private static async Task CoachCardAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            TrainingTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement before = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!before.GetProperty("coach").GetProperty("available").GetBoolean(), "No assistant yet");
            await AiTestKit.ReadyAsync(f, model, ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement coach = after.GetProperty("coach");
            Check.True(coach.GetProperty("available").GetBoolean(), "An assistant serves the member");
            Check.Equal(Api.Str(property.GetProperty("features"), "aiPlacement"), Api.Str(coach, "placement"), "Placement follows features.aiPlacement");
            f.Settings.Ai.Enabled = false;
            JsonElement off = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!off.GetProperty("coach").GetProperty("available").GetBoolean(), "ai.enabled off hides the coach (the features.ai rule)");
            f.Settings.Ai.Enabled = true;
        }

        private static async Task PropertyIsolationAsync(HttpFixture f, CancellationToken ct)
        {
            TrainingTestKit.Relax(f);
            string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
            string b = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
            f.PropertyId = TestProperty.SampleId;
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember inA = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await TrainingTestKit.LessonAsync(f, admin, "Isolated lesson", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/saves/Lesson/" + lesson, inA.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement a = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, inA.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, TrainingTestKit.Count(a.GetProperty("recentSaves")), "A sees its save");
            f.PropertyId = b;
            ApiResult crossed = await Api.SendAsync(f, HttpMethod.Get, _Overview, inA.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, crossed.Status, "A's token is refused in B");
            TestMember inB = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement other = await Api.ExpectAsync(f, HttpMethod.Get, _Overview, inB.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(other.GetProperty("firstRun").GetBoolean(), "B's member starts fresh");
            Check.Equal(0, TrainingTestKit.Count(other.GetProperty("recentSaves")), "No saves cross properties");
            Check.Equal(0, TrainingTestKit.Count(other.GetProperty("newForYou")), "No lessons cross properties");
            DatabaseDriverBase driverB = f.Raw.ForProperty(b);
            Check.Equal(0L, await driverB.Favorites.CountForUserAsync(inA.Id, ct).ConfigureAwait(false), "B's driver counts none of A's saves");
            f.PropertyId = TestProperty.SampleId;
        }

        #endregion
    }
}
