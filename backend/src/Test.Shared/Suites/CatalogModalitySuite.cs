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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services;

    /// <summary>Typed modality descriptors, old-client preservation, editorial safety and Unicode search.</summary>
    public static class CatalogModalitySuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id="CatalogModalities";
        /// <summary>Fresh HTTP/database cases using neutral property vocabularies and no external calls.</summary>
        public static TestSuiteDescriptor Build()=>new TestSuiteDescriptor(Id,"Catalog modality descriptors",new[]{
            CatalogTestKit.Http(Id,"NotationValidation","Configured instructional descriptors enforce pairs, bounds and full replacement compatibility",NotationValidationAsync),
            CatalogTestKit.Http(Id,"NotationPublic","Notation filters bind cursors and reviewed text is moderated, attributed and escaped",NotationPublicAsync),
            CatalogTestKit.Http(Id,"EnumerationCaps","Catalog entry and relationship requests retain 100-row caps with larger global configuration",CapsAsync),
            CatalogTestKit.Http(Id,"Configuration","Property-owned descriptors are bounded, validated and canonicalized without engine taxonomy",ConfigurationAsync),
            CatalogTestKit.Http(Id,"ReplacementAndCas","Localized names round-trip NFC; omission preserves data while explicit clear and stale CAS behave predictably",ReplacementAsync),
            CatalogTestKit.Http(Id,"PublicSearchAndHtml","Reviewed localized names are searchable, filtered and escaped in public HTML; drafts and holds stay private",PublicAsync),
            CatalogTestKit.Http(Id,"ImportAndModeration","Imported descriptors stay draft-only and preserve human edits; text follows wordfilter and transactional content review",ImportAsync)
        });
        private const string _Admin="/api/v1/admin/catalog/entries";
        private const string _Public="/api/v1/public/catalog/entries";
        private static async Task SetupAsync(HttpFixture f,CancellationToken token)
        {
            LibraryTestKit.Relax(f);f.Property.LibrarySection??=new PropertyLibrarySection();f.Property.DirectorySection??=new PropertyDirectorySection();
            f.Property.DirectorySection.Programs.Add(new PropertyOptionSeed{Key="sample-program",Label="Sample program"});
            await f.Database.DirectoryListings.Config.CreateProgramAsync(new DirectoryProgram{Id=IdGenerator.Generate("prg_"),Key="sample-program",Label="Sample program"},token).ConfigureAwait(false);
            f.Property.LibrarySection.CatalogDescriptorProfiles=new List<PropertyCatalogDescriptorProfile>{new PropertyCatalogDescriptorProfile{ProgramKey="sample-program",FamilyLabel="Sample family",Families=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="sample-family",Label="Example family"}},NameLanguages=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="el-Latn",Label="Example Latin-script language"},new PropertyOptionSeed{Key="el-Grek",Label="Example native-script language"}}}};
        }
        private static Dictionary<string,object?> Body(string slug)=>new Dictionary<string,object?>{{"name","Sample "+slug},{"slug",slug},{"kind","Technique"},{"program","sample-program"},{"instructions",new[]{"Reviewed example instruction"}},{"sources",new[]{new{title="Reviewed source",license="Original"}}}};
        private static object Details(string text="Para\u0301deigma")=>new{familyKey="sample-family",localizedNames=new[]{new{languageTag="EL-latn",text},new{languageTag="el-Grek",text="Παράδειγμα"}}};
        private static Task<JsonElement> CreateAsync(HttpFixture f,string slug,CancellationToken token,object? details=null,string? accessToken=null)
        {Dictionary<string,object?> body=Body(slug);if(details!=null)body["modalityDetails"]=details;return Api.ExpectAsync(f,HttpMethod.Post,_Admin,accessToken??HttpFixture.AdminToken,body,HttpStatusCode.Created,token);}
        private static async Task<JsonElement> PublishAsync(HttpFixture f,JsonElement entry,CancellationToken token)
        {
            foreach(string action in new[]{"submit","approve"})entry=await Api.ExpectAsync(f,HttpMethod.Post,_Admin+"/"+Api.Str(entry,"id")+"/"+action,HttpFixture.AdminToken,new{expectedRevision=entry.GetProperty("revision").GetInt64(),note="Reviewed licensed source"},HttpStatusCode.OK,token).ConfigureAwait(false);return entry;
        }
        private static async Task CapsAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);f.Settings.Enumeration.MaxResultsAdmin=500;f.Settings.Enumeration.MaxResultsMember=500;
            for(int i=0;i<101;i++)await f.Database.ExerciseCatalog.SaveAsync(new CatalogEntry{Id=IdGenerator.Generate("cex_"),Name="Bounded descriptor "+i,Slug="bounded-descriptor-"+i,Kind="Technique",Status="Published",Program="sample-program",ModalityDetails=new CatalogModalityDetails{FamilyKey="sample-family"}},null,true,null,token).ConfigureAwait(false);
            JsonElement first=await Api.ExpectAsync(f,HttpMethod.Post,_Admin+"/enumerate",HttpFixture.AdminToken,new{maxResults=101,filters=new{familyKey="sample-family"}},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(100,first.GetProperty("maxResults").GetInt32(),"Catalog route clamps administrative setting");Check.Equal(100,first.GetProperty("objects").GetArrayLength(),"First filtered page is bounded");
            JsonElement second=await Api.ExpectAsync(f,HttpMethod.Post,_Admin+"/enumerate",HttpFixture.AdminToken,new{maxResults=101,filters=new{familyKey="sample-family"},continuationToken=Api.Str(first,"continuationToken")},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(1,second.GetProperty("objects").GetArrayLength(),"All names/families remain reachable through paging");Check.True(!DirectoryTestKit.Ids(first).Intersect(DirectoryTestKit.Ids(second)).Any(),"No duplicate parent rows");
            await Api.ExpectAsync(f,HttpMethod.Post,_Admin+"/enumerate",HttpFixture.AdminToken,new{maxResults=101,filters=new{familyKey="other"},continuationToken=Api.Str(first,"continuationToken")},HttpStatusCode.BadRequest,token).ConfigureAwait(false);
            await Api.ExpectAsync(f,HttpMethod.Post,_Admin+"/enumerate",HttpFixture.AdminToken,new{maxResults=101,filters=new{familyKey="sample-family",nameLanguage="el-Latn"},continuationToken=Api.Str(first,"continuationToken")},HttpStatusCode.BadRequest,token).ConfigureAwait(false);
            JsonElement publicPage=await Api.ExpectAsync(f,HttpMethod.Post,_Public+"/enumerate",null,new{maxResults=101},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(100,publicPage.GetProperty("maxResults").GetInt32(),"Public route retains fixed cap too");
            string id=Api.Str(first.GetProperty("objects")[0],"id");foreach(string path in new[]{_Admin,_Public}){JsonElement edges=await Api.ExpectAsync(f,HttpMethod.Post,path+"/"+id+"/relationships/enumerate",path==_Admin?HttpFixture.AdminToken:null,new{maxResults=101},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(100,edges.GetProperty("maxResults").GetInt32(),"Relationship route shares fixed 100 cap");}
        }
        private static void ConfigureNotation(HttpFixture f)
        {
            PropertyCatalogDescriptorProfile p=f.Property.LibrarySection!.CatalogDescriptorProfiles[0];
            p.AttireLabel="Attire <label>";p.Attires=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="sample-attire",Label="Attire <option>"}};
            p.LevelLabel="Content <level>";p.Levels=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="sample-level",Label="Example instructional level"}};
            p.NotationSystems=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="sample-system",Label="Example <system>"}};
        }
        private static object Notation(string text="  E\u0301xample <script>  ")=>new{attireKey="sample-attire",levelKey="sample-level",notationSystemKey="sample-system",notationText=text};
        private static async Task NotationValidationAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);ConfigureNotation(f);
            PropertyCatalogDescriptorProfile profile=f.Property.LibrarySection!.CatalogDescriptorProfiles[0];
            Check.True(!PropertyValidator.Validate(f.Property).Any(p=>p.Contains("catalogDescriptorProfiles",StringComparison.Ordinal)),"Neutral descriptor configuration validates");
            foreach(string group in new[]{"attires","levels","notationSystems"})
            {
                List<PropertyOptionSeed> original=group=="attires"?profile.Attires:group=="levels"?profile.Levels:profile.NotationSystems;
                original.AddRange(Enumerable.Range(0,100).Select(i=>new PropertyOptionSeed{Key="option-"+i,Label="Option "+i}));
                Check.True(PropertyValidator.Validate(f.Property).Any(p=>p.Contains("."+group+" may hold at most 100",StringComparison.Ordinal)),"Each configured vocabulary has a fixed 100-option cap");original.RemoveRange(1,100);
            }
            profile.NotationSystems=null!;Check.True(PropertyValidator.Validate(f.Property).Any(p=>p.Contains("nonnull",StringComparison.Ordinal)),"Null vocabularies rejected");ConfigureNotation(f);
            foreach(object invalid in new object[]{new{attireKey="other"},new{levelKey="other"},new{notationSystemKey="other",notationText="Text"},new{notationSystemKey="sample-system"},new{notationText="Text"},new{notationSystemKey="sample-system",notationText="   "},Notation(new string('x',501))})
            {Dictionary<string,object?> body=Body("invalid-notation");body["modalityDetails"]=invalid;await Api.ExpectAsync(f,HttpMethod.Post,_Admin,HttpFixture.AdminToken,body,HttpStatusCode.BadRequest,token).ConfigureAwait(false);}
            JsonElement entry=await CreateAsync(f,"notation-preservation",token,Notation(new string('x',500))).ConfigureAwait(false);string id=Api.Str(entry,"id");
            Check.Equal(500,Api.Str(entry.GetProperty("modalityDetails"),"notationText").Length,"Inclusive 500-character bound");
            Dictionary<string,object?> update=Body("notation-preservation");update["expectedRevision"]=1;update["modalityDetails"]=null;
            entry=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal("sample-system",Api.Str(entry.GetProperty("modalityDetails"),"notationSystemKey"),"Null entire group preserves new fields");
            update["expectedRevision"]=2;update["program"]=null;await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.BadRequest,token).ConfigureAwait(false);update["program"]="sample-program";
            update["expectedRevision"]=1;update["modalityDetails"]=new{};await Api.ExpectErrorAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.Conflict,"CatalogChanged",token).ConfigureAwait(false);
            Check.Equal("sample-attire",(await f.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false))!.ModalityDetails!.AttireKey,"Stale clear cannot mutate typed scalar columns");
            update["expectedRevision"]=2;update["modalityDetails"]=Details();entry=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.OK,token).ConfigureAwait(false);
            foreach(string field in new[]{"attireKey","levelKey","notationSystemKey","notationText"})Check.Equal(JsonValueKind.Null,entry.GetProperty("modalityDetails").GetProperty(field).ValueKind,"Explicit older-shaped replacement clears omitted newer fields");
        }
        private static async Task NotationPublicAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);ConfigureNotation(f);TestMember editor=await LibraryTestKit.EditorAsync(f,token).ConfigureAwait(false);
            f.Settings.Moderation.WordFilterEnabled=true;await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/moderation/word-filter",HttpFixture.AdminToken,new{term="blockednotation",action="Block"},HttpStatusCode.Created,token).ConfigureAwait(false);
            Dictionary<string,object?> blocked=Body("blocked-notation");blocked["modalityDetails"]=Notation("blockednotation");await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin,editor.AccessToken,blocked,HttpStatusCode.BadRequest,"ContentFiltered",token).ConfigureAwait(false);
            JsonElement draft=await CreateAsync(f,"public-notation",token,Notation(),editor.AccessToken).ConfigureAwait(false);string id=Api.Str(draft,"id");
            Check.Equal("Éxample <script>",Api.Str(draft.GetProperty("modalityDetails"),"notationText"),"Notation is NFC normalized and trimmed");
            JsonElement changes=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/content-review/enumerate",HttpFixture.AdminToken,new{filters=new{targetType="CatalogEntry",targetId=id}},HttpStatusCode.OK,token).ConfigureAwait(false);Check.True(changes.GetProperty("objects").EnumerateArray().Any(row=>row.GetRawText().Contains("notationtext",StringComparison.OrdinalIgnoreCase)),"Notation participates in transactional attribution");
            await PublishAsync(f,draft,token).ConfigureAwait(false);await PublishAsync(f,await CreateAsync(f,"second-notation",token,Notation()).ConfigureAwait(false),token).ConfigureAwait(false);
            foreach(string field in new[]{"attireKey","levelKey","notationSystemKey"})
            {
                string value=field=="attireKey"?"sample-attire":field=="levelKey"?"sample-level":"sample-system";
                Dictionary<string,string> filters=new Dictionary<string,string>{{field,value}};
                JsonElement first=await Api.ExpectAsync(f,HttpMethod.Post,_Public+"/enumerate",null,new{maxResults=1,filters},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(1,first.GetProperty("objects").GetArrayLength(),"Exact descriptor filter matches");
                filters[field]="other";await Api.ExpectAsync(f,HttpMethod.Post,_Public+"/enumerate",null,new{maxResults=1,filters,continuationToken=Api.Str(first,"continuationToken")},HttpStatusCode.BadRequest,token).ConfigureAwait(false);
                JsonElement empty=await Api.ExpectAsync(f,HttpMethod.Post,_Public+"/enumerate",null,new{filters},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(0,empty.GetProperty("objects").GetArrayLength(),"Different exact descriptor does not match");
            }
            TrellisKit.Server.Services.PublicPages.PublicPageResult page=await f.Services.PublicPages.ResolveAsync("catalog","public-notation","127.0.0.1",token).ConfigureAwait(false);
            foreach(string text in new[]{"Attire &lt;label&gt;","Attire &lt;option&gt;","Content &lt;level&gt;","Example &lt;system&gt;","xample &lt;script&gt;"})Check.True(page.Body.Contains(text,StringComparison.Ordinal),"SSR escapes configured labels and notation: "+text);
        }
        private static async Task ConfigurationAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);
            JsonElement config=await Api.ExpectAsync(f,HttpMethod.Get,"/api/v1/catalog/config",null,null,HttpStatusCode.OK,token).ConfigureAwait(false);
            Check.Equal(1,config.GetProperty("descriptorProfiles").GetArrayLength(),"Only configured profiles returned");Check.Equal("el-Latn",Api.Str(config.GetProperty("descriptorProfiles")[0].GetProperty("nameLanguages")[0],"key"),"Language script case is canonical");
            Check.True(!PropertyValidator.Validate(f.Property).Any(p=>p.Contains("catalogDescriptorProfiles",StringComparison.Ordinal)),"Neutral configured descriptors validate");
            PropertyCatalogDescriptorProfile profile=f.Property.LibrarySection!.CatalogDescriptorProfiles[0];profile.NameLanguages.Add(new PropertyOptionSeed{Key="bad--tag",Label="Invalid"});
            Check.True(PropertyValidator.Validate(f.Property).Any(p=>p.Contains("language[-Script]",StringComparison.Ordinal)),"Malformed language tags rejected by property validation");profile.NameLanguages.RemoveAt(2);
            f.Property.LibrarySection.CatalogDescriptorProfiles.Add(profile);Check.True(PropertyValidator.Validate(f.Property).Any(p=>p.Contains("unique configured",StringComparison.Ordinal)),"Duplicate program profiles rejected");f.Property.LibrarySection.CatalogDescriptorProfiles.RemoveAt(1);
            foreach(object invalid in new object[]{new{localizedNames=new[]{new{languageTag="en",text="Unconfigured language"}}},new{familyKey="unconfigured"},new{localizedNames=new[]{new{languageTag="el-Latn",text="Parádeigma"},new{languageTag="EL-LATN",text="Para\u0301deigma"}}},new{localizedNames=Enumerable.Range(0,11).Select(i=>new{languageTag="el-Latn",text="Name "+i}).ToArray()},new{localizedNames=new[]{new{languageTag="el-Latn",text=new string('x',161)}}}})
            {Dictionary<string,object?> request=Body("invalid-descriptor");request["modalityDetails"]=invalid;await Api.ExpectAsync(f,HttpMethod.Post,_Admin,HttpFixture.AdminToken,request,HttpStatusCode.BadRequest,token).ConfigureAwait(false);}
        }
        private static async Task ReplacementAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);JsonElement legacy=await CreateAsync(f,"legacy-entry",token).ConfigureAwait(false);Check.Equal(0,legacy.GetProperty("modalityDetails").GetProperty("localizedNames").GetArrayLength(),"Legacy response is an empty typed group");
            JsonElement entry=await CreateAsync(f,"localized-entry",token,Details()).ConfigureAwait(false);string id=Api.Str(entry,"id");Check.Equal("Parádeigma",Api.Str(entry.GetProperty("modalityDetails").GetProperty("localizedNames")[0],"text"),"Unicode normalized without stripping diacritics");
            Dictionary<string,object?> oldClient=Body("localized-entry");oldClient["expectedRevision"]=1;
            JsonElement changed=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,oldClient,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(2,changed.GetProperty("modalityDetails").GetProperty("localizedNames").GetArrayLength(),"Omitted group preserved");
            oldClient["expectedRevision"]=2;oldClient["program"]=null;
            await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,oldClient,HttpStatusCode.BadRequest,token).ConfigureAwait(false);
            Check.Equal(2L,(await f.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false))!.Revision,"Changing program with preserved incompatible descriptors cannot mutate the entry");
            oldClient["program"]="sample-program";
            oldClient["expectedRevision"]=2;oldClient["modalityDetails"]=null;changed=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,oldClient,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal("sample-family",Api.Str(changed.GetProperty("modalityDetails"),"familyKey"),"Null group preserves classification");
            oldClient["modalityDetails"]=new{};await Api.ExpectErrorAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,oldClient,HttpStatusCode.Conflict,"CatalogChanged",token).ConfigureAwait(false);
            Check.Equal(2,(await f.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false))!.ModalityDetails!.LocalizedNames.Count,"Stale CAS did not delete child names");
            oldClient["expectedRevision"]=3;changed=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,oldClient,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(0,changed.GetProperty("modalityDetails").GetProperty("localizedNames").GetArrayLength(),"Explicit empty group clears children");Check.True(changed.GetProperty("modalityDetails").GetProperty("familyKey").ValueKind==JsonValueKind.Null,"Explicit empty group clears scalar");
            Check.True(await f.Database.Deployment.ForProperty(PropertyIds.Generate()).ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false)==null,"No cross-property names or family read");
        }
        private static async Task PublicAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);
            Dictionary<string,object?> input=Body("public-descriptors");input["aliases"]=new[]{"E\u0301xample legacy"};input["modalityDetails"]=Details("Example <script> name");
            JsonElement entry=await Api.ExpectAsync(f,HttpMethod.Post,_Admin,HttpFixture.AdminToken,input,HttpStatusCode.Created,token).ConfigureAwait(false);string id=Api.Str(entry,"id");
            await Api.ExpectAsync(f,HttpMethod.Get,_Public+"/"+id,null,null,HttpStatusCode.NotFound,token).ConfigureAwait(false);entry=await PublishAsync(f,entry,token).ConfigureAwait(false);
            JsonElement found=await Api.ExpectAsync(f,HttpMethod.Post,_Public+"/enumerate",null,new{search="Παράδειγμα",filters=new{familyKey="sample-family",nameLanguage="EL-grek"}},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(1,found.GetProperty("objects").GetArrayLength(),"Native script search and canonical language/family filters match published entry");
            JsonElement legacyAlias=await Api.ExpectAsync(f,HttpMethod.Post,_Public+"/enumerate",null,new{search="Éxample legacy"},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(1,legacyAlias.GetProperty("objects").GetArrayLength(),"NFC query handling also preserves matching of existing decomposed aliases");
            TrellisKit.Server.Services.PublicPages.PublicPageResult page=await f.Services.PublicPages.ResolveAsync("catalog",Api.Str(entry,"slug"),"127.0.0.1",token).ConfigureAwait(false);Check.Equal(200,page.StatusCode,"Public catalog HTML resolves");Check.True(page.Body.Contains("Example &lt;script&gt; name",StringComparison.Ordinal)&&!page.Body.Contains("Example <script> name",StringComparison.Ordinal),"Localized text is escaped in HTML");Check.True(page.Body.Contains("alternateName",StringComparison.Ordinal)&&page.Body.Contains("Example family",StringComparison.Ordinal),"Reviewed aliases and configured family appear in public representation");
            f.Property.LibrarySection!.CatalogDescriptorProfiles.Clear();JsonElement readable=await Api.ExpectAsync(f,HttpMethod.Get,_Public+"/"+id,null,null,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal("sample-family",Api.Str(readable.GetProperty("modalityDetails"),"familyKey"),"Vocabulary removal does not silently erase published values");
            CatalogEntry stored=(await f.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false))!;long revision=stored.Revision;stored.ModerationState=ModerationStateEnum.Hidden;stored.Revision++;Check.True(await f.Database.ExerciseCatalog.SaveAsync(stored,revision,false,null,token).ConfigureAwait(false),"Moderation fixture persisted");
            found=await Api.ExpectAsync(f,HttpMethod.Post,_Public+"/enumerate",null,new{search="Παράδειγμα"},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(0,found.GetProperty("objects").GetArrayLength(),"Moderation removes localized-name search result");Check.Equal(404,(await f.Services.PublicPages.ResolveAsync("catalog",stored.Slug,"127.0.0.1",token).ConfigureAwait(false)).StatusCode,"Moderated descriptors cannot leak through SSR");
        }
        private static async Task ImportAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);TestMember editor=await LibraryTestKit.EditorAsync(f,token).ConfigureAwait(false);f.Settings.Moderation.WordFilterEnabled=true;
            await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/moderation/word-filter",HttpFixture.AdminToken,new{term="blockeddescriptor",action="Block"},HttpStatusCode.Created,token).ConfigureAwait(false);
            Dictionary<string,object?> blocked=Body("blocked-localized");blocked["modalityDetails"]=Details("blockeddescriptor");await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin,editor.AccessToken,blocked,HttpStatusCode.BadRequest,"ContentFiltered",token).ConfigureAwait(false);
            JsonElement created=await CreateAsync(f,"attributed-localized",token,Details("Unique localized evidence"),editor.AccessToken).ConfigureAwait(false);string id=Api.Str(created,"id");JsonElement changes=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/content-review/enumerate",HttpFixture.AdminToken,new{filters=new{targetType="CatalogEntry",targetId=id}},HttpStatusCode.OK,token).ConfigureAwait(false);Check.True(changes.GetProperty("objects").EnumerateArray().Any(row=>row.GetRawText().Contains("Unique localized evidence",StringComparison.Ordinal)),"Localized names participate in transactional content review");
            CatalogImportSource source=new CatalogImportSource{Id=IdGenerator.Generate("cis_"),Name="Reviewed source",Url="https://example.test/catalog.json",License="CC0-1.0",Enabled=true,ReviewedUtc=DateTime.UtcNow};await f.Database.CatalogImports.SaveAsync(source,null,token).ConfigureAwait(false);
            Dictionary<string,object?> importBody=Body("imported-localized");ConfigureNotation(f);CatalogModalityDetails importedDetails=JsonSerializer.Deserialize<CatalogModalityDetails>(ApiJson.Serialize(Details()),ApiJson.Api)!;importedDetails.AttireKey="sample-attire";importedDetails.LevelKey="sample-level";importedDetails.NotationSystemKey="sample-system";importedDetails.NotationText="Reviewed import notation";importBody["modalityDetails"]=importedDetails;CatalogEntryRequest request=JsonSerializer.Deserialize<CatalogEntryRequest>(ApiJson.Serialize(importBody),ApiJson.Api)!;
            CatalogImportItem import=new CatalogImportItem{SourceId=source.Id,SourceRevision=1,ItemKey="item",SourceHash="source-one"};ContentActor actor=new ContentActor{IsSystemAdmin=true,IsBreakGlass=true,IsStaff=true,HasMembership=true,AgeAttested=true};
            Check.True(await f.Services.Library.ExerciseCatalog.ApplyImportAsync(actor,request,null,import,false,token).ConfigureAwait(false),"Reviewed source imports typed descriptors");CatalogEntry imported=(await f.Database.ExerciseCatalog.ReadAsync(import.EntryId,token).ConfigureAwait(false))!;Check.Equal("Draft",imported.Status,"Import never publishes descriptors");Check.Equal("sample-system",imported.ModalityDetails!.NotationSystemKey,"Imported notation retained");Check.Equal(2,imported.ModalityDetails.LocalizedNames.Count,"Imported relational names retained");Check.Equal("sample-family",imported.ModalityDetails.FamilyKey,"Imported family retained");
            request.ModalityDetails=null;import.SourceHash="source-two";Check.True(await f.Services.Library.ExerciseCatalog.ApplyImportAsync(actor,request,imported,import,false,token).ConfigureAwait(false),"Old manifest group omission can refresh");imported=(await f.Database.ExerciseCatalog.ReadAsync(import.EntryId,token).ConfigureAwait(false))!;Check.Equal("sample-attire",imported.ModalityDetails!.AttireKey,"Omitted source group preserves earlier descriptors");Check.Equal(2,imported.ModalityDetails.LocalizedNames.Count,"Old manifest preserves relational names too");
            long current=imported.Revision;imported.ModalityDetails.FamilyKey=null;imported.ModalityDetails.NotationText="Human reviewed notation";imported.Revision++;Check.True(await f.Database.ExerciseCatalog.SaveAsync(imported,current,true,null,token).ConfigureAwait(false),"Human edit fixture saved");Check.True(!await f.Services.Library.ExerciseCatalog.ApplyImportAsync(actor,request,imported,import,false,token).ConfigureAwait(false),"Importer cannot overwrite human descriptor edits");
        }
    }
}
