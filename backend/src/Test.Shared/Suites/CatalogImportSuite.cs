namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.CatalogImport;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>Actual HTTP source configuration, safe local-socket fetching and transactional draft refresh regressions.</summary>
    public static class CatalogImportSuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id="CatalogImport";
        /// <summary>Fresh isolated-property test descriptors.</summary>
        public static TestSuiteDescriptor Build(){return new TestSuiteDescriptor(Id,"Reviewed catalog imports",new[]{
            CatalogTestKit.Http(Id,"Draft.ApparatusCompatibility","Real reviewed imports preserve earlier fingerprints and apparatus context while respecting clears and human edits",ApparatusAsync),
            CatalogTestKit.Http(Id,"Source.PageCap","Source enumeration remains capped at 100 under larger admin settings, with complete nonduplicating continuation",PageCapAsync),
            CatalogTestKit.Http(Id,"Source.ReviewAndIsolation","Only administrators can configure reviewed permissive sources; updates revoke review and retain property/metadata bounds",ConfigurationAsync),
            CatalogTestKit.Http(Id,"Draft.RefreshAndPreservation","Real source fetch creates drafts idempotently, refreshes untouched drafts and preserves edits, holds, publication and tombstones",RefreshAsync),
            CatalogTestKit.Http(Id,"Source.Failures","Malformed, oversized, wrong-type, redirected and failed manifests never mutate catalog; transient failures retry",FailuresAsync),
            CatalogTestKit.Http(Id,"Source.AtomicGuards","Source revocation and human edits between read and write fail atomically without provenance drift",GuardsAsync),
            CatalogTestKit.Http(Id,"Source.BoundedRotation","The scheduled window rotates past twenty sources and explicit source selection cannot cross property boundaries",RotationAsync)
        });}
        private const string _Path="/api/v1/admin/catalog/import-sources";
        private static CatalogImportSourceRequest Request(string suffix="one"){return new CatalogImportSourceRequest{Name="Example licensed source "+suffix,Url="https://example.test/"+suffix+".json",License="CC0-1.0",Enabled=true,MaxEntries=20,Labels=new List<string>{"review-source"}};}
        private static async Task<CatalogImportSource> SourceAsync(HttpFixture f,CancellationToken token,string suffix="one")
        {
            JsonElement created=await Api.ExpectAsync(f,HttpMethod.Post,_Path,HttpFixture.AdminToken,Request(suffix),HttpStatusCode.Created,token).ConfigureAwait(false);
            string id=created.GetProperty("id").GetString()!;
            JsonElement reviewed=await Api.ExpectAsync(f,HttpMethod.Post,_Path+"/"+id+"/review",HttpFixture.AdminToken,new{expectedRevision=1,licenseReviewed=true},HttpStatusCode.OK,token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CatalogImportSource>(reviewed.GetRawText(),ApiJson.Api)!;
        }
        private static async Task PageCapAsync(HttpFixture f,CancellationToken token)
        {
            f.Settings.Enumeration.MaxResultsAdmin=500;
            for(int i=0;i<101;i++)
            {
                CatalogImportSource source=new CatalogImportSource{Id=IdGenerator.Generate("cis_"),Name="Source "+i.ToString("D3"),Url="https://example.test/source-"+i+".json",License="CC0-1.0",Labels=new List<string>{"bounded-source"}};
                Check.True(await f.Database.CatalogImports.SaveAsync(source,null,token).ConfigureAwait(false),"Source seed created");
            }
            JsonElement first=await Api.ExpectAsync(f,HttpMethod.Post,_Path+"/enumerate",HttpFixture.AdminToken,new{maxResults=101,labels=new[]{"bounded-source"}},HttpStatusCode.OK,token).ConfigureAwait(false);
            Check.Equal(100,first.GetProperty("maxResults").GetInt32(),"Route cap overrides larger administrator setting");Check.Equal(100,first.GetProperty("objects").GetArrayLength(),"Exactly 100 matching sources in first page");
            JsonElement second=await Api.ExpectAsync(f,HttpMethod.Post,_Path+"/enumerate",HttpFixture.AdminToken,new{maxResults=101,labels=new[]{"bounded-source"},continuationToken=Api.Str(first,"continuationToken")},HttpStatusCode.OK,token).ConfigureAwait(false);
            Check.Equal(1,second.GetProperty("objects").GetArrayLength(),"Remaining source is reachable");Check.True(!DirectoryTestKit.Ids(first).Intersect(DirectoryTestKit.Ids(second)).Any(),"Metadata-filtered continuation does not duplicate sources");
            JsonElement get=await Api.ExpectAsync(f,HttpMethod.Get,_Path+"?maxResults=101",HttpFixture.AdminToken,null,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(100,get.GetProperty("objects").GetArrayLength(),"GET uses the same cap");
        }
        private static async Task ConfigurationAsync(HttpFixture f,CancellationToken token)
        {
            TestMember member=await Api.MemberAsync(f,token).ConfigureAwait(false);
            await Api.ExpectAsync(f,HttpMethod.Post,_Path,member.AccessToken,Request(),HttpStatusCode.Forbidden,token).ConfigureAwait(false);
            foreach(Action<CatalogImportSourceRequest> mutate in new Action<CatalogImportSourceRequest>[]{p=>p.License="CC-BY-SA-4.0",p=>p.License="CC-BY-4.0",p=>p.Url="http://example.test/feed",p=>p.Url="https://user:secret@example.test/feed",p=>p.MaxEntries=101})
            {CatalogImportSourceRequest invalid=Request();mutate(invalid);await Api.ExpectAsync(f,HttpMethod.Post,_Path,HttpFixture.AdminToken,invalid,HttpStatusCode.BadRequest,token).ConfigureAwait(false);}
            CatalogImportSource source=await SourceAsync(f,token).ConfigureAwait(false);
            await Api.ExpectAsync(f,HttpMethod.Post,_Path+"/"+source.Id+"/review",HttpFixture.AdminToken,new{expectedRevision=1,licenseReviewed=true},HttpStatusCode.Conflict,token).ConfigureAwait(false);
            CatalogImportSourceRequest edit=Request();edit.ExpectedRevision=source.Revision;edit.Name="Changed source";
            JsonElement updated=await Api.ExpectAsync(f,HttpMethod.Put,_Path+"/"+source.Id,HttpFixture.AdminToken,edit,HttpStatusCode.OK,token).ConfigureAwait(false);
            Check.Equal(JsonValueKind.Null,updated.GetProperty("reviewedUtc").ValueKind,"Every edit revokes review");
            JsonElement page=await Api.ExpectAsync(f,HttpMethod.Post,_Path+"/enumerate",HttpFixture.AdminToken,new{maxResults=1,labels=new[]{"review-source"},filters=new{reviewed="false"}},HttpStatusCode.OK,token).ConfigureAwait(false);
            Check.Equal(1,page.GetProperty("objects").GetArrayLength(),"Bounded metadata filter");
            Check.True(await f.Services.Database.Deployment.ForProperty(PropertyIds.Generate()).CatalogImports.ReadAsync(source.Id,token).ConfigureAwait(false)==null,"Cross-property source isolation");
            await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/jobs/"+CatalogImportJobHandler.JobKey+"/run",HttpFixture.AdminToken,new{dryRun=true,parameters=ApiJson.Serialize(new{sourceIds=new[]{source.Id}})},HttpStatusCode.Created,token).ConfigureAwait(false);
        }
        private static CatalogImportManifest Manifest(string summary="Source version one",string key="movement-one",string name="Example movement")
        {return new CatalogImportManifest{Version=1,Entries=new List<CatalogImportRecord>{new CatalogImportRecord{Key=key,Entry=new CatalogEntryRequest{Kind="Technique",Name=name,Summary=summary,Instructions=new List<string>{"Read the reviewed movement notes."},Sources=new List<CatalogSource>{new CatalogSource{Title="Untrusted override",License="AllRightsReserved"}}}}}};}
        private static JobItemContext Context(HttpFixture f,CatalogImportSource source,bool dry=false){return new JobItemContext(f.Services,new JobDefinition(),new JobRun{IsDryRun=dry},new JobItem{TargetKey=source.Id,Payload=source.Revision.ToString(CultureInfo.InvariantCulture)},DateTime.UtcNow);}
        private static async Task<CatalogImportSource> LocalSourceAsync(HttpFixture f,FakeDirectoryServer site,CancellationToken token)
        {
            CatalogImportSource source=await SourceAsync(f,token).ConfigureAwait(false);
            site.Respond("/robots.txt",200,"text/plain",_=>"User-agent: *\nDisallow:\n");return source;
        }
        private static CatalogImportOutcome First(JobItemOutcome result){Check.True(result.Success,result.Error??"Source attempt succeeded");return JsonSerializer.Deserialize<CatalogImportOutcome>(JsonDocument.Parse(result.Result!).RootElement.GetProperty("entries")[0].GetRawText(),ApiJson.Api)!;}
        private static async Task RefreshAsync(HttpFixture f,CancellationToken token)
        {
            await using FakeDirectoryServer site=new FakeDirectoryServer();CatalogImportSource source=await LocalSourceAsync(f,site,token).ConfigureAwait(false);
            CatalogImportManifest manifest=Manifest();site.Respond("/one.json",200,"application/json",_=>ApiJson.Serialize(manifest));
            using HttpClient http=new HttpClient(new CatalogLoopbackTransport(site.BaseUrl));CatalogImportJobHandler handler=new CatalogImportJobHandler(http);
            Check.Equal("Planned",First(await handler.ExecuteAsync(Context(f,source,true),token).ConfigureAwait(false)).Outcome,"Preview validates fetched data");
            Check.True(await f.Services.Database.CatalogImports.ItemAsync(source.Id,"movement-one",token).ConfigureAwait(false)==null,"Preview writes no provenance or draft");
            CatalogImportOutcome created=First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false));Check.Equal("CreatedDraft",created.Outcome,"First import creates draft");
            CatalogEntry entry=(await f.Services.Database.ExerciseCatalog.ReadAsync(created.EntryId!,token).ConfigureAwait(false))!;
            Check.Equal("Draft",entry.Status,"Never publishes");Check.Equal("CC0-1.0",entry.Sources[0].License,"Reviewed source overrides remote claims");Check.Equal("Import",entry.Sources[0].Method,"Import provenance");
            await Api.ExpectAsync(f,HttpMethod.Get,"/api/v1/public/catalog/entries/"+entry.Id,null,null,HttpStatusCode.NotFound,token).ConfigureAwait(false);
            Check.Equal(HistoricalSourceHash(source,manifest.Entries[0].Entry,entry.Slug),(await f.Database.CatalogImports.ItemAsync(source.Id,"movement-one",token).ConfigureAwait(false))!.SourceHash,"Earlier serialized fingerprint is unchanged by new absent fields");
            Check.Equal("Unchanged",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Stable fingerprint avoids duplicate work");
            manifest.Entries[0].Entry.Summary="Source version two";
            Check.Equal("RefreshedDraft",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Untouched draft refreshes");
            entry=(await f.Services.Database.ExerciseCatalog.ReadAsync(entry.Id,token).ConfigureAwait(false))!;Check.Equal(2L,entry.Revision,"Refresh increments revision once");
            CatalogEntryRequest human=JsonSerializer.Deserialize<CatalogEntryRequest>(ApiJson.Serialize(entry),ApiJson.Api)!;human.ExpectedRevision=entry.Revision;human.Summary="Human editorial text";
            await Api.ExpectAsync(f,HttpMethod.Put,"/api/v1/admin/catalog/entries/"+entry.Id,HttpFixture.AdminToken,human,HttpStatusCode.OK,token).ConfigureAwait(false);
            manifest.Entries[0].Entry.Summary="Source version three";Check.Equal("Preserved",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Human edit wins");
            entry=(await f.Services.Database.ExerciseCatalog.ReadAsync(entry.Id,token).ConfigureAwait(false))!;Check.Equal("Human editorial text",entry.Summary,"Human content preserved");
            manifest.Entries[0].Key="duplicate-key";Check.Equal("DuplicateName",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"A different source key cannot duplicate an exact existing name");
            manifest.Entries[0].Key="movement-one";
            await Api.ExpectAsync(f,HttpMethod.Delete,"/api/v1/admin/catalog/entries/"+entry.Id+"?expectedRevision="+entry.Revision,HttpFixture.AdminToken,null,HttpStatusCode.NoContent,token).ConfigureAwait(false);
            Check.Equal("Preserved",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Deleted identity is not resurrected");
        }
        private static string HistoricalSourceHash(CatalogImportSource source,CatalogEntryRequest request,string slug)
        {
            CatalogEntryRequest historical=JsonSerializer.Deserialize<CatalogEntryRequest>(ApiJson.Serialize(request),ApiJson.Api)!;
            historical.ModalityDetails??=new CatalogModalityDetails();historical.Slug=slug;historical.ExpectedRevision=null;historical.LessonIds=new List<string>();historical.Labels=new List<string>{"import-draft"};historical.Tags=new Dictionary<string,string>{{"staff:importSource",source.Id},{"staff:importKey","movement-one"}};
            historical.Sources=new List<CatalogSource>{new CatalogSource{Url=source.Url,Title=source.Name,License=source.License,Attribution=source.Attribution,Method="Import"}};
            JsonObject historicalJson=(JsonObject)JsonSerializer.SerializeToNode(new{entry=historical,sourceRevision=source.Revision},ApiJson.Api)!;
            ((JsonObject)historicalJson["entry"]!).Remove("apparatusSetup");
            JsonObject historicalDetails=(JsonObject)historicalJson["entry"]!["modalityDetails"]!;
            foreach(string key in new[]{"attireKey","levelKey","notationSystemKey","notationText"})if(historicalDetails[key]==null)historicalDetails.Remove(key);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(historicalJson.ToJsonString(ApiJson.Api)))).ToLowerInvariant();
        }
        private static async Task ApparatusAsync(HttpFixture f,CancellationToken token)
        {
            f.Property.LibrarySection??=new PropertyLibrarySection();f.Property.LibrarySection.CatalogEquipment.Add(new PropertyOptionSeed{Key="sample-apparatus",Label="Example apparatus"});
            f.Property.LibrarySection.CatalogDescriptorProfiles=new List<PropertyCatalogDescriptorProfile>{new PropertyCatalogDescriptorProfile{ProgramKey="sample-program",Attires=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="sample-attire",Label="Example attire"}}}};
            await f.Database.DirectoryListings.Config.CreateProgramAsync(new DirectoryProgram{Id=IdGenerator.Generate("prg_"),Key="sample-program",Label="Sample program"},token).ConfigureAwait(false);
            await using FakeDirectoryServer site=new FakeDirectoryServer();CatalogImportSource source=await LocalSourceAsync(f,site,token).ConfigureAwait(false);CatalogImportManifest manifest=Manifest();CatalogEntryRequest request=manifest.Entries[0].Entry;
            request.Program="sample-program";request.Equipment.Add("sample-apparatus");request.ModalityDetails=new CatalogModalityDetails{AttireKey="sample-attire"};site.Respond("/one.json",200,"application/json",_=>ApiJson.Serialize(manifest));
            using HttpClient http=new HttpClient(new CatalogLoopbackTransport(site.BaseUrl));CatalogImportJobHandler handler=new CatalogImportJobHandler(http);
            CatalogImportOutcome first=First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false));Check.Equal("CreatedDraft",first.Outcome,"Earlier manifest creates a draft");CatalogEntry entry=(await f.Database.ExerciseCatalog.ReadAsync(first.EntryId!,token).ConfigureAwait(false))!;
            Check.Equal(HistoricalSourceHash(source,request,entry.Slug),(await f.Database.CatalogImports.ItemAsync(source.Id,"movement-one",token).ConfigureAwait(false))!.SourceHash,"A populated modality group keeps its exact fingerprint from before apparatus support");
            Check.Equal("Unchanged",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"No upgrade-only refresh");
            request.ApparatusSetup=new CatalogApparatusSetup{ApparatusKey="sample-apparatus",Manufacturer="Reviewed maker",Model="Reviewed model",SetupText="Source setup",SpringSetupText="Literal source spring text"};
            Check.Equal("RefreshedDraft",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Reviewed source adds typed apparatus context");
            entry=(await f.Database.ExerciseCatalog.ReadAsync(entry.Id,token).ConfigureAwait(false))!;Check.Equal("Literal source spring text",entry.ApparatusSetup!.SpringSetupText,"Source text preserved literally");Check.Equal("Draft",entry.Status,"Apparatus import never publishes");
            request.ApparatusSetup=null;request.Summary="Updated source summary";Check.Equal("RefreshedDraft",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Omitted group allows unrelated source refresh");
            entry=(await f.Database.ExerciseCatalog.ReadAsync(entry.Id,token).ConfigureAwait(false))!;Check.Equal("Reviewed model",entry.ApparatusSetup!.Model,"Omitted group keeps contextual model");Check.Equal("sample-attire",entry.ModalityDetails!.AttireKey,"Separate modality group unchanged");
            Check.Equal("Unchanged",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Effective preserved group fingerprint is stable");
            long revision=entry.Revision;request.ApparatusSetup=new CatalogApparatusSetup{ApparatusKey="sample-apparatus",SpringSetupText="Missing context"};Check.Equal("Invalid",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Invalid source spring context rejected");Check.Equal(revision,(await f.Database.ExerciseCatalog.ReadAsync(entry.Id,token).ConfigureAwait(false))!.Revision,"Invalid import cannot change revision");
            request.ApparatusSetup=new CatalogApparatusSetup();Check.Equal("RefreshedDraft",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Explicit empty import group clears apparatus");request.ApparatusSetup=null;Check.Equal("Unchanged",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Empty and absent apparatus canonicalize identically");
            entry=(await f.Database.ExerciseCatalog.ReadAsync(entry.Id,token).ConfigureAwait(false))!;CatalogEntryRequest human=JsonSerializer.Deserialize<CatalogEntryRequest>(ApiJson.Serialize(entry),ApiJson.Api)!;human.ExpectedRevision=entry.Revision;human.ApparatusSetup=new CatalogApparatusSetup{ApparatusKey="sample-apparatus",SetupText="Human reviewed setup"};await Api.ExpectAsync(f,HttpMethod.Put,"/api/v1/admin/catalog/entries/"+entry.Id,HttpFixture.AdminToken,human,HttpStatusCode.OK,token).ConfigureAwait(false);
            request.Summary="A later source revision";Check.Equal("Preserved",First(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Outcome,"Human apparatus changes win over source refresh");Check.Equal("Human reviewed setup",(await f.Database.ExerciseCatalog.ReadAsync(entry.Id,token).ConfigureAwait(false))!.ApparatusSetup!.SetupText,"Human source context untouched");
        }
        private static async Task FailuresAsync(HttpFixture f,CancellationToken token)
        {
            await using FakeDirectoryServer site=new FakeDirectoryServer();CatalogImportSource source=await LocalSourceAsync(f,site,token).ConfigureAwait(false);
            using HttpClient http=new HttpClient(new CatalogLoopbackTransport(site.BaseUrl));CatalogImportJobHandler handler=new CatalogImportJobHandler(http);
            foreach(int status in new[]{429,503,404,302})
            {site.Respond("/one.json",status,"application/json",_=>"{}");JobItemOutcome result=await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false);Check.True(!result.Success&&result.Retryable==(status==429||status==503),"Retry classification "+status);}
            foreach(string body in new[]{"{invalid",ApiJson.Serialize(new{version=1,publish=true,entries=new object[0]}),new string('x',1000001),ApiJson.Serialize(new{version=1,entries=new[]{new{key="same",entry=new{}},new{key="same",entry=new{}}}})})
            {site.Respond("/one.json",200,"application/json",_=>body);Check.True(!(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Success,"Malformed or over-limit manifest rejected");}
            site.Respond("/one.json",200,"text/html",_=>ApiJson.Serialize(Manifest()));Check.True(!(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Success,"Wrong media type rejected");
            site.Respond("/robots.txt",200,"text/plain",_=>"User-agent: *\nDisallow: /one.json\n");int before=site.Requests.Count(r=>r.StartsWith("GET /one.json",StringComparison.Ordinal));
            Check.True(!(await handler.ExecuteAsync(Context(f,source),token).ConfigureAwait(false)).Success,"Robots exclusion honored");Check.Equal(before,site.Requests.Count(r=>r.StartsWith("GET /one.json",StringComparison.Ordinal)),"Blocked manifest never requested");
            Check.True(await f.Services.Database.CatalogImports.ItemAsync(source.Id,"movement-one",token).ConfigureAwait(false)==null,"Failures create no catalog provenance");
        }
        private static async Task GuardsAsync(HttpFixture f,CancellationToken token)
        {
            CatalogImportSource source=await SourceAsync(f,token).ConfigureAwait(false);CatalogEntryRequest request=Manifest().Entries[0].Entry;request.Slug="atomic-import";request.Sources=new List<CatalogSource>{new CatalogSource{Title=source.Name,Url=source.Url,License=source.License,Method="Import"}};
            CatalogImportItem import=new CatalogImportItem{SourceId=source.Id,ItemKey="one",SourceHash="original",SourceRevision=source.Revision};ContentActor actor=new ContentActor{IsSystemAdmin=true,IsBreakGlass=true};
            Check.True(await f.Services.Library.ExerciseCatalog.ApplyImportAsync(actor,request,null,import,false,token).ConfigureAwait(false),"Initial transactional save");CatalogEntry previous=(await f.Services.Database.ExerciseCatalog.ReadAsync(import.EntryId,token).ConfigureAwait(false))!;
            source.Enabled=false;source.ReviewedUtc=null;source.Revision++;await f.Services.Database.CatalogImports.SaveAsync(source,source.Revision-1,token).ConfigureAwait(false);
            request.Summary="Must not be applied";Check.True(!await f.Services.Library.ExerciseCatalog.ApplyImportAsync(actor,request,previous,import,false,token).ConfigureAwait(false),"Revocation after read aborts atomic write");
            Check.Equal("original",(await f.Services.Database.CatalogImports.ItemAsync(source.Id,"one",token).ConfigureAwait(false))!.SourceHash,"Failed write leaves provenance unchanged");
            source.Enabled=true;source.ReviewedUtc=DateTime.UtcNow;source.Revision++;await f.Services.Database.CatalogImports.SaveAsync(source,source.Revision-1,token).ConfigureAwait(false);import.SourceRevision=source.Revision;
            foreach(string state in new[]{"held","published","human"})
            {
                await f.Raw.ExecuteQueryAsync("UPDATE catalogentries SET moderationstate="+(state=="held"?"'Hidden'":"'None'")+",status="+(state=="published"?"'Published'":"'Draft'")+",revision="+(state=="human"?"2":"1")+" WHERE propertyid='"+f.Services.PropertyId+"' AND id='"+previous.Id+"';",false,token).ConfigureAwait(false);
                Check.True(!await f.Services.Library.ExerciseCatalog.ApplyImportAsync(actor,request,previous,import,false,token).ConfigureAwait(false),"Concurrent "+state+" protects original entry");
            }
        }
        private static async Task RotationAsync(HttpFixture f,CancellationToken token)
        {
            List<CatalogImportSource> all=new List<CatalogImportSource>();for(int i=0;i<22;i++)all.Add(await SourceAsync(f,token,i.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false));
            List<CatalogImportSource> first=await f.Services.Database.CatalogImports.PlanAsync(Array.Empty<string>(),token).ConfigureAwait(false);Check.Equal(20,first.Count,"Scheduled bound");
            foreach(CatalogImportSource source in first)await f.Services.Database.CatalogImports.TouchAsync(source.Id,DateTime.UtcNow,token).ConfigureAwait(false);
            List<CatalogImportSource> next=await f.Services.Database.CatalogImports.PlanAsync(Array.Empty<string>(),token).ConfigureAwait(false);Check.True(next.Take(2).All(s=>first.All(old=>old.Id!=s.Id)),"Sources beyond the first window run next");
            Check.Equal(0,(await f.Services.Database.Deployment.ForProperty(PropertyIds.Generate()).CatalogImports.PlanAsync(new[]{all[0].Id},token).ConfigureAwait(false)).Count,"Explicit IDs cannot cross properties");
            CatalogImportJobHandler handler=new CatalogImportJobHandler();CatalogImportSource stale=all[0];JobItemContext context=Context(f,stale);stale.ReviewedUtc=null;stale.Revision++;await f.Services.Database.CatalogImports.SaveAsync(stale,stale.Revision-1,token).ConfigureAwait(false);
            Check.True(!(await handler.ExecuteAsync(context,token).ConfigureAwait(false)).Success,"Already planned jobs cannot use a withdrawn review");
        }
    }
}
