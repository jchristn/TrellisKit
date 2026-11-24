namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 2 word filter (contract section 9.4): normalization and matching, Block and Flag, system reports, the
    /// admin routes (enumeration, create, update, delete, import, test), audit records, the SystemAdmin exemption,
    /// the setting, and the metadata hook.
    /// </summary>
    public static class WordFilterSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "WordFilter";

        #endregion

        #region Private-Members

        private const string _Base = "/api/v1/admin/moderation/word-filter";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Word filter (" + TestEnvironment.Provider + ")", new[]
            {
                Unit("Normalize.Leetspeak", "Substitutions 0=o 1=i 3=e 4=a 5=s 7=t @=a $=s apply on both sides", () =>
                {
                    List<WordFilterTerm> terms = new List<WordFilterTerm> { Term("t1", "shit"), Term("t2", "toast"), Term("t3", "bad") };
                    Check.True(Matches("$h1t happens", terms).Contains("t1"), "$h1t");
                    Check.True(Matches("7045+", terms).Count == 0, "Plus breaks the word");
                    Check.True(Matches("t0@57", terms).Contains("t2"), "t0@57");
                    Check.True(Matches("B4D", terms).Contains("t3"), "Case folded");
                    Check.Equal("bad", WordFilterNormalizer.NormalizeTerm("  B4D "), "Normalized term");
                }),
                Unit("Normalize.Diacritics", "Diacritics, compatibility forms, and zero-width characters are removed", () =>
                {
                    List<WordFilterTerm> terms = new List<WordFilterTerm> { Term("t1", "bad") };
                    Check.True(Matches("so bád", terms).Contains("t1"), "Precomposed acute");
                    Check.True(Matches("so bád", terms).Contains("t1"), "Combining acute");
                    Check.True(Matches("ｂａｄ", terms).Contains("t1"), "Full width");
                    Check.True(Matches("b\u200Ba\u200Dd", terms).Contains("t1"), "Zero-width");
                    Check.True(Matches("b\u00ADad", terms).Contains("t1"), "Soft hyphen");
                    Check.Equal("ss", WordFilterNormalizer.NormalizeTerm("ß"), "Sharp s folds to ss");
                }),
                Unit("Normalize.Repeats", "Runs of the same letter longer than two collapse to two on both sides", () =>
                {
                    List<WordFilterTerm> terms = new List<WordFilterTerm> { Term("t1", "ass", WordFilterMatchTypeEnum.Word), Term("t2", "baaaaad") };
                    Check.True(Matches("you asssssss", terms).Contains("t1"), "asssss collapses to ass");
                    Check.True(Matches("baad", terms).Contains("t2"), "Term collapses too");
                    Check.True(Matches("baaaaaaaaaad", terms).Contains("t2"), "Text collapses");
                    Check.Equal("baad", WordFilterNormalizer.NormalizeTerm("baaaaad"), "Collapsed term");
                    Check.True(Matches("good", new List<WordFilterTerm> { Term("t3", "god") }).Count == 0, "Double letters are kept");
                }),
                Unit("Match.WordAndSubstring", "Word terms need non-alphanumeric neighbors; Substring terms match anywhere", () =>
                {
                    List<WordFilterTerm> word = new List<WordFilterTerm> { Term("w", "ass", WordFilterMatchTypeEnum.Word) };
                    List<WordFilterTerm> sub = new List<WordFilterTerm> { Term("s", "ass", WordFilterMatchTypeEnum.Substring) };
                    Check.True(Matches("first class", word).Count == 0, "class is not the word");
                    Check.True(Matches("ass2", word).Count == 0, "digit neighbor");
                    Check.True(Matches("ass.", word).Contains("w"), "punctuation neighbor");
                    Check.True(Matches("(ass)", word).Contains("w"), "parentheses");
                    Check.True(Matches("first class", sub).Contains("s"), "Substring inside a word");
                    Check.True(Matches("hello bad world", new List<WordFilterTerm> { Term("p", "bad world") }).Contains("p"), "Phrase");
                    Check.True(Matches("hello bad    world", new List<WordFilterTerm> { Term("p", "bad world") }).Contains("p"), "Whitespace runs collapse");
                }),
                Unit("Match.OriginalPositions", "start and length refer to the original text", () =>
                {
                    List<WordFilterTerm> terms = new List<WordFilterTerm> { Term("t", "bad") };
                    WordFilterMatchView m = WordFilterService.Match("Hello B4D world", terms).Single();
                    Check.Equal(6, m.Start, "Start");
                    Check.Equal(3, m.Length, "Length");
                    m = WordFilterService.Match("x b\u200Bád!", terms).Single();
                    Check.Equal(2, m.Start, "Start with invisible characters");
                    Check.Equal(5, m.Length, "Length covers the zero-width and combining characters");
                    m = WordFilterService.Match("😀 baaaad", new List<WordFilterTerm> { Term("t2", "baad") }).Single();
                    Check.Equal(3, m.Start, "After an emoji");
                    Check.Equal(6, m.Length, "Covers the collapsed run");
                    Check.Equal(2, WordFilterService.Match("bad and bad", terms).Count, "Every occurrence");
                }),
                Case("Admin.Crud", "Create, duplicate 409, update, delete, enumeration with filters and orderings, and audit records", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, _Base, admin.AccessToken, new { term = "Bad", matchType = "Word", action = "Block", note = "first", labels = new[] { "profanity" }, tags = new Dictionary<string, string> { { "severity", "high" } } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string id = Api.Str(created, "id");
                    Check.True(id.StartsWith("wft_", StringComparison.Ordinal), "Id prefix");
                    Check.Equal("Admin Person", Api.Str(created, "createdByName"), "createdByName");
                    Check.Equal("profanity", created.GetProperty("labels")[0].GetString(), "labels stored");
                    Check.Equal("high", created.GetProperty("tags").GetProperty("severity").GetString(), "tags stored");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base, admin.AccessToken, new { term = "B4D" }, HttpStatusCode.Conflict, "Conflict", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base, admin.AccessToken, new { term = "  " }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement byToken = await Api.ExpectAsync(f, HttpMethod.Post, _Base, HttpFixture.AdminToken, new { term = "meh", action = "Flag", matchType = "Substring" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("admin token", Api.Str(byToken, "createdByName"), "Admin token name");
                    Check.Equal("Flag", Api.Str(byToken, "action"), "Action");

                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/" + id, admin.AccessToken, new { action = "Flag", note = (string?)null }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Flag", Api.Str(updated, "action"), "Updated action");
                    Check.Equal("Word", Api.Str(updated, "matchType"), "Unchanged match type");
                    Check.True(updated.GetProperty("note").ValueKind == JsonValueKind.Null, "Note cleared");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/" + id, admin.AccessToken, new { term = "MEH" }, HttpStatusCode.Conflict, "Conflict", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/wft_missing", admin.AccessToken, new { action = "Flag" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

                    JsonElement flags = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "?action=Flag&ordering=TermAscending", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Bad,meh", String.Join(",", flags.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "term"))), "Filter and TermAscending");
                    Check.Equal(2L, flags.GetProperty("totalRecords").GetInt64(), "Admin totals");
                    JsonElement substring = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/enumerate", admin.AccessToken, new { filters = new { matchType = "Substring" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, substring.GetProperty("objects").GetArrayLength(), "matchType filter");
                    JsonElement searched = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "?search=ME", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, searched.GetProperty("objects").GetArrayLength(), "Search on term");
                    JsonElement labelled = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "?labels=profanity", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(id, Api.Str(labelled.GetProperty("objects")[0], "id"), "Label filter");
                    JsonElement tagged = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "?tag.severity=*", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, tagged.GetProperty("objects").GetArrayLength(), "Tag filter");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Base + "?action=Nope", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Base + "?ordering=Nope", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement byHits = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "?ordering=HitsDescending&maxResults=1", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, byHits.GetProperty("objects").GetArrayLength(), "HitsDescending");
                    Check.True(byHits.GetProperty("continuationToken").ValueKind == JsonValueKind.String, "Continuation token");

                    TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, _Base, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

                    await Api.ExpectAsync(f, HttpMethod.Delete, _Base + "/" + id, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, _Base + "/" + id, admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    Check.True((await f.Database.Metadata.ReadAsync(TargetTypeEnum.WordFilterTerm, id, ct).ConfigureAwait(false)).Labels.Count == 0, "Metadata deleted with the term");

                    foreach (string action in new[] { "moderation.wordFilter.create", "moderation.wordFilter.update", "moderation.wordFilter.delete" })
                    {
                        EnumerationResult<AuditRecord> audit = await f.Database.Audit.EnumerateAsync(EnumerationNormalizer.Normalize(null, PhaseOneCollections.Audit, f.Settings.Enumeration, true), new AuditFilter { Action = action }, ct).ConfigureAwait(false);
                        Check.True(audit.Objects.Count > 0, "Audit " + action);
                    }
                }),
                Case("Admin.ImportAndTest", "Import skips duplicates and invalid entries; the test route returns positions; both audited", async (f, ct) =>
                {
                    JsonElement first = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/import", HttpFixture.AdminToken,
                        new { terms = new[] { "alpha", "ALPHA", "b3ta", "", new string('x', 101), "gamma" }, matchType = "Substring", action = "Flag" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3, first.GetProperty("added").GetInt32(), "Added");
                    Check.Equal(3, first.GetProperty("skipped").GetInt32(), "Skipped");
                    JsonElement second = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/import", HttpFixture.AdminToken, new { terms = new[] { "beta", "delta" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, second.GetProperty("added").GetInt32(), "Existing normalized term skipped");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/import", HttpFixture.AdminToken, new { terms = Enumerable.Range(0, 1001).Select(i => "t" + i).ToArray() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/import", HttpFixture.AdminToken, new { terms = new string[0] }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/test", HttpFixture.AdminToken, new { text = "An ALPHAbet and d3lta." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement[] matches = test.GetProperty("matches").EnumerateArray().ToArray();
                    Check.Equal(2, matches.Length, "Two matches");
                    Check.Equal("alpha", Api.Str(matches[0], "term"), "Substring term");
                    Check.Equal(3, matches[0].GetProperty("start").GetInt32(), "Start");
                    Check.Equal(5, matches[0].GetProperty("length").GetInt32(), "Length");
                    Check.Equal("Flag", Api.Str(matches[0], "action"), "Action");
                    Check.True(matches[0].TryGetProperty("termId", out JsonElement _), "termId");
                    Check.Equal(16, matches[1].GetProperty("start").GetInt32(), "Word term (Block default) start");
                    f.Settings.Moderation.WordFilterEnabled = false;
                    JsonElement off = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/test", HttpFixture.AdminToken, new { text = "alpha" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, off.GetProperty("matches").GetArrayLength(), "Test runs while the filter is off");
                    EnumerationResult<AuditRecord> audit = await f.Database.Audit.EnumerateAsync(EnumerationNormalizer.Normalize(null, PhaseOneCollections.Audit, f.Settings.Enumeration, true), new AuditFilter { Action = "moderation.wordFilter.import" }, ct).ConfigureAwait(false);
                    Check.Equal(2, audit.Objects.Count, "Import audited");
                }),
                Case("Check.BlockAndFlag", "Block throws ContentFiltered with field errors and records hits; Flag files one system report per target", async (f, ct) =>
                {
                    WordFilterService filter = f.Services.WordFilter;
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base, HttpFixture.AdminToken, new { term = "blocked", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base, HttpFixture.AdminToken, new { term = "flagged", action = "Flag" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    ContentActor actor = await f.Services.Actors.ForUserAsync(member.Id, ct).ConfigureAwait(false);
                    Dictionary<string, string?> fields = new Dictionary<string, string?> { { "title", "fine" }, { "body", "this is bl0cked" }, { "mediaIds[1].altText", "BLOCKED too" } };
                    ApiErrorException error = await Check.ThrowsAsync<ApiErrorException>(() => filter.CheckAsync(actor, TargetTypeEnum.FeedPost, fields, ct), "Block").ConfigureAwait(false);
                    Check.Equal(400, error.StatusCode, "400");
                    Check.Equal("ContentFiltered", error.Error, "ContentFiltered");
                    Check.Equal(WordFilterService.BlockedFieldMessage, error.FieldErrors!["body"], "Body field error");
                    Check.True(error.FieldErrors.ContainsKey("mediaIds[1].altText") && !error.FieldErrors.ContainsKey("title"), "Only blocked fields");
                    Check.True(!error.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase), "Term not echoed");
                    Check.Equal(2L, Count(await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM wordfilterhits WHERE targetid IS NULL AND action = 'Block';", false, ct).ConfigureAwait(false)), "Block hits with null target");

                    WordFilterResult result = await filter.CheckAsync(actor, TargetTypeEnum.Comment, new Dictionary<string, string?> { { "body", "this is flagged, Flagged!" } }, ct).ConfigureAwait(false);
                    Check.Equal(1, result.Flags.Count, "One flag per field and term");
                    Check.True(await filter.RecordFlagsAsync(result, TargetTypeEnum.Comment, "cmt_x1", member.Id, ct).ConfigureAwait(false), "Report filed");
                    Check.True(!await filter.RecordFlagsAsync(result, TargetTypeEnum.Comment, "cmt_x1", member.Id, ct).ConfigureAwait(false), "No duplicate while Open");
                    DataTable reports = await f.Raw.ExecuteQueryAsync("SELECT reporteruserid, reason, status, scopetype, scopeid, targetauthoruserid, dueutc, createdutc FROM reports WHERE targetid = 'cmt_x1';", false, ct).ConfigureAwait(false);
                    Check.Equal(1, reports.Rows.Count, "One system report");
                    DataRow row = reports.Rows[0];
                    Check.True(row["reporteruserid"] == DBNull.Value, "No reporter");
                    Check.Equal("Filter", row["reason"].ToString(), "Reason Filter");
                    Check.Equal("Open", row["status"].ToString(), "Open");
                    Check.Equal("Feed", row["scopetype"].ToString(), "Scope Feed");
                    Check.Equal("*", row["scopeid"].ToString(), "Scope id");
                    Check.Equal(member.Id, row["targetauthoruserid"].ToString(), "Author");
                    Check.Equal(2L, Count(await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM wordfilterhits WHERE targetid = 'cmt_x1';", false, ct).ConfigureAwait(false)), "Flag hits per record call");
                    await f.Raw.ExecuteQueryAsync("UPDATE reports SET status = 'Dismissed' WHERE targetid = 'cmt_x1';", false, ct).ConfigureAwait(false);
                    Check.True(await filter.RecordFlagsAsync(result, TargetTypeEnum.Comment, "cmt_x1", member.Id, ct).ConfigureAwait(false), "New report after the old one closed");

                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "?ordering=HitsDescending", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("flagged", Api.Str(list.GetProperty("objects")[0], "term"), "Most hits first");
                    Check.Equal(3L, list.GetProperty("objects")[0].GetProperty("hitCount30d").GetInt64(), "hitCount30d");

                    Check.True(await filter.IsBlockedAsync("so b-l-o-c-k-e-d? no, BL0CKED", ct).ConfigureAwait(false), "IsBlocked");
                    Check.True(!await filter.IsBlockedAsync("flagged only", ct).ConfigureAwait(false), "Flag terms do not block");
                }),
                Case("Regression.WordFilter.ProfileText", "Display names and bios are screened at registration, on profile edits, and on staff edits of a member's profile: Block is 400 ContentFiltered naming the field; Flag files a system report on the member", async (f, ct) =>
                {
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base, HttpFixture.AdminToken, new { term = "blockword", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base, HttpFixture.AdminToken, new { term = "flagword", action = "Flag" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    ApiResult register = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email = Api.UniqueEmail(), password = Api.Password, displayName = "Mr Blockword", dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, register.Status, "Registration blocked (body: " + register.Text + ")");
                    Check.Equal("ContentFiltered", register.Error, "ContentFiltered at registration");
                    Check.True(register.Body.GetProperty("fieldErrors").TryGetProperty("displayName", out JsonElement _), "fieldErrors.displayName at registration");
                    TestMember flaggedAtSignup = await Api.RegisterAsync(f, ct, null, "Flagword Fan").ConfigureAwait(false);
                    Check.Equal(1L, Count(await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM reports WHERE targettype = 'User' AND targetid = '" + flaggedAtSignup.Id + "' AND reason = 'Filter' AND reporteruserid IS NULL;", false, ct).ConfigureAwait(false)), "Flag at registration files a system report");

                    TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    ApiResult bio = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { bio = "I love BLOCKWORD" }, ct).ConfigureAwait(false);
                    Check.Equal("ContentFiltered", bio.Error, "Bio blocked (body: " + bio.Text + ")");
                    Check.True(bio.Body.GetProperty("fieldErrors").TryGetProperty("bio", out JsonElement _), "fieldErrors.bio");
                    ApiResult name = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { displayName = "Blockword Person" }, ct).ConfigureAwait(false);
                    Check.True(name.Body.GetProperty("fieldErrors").TryGetProperty("displayName", out JsonElement _), "fieldErrors.displayName");
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!Api.Str(me, "displayName").Contains("Blockword", StringComparison.Ordinal) && me.GetProperty("bio").ValueKind == JsonValueKind.Null, "Nothing saved");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { bio = "a flagword here" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, Count(await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM reports WHERE targettype = 'User' AND targetid = '" + member.Id + "' AND reason = 'Filter';", false, ct).ConfigureAwait(false)), "Flag in a bio files a system report");

                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    ContentActor staff = await f.Services.Actors.ForUserAsync(admin.Id, ct).ConfigureAwait(false);
                    ApiErrorException staffEdit = await Check.ThrowsAsync<ApiErrorException>(() => f.Services.Profiles.UpdateAsync(member.Id, new TrellisKit.Core.Requests.ProfileUpdateRequest { Bio = "blockword by staff" },
                        new HashSet<string> { "bio" }, staff, ct), "Staff edit of a member's bio is screened").ConfigureAwait(false);
                    Check.Equal("ContentFiltered", staffEdit.Error, "Staff edit ContentFiltered");
                    await f.Services.Profiles.UpdateAsync(admin.Id, new TrellisKit.Core.Requests.ProfileUpdateRequest { Bio = "blockword is mine" }, new HashSet<string> { "bio" }, staff, ct).ConfigureAwait(false);
                }),
                Case("Check.ExemptAndOff", "SystemAdmin authors are not filtered and wordFilterEnabled = false turns the checks off", async (f, ct) =>
                {
                    WordFilterService filter = f.Services.WordFilter;
                    await Api.ExpectAsync(f, HttpMethod.Post, _Base, HttpFixture.AdminToken, new { term = "nope" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    ContentActor adminActor = await f.Services.Actors.ForUserAsync(admin.Id, ct).ConfigureAwait(false);
                    Dictionary<string, string?> fields = new Dictionary<string, string?> { { "body", "nope" } };
                    WordFilterResult exempt = await filter.CheckAsync(adminActor, TargetTypeEnum.FeedPost, fields, ct).ConfigureAwait(false);
                    Check.True(!exempt.HasFlags, "Admin exempt");
                    TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    ContentActor actor = await f.Services.Actors.ForUserAsync(member.Id, ct).ConfigureAwait(false);
                    await Check.ThrowsAsync<ApiErrorException>(() => filter.CheckAsync(actor, TargetTypeEnum.FeedPost, fields, ct), "Member filtered").ConfigureAwait(false);
                    f.Settings.Moderation.WordFilterEnabled = false;
                    await filter.CheckAsync(actor, TargetTypeEnum.FeedPost, fields, ct).ConfigureAwait(false);
                    Check.True(!await filter.IsBlockedAsync("nope", ct).ConfigureAwait(false), "IsBlocked off");
                    f.Settings.Moderation.WordFilterEnabled = true;
                    ApiErrorException metadata = await Check.ThrowsAsync<ApiErrorException>(() => f.Services.Metadata.ValidateAsync(actor, new List<string> { "fine", "n0pe" }, new Dictionary<string, string> { { "level", "ok" } }, false, ct), "Labels filtered").ConfigureAwait(false);
                    Check.Equal("ContentFiltered", metadata.Error, "Metadata ContentFiltered");
                    Check.True(metadata.FieldErrors!.ContainsKey("labels[1]"), "labels[i] field");
                    ApiErrorException tags = await Check.ThrowsAsync<ApiErrorException>(() => filter.CheckMetadataAsync(actor, TargetTypeEnum.FeedPost, null, new Dictionary<string, string?> { { "level", "nope" } }, ct), "Tags filtered").ConfigureAwait(false);
                    Check.True(tags.FieldErrors!.ContainsKey("tags.level"), "tags.key field");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static WordFilterTerm Term(string id, string term, WordFilterMatchTypeEnum matchType = WordFilterMatchTypeEnum.Word, WordFilterActionEnum action = WordFilterActionEnum.Block)
        {
            return new WordFilterTerm { Id = id, Term = term, NormalizedTerm = WordFilterNormalizer.NormalizeTerm(term), MatchType = matchType, Action = action };
        }

        private static List<string> Matches(string text, List<WordFilterTerm> terms)
        {
            return WordFilterService.Match(text, terms).Select(m => m.TermId).ToList();
        }

        private static long Count(DataTable table)
        {
            return Convert.ToInt64(table.Rows[0]["cnt"], System.Globalization.CultureInfo.InvariantCulture);
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static TestCaseDescriptor Unit(string caseId, string name, Action body)
        {
            return SuiteCases.Unit(Id, caseId, name, ct =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        #endregion
    }
}
