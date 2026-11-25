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
    using TrellisKit.Core.PropertyConfig;

    /// <summary>Actual HTTP and PostgreSQL catalog lifecycle, bounds, relationships and concurrency.</summary>
    public static class ExerciseCatalogSuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id = "ExerciseCatalog";
        private const string _Admin = "/api/v1/admin/catalog/entries";
        private const string _Public = "/api/v1/public/catalog/entries";
        /// <summary>Build the suite.</summary>
        public static TestSuiteDescriptor Build() => new TestSuiteDescriptor(Id,"Reviewed catalog",new[] {
            CatalogTestKit.Http(Id,Id + ".Workflow","Author/reviewer separation, publication, revision conflicts and edits",WorkflowAsync),
            CatalogTestKit.Http(Id,Id + ".Relationships","Atomic directed relationship replacement and public visibility",RelationshipsAsync),
            CatalogTestKit.Http(Id,Id + ".Collections","Bounded metadata and alias search with continuation",CollectionsAsync),
            CatalogTestKit.Http(Id,Id + ".Validation","Bounds, licensing and property-scoped references",ValidationAsync),
            CatalogTestKit.Http(Id,Id + ".SlugContract","Catalog create/update and public slug checks agree on boundaries, reserved names and availability",SlugContractAsync),
            CatalogTestKit.Http(Id,Id + ".Library","Existing library references preserve audience and feature gates",LibraryAsync),
            CatalogTestKit.Http(Id,Id + ".Moderation","Published-only search and moderation withdrawal require renewed review",ModerationAsync),
            CatalogTestKit.Http(Id,Id + ".SourceReview","Sources, descriptors and relationship notes follow word filtering and the content changelog",SourceReviewAsync),
            CatalogTestKit.Http(Id,Id + ".PublicRateLimit","Anonymous catalog configuration and data share a bounded IP read allowance",RateLimitAsync),
            CatalogTestKit.Http(Id,Id + ".ModerationSurvivesEdits","Moderation blocks every public surface and survives edits until explicit restoration",ModerationEditsAsync)
        });
        private static async Task SetupAsync(HttpFixture f,CancellationToken ct)
        {
            LibraryTestKit.Relax(f);
            f.Property.LibrarySection ??= new PropertyLibrarySection();
            f.Property.LibrarySection.CatalogMuscles = new List<PropertyOptionSeed> { new PropertyOptionSeed { Key = "sample-muscle", Label = "Sample muscle" } };
            f.Property.LibrarySection.CatalogEquipment = new List<PropertyOptionSeed> { new PropertyOptionSeed { Key = "sample-equipment", Label = "Sample equipment" } };
            await f.Services.Database.DirectoryListings.Config.CreateProgramAsync(new DirectoryProgram { Id = "prg_" + Guid.NewGuid().ToString("N"), Key = "sample-program", Label = "Sample program" },ct).ConfigureAwait(false);
        }
        private static Dictionary<string,object?> Body(string name) => new Dictionary<string,object?> { { "name",name }, { "slug",name.ToLowerInvariant().Replace(' ','-') }, { "kind","Exercise" }, { "program","sample-program" }, { "primaryMuscles",new[] { "sample-muscle" } }, { "equipment",new[] { "sample-equipment" } }, { "instructions",new[] { "Sample reviewed instruction." } }, { "sources",new[] { new { title = "Original reviewed contribution", license = "Original" } } } };
        private static Task<JsonElement> CreateAsync(HttpFixture f,string name,CancellationToken ct,string? token=null,Dictionary<string,object?>? body=null) => Api.ExpectAsync(f,HttpMethod.Post,_Admin,token ?? HttpFixture.AdminToken,body ?? Body(name),HttpStatusCode.Created,ct);
        private static Task<JsonElement> ActAsync(HttpFixture f,JsonElement entry,string action,CancellationToken ct,string? token=null) => Api.ExpectAsync(f,HttpMethod.Post,_Admin + "/" + Api.Str(entry,"id") + "/" + action,token ?? HttpFixture.AdminToken,new { expectedRevision = entry.GetProperty("revision").GetInt64(),note = "Reviewed source and instructions" },HttpStatusCode.OK,ct);
        private static async Task<JsonElement> PublishAsync(HttpFixture f,JsonElement entry,CancellationToken ct) => await ActAsync(f,await ActAsync(f,entry,"submit",ct).ConfigureAwait(false),"approve",ct).ConfigureAwait(false);
        private static async Task WorkflowAsync(HttpFixture f,CancellationToken ct)
        {
            await SetupAsync(f,ct).ConfigureAwait(false);
            TestMember editor = await LibraryTestKit.EditorAsync(f,ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f,ct).ConfigureAwait(false);
            JsonElement entry = await CreateAsync(f,"Sample exercise",ct,editor.AccessToken).ConfigureAwait(false); string id = Api.Str(entry,"id");
            Check.Equal("Draft",Api.Str(entry,"status"),"Created as draft");
            await Api.ExpectErrorAsync(f,HttpMethod.Get,_Public + "/" + id,null,null,HttpStatusCode.NotFound,"NotFound",ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin,member.AccessToken,Body("Denied"),HttpStatusCode.Forbidden,"Forbidden",ct).ConfigureAwait(false);
            entry = await ActAsync(f,entry,"submit",ct,editor.AccessToken).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin + "/" + id + "/approve",editor.AccessToken,new { expectedRevision = 2 },HttpStatusCode.Forbidden,"Forbidden",ct).ConfigureAwait(false);
            entry = await ActAsync(f,entry,"approve",ct).ConfigureAwait(false);
            JsonElement published = await Api.ExpectAsync(f,HttpMethod.Get,_Public + "/sample-exercise",null,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal("Published",Api.Str(published,"status"),"Published after review");
            Check.True(!published.TryGetProperty("reviewNote",out JsonElement note) || note.ValueKind == JsonValueKind.Null,"Private reviewer note hidden");
            Dictionary<string,object?> changed = Body("Changed exercise"); changed["expectedRevision"] = 1;
            await Api.ExpectErrorAsync(f,HttpMethod.Put,_Admin + "/" + id,editor.AccessToken,changed,HttpStatusCode.Conflict,"CatalogChanged",ct).ConfigureAwait(false);
            changed["expectedRevision"] = entry.GetProperty("revision").GetInt64();
            entry = await Api.ExpectAsync(f,HttpMethod.Put,_Admin + "/" + id,editor.AccessToken,changed,HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal("Draft",Api.Str(entry,"status"),"Content edit requires renewed review");
            await Api.ExpectErrorAsync(f,HttpMethod.Get,_Public + "/" + id,null,null,HttpStatusCode.NotFound,"NotFound",ct).ConfigureAwait(false);
            CatalogEntry snapshot = (await f.Services.Database.ExerciseCatalog.ReadAsync(id,ct).ConfigureAwait(false))!;
            snapshot.Name = "Stale overwrite"; snapshot.Revision++;
            Check.True(!await f.Services.Database.ExerciseCatalog.SaveAsync(snapshot,1,true,null,ct).ConfigureAwait(false),"Storage CAS rejects stale aggregate");
            Check.Equal("Changed exercise",(await f.Services.Database.ExerciseCatalog.ReadAsync(id,ct).ConfigureAwait(false))!.Name,"CAS left content intact");
        }
        private static async Task RelationshipsAsync(HttpFixture f,CancellationToken ct)
        {
            await SetupAsync(f,ct).ConfigureAwait(false);
            JsonElement source = await CreateAsync(f,"Source",ct).ConfigureAwait(false);
            JsonElement target = await PublishAsync(f,await CreateAsync(f,"Target",ct).ConfigureAwait(false),ct).ConfigureAwait(false);
            string sourceId = Api.Str(source,"id"), targetId = Api.Str(target,"id");
            source = await Api.ExpectAsync(f,HttpMethod.Put,_Admin + "/" + sourceId + "/relationships",HttpFixture.AdminToken,new { expectedRevision = 1, relationships = new[] { new { targetId,kind = "Progression",note = "Related example",labels = new[] { "sample" },tags = new { track = "example" } } } },HttpStatusCode.OK,ct).ConfigureAwait(false);
            source = await PublishAsync(f,source,ct).ConfigureAwait(false);
            JsonElement page = await Api.ExpectAsync(f,HttpMethod.Post,_Public + "/" + sourceId + "/relationships/enumerate",null,new { labels = new[] { "sample" },tags = new { track = "example" },filters = new { kind = "Progression" } },HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal(1,page.GetProperty("objects").GetArrayLength(),"Published edge");
            Check.Equal("Target",Api.Str(page.GetProperty("objects")[0],"targetName"),"Target display reference");
            await Api.ExpectErrorAsync(f,HttpMethod.Put,_Admin + "/" + sourceId + "/relationships",HttpFixture.AdminToken,new { expectedRevision = source.GetProperty("revision").GetInt64(),relationships = new[] { new { targetId = sourceId,kind = "Progression" } } },HttpStatusCode.BadRequest,"ValidationFailed",ct).ConfigureAwait(false);
            await ActAsync(f,target,"archive",ct).ConfigureAwait(false);
            page = await Api.ExpectAsync(f,HttpMethod.Get,_Public + "/" + sourceId + "/relationships",null,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal(0,page.GetProperty("objects").GetArrayLength(),"Archived targets hidden immediately");
            await Api.ExpectErrorAsync(f,HttpMethod.Put,_Admin + "/" + sourceId + "/relationships",HttpFixture.AdminToken,new { expectedRevision = 1,relationships = Array.Empty<object>() },HttpStatusCode.Conflict,"CatalogChanged",ct).ConfigureAwait(false);
            page = await Api.ExpectAsync(f,HttpMethod.Get,_Admin + "/" + sourceId + "/relationships",HttpFixture.AdminToken,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal(1,page.GetProperty("objects").GetArrayLength(),"Stale replacement leaves edges intact");
        }
        private static async Task CollectionsAsync(HttpFixture f,CancellationToken ct)
        {
            await SetupAsync(f,ct).ConfigureAwait(false);
            foreach (string name in new[] { "Alpha", "Beta", "Gamma" })
            {
                Dictionary<string,object?> body = Body(name); body["aliases"] = new[] { "Shared alias " + name }; body["labels"] = new[] { "visible", "staff:internal" }; body["tags"] = new Dictionary<string,string> { { "track","sample" }, { "staff:note","private" } };
                await PublishAsync(f,await CreateAsync(f,name,ct,body:body).ConfigureAwait(false),ct).ConfigureAwait(false);
            }
            JsonElement first = await Api.ExpectAsync(f,HttpMethod.Post,_Public + "/enumerate",null,new { maxResults=1,search="Shared alias",labels=new[] { "visible" },tags=new { track="sample" },filters=new { muscle="sample-muscle",equipment="sample-equipment",program="sample-program" } },HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal(1,first.GetProperty("objects").GetArrayLength(),"Page bound");
            Check.True(!first.GetProperty("endOfResults").GetBoolean(),"Continuation available");
            JsonElement row=first.GetProperty("objects")[0]; Check.Equal(1,row.GetProperty("labels").GetArrayLength(),"Staff metadata hidden");
            JsonElement second = await Api.ExpectAsync(f,HttpMethod.Post,_Public + "/enumerate",null,new { maxResults=1,search="Shared alias",labels=new[] { "visible" },tags=new { track="sample" },filters=new { muscle="sample-muscle",equipment="sample-equipment",program="sample-program" },continuationToken=Api.Str(first,"continuationToken") },HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.True(Api.Str(row,"id") != Api.Str(second.GetProperty("objects")[0],"id"),"Continuation advances");
            await Api.ExpectErrorAsync(f,HttpMethod.Post,_Public + "/enumerate",null,new { filters=new { status="Draft" } },HttpStatusCode.BadRequest,"ValidationFailed",ct).ConfigureAwait(false);
        }
        private static async Task SlugContractAsync(HttpFixture f,CancellationToken ct)
        {
            await SetupAsync(f,ct).ConfigureAwait(false);
            const string checkPath="/api/v1/public-pages/slugs/check";
            foreach(string slug in new[] { "a", "ab", new string('x',81), "admin", " ADMIN ", "bad--slug", "-leading" })
            {
                JsonElement check=await Api.ExpectAsync(f,HttpMethod.Post,checkPath,HttpFixture.AdminToken,new { kind="Catalog",slug },HttpStatusCode.OK,ct).ConfigureAwait(false);
                Check.True(!check.GetProperty("valid").GetBoolean() && !check.GetProperty("available").GetBoolean(),"Slug check rejects invalid catalog slug: "+slug);
                Dictionary<string,object?> invalid=Body("Rejected boundary");invalid["slug"]=slug;
                await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin,HttpFixture.AdminToken,invalid,HttpStatusCode.BadRequest,"ValidationFailed",ct).ConfigureAwait(false);
            }
            foreach(string slug in new[] { " AbC ", new string('x',80) })
            {
                string normalized=slug.Trim().ToLowerInvariant();
                JsonElement check=await Api.ExpectAsync(f,HttpMethod.Post,checkPath,HttpFixture.AdminToken,new { kind="Catalog",slug },HttpStatusCode.OK,ct).ConfigureAwait(false);
                Check.True(check.GetProperty("valid").GetBoolean() && check.GetProperty("available").GetBoolean(),"Boundary slug available before creation");
                Dictionary<string,object?> body=Body("Accepted boundary");body["slug"]=slug;
                JsonElement entry=await CreateAsync(f,"Accepted boundary",ct,body:body).ConfigureAwait(false);string id=Api.Str(entry,"id");
                Check.Equal(normalized,Api.Str(entry,"slug"),"Create uses the same normalization as slug check");
                check=await Api.ExpectAsync(f,HttpMethod.Post,checkPath,HttpFixture.AdminToken,new { kind="Catalog",slug },HttpStatusCode.OK,ct).ConfigureAwait(false);
                Check.True(check.GetProperty("valid").GetBoolean() && !check.GetProperty("available").GetBoolean(),"Draft reserves its valid slug");Check.Equal("taken",Api.Str(check,"problem"),"Taken status matches catalog uniqueness");
                check=await Api.ExpectAsync(f,HttpMethod.Post,checkPath,HttpFixture.AdminToken,new { kind="Catalog",slug,id },HttpStatusCode.OK,ct).ConfigureAwait(false);
                Check.True(check.GetProperty("available").GetBoolean(),"Editing the same entry can retain its slug");
                body["expectedRevision"]=entry.GetProperty("revision").GetInt64();
                entry=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,body,HttpStatusCode.OK,ct).ConfigureAwait(false);
                body["expectedRevision"]=entry.GetProperty("revision").GetInt64();body["slug"]="ad";
                await Api.ExpectErrorAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,body,HttpStatusCode.BadRequest,"ValidationFailed",ct).ConfigureAwait(false);
                JsonElement unchanged=await Api.ExpectAsync(f,HttpMethod.Get,_Admin+"/"+id,HttpFixture.AdminToken,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
                Check.Equal(normalized,Api.Str(unchanged,"slug"),"Rejected edit keeps the previous slug");Check.Equal(entry.GetProperty("revision").GetInt64(),unchanged.GetProperty("revision").GetInt64(),"Rejected edit does not advance the revision");
                await PublishAsync(f,entry,ct).ConfigureAwait(false);
                await Api.ExpectAsync(f,HttpMethod.Get,_Public+"/"+normalized,null,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
            }
        }
        private static async Task ValidationAsync(HttpFixture f,CancellationToken ct)
        {
            await SetupAsync(f,ct).ConfigureAwait(false);
            foreach (KeyValuePair<string,object?> invalid in new Dictionary<string,object?> { { "instructions",Enumerable.Repeat("step",51).ToArray() }, { "aliases",new[] { "repeat","Repeat" } }, { "primaryMuscles",new[] { "foreign-key" } }, { "lessonIds",new[] { "lsn_foreign" } }, { "sources",new[] { new { title="Invalid",license="Original",url="http://example.com/source" } } } })
            { Dictionary<string,object?> body=Body("Invalid");body[invalid.Key]=invalid.Value; await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin,HttpFixture.AdminToken,body,HttpStatusCode.BadRequest,"ValidationFailed",ct).ConfigureAwait(false); }
            Dictionary<string,object?> unlicensed=Body("Unlicensed"); unlicensed["sources"]=new[] { new { title="Unverified",license="AllRightsReserved",url="https://example.com/source" } };
            JsonElement entry=await CreateAsync(f,"Unlicensed",ct,body:unlicensed).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin + "/" + Api.Str(entry,"id") + "/submit",HttpFixture.AdminToken,new { expectedRevision=1 },HttpStatusCode.BadRequest,"ValidationFailed",ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin,HttpFixture.AdminToken,unlicensed,HttpStatusCode.Conflict,"SlugInUse",ct).ConfigureAwait(false);
            f.Property.Features.Catalog=false;
            await Api.ExpectErrorAsync(f,HttpMethod.Get,"/api/v1/catalog/config",null,null,HttpStatusCode.NotFound,"NotAvailable",ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f,HttpMethod.Get,_Public,null,null,HttpStatusCode.NotFound,"NotAvailable",ct).ConfigureAwait(false);
        }
        private static async Task ModerationAsync(HttpFixture f,CancellationToken ct)
        {
            await SetupAsync(f,ct).ConfigureAwait(false);
            JsonElement entry=await CreateAsync(f,"Searchable example",ct).ConfigureAwait(false); string id=Api.Str(entry,"id");
            TrellisKit.Server.Services.ExerciseCatalogContentProvider provider=new TrellisKit.Server.Services.ExerciseCatalogContentProvider(f.Services);
            Check.True(await provider.SearchDocumentAsync(TrellisKit.Core.Enums.TargetTypeEnum.CatalogEntry,id,ct).ConfigureAwait(false) == null,"Draft has no search document");
            entry=await PublishAsync(f,entry,ct).ConfigureAwait(false);
            Check.True(await provider.SearchDocumentAsync(TrellisKit.Core.Enums.TargetTypeEnum.CatalogEntry,id,ct).ConfigureAwait(false) != null,"Published document indexed");
            TrellisKit.Server.Services.ModerationTarget target=(await provider.LoadModerationTargetAsync(TrellisKit.Core.Enums.TargetTypeEnum.CatalogEntry,id,ct).ConfigureAwait(false))!;
            await provider.SetModerationStateAsync(target,TrellisKit.Core.Enums.ModerationStateEnum.Hidden,null,"Review requested",ct).ConfigureAwait(false);
            Check.True(await provider.SearchDocumentAsync(TrellisKit.Core.Enums.TargetTypeEnum.CatalogEntry,id,ct).ConfigureAwait(false) == null,"Hidden content invalidates index");
            await Api.ExpectErrorAsync(f,HttpMethod.Get,_Public + "/" + id,null,null,HttpStatusCode.NotFound,"NotFound",ct).ConfigureAwait(false);
            await provider.SetModerationStateAsync(target,TrellisKit.Core.Enums.ModerationStateEnum.None,null,"Reconsider",ct).ConfigureAwait(false);
            Check.Equal("Draft",(await f.Services.Database.ExerciseCatalog.ReadAsync(id,ct).ConfigureAwait(false))!.Status,"Restoration requires reviewed publication");
            TrellisKit.Core.Database.DatabaseDriverBase other=f.Database.ForProperty(TrellisKit.Core.Helpers.PropertyIds.Generate());
            Check.True(await other.ExerciseCatalog.ReadAsync(id,ct).ConfigureAwait(false) == null,"Other property cannot read the entry");
            CatalogEntry foreign=new CatalogEntry { Id="cex_foreign",Name="Foreign",Slug="foreign",Kind="Technique" };
            await other.ExerciseCatalog.SaveAsync(foreign,null,true,null,ct).ConfigureAwait(false);
            JsonElement current=await Api.ExpectAsync(f,HttpMethod.Get,_Admin + "/" + id,HttpFixture.AdminToken,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f,HttpMethod.Put,_Admin + "/" + id + "/relationships",HttpFixture.AdminToken,new { expectedRevision=current.GetProperty("revision").GetInt64(),relationships=new[] { new { targetId=foreign.Id,kind="Progression" } } },HttpStatusCode.NotFound,"NotFound",ct).ConfigureAwait(false);
            JsonElement changes=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/content-review/enumerate",HttpFixture.AdminToken,new { filters=new { targetType="CatalogEntry",targetId=id } },HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.True(changes.GetProperty("objects").GetArrayLength() > 0,"Catalog content contributes to the review changelog");
            await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/content-review/" + Api.Str(changes.GetProperty("objects")[0],"id") + "/action",HttpFixture.AdminToken,new { action="Hide",reason="Review this source",notifyAuthor=false },HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal("Archived",(await f.Services.Database.ExerciseCatalog.ReadAsync(id,ct).ConfigureAwait(false))!.Status,"Review queue hides catalog through shared moderation");

        }
        private static async Task LibraryAsync(HttpFixture f,CancellationToken ct)
        {
            await SetupAsync(f,ct).ConfigureAwait(false); TestMember admin=await Api.AdminAsync(f,ct).ConfigureAwait(false);
            JsonElement series=await LibraryTestKit.SeriesAsync(f,admin.AccessToken,"Demonstrations",ct).ConfigureAwait(false);
            JsonElement lesson=await LibraryTestKit.LessonAsync(f,admin.AccessToken,Api.Str(series,"id"),"Private demonstration",ct).ConfigureAwait(false);
            Dictionary<string,object?> body=Body("Reference");body["lessonIds"]=new[] { Api.Str(lesson,"id") };
            JsonElement entry=await PublishAsync(f,await CreateAsync(f,"Reference",ct,body:body).ConfigureAwait(false),ct).ConfigureAwait(false);
            JsonElement publicEntry=await Api.ExpectAsync(f,HttpMethod.Get,_Public + "/" + Api.Str(entry,"id"),null,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal(0,publicEntry.GetProperty("lessonIds").GetArrayLength(),"Anonymous cannot discover member-only demonstration IDs");
            JsonElement adminEntry=await Api.ExpectAsync(f,HttpMethod.Get,_Admin + "/" + Api.Str(entry,"id"),HttpFixture.AdminToken,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
            Check.Equal(1,adminEntry.GetProperty("lessonIds").GetArrayLength(),"Author sees preserved reference");
        }

        private static async Task SourceReviewAsync(HttpFixture f, CancellationToken ct)
        {
            await SetupAsync(f, ct).ConfigureAwait(false);
            TestMember editor = await LibraryTestKit.EditorAsync(f, ct).ConfigureAwait(false);
            f.Settings.Moderation.WordFilterEnabled = true;
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "blockedterm", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Dictionary<string, object?> blocked = Body("Filtered source");
            blocked["sources"] = new[] { new { title = "Reviewed source", license = "Original", attribution = "blockedterm" } };
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin, editor.AccessToken, blocked, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
            blocked = Body("Filtered descriptor"); blocked["movementPattern"] = "blockedterm";
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin, editor.AccessToken, blocked, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
            Dictionary<string, object?> good = Body("Source evidence");
            good["sources"] = new[] { new { title = "Reviewed source", license = "Original", attribution = "Unique source attribution evidence" } };
            JsonElement entry = await CreateAsync(f, "Source evidence", ct, editor.AccessToken, good).ConfigureAwait(false);
            string id = Api.Str(entry, "id");
            JsonElement changes = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/content-review/enumerate", HttpFixture.AdminToken, new { filters = new { targetType = "CatalogEntry", targetId = id } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(changes.GetProperty("objects").EnumerateArray().Any(row => row.GetRawText().Contains("Unique source attribution evidence", StringComparison.Ordinal)), "Source attribution is recorded transactionally for content review");
            JsonElement target = await CreateAsync(f, "Related target", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Admin + "/" + id + "/relationships", editor.AccessToken,
                new { expectedRevision = 1, relationships = new[] { new { targetId = Api.Str(target, "id"), kind = "Progression", note = "blockedterm" } } }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
            Check.Equal(1L, (await f.Services.Database.ExerciseCatalog.ReadAsync(id, ct).ConfigureAwait(false))!.Revision, "Blocked edge does not change revision");
        }

        private static async Task RateLimitAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.RateLimits.PublicFeedPerMinute = 1;
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/catalog/config", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Public, null, null, HttpStatusCode.TooManyRequests, "RateLimited", ct).ConfigureAwait(false);
        }

        private static async Task ModerationEditsAsync(HttpFixture f, CancellationToken ct)
        {
            await SetupAsync(f, ct).ConfigureAwait(false);
            TestMember editor = await LibraryTestKit.EditorAsync(f, ct).ConfigureAwait(false);
            JsonElement published = await PublishAsync(f, await CreateAsync(f, "Moderated movement", ct, editor.AccessToken).ConfigureAwait(false), ct).ConfigureAwait(false);
            string id = Api.Str(published, "id");
            CatalogEntry stored = (await f.Services.Database.ExerciseCatalog.ReadAsync(id, ct).ConfigureAwait(false))!;
            long expected = stored.Revision;
            stored.ModerationState = TrellisKit.Core.Enums.ModerationStateEnum.Hidden;
            stored.Revision++;
            Check.True(await f.Services.Database.ExerciseCatalog.SaveAsync(stored, expected, false, null, ct).ConfigureAwait(false), "Simulate a legacy row with independent moderation and publication states");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Public + "/" + id, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            using (HttpResponseMessage html = await f.Client.GetAsync("/api/v1/public/pages/resolve/catalog/" + stored.Slug, ct).ConfigureAwait(false))
                Check.Equal(HttpStatusCode.NotFound, html.StatusCode, "Hidden catalog has no public HTML");
            Check.Equal(0L, await f.Services.Database.PublicPages.CountSitemapAsync(TrellisKit.Core.Enums.PublicPageKindEnum.Catalog, DateTime.UtcNow, ct).ConfigureAwait(false), "Hidden catalog omitted from sitemap");
            Dictionary<string, object?> edit = Body("Edited moderated movement"); edit["expectedRevision"] = stored.Revision;
            JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, _Admin + "/" + id, editor.AccessToken, edit, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(TrellisKit.Core.Enums.ModerationStateEnum.Hidden, (await f.Services.Database.ExerciseCatalog.ReadAsync(id, ct).ConfigureAwait(false))!.ModerationState, "Editing cannot clear staff moderation");
            Check.True(!updated.GetProperty("viewer").GetProperty("canSubmit").GetBoolean(), "Editor is not offered bypass action");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + id + "/submit", editor.AccessToken, new { expectedRevision = updated.GetProperty("revision").GetInt64() }, HttpStatusCode.Conflict, "InvalidTransition", ct).ConfigureAwait(false);
            TrellisKit.Server.Services.ExerciseCatalogContentProvider provider = new TrellisKit.Server.Services.ExerciseCatalogContentProvider(f.Services);
            TrellisKit.Server.Services.ModerationTarget target = (await provider.LoadModerationTargetAsync(TrellisKit.Core.Enums.TargetTypeEnum.CatalogEntry, id, ct).ConfigureAwait(false))!;
            await provider.SetModerationStateAsync(target, TrellisKit.Core.Enums.ModerationStateEnum.None, null, "Restored after review", ct).ConfigureAwait(false);
            JsonElement restored = await Api.ExpectAsync(f, HttpMethod.Get, _Admin + "/" + id, editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Draft", Api.Str(restored, "status"), "Restoration still requires publication review");
            Check.True(restored.GetProperty("viewer").GetProperty("canSubmit").GetBoolean(), "Explicit restoration enables submission");
        }
    }
}
