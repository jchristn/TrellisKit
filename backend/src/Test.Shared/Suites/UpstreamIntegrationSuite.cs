namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Ai;
    using TrellisKit.Server.Services.Ai.Benchmark;

    /// <summary>Regression checks for generic upstream boundaries, property policies and data isolation.</summary>
    public static class UpstreamIntegrationSuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id = "UpstreamIntegration";

        /// <summary>Create the suite.</summary>
        /// <returns>Independent cases.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Generic upstream integration", new[]
            {
                SuiteCases.Unit(Id, "Roles.PropertyIsolationAndValidation", "Custom task roles are bounded and isolated, cannot replace built-ins, and cannot grant interactive harness access", RolesAsync),
                SuiteCases.Unit(Id, "Policy.CodeAndDisclosure", "Technical communities can permit code while instruction disclosure stays blocked", PolicyAsync),
                AiTestKit.Case(Id, "Policy.CodeCommunity", "A technical property delivers code, benchmarks its actual policy, and invalidates strict-policy benchmark hashes", CodeAsync, property: p => p.AiPolicy.AllowCode = true),
                SuiteCases.Http(Id, "Data.PropertyIsolationAndErasure", "Jobs, prompt versions and content snapshots never cross property boundaries; erasure removes member snapshots", IsolationAsync)
            });
        }

        private static Task RolesAsync(CancellationToken token)
        {
            string directory = TestProperty.CopySample();
            try
            {
                Directory.CreateDirectory(Path.Combine(directory, "ai"));
                string path = Path.Combine(directory, "ai", "roles.json");
                File.WriteAllText(path, "[{\"role\":\"EditorialReview\",\"title\":\"Editorial review\",\"allowsHarness\":true}]");
                IReadOnlyList<TaskRoleDefinition> custom = TaskRoleCatalog.Load(directory);
                Check.True(custom.Any(r => r.Role == "EditorialReview"), "Property definition loaded");
                Check.True(!TaskRoleCatalog.Load(null).Any(r => r.Role == "EditorialReview"), "No cross-property global mutation");
                custom[0].Title = "Changed in one runtime";
                Check.True(TaskRoleCatalog.Load(null)[0].Title != custom[0].Title, "Built-in definition objects are independent");
                File.WriteAllText(path, "[{\"role\":\"Moderation\",\"title\":\"Replace built-in\"}]");
                Check.Throws<InvalidOperationException>(() => TaskRoleCatalog.Load(directory), "Built-ins cannot be replaced");
                File.WriteAllText(path, "[{\"role\":\"InteractiveAgent\",\"title\":\"Agent\",\"interactive\":true,\"allowsHarness\":true}]");
                Check.Throws<InvalidOperationException>(() => TaskRoleCatalog.Load(directory), "Interactive harness rejected");
                File.WriteAllText(path, "[null]");
                Check.Throws<InvalidOperationException>(() => TaskRoleCatalog.Load(directory), "Null role rejected with useful validation");
            }
            finally { TestProperty.Delete(directory); }
            return Task.CompletedTask;
        }

        private static Task PolicyAsync(CancellationToken token)
        {
            const string code = "```sql\nSELECT 1;\n```";
            Check.Equal(ScopeOutputCheck.ReasonCode, ScopeOutputCheck.RejectReason(code)!, "Bounded default rejects code");
            Check.True(ScopeOutputCheck.RejectReason(code, true) == null, "Technical policy accepts code");
            Check.Equal(ScopeOutputCheck.ReasonRules, ScopeOutputCheck.RejectReason(ScopeGuard.ForPolicy(true), true)!, "Rule disclosure remains blocked");
            Check.True(!ScopeGuard.Compose("Community topic", true).Contains("never write, complete", StringComparison.Ordinal), "No contradictory code restriction in the guard");
            Check.True(ScopeGuard.Compose("Community topic").Contains("never write, complete", StringComparison.Ordinal), "Default policy preserved");
            return Task.CompletedTask;
        }

        private static async Task CodeAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            string assistant = await AiTestKit.ReadyAsync(f, model, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            model.ReplyText = "```sql\nSELECT 1;\n```";
            JsonElement result = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Write a SQL query selecting one", ct).ConfigureAwait(false);
            Check.Equal(model.ReplyText, Api.Str(result.GetProperty("reply"), "content"), "Code request and reply delivered");
            ScopeBenchmarkSet strict = ScopeBenchmark.Load(f.Services.Property.Property.Directory);
            ScopeBenchmarkSet actual = f.Services.Ai.Benchmarks.Set();
            Check.True(strict.Sha256 != actual.Sha256, "Policy included in evaluation identity");
            Check.True(ScopeDeterministicGate.Evaluate(actual, true).Passed, "Technical policy deterministic gate passes");
            Check.True(strict.Cases.Where(c => c.Category == ScopeBenchmark.CodeRequest).Any(c => c.Expect.Detector == ScopeBenchmark.Block), "Cached strict dataset is not mutated");
            string classifier = await f.Services.Ai.Prompts.GetAsync(PromptCatalog.ModerationClassifier, ct).ConfigureAwait(false);
            Check.True(!classifier.Contains("requests to write, explain, or debug computer code", StringComparison.Ordinal), "Default classifier respects technical policy");
        }

        private static async Task IsolationAsync(HttpFixture f, CancellationToken ct)
        {
            DatabaseDriverBase other = f.Raw.ForProperty(TestProperty.SecondId);
            await f.Services.Jobs.EnsureDefinitionsAsync(ct).ConfigureAwait(false);
            Check.True(await f.Database.Jobs.CountDefinitionsAsync(false, ct).ConfigureAwait(false) > 0, "Primary property has jobs");
            Check.Equal(0, await other.Jobs.CountDefinitionsAsync(false, ct).ConfigureAwait(false), "Other property cannot list definitions");
            await f.Database.Ai.Prompts.SaveDraftAsync(PromptCatalog.AssistantGuard, "Private primary-property prompt", "apv_isolation", null, DateTime.UtcNow, ct).ConfigureAwait(false);
            Check.Equal(0, (await other.Ai.Prompts.ReadAllAsync(ct).ConfigureAwait(false)).Count, "Other property cannot read live prompts");
            Check.True(!((await other.Ai.Prompts.ReadNewestVersionsAsync(ct).ConfigureAwait(false)).ContainsKey(PromptCatalog.AssistantGuard)), "Other property cannot read draft versions");
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Private review snapshot", ct).ConfigureAwait(false);
            ContentChangeRecord row = (await f.Database.ContentChanges.ListPendingAsync(100, ct).ConfigureAwait(false)).First(r => r.TargetId == post);
            Check.True(await other.ContentChanges.ReadAsync(row.Id, ct).ConfigureAwait(false) == null, "Other property cannot read snapshot by id");
            await other.ContentChanges.EraseForUserAsync(poster.Id, DateTime.UtcNow, ct).ConfigureAwait(false);
            Check.True((await f.Database.ContentChanges.ReadAsync(row.Id, ct).ConfigureAwait(false))!.Snapshot.Length > 0, "Cross-property erasure has no effect");
            await f.Database.ContentChanges.EraseForUserAsync(poster.Id, DateTime.UtcNow, ct).ConfigureAwait(false);
            ContentChangeRecord erased = (await f.Database.ContentChanges.ReadAsync(row.Id, ct).ConfigureAwait(false))!;
            Check.True(String.IsNullOrEmpty(erased.Snapshot) && erased.MediaRefs.Count == 0, "Erasure removes text and media references");
        }
    }
}
