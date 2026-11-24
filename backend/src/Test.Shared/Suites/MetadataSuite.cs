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
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Labels and tags (docs/ENUMERATION_AND_METADATA.md section 1): the generic GET, PUT, and PATCH routes, staff-only
    /// entries, limits and allowed characters, de-duplication, admin bulk changes, discovery enumerations, the word
    /// filter hook, mutes, labels and tags on Phase 1 create and update routes, and deletion.
    /// </summary>
    public static class MetadataSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Metadata";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Labels and tags (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Routes.OwnerAndOthers", "The owner edits their own public metadata (PUT replaces, PATCH changes); other members read it but get 403 on writes", async (f, ct) =>
                {
                    TestMember owner = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    string path = "/api/v1/metadata/User/" + owner.Id;
                    JsonElement put = await Api.ExpectAsync(f, HttpMethod.Put, path, owner.AccessToken, new { labels = new[] { "Topics", "Passing" }, tags = new Dictionary<string, string> { { "level", "beginner" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("User", Api.Str(put, "targetType"), "Target type");
                    Check.Equal(owner.Id, Api.Str(put, "targetId"), "Target id");
                    Check.True(Labels(put).SequenceEqual(new[] { "Topics", "Passing" }), "Labels stored");
                    JsonElement tagsOnly = await Api.ExpectAsync(f, HttpMethod.Put, path, owner.AccessToken, new { tags = new Dictionary<string, string> { { "level", "advanced" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(tagsOnly).SequenceEqual(new[] { "Topics", "Passing" }), "Omitted labels are unchanged");
                    Check.Equal("advanced", Api.Str(tagsOnly.GetProperty("tags"), "level"), "Tags replaced");
                    JsonElement patched = await Api.ExpectAsync(f, HttpMethod.Patch, path, owner.AccessToken,
                        new { addLabels = new[] { "Updates", "topics" }, removeLabels = new[] { "PASSING" }, setTags = new Dictionary<string, string> { { "gym", "north" } }, removeTagKeys = new[] { "LEVEL" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(patched).SequenceEqual(new[] { "Topics", "Updates" }), "PATCH adds and removes case-insensitively");
                    Check.True(patched.GetProperty("tags").EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "gym" }), "PATCH sets and removes tag keys");
                    JsonElement seen = await Api.ExpectAsync(f, HttpMethod.Get, path, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(seen).SequenceEqual(new[] { "Topics", "Updates" }), "Other members read a member's public metadata");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, path, other.AccessToken, new { labels = new[] { "nope" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Patch, path, other.AccessToken, new { addLabels = new[] { "nope" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/metadata/User/usr_missing", owner.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/metadata/Spaceship/" + owner.Id, owner.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/metadata/3/" + owner.Id, owner.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement code = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Staff only", grantType = "Lifetime" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/metadata/SignupCode/" + Api.Str(code, "id"), owner.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/metadata/SignupCode/" + Api.Str(code, "id"), owner.AccessToken, new { labels = new[] { "x" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/SignupCode/" + Api.Str(code, "id"), HttpFixture.AdminToken, new { labels = new[] { "x" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", owner.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(me).SequenceEqual(new[] { "Topics", "Updates" }) && Api.Str(me.GetProperty("tags"), "gym") == "north", "Me carries labels and tags");
                }),
                Case("Routes.Phase4And5Types", "The generic routes serve Event, Lesson, Series, and Idea metadata: owners (the idea author) edit their own, the matching staff role edits the type, other members get 404 on reads and 403 on writes", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember author = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    f.Settings.Ideas.MemberSubmissions = IdeaSubmissionModeEnum.Direct;
                    string idea = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/ideas", author.AccessToken, new { title = "Metadata idea", body = "Body" }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
                    JsonElement own = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/Idea/" + idea, author.AccessToken, new { labels = new[] { "mine" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(own).SequenceEqual(new[] { "mine" }), "The idea author edits its metadata");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/metadata/Idea/" + idea, other.AccessToken, new { labels = new[] { "nope" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    TestMember manager = await EventTestKit.ManagerAsync(f, ct).ConfigureAwait(false);
                    string evt = await EventTestKit.PublishedAsync(f, manager.AccessToken, "Metadata event", ct).ConfigureAwait(false);
                    JsonElement staffEvent = await Api.ExpectAsync(f, HttpMethod.Patch, "/api/v1/metadata/Event/" + evt, manager.AccessToken, new { addLabels = new[] { "featured" }, setTags = new Dictionary<string, string> { { "staff:source", "test" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(staffEvent).SequenceEqual(new[] { "featured" }) && Api.Str(staffEvent.GetProperty("tags"), "staff:source") == "test", "An event manager edits event metadata, staff entries included");
                    JsonElement readEvent = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/metadata/Event/" + evt, manager.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(readEvent).SequenceEqual(new[] { "featured" }), "GET Event metadata");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/metadata/Event/" + evt, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/metadata/Event/" + evt, other.AccessToken, new { labels = new[] { "nope" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    LibraryTestKit.Relax(f);
                    string series = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Metadata series", ct).ConfigureAwait(false), "id");
                    string lesson = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, series, "Metadata lesson", ct).ConfigureAwait(false), "id");
                    TestMember editor = await LibraryTestKit.EditorAsync(f, ct).ConfigureAwait(false);
                    JsonElement lessonMeta = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/Lesson/" + lesson, editor.AccessToken, new { labels = new[] { "fundamentals" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(lessonMeta).SequenceEqual(new[] { "fundamentals" }), "A library editor edits lesson metadata");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/Series/" + series, editor.AccessToken, new { labels = new[] { "course" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/metadata/Lesson/" + lesson, manager.AccessToken, new { labels = new[] { "nope" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/metadata/Lesson/les_missing", admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),
                Case("Staff.Entries", "Staff edit and see staff: entries; non-staff never see them, their staff: writes are ignored, and stored staff entries survive their replace", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    string path = "/api/v1/metadata/User/" + member.Id;
                    await Api.ExpectAsync(f, HttpMethod.Put, path, admin.AccessToken, new { labels = new[] { "staff:watch", "public" }, tags = new Dictionary<string, string> { { "staff:note", "check id" }, { "level", "intermediate" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement own = await Api.ExpectAsync(f, HttpMethod.Get, path, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(own).SequenceEqual(new[] { "public" }) && !own.GetProperty("tags").TryGetProperty("staff:note", out JsonElement _), "Owner does not see staff entries");
                    JsonElement replaced = await Api.ExpectAsync(f, HttpMethod.Put, path, member.AccessToken, new { labels = new[] { "mine", "staff:forged" }, tags = new Dictionary<string, string> { { "staff:note", "cleared" }, { "level", "advanced" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(replaced).SequenceEqual(new[] { "mine" }), "A member's staff: label is ignored");
                    Check.Equal("advanced", Api.Str(replaced.GetProperty("tags"), "level"), "Public tag replaced");
                    JsonElement staffView = await Api.ExpectAsync(f, HttpMethod.Get, path, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(staffView).OrderBy(l => l, StringComparer.Ordinal).SequenceEqual(new[] { "mine", "staff:watch" }), "Staff entries survive the member's replace and the forged one was not stored");
                    Check.Equal("check id", Api.Str(staffView.GetProperty("tags"), "staff:note"), "Staff tag unchanged by the member");
                    await Api.ExpectAsync(f, HttpMethod.Patch, path, member.AccessToken, new { removeLabels = new[] { "staff:watch" }, removeTagKeys = new[] { "staff:note" }, addLabels = new[] { "staff:sneaky" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    staffView = await Api.ExpectAsync(f, HttpMethod.Get, path, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(staffView).Contains("staff:watch") && !Labels(staffView).Contains("staff:sneaky") && staffView.GetProperty("tags").TryGetProperty("staff:note", out JsonElement _), "PATCH by a member cannot touch staff entries");

                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!JsonText(me).Contains("staff:", StringComparison.Ordinal), "Me never carries staff entries");
                    JsonElement profile = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { bio = "hello" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!JsonText(profile).Contains("staff:", StringComparison.Ordinal), "Profile update response never carries staff entries");
                    JsonElement labels = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/labels/enumerate", member.AccessToken, new { targetType = "User" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!JsonText(labels).Contains("staff:", StringComparison.Ordinal), "Discovery never shows staff labels to members");
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members/" + member.Id, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(detail).Contains("staff:watch"), "The admin member detail shows staff entries");
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members?labels=staff:watch", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(member.Id, Api.Str(list.GetProperty("objects")[0], "id"), "Staff filter on staff labels");
                    JsonElement adminMe = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(adminMe.GetProperty("labels").ValueKind == JsonValueKind.Array, "Admin Me carries labels");
                }),
                Case("Limits.AndCharacters", "50 labels, 50 tags, 64-character keys, 512-character values, allowed characters, and case-insensitive de-duplication keeping the first casing", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    string path = "/api/v1/metadata/User/" + member.Id;
                    await Api.ExpectAsync(f, HttpMethod.Put, path, member.AccessToken, new { labels = Enumerable.Range(0, 50).Select(i => "l" + i).ToArray(), tags = Enumerable.Range(0, 50).ToDictionary(i => "k" + i, i => "v") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Invalid(f, path, member, new { labels = Enumerable.Range(0, 51).Select(i => "l" + i).ToArray() }, "labels", ct).ConfigureAwait(false);
                    await Invalid(f, path, member, new { tags = Enumerable.Range(0, 51).ToDictionary(i => "k" + i, i => "v") }, "tags", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, path, member.AccessToken, new { labels = new[] { new string('a', 64) }, tags = new Dictionary<string, string> { { new string('k', 64), new string('v', 512) } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Invalid(f, path, member, new { labels = new[] { new string('a', 65) } }, "labels[0]", ct).ConfigureAwait(false);
                    await Invalid(f, path, member, new { tags = new Dictionary<string, string> { { new string('k', 65), "v" } } }, "tags." + new string('k', 65), ct).ConfigureAwait(false);
                    await Invalid(f, path, member, new { tags = new Dictionary<string, string> { { "k", new string('v', 513) } } }, "tags.k", ct).ConfigureAwait(false);
                    await Invalid(f, path, member, new { labels = new[] { "ok", "no!" } }, "labels[1]", ct).ConfigureAwait(false);
                    await Invalid(f, path, member, new { labels = new[] { "  " } }, "labels[0]", ct).ConfigureAwait(false);
                    await Invalid(f, path, member, new { tags = new Dictionary<string, string> { { "Level", "a" }, { "level", "b" } } }, "tags.level", ct).ConfigureAwait(false);
                    JsonElement chars = await Api.ExpectAsync(f, HttpMethod.Put, path, member.AccessToken, new { labels = new[] { " Half Topics ", "half topics", "HALF TOPICS", "x-ray_1.2:3/4", "Ünïcode" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(chars).SequenceEqual(new[] { "Half Topics", "x-ray_1.2:3/4", "Ünïcode" }), "Trimmed, de-duplicated case-insensitively, first casing kept, allowed characters accepted");
                    JsonElement add = await Api.ExpectAsync(f, HttpMethod.Patch, path, member.AccessToken, new { addLabels = new[] { "HALF TOPICS" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(add).Count(l => l.Equals("half topics", StringComparison.OrdinalIgnoreCase)) == 1 && Labels(add).Contains("Half Topics"), "PATCH add keeps the stored casing");
                    JsonElement empty = await Api.ExpectAsync(f, HttpMethod.Put, path, member.AccessToken, new { labels = Array.Empty<string>(), tags = new Dictionary<string, string>() }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(empty.GetProperty("labels").GetArrayLength() == 0 && empty.GetProperty("tags").EnumerateObject().Count() == 0, "Empty lists clear and are present as [] and {}");
                }),
                Case("Regression.Metadata.StaffLimitSeparate", "staff: labels and tags have their own 50 limit: a member with 50 public entries beside hidden staff entries saves without a limit error, and staff can add 50 staff: entries", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    string path = "/api/v1/metadata/User/" + member.Id;
                    await Api.ExpectAsync(f, HttpMethod.Put, path, HttpFixture.AdminToken, new
                    {
                        labels = Enumerable.Range(0, 50).Select(i => "staff:l" + i).ToArray(),
                        tags = Enumerable.Range(0, 50).ToDictionary(i => "staff:k" + i, i => "v")
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Put, path, member.AccessToken, new
                    {
                        labels = Enumerable.Range(0, 50).Select(i => "l" + i).ToArray(),
                        tags = Enumerable.Range(0, 50).ToDictionary(i => "k" + i, i => "v")
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(50, saved.GetProperty("labels").GetArrayLength(), "Member sees their 50 labels");
                    JsonElement staffView = await Api.ExpectAsync(f, HttpMethod.Get, path, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(100, staffView.GetProperty("labels").GetArrayLength(), "50 public plus 50 staff labels stored");
                    Check.Equal(100, staffView.GetProperty("tags").EnumerateObject().Count(), "50 public plus 50 staff tags stored");
                    await Invalid(f, path, member, new { labels = Enumerable.Range(0, 51).Select(i => "l" + i).ToArray() }, "labels", ct).ConfigureAwait(false);
                    ApiResult tooManyStaff = await Api.SendAsync(f, HttpMethod.Patch, path, HttpFixture.AdminToken, new { addLabels = new[] { "staff:extra" } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, tooManyStaff.Status, "A 51st staff: label is refused (body: " + tooManyStaff.Text + ")");
                }),
                Case("Regression.Metadata.DiscoveryTagsKey", "A tag filter on a discovery enumeration is 400 keyed under tags, and a label filter under labels", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    ApiResult tags = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/labels/enumerate", member.AccessToken, new { tags = new Dictionary<string, string> { { "level", "*" } } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, tags.Status, "Tags status");
                    JsonElement tagErrors = tags.Body.GetProperty("fieldErrors");
                    Check.True(tagErrors.TryGetProperty("tags", out JsonElement _) && !tagErrors.TryGetProperty("labels", out JsonElement _), "Keyed under tags: " + tags.Text);
                    ApiResult labels = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/tags/keys/enumerate", member.AccessToken, new { labels = new[] { "topics" } }, ct).ConfigureAwait(false);
                    Check.True(labels.Body.GetProperty("fieldErrors").TryGetProperty("labels", out JsonElement _), "Keyed under labels: " + labels.Text);
                }),
                Case("Bulk.Admin", "Admin bulk changes up to 500 objects of one type and reports unknown ids; members are refused", async (f, ct) =>
                {
                    TestMember a = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + b.Id, b.AccessToken, new { labels = new[] { "old" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement result = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/metadata/bulk", HttpFixture.AdminToken,
                        new { targetType = "User", targetIds = new[] { a.Id, b.Id, b.Id, "usr_missing" }, addLabels = new[] { "cohort-1", "staff:reviewed" }, removeLabels = new[] { "old" }, setTags = new Dictionary<string, string> { { "batch", "7" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, result.GetProperty("updated").GetInt32(), "Updated (duplicates counted once)");
                    Check.True(result.GetProperty("notFound").EnumerateArray().Select(n => n.GetString()).SequenceEqual(new[] { "usr_missing" }), "Not found ids");
                    JsonElement bView = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/metadata/User/" + b.Id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(bView).OrderBy(l => l, StringComparer.Ordinal).SequenceEqual(new[] { "cohort-1", "staff:reviewed" }) && Api.Str(bView.GetProperty("tags"), "batch") == "7", "Bulk applied");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/metadata/bulk", HttpFixture.AdminToken, new { targetType = "User", targetIds = Enumerable.Range(0, 501).Select(i => "usr_" + i).ToArray(), addLabels = new[] { "x" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/metadata/bulk", HttpFixture.AdminToken, new { targetType = "User", targetIds = Array.Empty<string>(), addLabels = new[] { "x" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement full = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/metadata/bulk", HttpFixture.AdminToken, new { targetType = "User", targetIds = Enumerable.Range(0, 500).Select(i => "usr_none" + i).ToArray(), addLabels = new[] { "x" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(500, full.GetProperty("notFound").GetArrayLength(), "500 accepted");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/metadata/bulk", a.AccessToken, new { targetType = "User", targetIds = new[] { a.Id }, addLabels = new[] { "x" } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/metadata/bulk", HttpFixture.AdminToken, new { targetType = "User", targetIds = new[] { a.Id }, addLabels = new[] { "bad!" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Case("Discovery.CountsPrefixPaging", "Label, tag key, and tag value enumerations count uses, filter by prefix and type, page by token, and hide staff entries and invisible objects from members", async (f, ct) =>
                {
                    TestMember viewer = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    List<TestMember> members = new List<TestMember>();
                    for (int i = 0; i < 3; i++) members.Add(await Api.MemberAsync(f, ct).ConfigureAwait(false));
                    await Put(f, members[0], new[] { "topics", "passing" }, new Dictionary<string, string> { { "level", "beginner" }, { "gym", "north" } }, ct).ConfigureAwait(false);
                    await Put(f, members[1], new[] { "Topics", "news" }, new Dictionary<string, string> { { "level", "beginner" } }, ct).ConfigureAwait(false);
                    await Put(f, members[2], new[] { "topics" }, new Dictionary<string, string> { { "level", "advanced" } }, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + members[2].Id, HttpFixture.AdminToken, new { labels = new[] { "topics", "staff:flagged" }, tags = new Dictionary<string, string> { { "level", "advanced" }, { "staff:queue", "a" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement code = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Hidden", grantType = "Lifetime", labels = new[] { "topics", "secretcode" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(Labels(code).Contains("secretcode"), "Code created with labels");

                    JsonElement labels = await Discover(f, "/api/v1/labels/enumerate", viewer.AccessToken, new Dictionary<string, object?>(), ct).ConfigureAwait(false);
                    List<string> names = labels.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "label")).ToList();
                    Check.Equal("topics", names[0].ToLowerInvariant(), "Most used first");
                    Check.Equal(3L, labels.GetProperty("objects")[0].GetProperty("count").GetInt64(), "Counts only objects members can see (not the sign-up code)");
                    Check.True(!names.Contains("secretcode") && !names.Any(n => n.StartsWith("staff:", StringComparison.Ordinal)), "Invisible objects and staff labels are hidden");
                    JsonElement staffLabels = await Discover(f, "/api/v1/labels/enumerate", HttpFixture.AdminToken, new Dictionary<string, object?>(), ct).ConfigureAwait(false);
                    List<string> staffNames = staffLabels.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "label")).ToList();
                    Check.True(staffNames.Contains("staff:flagged") && staffNames.Contains("secretcode"), "Staff see everything");
                    Check.Equal(4L, staffLabels.GetProperty("objects")[0].GetProperty("count").GetInt64(), "Staff counts include every type");
                    JsonElement typed = await Discover(f, "/api/v1/labels/enumerate", HttpFixture.AdminToken, new Dictionary<string, object?> { { "targetType", "SignupCode" } }, ct).ConfigureAwait(false);
                    Check.Equal(2L, typed.GetProperty("totalRecords").GetInt64(), "Type scoped");
                    JsonElement prefixed = await Discover(f, "/api/v1/labels/enumerate", viewer.AccessToken, new Dictionary<string, object?> { { "prefix", "NE" } }, ct).ConfigureAwait(false);
                    Check.True(prefixed.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "label")).SequenceEqual(new[] { "news" }), "Prefix (case-insensitive)");

                    List<string> paged = new List<string>();
                    Dictionary<string, object?> body = new Dictionary<string, object?> { { "maxResults", 1 }, { "targetType", "User" } };
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        JsonElement page = await Discover(f, "/api/v1/labels/enumerate", viewer.AccessToken, body, ct).ConfigureAwait(false);
                        paged.AddRange(page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "label").ToLowerInvariant()));
                        if (page.GetProperty("continuationToken").ValueKind != JsonValueKind.String) break;
                        body["continuationToken"] = page.GetProperty("continuationToken").GetString();
                    }
                    Check.True(paged.SequenceEqual(new[] { "topics", "news", "passing" }), "Paging by token, most used first then alphabetical");

                    JsonElement keys = await Discover(f, "/api/v1/tags/keys/enumerate", viewer.AccessToken, new Dictionary<string, object?> { { "targetType", "User" } }, ct).ConfigureAwait(false);
                    Check.True(keys.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "key") + "=" + o.GetProperty("count").GetInt64()).SequenceEqual(new[] { "level=3", "gym=1" }), "Tag keys with counts, no staff keys");
                    JsonElement values = await Discover(f, "/api/v1/tags/values/enumerate", viewer.AccessToken, new Dictionary<string, object?> { { "key", "LEVEL" } }, ct).ConfigureAwait(false);
                    Check.True(values.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "value") + "=" + o.GetProperty("count").GetInt64()).SequenceEqual(new[] { "beginner=2", "advanced=1" }), "Tag values with counts");
                    JsonElement valuePrefix = await Discover(f, "/api/v1/tags/values/enumerate", viewer.AccessToken, new Dictionary<string, object?> { { "key", "level" }, { "prefix", "adv" } }, ct).ConfigureAwait(false);
                    Check.Equal(1, valuePrefix.GetProperty("objects").GetArrayLength(), "Value prefix");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tags/values/enumerate", viewer.AccessToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/tags/values/enumerate", viewer.AccessToken, new { key = "staff:queue" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement staffValues = await Discover(f, "/api/v1/tags/values/enumerate", HttpFixture.AdminToken, new Dictionary<string, object?> { { "key", "staff:queue" } }, ct).ConfigureAwait(false);
                    Check.Equal(1, staffValues.GetProperty("objects").GetArrayLength(), "Staff read staff tag values");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/labels/enumerate", viewer.AccessToken, new { targetType = "Nope" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", members[1].AccessToken, new { confirm = "DELETE" }, (HttpStatusCode)202, ct).ConfigureAwait(false);
                    JsonElement afterDelete = await Discover(f, "/api/v1/labels/enumerate", viewer.AccessToken, new Dictionary<string, object?> { { "targetType", "User" } }, ct).ConfigureAwait(false);
                    Check.True(!afterDelete.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "label") == "news"), "A deleted member's labels are gone from discovery");
                }),
                Case("TextFilter.Hook", "The word filter hook sees member labels and tag values (and can block them) and is never called for staff", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    List<KeyValuePair<string, string>> seen = new List<KeyValuePair<string, string>>();
                    f.Services.Metadata.TextFilter = (actor, type, fields, token) =>
                    {
                        lock (seen) seen.AddRange(fields);
                        if (fields.Any(p => p.Value.Contains("badword", StringComparison.OrdinalIgnoreCase))) throw new ApiErrorException(400, "ContentFiltered", "Blocked.");
                        return Task.CompletedTask;
                    };
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, member.AccessToken, new { labels = new[] { "friendly" }, tags = new Dictionary<string, string> { { "motto", "keep going" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(seen.Any(p => p.Key == "labels[0]" && p.Value == "friendly"), "Labels filtered");
                    Check.True(seen.Any(p => p.Key == "tags.motto" && p.Value.Contains("keep going", StringComparison.Ordinal)), "Tag values filtered");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, member.AccessToken, new { labels = new[] { "badword" } }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Patch, "/api/v1/metadata/User/" + member.Id, member.AccessToken, new { setTags = new Dictionary<string, string> { { "motto", "BadWord here" } } }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { labels = new[] { "badword" } }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
                    JsonElement kept = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/metadata/User/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(kept).SequenceEqual(new[] { "friendly" }), "Blocked writes change nothing");
                    int before = seen.Count;
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, HttpFixture.AdminToken, new { labels = new[] { "badword" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Filtered", grantType = "Lifetime", labels = new[] { "badword" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal(before, seen.Count, "Never called for staff");
                    f.Services.Metadata.TextFilter = null;
                }),
                Case("Muted.CannotEdit", "A muted member cannot change metadata through the generic routes or the profile, and staff still can", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/mute", HttpFixture.AdminToken, new { untilUtc = (DateTime?)null, reason = "cool off" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, member.AccessToken, new { labels = new[] { "x" } }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Patch, "/api/v1/metadata/User/" + member.Id, member.AccessToken, new { addLabels = new[] { "x" } }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { tags = new Dictionary<string, string> { { "a", "b" } } }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { city = "still allowed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/metadata/User/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, HttpFixture.AdminToken, new { labels = new[] { "staff:muted" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("CreateUpdate.Routes", "Profile update, sign-up code create, update, and batch, and comp grants accept labels and tags; every payload carries them", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { bio = "x", labels = new[] { "Competitor" }, tags = new Dictionary<string, string> { { "weight", "light" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(me).SequenceEqual(new[] { "Competitor" }) && Api.Str(me.GetProperty("tags"), "weight") == "light", "Profile update stores labels and tags");
                    JsonElement unchanged = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { bio = "y" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(unchanged).SequenceEqual(new[] { "Competitor" }), "Omitted labels are unchanged");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { labels = new[] { "bad!" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

                    JsonElement code = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Labeled", grantType = "Lifetime", labels = new[] { "spring", "staff:vip" }, tags = new Dictionary<string, string> { { "channel", "email" } } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(Labels(code).SequenceEqual(new[] { "spring", "staff:vip" }) && Api.Str(code.GetProperty("tags"), "channel") == "email", "Code create");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Bad", grantType = "Lifetime", labels = new[] { "bad!" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/signup-codes/" + Api.Str(code, "id"), HttpFixture.AdminToken, new { labels = new[] { "summer" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(updated).SequenceEqual(new[] { "summer" }) && Api.Str(updated.GetProperty("tags"), "channel") == "email", "Code update replaces labels and keeps tags");
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + Api.Str(code, "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(detail).SequenceEqual(new[] { "summer" }), "Code detail");
                    JsonElement batch = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes/batches", HttpFixture.AdminToken, new { name = "Batch", quantity = 3, grantType = "Lifetime", labels = new[] { "event-a" }, tags = new Dictionary<string, string> { { "event", "a" } } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(Labels(batch).SequenceEqual(new[] { "event-a" }) && batch.GetProperty("codes").EnumerateArray().All(c => Labels(c).SequenceEqual(new[] { "event-a" })), "Batch and codes carry the labels");
                    JsonElement batchMeta = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/metadata/SignupCodeBatch/" + Api.Str(batch, "batchId"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("a", Api.Str(batchMeta.GetProperty("tags"), "event"), "Batch metadata stored");
                    JsonElement byLabel = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes?labels=event-a", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, byLabel.GetProperty("totalRecords").GetInt64(), "Batch codes filter by label");

                    JsonElement granted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/comp", HttpFixture.AdminToken, new { grantType = "DurationMonths", months = 2, reason = "Event", labels = new[] { "promo" }, tags = new Dictionary<string, string> { { "staff:approver", "lead" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement comp = granted.GetProperty("comps").GetProperty("objects").EnumerateArray().First(c => Labels(c).Contains("promo"));
                    Check.Equal("lead", Api.Str(comp.GetProperty("tags"), "staff:approver"), "Comp metadata (staff view)");
                    JsonElement history = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership/history", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement promo = history.GetProperty("objects").EnumerateArray().First(h => Labels(h).Contains("promo"));
                    Check.True(!promo.GetProperty("tags").TryGetProperty("staff:approver", out JsonElement _), "The member sees the comp's public labels only");
                    foreach (string list in new[] { "subscriptions", "redemptions", "comps", "sessions", "membership-history" })
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members/" + member.Id + "/" + list, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(page.GetProperty("objects").EnumerateArray().All(o => o.GetProperty("labels").ValueKind == JsonValueKind.Array && o.GetProperty("tags").ValueKind == JsonValueKind.Object), list + " items carry labels and tags");
                        JsonElement preview = (await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members/" + member.Id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty(list == "membership-history" ? "membershipHistory" : list);
                        Check.Equal("/api/v1/admin/members/" + member.Id + "/" + list + "/enumerate", Api.Str(preview, "enumerate"), list + " preview path");
                        Check.Equal(page.GetProperty("totalRecords").GetInt64(), preview.GetProperty("totalRecords").GetInt64(), list + " preview total");
                        JsonElement posted = await Api.ExpectAsync(f, HttpMethod.Post, Api.Str(preview, "enumerate"), HttpFixture.AdminToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(posted.GetProperty("objects").GetArrayLength() <= 1, list + " POST enumerate");
                    }
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(audit.GetProperty("objects").EnumerateArray().All(o => o.TryGetProperty("labels", out JsonElement _) && o.TryGetProperty("tags", out JsonElement _)), "Audit records carry labels and tags");
                    JsonElement subscriptions = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/subscriptions", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(subscriptions.GetProperty("objects").ValueKind == JsonValueKind.Array, "Subscription list");
                }),
                Case("Delete.RemovesMetadata", "Request history delete, bulk delete, and pruning delete labels and tags in the same transaction; account deletion keeps only staff entries", async (f, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    List<RequestHistoryEntry> entries = new List<RequestHistoryEntry>();
                    for (int i = 0; i < 4; i++)
                    {
                        RequestHistoryEntry entry = new RequestHistoryEntry { Id = IdGenerator.Generate("req_"), Method = "GET", Path = "/meta/" + i, Url = "/meta/" + i, StatusCode = i == 1 ? 418 : 200, CreatedUtc = i == 2 ? now.AddDays(-400) : now };
                        await f.Database.RequestHistory.CreateAsync(entry, ct).ConfigureAwait(false);
                        await f.Database.Metadata.WriteAsync(TargetTypeEnum.RequestHistory, entry.Id, new ObjectMetadata { Labels = new List<string> { "keep-an-eye" }, Tags = new Dictionary<string, string> { { "n", i.ToString(System.Globalization.CultureInfo.InvariantCulture) } } }, ct).ConfigureAwait(false);
                        entries.Add(entry);
                    }
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history/" + entries[3].Id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Labels(read).SequenceEqual(new[] { "keep-an-eye" }), "Entry carries labels");
                    JsonElement listed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history?labels=keep-an-eye", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4L, listed.GetProperty("totalRecords").GetInt64(), "List filters by label");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/request-history/" + entries[0].Id, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/request-history?statusCode=418", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await f.Database.RequestHistory.PruneAsync(now.AddDays(-300), ct).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        ObjectMetadata gone = await f.Database.Metadata.ReadAsync(TargetTypeEnum.RequestHistory, entries[i].Id, ct).ConfigureAwait(false);
                        Check.True(gone.Labels.Count == 0 && gone.Tags.Count == 0, "Metadata deleted with entry " + i);
                    }
                    Check.Equal(1, (await f.Database.Metadata.ReadAsync(TargetTypeEnum.RequestHistory, entries[3].Id, ct).ConfigureAwait(false)).Labels.Count, "Other entries keep theirs");

                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, member.AccessToken, new { labels = new[] { "public" }, tags = new Dictionary<string, string> { { "city", "x" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Patch, "/api/v1/metadata/User/" + member.Id, HttpFixture.AdminToken, new { addLabels = new[] { "staff:case-12" }, setTags = new Dictionary<string, string> { { "staff:reason", "fraud" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", member.AccessToken, new { confirm = "DELETE" }, (HttpStatusCode)202, ct).ConfigureAwait(false);
                    ObjectMetadata left = await f.Database.Metadata.ReadAsync(TargetTypeEnum.User, member.Id, ct).ConfigureAwait(false);
                    Check.True(left.Labels.SequenceEqual(new[] { "staff:case-12" }) && left.Tags.Count == 1 && left.Tags.ContainsKey("staff:reason"), "Account deletion removes public metadata and keeps staff entries");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static List<string> Labels(JsonElement element)
        {
            return element.GetProperty("labels").EnumerateArray().Select(l => l.GetString() ?? String.Empty).ToList();
        }

        private static string JsonText(JsonElement element)
        {
            return element.GetRawText();
        }

        private static async Task Invalid(HttpFixture f, string path, TestMember member, object body, string field, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Put, path, member.AccessToken, body, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, result.Status, "PUT " + field + " status (" + result.Text + ")");
            Check.True(result.Body.GetProperty("fieldErrors").TryGetProperty(field, out JsonElement _), "fieldErrors." + field + " (" + result.Text + ")");
        }

        private static Task Put(HttpFixture f, TestMember member, string[] labels, Dictionary<string, string> tags, CancellationToken ct)
        {
            return Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, member.AccessToken, new { labels, tags }, HttpStatusCode.OK, ct);
        }

        private static Task<JsonElement> Discover(HttpFixture f, string path, string bearer, Dictionary<string, object?> body, CancellationToken ct)
        {
            return Api.ExpectAsync(f, HttpMethod.Post, path, bearer, body, HttpStatusCode.OK, ct);
        }

        #endregion
    }
}
