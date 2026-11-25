namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services.CatalogGeneration;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>Source-grounded catalog authoring with local models and isolated property databases.</summary>
    public static class CatalogGenerationSuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id = "CatalogGeneration";
        /// <summary>Fresh test descriptors.</summary>
        /// <returns>Bounded input, draft isolation and model validation tests.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Catalog draft generation", new[]
            {
                SuiteCases.Unit(Id, "Parameters.ReviewedSource", "Only bounded topics with reviewed permissive sources are accepted", ParametersAsync),
                AiTestKit.Case(Id, "Job.DraftAndIdempotency", "Dry run makes no calls; real generation creates a private draft once", DraftAsync, null, p => p.Features.Catalog = true),
                AiTestKit.Case(Id, "Job.ApparatusFieldsClosed", "A valid apparatus group cannot widen the closed model generation contract", ApparatusFieldsAsync, null, p => p.Features.Catalog = true),
                AiTestKit.Case(Id, "Job.ModalityFieldsClosed", "A valid configured modality group cannot widen the closed model authoring contract", ModalityFieldsAsync, null, p => p.Features.Catalog = true),
                AiTestKit.Case(Id, "Job.UnsupportedAndInvalid", "Unsupported source material and malformed or invalid model replies create no entries", InvalidAsync, null, p => p.Features.Catalog = true),
                CatalogTestKit.Http(Id, "Job.RunParameters", "Administrator run API rejects unreviewed source material and accepts a bounded preview", RunApiAsync)
            });
        }

        private static CatalogDraftParameters Parameters()
        {
            return new CatalogDraftParameters
            {
                Topics = new List<string> { "Example movement" }, Kind = "Technique",
                SourceText = "Example movement: stand in a comfortable position. Raise one arm slowly, then lower it to the starting position.",
                SourceReviewed = true,
                Source = new CatalogSource { Title = "Example source", Url = "https://example.test/movement", License = "CC0-1.0" }
            };
        }

        private static Task ParametersAsync(CancellationToken token)
        {
            CatalogDraftParameters good = Parameters();
            Check.Equal(1, CatalogDraftJobHandler.Parse(ApiJson.Serialize(good)).Topics.Count, "One topic accepted");
            foreach (Action<CatalogDraftParameters> mutate in new Action<CatalogDraftParameters>[]
            {
                p => p.SourceReviewed = false, p => p.Source.License = "AllRightsReserved",
                p => p.Source.License = "CC-BY-4.0", p => p.Source.Url = "http://example.test/source",
                p => p.Topics.Add("example movement"), p => p.Topics = new List<string>(),
                p => p.SourceText = "Too short", p => p.MaxOutputTokens = 12001
            })
            {
                CatalogDraftParameters invalid = Parameters(); mutate(invalid);
                bool rejected = false;
                try { CatalogDraftJobHandler.Parse(ApiJson.Serialize(invalid)); }
                catch (ArgumentException) { rejected = true; }
                Check.True(rejected, "Unreviewed or unbounded input rejected");
            }
            bool unknown = false;
            try { CatalogDraftJobHandler.Parse("{\"publish\":true}"); }
            catch (JsonException) { unknown = true; }
            Check.True(unknown, "Unknown publication switch rejected");
            return Task.CompletedTask;
        }

        private static async Task ConfigureAsync(HttpFixture f, FakeModelServer model, CancellationToken token)
        {
            string endpoint = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Catalog drafts"), token).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/roles/CatalogAuthoring", HttpFixture.AdminToken,
                new { executors = new object[] { new { runtime = "ApiEndpoint", endpointId = endpoint } } }, HttpStatusCode.OK, token).ConfigureAwait(false);
            f.Services.Ai.Roles.Invalidate();
        }

        private static JobItemContext Context(HttpFixture f, CatalogDraftParameters parameters, bool dry)
        {
            return new JobItemContext(f.Services, new JobDefinition(), new JobRun { IsDryRun = dry, Parameters = ApiJson.Serialize(parameters) },
                new JobItem { TargetKey = parameters.Topics[0] }, DateTime.UtcNow);
        }

        private static async Task DraftAsync(HttpFixture f, FakeModelServer model, CancellationToken token)
        {
            await ConfigureAsync(f, model, token).ConfigureAwait(false);
            CatalogDraftJobHandler handler = new CatalogDraftJobHandler();
            CatalogDraftParameters p = Parameters();
            model.ReplyText = "{\"supported\":true,\"entry\":{\"name\":\"Model name\",\"kind\":\"Exercise\",\"instructions\":[\"Stand comfortably.\",\"Raise one arm slowly and lower it.\"],\"labels\":[\"published\"],\"tags\":{\"generationSourceHash\":\"spoof\"}}}";
            model.ClearRequests();
            JobItemOutcome dry = await handler.ExecuteAsync(Context(f, p, true), token).ConfigureAwait(false);
            Check.True(dry.Success, dry.Error ?? "Preview succeeded");
            CatalogDraftResult preview = JsonSerializer.Deserialize<CatalogDraftResult>(dry.Result!, ApiJson.Api)!;
            Check.Equal("Planned", preview.Outcome, "Preview plans only");
            Check.Equal(0, model.Requests.Count, "No dry-run model call");
            Check.True(await f.Services.Database.ExerciseCatalog.ReadAsync(preview.Slug, token).ConfigureAwait(false) == null, "No dry-run catalog writes");
            JobItemOutcome actual = await handler.ExecuteAsync(Context(f, p, false), token).ConfigureAwait(false);
            Check.True(actual.Success, actual.Error ?? "Draft created");
            CatalogDraftResult created = JsonSerializer.Deserialize<CatalogDraftResult>(actual.Result!, ApiJson.Api)!;
            CatalogEntry stored = (await f.Services.Database.ExerciseCatalog.ReadAsync(created.EntryId!, token).ConfigureAwait(false))!;
            Check.Equal("Draft", stored.Status, "Generation never publishes");
            Check.Equal(p.Topics[0], stored.Name, "Requested topic wins over model name");
            Check.Equal("Technique", stored.Kind, "Requested kind wins");
            Check.Equal("ai-draft", stored.Labels[0], "Model metadata ignored");
            Check.True(stored.Tags.ContainsKey("staff:generationSourceHash") && stored.Tags.ContainsKey("staff:generationJobRunId") && !stored.Tags.ContainsKey("generationSourceHash"), "Generation diagnostic tags remain staff-only");
            Check.Equal("Model", stored.Sources[0].Method, "Model provenance retained");
            Check.Equal(p.Source.Url, stored.Sources[0].Url, "Reviewed source retained");
            Check.True(created.HumanReviewRequired, "Human publication gate retained");
            int requests = model.Requests.Count;
            Check.True(requests > 0, "Catalog authoring role called");
            JobItemOutcome again = await handler.ExecuteAsync(Context(f, p, false), token).ConfigureAwait(false);
            Check.Equal("Existing", JsonSerializer.Deserialize<CatalogDraftResult>(again.Result!, ApiJson.Api)!.Outcome, "Retry finds draft");
            Check.Equal(requests, model.Requests.Count, "Retry avoids duplicate cost");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/catalog/entries/" + stored.Id, null, null, HttpStatusCode.NotFound, token).ConfigureAwait(false);
        }

        private static async Task InvalidAsync(HttpFixture f, FakeModelServer model, CancellationToken token)
        {
            await ConfigureAsync(f, model, token).ConfigureAwait(false);
            CatalogDraftJobHandler handler = new CatalogDraftJobHandler();
            CatalogDraftParameters p = Parameters();
            model.ReplyText = "{\"supported\":false,\"entry\":null}";
            JobItemOutcome unsupported = await handler.ExecuteAsync(Context(f, p, false), token).ConfigureAwait(false);
            Check.True(unsupported.Success, "Unsupported source is a deliberate skip");
            CatalogDraftResult skipped = JsonSerializer.Deserialize<CatalogDraftResult>(unsupported.Result!, ApiJson.Api)!;
            Check.Equal("Unsupported", skipped.Outcome, "Unsupported reason recorded");
            foreach (string reply in new[]
            {
                "not json", "{\"supported\":true,\"entry\":{\"status\":\"Published\"}}",
                "{\"supported\":true,\"entry\":{\"instructions\":[]}}",
                "{\"supported\":true,\"entry\":{\"instructions\":[\"Move slowly.\"],\"primaryMuscles\":[\"unconfigured-muscle\"]}}"
            })
            {
                model.ReplyText = reply;
                JobItemOutcome rejected = await handler.ExecuteAsync(Context(f, p, false), token).ConfigureAwait(false);
                Check.True(!rejected.Success && !rejected.Retryable, "Invalid response rejected without retries");
                Check.True(await f.Services.Database.ExerciseCatalog.ReadAsync(skipped.Slug, token).ConfigureAwait(false) == null, "Rejected response creates no entry");
            }
        }

        private static async Task ModalityFieldsAsync(HttpFixture f,FakeModelServer model,CancellationToken token)
        {
            await ConfigureAsync(f,model,token).ConfigureAwait(false);
            await f.Database.DirectoryListings.Config.CreateProgramAsync(new DirectoryProgram{Id=IdGenerator.Generate("prg_"),Key="sample-program",Label="Sample program"},token).ConfigureAwait(false);
            f.Property.LibrarySection??=new PropertyLibrarySection();f.Property.LibrarySection.CatalogDescriptorProfiles=new List<PropertyCatalogDescriptorProfile>{new PropertyCatalogDescriptorProfile{ProgramKey="sample-program",Families=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="sample-family",Label="Sample family"}},NameLanguages=new List<PropertyOptionSeed>{new PropertyOptionSeed{Key="el-Latn",Label="Sample language"}}}};
            CatalogDraftParameters p=Parameters();p.Program="sample-program";
            model.ReplyText=ApiJson.Serialize(new{supported=true,entry=new{instructions=new[]{"Source instruction"},modalityDetails=new{familyKey="sample-family",localizedNames=new[]{new{languageTag="el-Latn",text="Unrequested model name"}}}}});
            CatalogDraftJobHandler handler=new CatalogDraftJobHandler();JobItemOutcome preview=await handler.ExecuteAsync(Context(f,p,true),token).ConfigureAwait(false);string slug=JsonSerializer.Deserialize<CatalogDraftResult>(preview.Result!,ApiJson.Api)!.Slug;
            JobItemOutcome result=await handler.ExecuteAsync(Context(f,p,false),token).ConfigureAwait(false);Check.True(!result.Success&&!result.Retryable&&result.Error!.Contains("does not support modalityDetails",StringComparison.Ordinal),"Even vocabulary-valid model descriptors are rejected by the closed generator");Check.True(await f.Database.ExerciseCatalog.ReadAsync(slug,token).ConfigureAwait(false)==null,"Rejected model details create no draft");
        }

        private static async Task ApparatusFieldsAsync(HttpFixture f,FakeModelServer model,CancellationToken token)
        {
            await ConfigureAsync(f,model,token).ConfigureAwait(false);f.Property.LibrarySection??=new PropertyLibrarySection();f.Property.LibrarySection.CatalogEquipment.Add(new PropertyOptionSeed{Key="sample-apparatus",Label="Example apparatus"});
            CatalogDraftParameters p=Parameters();model.ReplyText=ApiJson.Serialize(new{supported=true,entry=new{instructions=new[]{"Reviewed instruction"},equipment=new[]{"sample-apparatus"},apparatusSetup=new{apparatusKey="sample-apparatus",manufacturer="Example manufacturer",model="Example model",springSetupText="Unrequested model setup"}}});
            CatalogDraftJobHandler handler=new CatalogDraftJobHandler();JobItemOutcome preview=await handler.ExecuteAsync(Context(f,p,true),token).ConfigureAwait(false);string slug=JsonSerializer.Deserialize<CatalogDraftResult>(preview.Result!,ApiJson.Api)!.Slug;
            JobItemOutcome result=await handler.ExecuteAsync(Context(f,p,false),token).ConfigureAwait(false);Check.True(!result.Success&&!result.Retryable&&result.Error!.Contains("does not support apparatusSetup",StringComparison.Ordinal),"Even structurally valid model apparatus setup is outside closed generation contract");Check.True(await f.Database.ExerciseCatalog.ReadAsync(slug,token).ConfigureAwait(false)==null,"Rejected model apparatus creates no draft");
        }

        private static async Task RunApiAsync(HttpFixture f, CancellationToken token)
        {
            string path = "/api/v1/admin/jobs/" + CatalogDraftJobHandler.JobKey + "/run";
            CatalogDraftParameters p = Parameters(); p.SourceReviewed = false;
            await Api.ExpectErrorAsync(f, HttpMethod.Post, path, HttpFixture.AdminToken, new { dryRun = true, parameters = ApiJson.Serialize(p) }, HttpStatusCode.BadRequest, "InvalidJobParameters", token).ConfigureAwait(false);
            p.SourceReviewed = true;
            JsonElement queued = await Api.ExpectAsync(f, HttpMethod.Post, path, HttpFixture.AdminToken,
                new { dryRun = true, parameters = ApiJson.Serialize(p) }, HttpStatusCode.Created, token).ConfigureAwait(false);
            Check.True(queued.GetProperty("isDryRun").GetBoolean(), "Preview flag preserved");
        }
    }
}
