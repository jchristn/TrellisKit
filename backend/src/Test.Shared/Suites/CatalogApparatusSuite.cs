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

    /// <summary>Reviewed apparatus context, compatibility, privacy and literal source rendering through real HTTP and PostgreSQL.</summary>
    public static class CatalogApparatusSuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id="CatalogApparatus";
        /// <summary>Independent bounded fixtures with neutral equipment and no external calls.</summary>
        public static TestSuiteDescriptor Build()=>new TestSuiteDescriptor(Id,"Catalog apparatus setup",new[]{
            CatalogTestKit.Http(Id,"Validation","Apparatus requires configured selected equipment and complete contextual spring fields within bounds",ValidationAsync),
            CatalogTestKit.Http(Id,"Replacement","Old clients preserve separate setup; equipment removal, stale revisions and explicit replacement remain safe",ReplacementAsync),
            CatalogTestKit.Http(Id,"ReviewedPublic","Apparatus text is moderated, attributed, escaped with context and hidden with unpublished or held entries",PublicAsync)
        });
        private const string _Admin="/api/v1/admin/catalog/entries";
        private const string _Public="/api/v1/public/catalog/entries";
        private static async Task SetupAsync(HttpFixture f,CancellationToken token)
        {
            LibraryTestKit.Relax(f);f.Property.LibrarySection??=new PropertyLibrarySection();
            f.Property.LibrarySection.CatalogEquipment=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="sample-apparatus",Label="Example <apparatus>"},new PropertyOptionSeed{Key="other-apparatus",Label="Other apparatus"}};
            await f.Database.DirectoryListings.Config.CreateProgramAsync(new DirectoryProgram{Id=IdGenerator.Generate("prg_"),Key="sample-program",Label="Sample program"},token).ConfigureAwait(false);
        }
        private static Dictionary<string,object?> Body(string slug)=>new Dictionary<string,object?>{{"kind","Technique"},{"name","Example "+slug},{"slug",slug},{"program","sample-program"},{"equipment",new[]{"sample-apparatus"}},{"instructions",new[]{"Reviewed source instruction"}},{"sources",new[]{new{title="Reviewed source",license="Original"}}}};
        private static CatalogApparatusSetup Details()=>new CatalogApparatusSetup{ApparatusKey="sample-apparatus",Manufacturer="  E\u0301xample <maker>  ",Model="Example <model>",SetupText="  Source <setup>  literal\nSecond line  ",SpringSetupText="Literal <spring>\nSource notation"};
        private static Task<JsonElement> CreateAsync(HttpFixture f,string slug,CancellationToken token,CatalogApparatusSetup? setup=null,string? accessToken=null)
        {Dictionary<string,object?> body=Body(slug);if(setup!=null)body["apparatusSetup"]=setup;return Api.ExpectAsync(f,HttpMethod.Post,_Admin,accessToken??HttpFixture.AdminToken,body,HttpStatusCode.Created,token);}
        private static async Task ValidationAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);
            foreach(Action<CatalogApparatusSetup> change in new Action<CatalogApparatusSetup>[]
            {
                s=>s.ApparatusKey="unconfigured",s=>s.ApparatusKey="other-apparatus",s=>s.ApparatusKey=null,s=>s.Manufacturer=null,s=>s.Manufacturer="  ",s=>s.Model=null,s=>s.Model="  ",
                s=>s.Manufacturer=new string('m',121),s=>s.Model=new string('m',121),s=>s.SetupText=new string('x',501),s=>s.SpringSetupText=new string('x',501)
            })
            {CatalogApparatusSetup invalid=Details();change(invalid);Dictionary<string,object?> body=Body("invalid-apparatus");body["apparatusSetup"]=invalid;await Api.ExpectAsync(f,HttpMethod.Post,_Admin,HttpFixture.AdminToken,body,HttpStatusCode.BadRequest,token).ConfigureAwait(false);}
            CatalogApparatusSetup max=Details();max.Manufacturer=new string('m',120);max.Model=new string('m',120);max.SetupText=new string('x',500);max.SpringSetupText=new string('x',500);
            JsonElement saved=await CreateAsync(f,"maximum-apparatus",token,max).ConfigureAwait(false);Check.Equal(500,Api.Str(saved.GetProperty("apparatusSetup"),"springSetupText").Length,"Inclusive 500 bound");
            JsonElement general=await CreateAsync(f,"general-apparatus",token,new CatalogApparatusSetup{ApparatusKey="sample-apparatus",SetupText="General source setup"}).ConfigureAwait(false);Check.Equal(JsonValueKind.Null,general.GetProperty("apparatusSetup").GetProperty("model").ValueKind,"General setup does not invent model context");
            JsonElement empty=await CreateAsync(f,"empty-apparatus",token,new CatalogApparatusSetup{Manufacturer=" ",Model=" ",SetupText="\n",SpringSetupText=" "}).ConfigureAwait(false);Check.True(empty.GetProperty("apparatusSetup").EnumerateObject().All(p=>p.Value.ValueKind==JsonValueKind.Null),"Whitespace-only entire group clears without inferred apparatus");
        }
        private static async Task ReplacementAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);JsonElement legacy=await CreateAsync(f,"legacy-apparatus",token).ConfigureAwait(false);Check.True(legacy.GetProperty("apparatusSetup").EnumerateObject().All(p=>p.Value.ValueKind==JsonValueKind.Null),"Legacy entry reads an empty typed group");
            JsonElement entry=await CreateAsync(f,"retained-apparatus",token,Details()).ConfigureAwait(false);string id=Api.Str(entry,"id");Check.Equal("Éxample <maker>",Api.Str(entry.GetProperty("apparatusSetup"),"manufacturer"),"Manufacturer NFC normalization preserves literal text");
            Dictionary<string,object?> update=Body("retained-apparatus");update["expectedRevision"]=1;update["modalityDetails"]=new{};
            entry=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal("Literal <spring>\nSource notation",Api.Str(entry.GetProperty("apparatusSetup"),"springSetupText"),"Older client replacing modality group preserves separate setup and literal line breaks");
            update["expectedRevision"]=2;update["apparatusSetup"]=null;update["equipment"]=Array.Empty<string>();await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.BadRequest,token).ConfigureAwait(false);
            Check.Equal(2L,(await f.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false))!.Revision,"Equipment removal cannot silently discard preserved context");
            update["equipment"]=new[]{"sample-apparatus"};entry=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal("sample-apparatus",Api.Str(entry.GetProperty("apparatusSetup"),"apparatusKey"),"Null whole group preserves context");
            update["apparatusSetup"]=new{};await Api.ExpectErrorAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.Conflict,"CatalogChanged",token).ConfigureAwait(false);Check.True((await f.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false))!.ApparatusSetup!.SpringSetupText!=null,"Stale clear leaves setup intact");
            update["expectedRevision"]=3;update["apparatusSetup"]=new{apparatusKey="sample-apparatus",setupText="Replacement"};entry=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(JsonValueKind.Null,entry.GetProperty("apparatusSetup").GetProperty("springSetupText").ValueKind,"Explicit partial object replaces group and clears old springs");
            update["expectedRevision"]=4;update["apparatusSetup"]=new{};update["equipment"]=Array.Empty<string>();entry=await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.OK,token).ConfigureAwait(false);Check.True(entry.GetProperty("apparatusSetup").EnumerateObject().All(p=>p.Value.ValueKind==JsonValueKind.Null),"Explicit clear permits equipment removal");
            Check.True(await f.Database.Deployment.ForProperty(PropertyIds.Generate()).ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false)==null,"Apparatus context is property scoped");
        }
        private static async Task PublicAsync(HttpFixture f,CancellationToken token)
        {
            await SetupAsync(f,token).ConfigureAwait(false);TestMember editor=await LibraryTestKit.EditorAsync(f,token).ConfigureAwait(false);f.Settings.Moderation.WordFilterEnabled=true;
            await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/moderation/word-filter",HttpFixture.AdminToken,new{term="blockedapparatus",action="Block"},HttpStatusCode.Created,token).ConfigureAwait(false);
            foreach(Action<CatalogApparatusSetup> change in new Action<CatalogApparatusSetup>[]{s=>s.Manufacturer="blockedapparatus",s=>s.Model="blockedapparatus",s=>s.SetupText="blockedapparatus",s=>s.SpringSetupText="blockedapparatus"})
            {CatalogApparatusSetup invalid=Details();change(invalid);Dictionary<string,object?> body=Body("blocked-apparatus");body["apparatusSetup"]=invalid;await Api.ExpectErrorAsync(f,HttpMethod.Post,_Admin,editor.AccessToken,body,HttpStatusCode.BadRequest,"ContentFiltered",token).ConfigureAwait(false);}
            JsonElement entry=await CreateAsync(f,"public-apparatus",token,Details(),editor.AccessToken).ConfigureAwait(false);string id=Api.Str(entry,"id");await Api.ExpectAsync(f,HttpMethod.Get,_Public+"/"+id,null,null,HttpStatusCode.NotFound,token).ConfigureAwait(false);
            JsonElement changes=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/content-review/enumerate",HttpFixture.AdminToken,new{filters=new{targetType="CatalogEntry",targetId=id}},HttpStatusCode.OK,token).ConfigureAwait(false);Check.True(changes.GetProperty("objects").EnumerateArray().Any(row=>row.GetRawText().Contains("apparatusspringsetuptext",StringComparison.OrdinalIgnoreCase)),"Setup change belongs to transactional contribution review");
            foreach(string action in new[]{"submit","approve"})entry=await Api.ExpectAsync(f,HttpMethod.Post,_Admin+"/"+id+"/"+action,HttpFixture.AdminToken,new{expectedRevision=entry.GetProperty("revision").GetInt64(),note="Reviewed source context"},HttpStatusCode.OK,token).ConfigureAwait(false);
            JsonElement page=await Api.ExpectAsync(f,HttpMethod.Post,_Public+"/enumerate",null,new{maxResults=1,filters=new{equipment="sample-apparatus"}},HttpStatusCode.OK,token).ConfigureAwait(false);Check.Equal(1,page.GetProperty("objects").GetArrayLength(),"Existing equipment filter finds reviewed apparatus");
            string html=(await f.Services.PublicPages.ResolveAsync("catalog","public-apparatus","127.0.0.1",token).ConfigureAwait(false)).Body;
            foreach(string text in new[]{"Example &lt;apparatus&gt;","xample &lt;maker&gt;","Example &lt;model&gt;","Source &lt;setup&gt;  literal","setup-notes{white-space:pre-wrap;overflow-wrap:anywhere}","Literal &lt;spring&gt;","do not transfer to other equipment"})Check.True(html.Contains(text,StringComparison.Ordinal),"Public context escapes values: "+text);
            f.Property.LibrarySection!.CatalogEquipment.Clear();await Api.ExpectAsync(f,HttpMethod.Get,_Public+"/"+id,null,null,HttpStatusCode.OK,token).ConfigureAwait(false);Dictionary<string,object?> update=Body("public-apparatus");update["expectedRevision"]=entry.GetProperty("revision").GetInt64();await Api.ExpectAsync(f,HttpMethod.Put,_Admin+"/"+id,HttpFixture.AdminToken,update,HttpStatusCode.BadRequest,token).ConfigureAwait(false);
            CatalogEntry held=(await f.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false))!;long revision=held.Revision;held.Revision++;held.ModerationState=ModerationStateEnum.Hidden;Check.True(await f.Database.ExerciseCatalog.SaveAsync(held,revision,false,null,token).ConfigureAwait(false),"Hold fixture saved");await Api.ExpectAsync(f,HttpMethod.Get,_Public+"/"+id,null,null,HttpStatusCode.NotFound,token).ConfigureAwait(false);Check.Equal(404,(await f.Services.PublicPages.ResolveAsync("catalog","public-apparatus","127.0.0.1",token).ConfigureAwait(false)).StatusCode,"Held apparatus context is not public HTML");
        }
    }
}
