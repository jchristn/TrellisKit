namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Property documents (docs/TRELLISKIT.md section 14): property.json validation; seeding from the property folder and
    /// the folder winning on change; the public read and GET /api/v1/property; acceptance required at email sign-up and
    /// recorded at the social onboarding step; the 403 DocumentAcceptanceRequired gate on member and staff routes (not on
    /// the admin token) and the re-acceptance prompt after an admin publish; admin routes, versions, and acceptances; the
    /// account export; and isolation between two properties with the platform routes.
    /// </summary>
    public static class DocumentSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Documents";

        #endregion

        #region Private-Members

        private const string _Admin = "/api/v1/admin/documents";
        private const string _Required = "participation-agreement";
        private const string _Terms = "terms";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Property documents and acceptances (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Config.Validation", "The sample declares a separately accepted sign-up document; validation rejects bad keys, slugs, paths, duplicates, and misplaced options; files must exist", ct =>
                {
                    PropertyConfiguration sample = TestProperty.LoadSample();
                    PropertyDocument required = sample.Documents!.Single(d => d.Key == _Required);
                    Check.True(required.Acceptance == DocumentAcceptanceModeEnum.RequiredAtSignup && required.SeparateCheckbox && required.ReacceptOnChange && required.IsGating && !required.CoveredByTerms, "Sample RequiredAtSignup document");
                    Check.True(sample.Documents!.Single(d => d.Key == _Terms).CoveredByTerms, "Terms covered by the terms checkbox");
                    sample.Documents!.Add(new PropertyDocument { Key = "bad key", Title = "", File = "../outside.md", Version = "", Slug = "Bad_Slug", SeparateCheckbox = true, ReacceptOnChange = true, Acceptance = DocumentAcceptanceModeEnum.ShownAtPurchase });
                    sample.Documents!.Add(new PropertyDocument { Key = _Terms, Title = "Again", File = "legal/terms-of-service.md", Version = "1.0", Slug = "terms" });
                    List<string> problems = PropertyValidator.Validate(sample);
                    foreach (string expected in new[] { ".key is required", ".title is required", ".file must be a relative path", ".version is required", ".slug is required", ".separateCheckbox applies only", ".reacceptOnChange applies only", "duplicates key terms", "duplicates slug terms" })
                        Check.True(problems.Any(p => p.Contains(expected, StringComparison.Ordinal)), "Problem: " + expected + " in " + String.Join("; ", problems));
                    Check.True(!PropertyLoader.IsSafeDocumentPath("/etc/passwd.md") && !PropertyLoader.IsSafeDocumentPath("legal/../x.md") && !PropertyLoader.IsSafeDocumentPath("legal/x.txt") && PropertyLoader.IsSafeDocumentPath("legal/x.md"), "Document paths");
                    string copy = TestProperty.CopySample();
                    try
                    {
                        File.Delete(Path.Combine(copy, required.File));
                        PropertyConfigurationException? missing = null;
                        try
                        {
                            PropertyLoader.Load(copy);
                        }
                        catch (PropertyConfigurationException e)
                        {
                            missing = e;
                        }
                        Check.True(missing != null && missing.Message.Contains("documents[", StringComparison.Ordinal), "A missing document file refuses to load");
                    }
                    finally
                    {
                        TestProperty.Delete(copy);
                    }
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "Seed.PublicRead", "Every declared document is seeded as version 1 from its file; GET /api/v1/property lists them; the public read serves the content; unknown keys are 404", async (f, ct) =>
                {
                    Check.Equal(f.Property.Documents!.Count, f.Services.Documents.Snapshot.Count, "Every document seeded");
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement documents = property.GetProperty("documents");
                    Check.Equal(f.Property.Documents!.Count, documents.GetArrayLength(), "GET /property documents");
                    JsonElement first = documents[0];
                    Check.Equal(f.Property.Documents![0].Key, Api.Str(first, "key"), "property.json order");
                    Check.Equal("1.0", Api.Str(first, "version"), "Version from property.json");
                    Check.Equal(1, first.GetProperty("sequence").GetInt32(), "Sequence 1");
                    Check.True(first.GetProperty("labels").ValueKind == JsonValueKind.Array && first.GetProperty("tags").ValueKind == JsonValueKind.Object, "Labels and tags present");
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/documents/" + _Required, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string file = File.ReadAllText(Path.Combine(f.Property.Directory, "legal", "participation-agreement.md")).Replace("\r\n", "\n");
                    Check.Equal(file, Api.Str(doc, "content"), "Content from the file");
                    Check.Equal("RequiredAtSignup", Api.Str(doc, "acceptance"), "Acceptance");
                    Check.True(doc.GetProperty("separateCheckbox").GetBoolean(), "Separate checkbox");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/documents/nope", null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Signup.EmailRequiresAcceptance", "Email sign-up refuses a missing or stale separately accepted document (fieldErrors acceptedDocuments.<key>) and records every accepted document with method Signup and the client", async (f, ct) =>
                {
                    ApiResult missing = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email = Api.UniqueEmail(), password = Api.Password, displayName = "No Docs", dateOfBirth = "1990-01-01", acceptTerms = true }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, missing.Status, "Missing document refused: " + missing.Text);
                    Check.True(missing.Body.GetProperty("fieldErrors").TryGetProperty("acceptedDocuments." + _Required, out JsonElement _), "Field error names the document");
                    ApiResult stale = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email = Api.UniqueEmail(), password = Api.Password, displayName = "Stale Docs", dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = new[] { new { key = _Required, version = "0.9" } } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, stale.Status, "Stale version refused");
                    Check.True(Api.Str(stale.Body.GetProperty("fieldErrors"), "acceptedDocuments." + _Required).Contains("1.0", StringComparison.Ordinal), "Names the current version");
                    Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM users WHERE displayname IN ('No Docs', 'Stale Docs');", ct).ConfigureAwait(false), "Nothing stored");

                    TestMember member = new TestMember { Email = Api.UniqueEmail(), Password = Api.Password };
                    ApiResult created = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email = member.Email, password = member.Password, displayName = "Web Member", dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, ct, "web").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, created.Status, "Created: " + created.Text);
                    member.Id = Api.Str(created.Body.GetProperty("user"), "id");
                    Check.Equal(0, created.Body.GetProperty("user").GetProperty("pendingDocuments").GetArrayLength(), "Nothing pending after sign-up");
                    int gating = f.Property.Documents!.Count(d => d.IsGating);
                    Check.Equal((long)gating, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM propertydocumentacceptances WHERE userid = '" + member.Id + "' AND method = 'Signup' AND client = 'Web';", ct).ConfigureAwait(false), "Every gating document recorded");
                    Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'document.accept' AND targetid = '" + member.Id + "';", ct).ConfigureAwait(false) >= 1, "Audited");

                    TestMember mobile = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM propertydocumentacceptances WHERE userid = '" + mobile.Id + "' AND documentkey = '" + _Required + "' AND client = 'Other';", ct).ConfigureAwait(false), "Mobile without a platform header is Other");
                }),
                SuiteCases.Http(Id, "Signup.SocialGate", "A social sign-up is gated after the age attestation until it accepts (403 DocumentAcceptanceRequired with the document keys); accepting at the attestation step or with POST /me/documents/accept opens the routes; the platform header names the client", async (f, ct) =>
                {
                    TestMember social = await Api.SocialAsync(f, ct).ConfigureAwait(false);
                    await Api.GrantCompAsync(f, social.Id, ct).ConfigureAwait(false);
                    JsonElement attested = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/age-attestation", social.AccessToken, new { dateOfBirth = "1985-03-03" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> pending = attested.GetProperty("pendingDocuments").EnumerateArray().Select(d => Api.Str(d, "key")).ToList();
                    Check.True(pending.Contains(_Required) && pending.Contains(_Terms), "Me lists the pending documents: " + String.Join(",", pending));
                    Check.True(attested.GetProperty("pendingDocuments").EnumerateArray().All(d => !d.GetProperty("previouslyAccepted").GetBoolean()), "Never accepted: previouslyAccepted is false (Please review and accept)");
                    ApiResult gated = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/users/" + social.Id, social.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Forbidden, gated.Status, "Gated: " + gated.Text);
                    Check.Equal("DocumentAcceptanceRequired", gated.Error, "Error code");
                    List<string> keys = gated.Body.GetProperty("documents").EnumerateArray().Select(e => e.GetString() ?? String.Empty).ToList();
                    Check.True(keys.Contains(_Required) && keys.Contains(_Terms), "Body lists the keys");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", social.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/me/documents/accept", social.AccessToken, new { documents = new[] { new { key = "unknown", version = "1.0" } } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, bad.Status, "Unknown key refused");
                    ApiResult accepted = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/me/documents/accept", social.AccessToken,
                        new { acceptTerms = true, documents = Api.SignupDocuments }, ct, "mobile", new Dictionary<string, string> { ["X-Client-Platform"] = "ios" }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, accepted.Status, "Accepted: " + accepted.Text);
                    Check.Equal(0, accepted.Body.GetProperty("pendingDocuments").GetArrayLength(), "Nothing pending");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + social.Id, social.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM propertydocumentacceptances WHERE userid = '" + social.Id + "' AND documentkey = '" + _Required + "' AND method = 'Signup' AND client = 'Ios';", ct).ConfigureAwait(false), "Recorded as Signup from iOS");

                    TestMember onboarded = await Api.SocialAsync(f, ct, "apple").ConfigureAwait(false);
                    await Api.GrantCompAsync(f, onboarded.Id, ct).ConfigureAwait(false);
                    JsonElement both = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/age-attestation", onboarded.AccessToken,
                        new { dateOfBirth = "1985-03-03", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, both.GetProperty("pendingDocuments").GetArrayLength(), "Accepted at the attestation step");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + onboarded.Id, onboarded.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult staleAttest = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/me/age-attestation", (await Api.SocialAsync(f, ct, "facebook").ConfigureAwait(false)).AccessToken,
                        new { dateOfBirth = "1985-03-03", acceptedDocuments = new[] { new { key = _Required, version = "9.9" } } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, staleAttest.Status, "A stale version at the attestation step is refused");
                }),
                SuiteCases.Http(Id, "Reacceptance.PromptAndAdmin", "Publishing a version that requires re-acceptance gates members and SystemAdmin members (not the admin token) until they accept; method Reacceptance; versions, counts, and acceptances in the admin routes; VersionExists; a version without re-acceptance gates nobody", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, _Admin, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement before = list.GetProperty("objects").EnumerateArray().Single(d => Api.Str(d, "key") == _Required);
                    Check.Equal(2L, before.GetProperty("acceptedCurrentCount").GetInt64(), "Both members accepted version 1");
                    Check.True(list.GetProperty("syncsFromFolder").GetBoolean(), "SingleProperty follows the folder");

                    ApiResult invalid = await Api.SendAsync(f, HttpMethod.Post, _Admin + "/" + _Required + "/versions", HttpFixture.AdminToken, new { version = "", content = "", changeSummary = "" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, invalid.Status, "Validation");
                    foreach (string field in new[] { "version", "content", "changeSummary" }) Check.True(invalid.Body.GetProperty("fieldErrors").TryGetProperty(field, out JsonElement _), "Field error " + field);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + _Required + "/versions", HttpFixture.AdminToken, new { version = "1.0", content = "# Again", changeSummary = "Same version" }, HttpStatusCode.Conflict, "VersionExists", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + _Required + "/versions", member.AccessToken, new { version = "2.0", content = "# x", changeSummary = "x" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

                    JsonElement published = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + _Required + "/versions", HttpFixture.AdminToken,
                        new { version = "2.0", content = "# participation agreement\n\nVersion 2.", changeSummary = "Clarified section 2." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("2.0", Api.Str(published, "version"), "Current version 2.0");
                    Check.Equal(2, published.GetProperty("sequence").GetInt32(), "Sequence 2");
                    Check.Equal(2, published.GetProperty("requiredSequence").GetInt32(), "Requires sequence 2 (reacceptOnChange default)");
                    Check.Equal("Edit", Api.Str(published, "source"), "Source Edit");
                    Check.Equal(0L, published.GetProperty("acceptedCurrentCount").GetInt64(), "Nobody accepted 2.0 yet");

                    ApiResult gated = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal("DocumentAcceptanceRequired", gated.Error, "Member gated after the publish: " + gated.Text);
                    Check.Equal(_Required, gated.Body.GetProperty("documents")[0].GetString(), "Names the document");
                    ApiResult staff = await Api.SendAsync(f, HttpMethod.Get, _Admin, admin.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal("DocumentAcceptanceRequired", staff.Error, "Staff routes are gated too");
                    await Api.ExpectAsync(f, HttpMethod.Get, _Admin, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("2.0", Api.Str(me.GetProperty("pendingDocuments")[0], "version"), "Me shows the new version");
                    Check.True(me.GetProperty("pendingDocuments")[0].GetProperty("previouslyAccepted").GetBoolean(), "A newer version of an accepted document is marked previouslyAccepted (updated)");
                    JsonElement publicDoc = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/documents/" + _Required, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Api.Str(publicDoc, "content").Contains("Version 2.", StringComparison.Ordinal), "The public read serves the new content");
                    Check.Equal("Clarified section 2.", Api.Str(publicDoc, "changeSummary"), "Change summary");

                    ApiResult stale = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/me/documents/accept", member.AccessToken, new { documents = new[] { new { key = _Required, version = "1.0" } } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, stale.Status, "Accepting the old version is refused");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/documents/accept", member.AccessToken, new { documents = new[] { new { key = _Required, version = "2.0" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM propertydocumentacceptances WHERE userid = '" + member.Id + "' AND sequence = 2 AND method = 'Reacceptance';", ct).ConfigureAwait(false), "Recorded as Reacceptance");

                    JsonElement versions = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + _Required + "/versions/enumerate", HttpFixture.AdminToken, new { maxResults = 10 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, versions.GetProperty("objects").GetArrayLength(), "Two versions");
                    JsonElement newest = versions.GetProperty("objects")[0];
                    Check.Equal("2.0", Api.Str(newest, "version"), "Newest first");
                    Check.Equal(1L, newest.GetProperty("acceptanceCount").GetInt64(), "One acceptance of 2.0");
                    Check.True(newest.GetProperty("content").ValueKind == JsonValueKind.Null, "Content omitted in the list");
                    JsonElement one = await Api.ExpectAsync(f, HttpMethod.Get, _Admin + "/" + _Required + "/versions/" + Api.Str(versions.GetProperty("objects")[1], "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Seed", Api.Str(one, "source"), "The seed version");
                    Check.True(Api.Str(one, "content").Length > 0, "Single reads carry content");
                    JsonElement history = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/acceptances/enumerate", HttpFixture.AdminToken,
                        new { filters = new Dictionary<string, string> { ["userId"] = member.Id, ["documentKey"] = _Required } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, history.GetProperty("objects").GetArrayLength(), "Member drawer history: version 1 and 2");
                    Check.Equal(member.Id, Api.Str(history.GetProperty("objects")[0].GetProperty("user"), "id"), "With the member summary");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/acceptances/enumerate", HttpFixture.AdminToken, new { filters = new Dictionary<string, string> { ["method"] = "Nope" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/documents/acceptances", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(mine.GetProperty("objects").GetArrayLength() >= 2 && mine.GetProperty("objects").EnumerateArray().All(a => Api.Str(a, "userId") == member.Id), "Own history only");

                    await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + _Required + "/versions", HttpFixture.AdminToken,
                        new { version = "2.1", content = "# participation agreement\n\nTypo fixed.", changeSummary = "Typo.", requireReacceptance = false }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'document.publish';", ct).ConfigureAwait(false) == 2, "Publishes audited");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/subscription-terms/versions", HttpFixture.AdminToken, new { version = "2.0", content = "# x", changeSummary = "x", requireReacceptance = true }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Seeding.FolderWinsOnChange", "Admin versions stand while the folder is unchanged; a changed version or file becomes a DirectorySync version on the next load in SingleProperty (re-acceptance only when the version changed and reacceptOnChange); without the folder rule the stored version stands", async (f, ct) =>
                {
                    string copy = TestProperty.CopySample();
                    try
                    {
                        string original = f.Property.Directory;
                        await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + _Required + "/versions", HttpFixture.AdminToken, new { version = "1.1", content = "# Admin edit", changeSummary = "Admin edit." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                        await f.Services.Documents.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                        Check.Equal("1.1", Current(f, _Required).Version, "An unchanged folder keeps the admin version");
                        f.Property.Directory = copy;
                        File.AppendAllText(Path.Combine(copy, "legal", "participation-agreement.md"), "\nA new paragraph.\n");
                        await f.Services.Documents.EnsureSeededAsync(false, ct).ConfigureAwait(false);
                        Check.Equal("1.1", Current(f, _Required).Version, "Without the folder rule the stored version stands");
                        await f.Services.Documents.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                        Check.Equal(DocumentVersionSourceEnum.DirectorySync, Current(f, _Required).Source, "The changed file wins");
                        Check.Equal(3, Current(f, _Required).Sequence, "A new sequence");
                        Check.True(!Current(f, _Required).RequiresReacceptance, "Same version: no re-acceptance");
                        Check.Equal(2, f.Services.Documents.Snapshot.Single(s => s.Config.Key == _Required).RequiredSequence, "Still requires the admin's version");
                        await f.Services.Documents.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                        Check.Equal(3, Current(f, _Required).Sequence, "Synchronized once");
                        f.Property.Documents!.Single(d => d.Key == _Required).Version = "2.0";
                        await f.Services.Documents.EnsureSeededAsync(true, ct).ConfigureAwait(false);
                        Check.Equal("2.0", Current(f, _Required).Version, "The folder's new version");
                        Check.True(Current(f, _Required).RequiresReacceptance, "A new version requires re-acceptance");
                        Check.Equal(4, f.Services.Documents.Snapshot.Single(s => s.Config.Key == _Required).RequiredSequence, "Members must accept sequence 4");
                        Check.Equal(2L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'document.sync';", ct).ConfigureAwait(false), "Syncs audited");
                        f.Property.Directory = original;
                    }
                    finally
                    {
                        TestProperty.Delete(copy);
                    }
                }),
                SuiteCases.Http(Id, "Export.IncludesAcceptances", "The account export carries documentAcceptances", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement file = await AccountExportSuite.ExportAsync(f, member, ct).ConfigureAwait(false);
                    JsonElement rows = file.GetProperty("documentAcceptances");
                    Check.Equal(f.Property.Documents!.Count(d => d.IsGating), rows.GetArrayLength(), "Every acceptance exported");
                    Check.True(rows.EnumerateArray().Any(r => Api.Str(r, "documentKey") == _Required && Api.Str(r, "version") == "1.0"), "Key and version");
                }),
                SuiteCases.Http(Id, "Isolation.TwoProperties", "One MultiProperty deployment: documents, versions, and acceptances are per property; the platform routes read and publish any property's documents (audited in both audits) and require a platform token", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    TestMember inA = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    f.PropertyId = second;
                    JsonElement listB = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + second + "/documents", platform, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(listB.GetProperty("objects").EnumerateArray().All(d => d.GetProperty("sequence").GetInt32() == 0), "B has no stored versions (registered without a folder)");
                    JsonElement publishedB = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/documents/" + _Required + "/versions", platform,
                        new { version = "B1", content = "# B agreement", changeSummary = "First version for B." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal(1, publishedB.GetProperty("sequence").GetInt32(), "B's first version");
                    JsonElement registeredB = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email = Api.UniqueEmail(), password = Api.Password, displayName = "Member B", dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = new[] { new { key = _Required, version = "B1" } } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    TestMember inB = new TestMember { Id = Api.Str(registeredB.GetProperty("user"), "id") };
                    JsonElement acceptancesB = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/documents/acceptances/enumerate", platform, new { maxResults = 50 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(acceptancesB.GetProperty("objects").EnumerateArray().All(a => Api.Str(a, "userId") == inB.Id), "B lists only its own acceptances");
                    Check.True(acceptancesB.GetProperty("objects").EnumerateArray().Any(a => Api.Str(a, "version") == "B1"), "B's member accepted B's version");
                    JsonElement publicB = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/documents/" + _Required, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("# B agreement", Api.Str(publicB, "content"), "B serves its own content");
                    f.PropertyId = TestProperty.SampleId;
                    JsonElement publicA = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/documents/" + _Required, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("1.0", Api.Str(publicA, "version"), "A keeps its own version");
                    JsonElement acceptancesA = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/acceptances/enumerate", HttpFixture.AdminToken, new { maxResults = 50 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(acceptancesA.GetProperty("objects").EnumerateArray().All(a => Api.Str(a, "userId") == inA.Id), "A lists only its own acceptances");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = 'platform' AND action = 'document.publish' AND targetid = '" + second + "';", ct).ConfigureAwait(false), "Platform audit");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE propertyid = '" + second + "' AND action = 'document.publish';", ct).ConfigureAwait(false), "B's audit");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + second + "/documents", null, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TrellisKit.Core.Models.PropertyDocumentVersion Current(HttpFixture f, string key)
        {
            return f.Services.Documents.Snapshot.Single(s => s.Config.Key == key).Current;
        }

        #endregion
    }
}
