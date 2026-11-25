namespace TrellisKit.Server.Services.CatalogGeneration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services.Ai;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// Manual, source-grounded catalog draft authoring through CatalogAuthoring. Never submits or publishes a draft.
    /// Dry runs make no model calls or writes. Deterministic slugs and fingerprints make retries idempotent.
    /// Thread safe; source material remains private in administrator job parameters.
    /// </summary>
    public sealed class CatalogDraftJobHandler : IJobHandler, IJobParameterValidator
    {
        /// <summary>Stable job key.</summary>
        public const string JobKey = "catalog.generateDrafts";
        /// <inheritdoc />
        public string Key { get { return JobKey; } }
        /// <inheritdoc />
        public JobHandlerDefaults Defaults { get; } = new JobHandlerDefaults
        {
            Name = "Generate catalog drafts",
            Description = "Creates exercise or technique drafts from administrator-reviewed permissive source text. Nothing is published without review.",
            Enabled = false, IntervalType = JobIntervalTypeEnum.Manual, MaxConcurrency = 2,
            MaxAttempts = 2, BudgetUsdPerRun = 25, SupportsDryRun = true
        };
        private static readonly JsonSerializerOptions _Strict = new JsonSerializerOptions(ApiJson.Api) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        private static readonly string[] _Licenses = { "CC0-1.0", "CC-BY-4.0", "PublicDomain" };

        /// <inheritdoc />
        public void ValidateParameters(string? parameters) { Parse(parameters); }

        /// <inheritdoc />
        public async Task<List<JobItemPlan>> PlanAsync(JobRunContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            token.ThrowIfCancellationRequested();
            CatalogDraftParameters p = Parse(context.Run.Parameters);
            await ValidateConfigurationAsync(context, p, token).ConfigureAwait(false);
            return p.Topics.Select(topic => new JobItemPlan(topic.Trim(), null)).ToList();
        }

        /// <inheritdoc />
        public async Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            CatalogDraftParameters p = Parse(context.Run.Parameters);
            await ValidateConfigurationAsync(context, p, token).ConfigureAwait(false);
            string topic = context.Item.TargetKey;
            if (!p.Topics.Any(t => t.Trim() == topic)) return JobItemOutcome.Fail("The requested topic is not in this run's parameters.", false);
            string hash = Fingerprint(ApiJson.Serialize(new { topic, p.Kind, p.Program, p.SourceText, p.Source }));
            string namePart = PublicSlugRules.Derive("movement", topic);
            string slug = namePart.Substring(0, Math.Min(60, namePart.Length)).TrimEnd('-') + "-" + hash.Substring(0, 12);
            CatalogDraftResult result = new CatalogDraftResult { Topic = topic, SourceHash = hash, Slug = slug, DryRun = context.IsDryRun };
            CatalogEntry? existing = await context.Services.Database.ExerciseCatalog.ReadAsync(slug, token).ConfigureAwait(false);
            if (existing != null)
            {
                if (existing.Tags.GetValueOrDefault("staff:generationSourceHash") != hash) return JobItemOutcome.Fail("The generated slug belongs to a different catalog entry.", false);
                result.Outcome = "Existing"; result.EntryId = existing.Id;
                return JobItemOutcome.Ok(ApiJson.Serialize(result));
            }
            if (context.IsDryRun) { result.Outcome = "Planned"; return JobItemOutcome.Ok(ApiJson.Serialize(result)); }
            if (!context.Services.Property.Property.Features.Ai) return JobItemOutcome.Fail("AI is disabled for this property.", false);
            string system = await context.Services.Ai.Prompts.GetAsync(PromptCatalog.CatalogDraftAuthoring, token).ConfigureAwait(false);
            RoleOutcome response = await context.Services.Ai.Roles.PromptAsync("CatalogAuthoring", ApiJson.Serialize(new
            {
                topic, kind = p.Kind, program = p.Program,
                sourceData = p.SourceText,
                muscleVocabulary = context.Services.Property.Property.LibrarySection?.CatalogMuscles,
                equipmentVocabulary = context.Services.Property.Property.LibrarySection?.CatalogEquipment,
                responseShape = "{supported:boolean,entry:null|{name:string,summary:string,difficulty:Beginner|Intermediate|Advanced|AllLevels,movementPattern:null,plane:null,laterality:null,techniqueType:null,fromPosition:null,toPosition:null,aliases:[],primaryMuscles:[],secondaryMuscles:[],equipment:[],instructions:[],cues:[],commonErrors:[],regressions:[],progressions:[],substitutions:[],contraindications:[]}}"
            }), system, p.MaxOutputTokens, token).ConfigureAwait(false);
            if (!response.Success) return JobItemOutcome.Fail("Catalog authoring unavailable: " + response.ErrorCode, response.Retryable);
            CatalogDraftReply? reply;
            try { reply = JsonSerializer.Deserialize<CatalogDraftReply>(response.Text, _Strict); }
            catch (JsonException e) { return JobItemOutcome.Fail("Invalid catalog draft JSON: " + e.Message, false); }
            if (reply == null) return JobItemOutcome.Fail("Empty catalog draft reply.", false);
            if (!reply.Supported) { result.Outcome = "Unsupported"; return JobItemOutcome.Ok(ApiJson.Serialize(result)); }
            CatalogEntryRequest? entry = reply.Entry;
            if (entry == null || entry.Instructions == null || entry.Instructions.Count == 0) return JobItemOutcome.Fail("A supported draft needs explicit instructions from its source.", false);
            if(entry.ApparatusSetup!=null)return JobItemOutcome.Fail("This catalog generator does not support apparatusSetup; author it through reviewed catalog edits or imports.",false);
            if(entry.ModalityDetails!=null)return JobItemOutcome.Fail("This catalog generator does not support modalityDetails; author them through reviewed catalog edits or imports.",false);
            entry.Name = topic; entry.Slug = slug; entry.Kind = p.Kind; entry.Program = p.Program; entry.ExpectedRevision = null;
            entry.LessonIds = new List<string>();
            entry.Labels = new List<string> { "ai-draft" };
            entry.Tags = new Dictionary<string, string> { ["staff:generationSourceHash"] = hash, ["staff:generationJobRunId"] = context.Run.Id };
            CatalogSource source = new CatalogSource { Url = p.Source.Url, Title = p.Source.Title, License = p.Source.License, Attribution = p.Source.Attribution };
            source.Method = "Model"; source.Model = response.Model; source.RetrievedUtc = context.NowUtc;
            entry.Sources = new List<CatalogSource> { source };
            ContentActor actor = new ContentActor { IsBreakGlass = true, IsSystemAdmin = true, IsStaff = true,
                HasMembership = true, AgeAttested = true, UserId = context.Run.RequestedByUserId, Access = context.Services.Access.Current };
            try
            {
                CatalogEntryView created = await context.Services.Library.ExerciseCatalog.CreateAsync(actor,
                    new RequestInfo { UserAgent = "Job " + JobKey }, entry, token).ConfigureAwait(false);
                result.EntryId = created.Id; result.Outcome = "CreatedDraft";
                return JobItemOutcome.Ok(ApiJson.Serialize(result));
            }
            catch (ApiErrorException e) when (e.StatusCode == 409)
            {
                // Another instance may have finished the same input while this one was awaiting its model.
                CatalogEntry? duplicate = await context.Services.Database.ExerciseCatalog.ReadAsync(slug, token).ConfigureAwait(false);
                if (duplicate != null && duplicate.Tags.GetValueOrDefault("staff:generationSourceHash") == hash)
                { result.EntryId = duplicate.Id; result.Outcome = "Existing"; return JobItemOutcome.Ok(ApiJson.Serialize(result)); }
                return JobItemOutcome.Fail("Catalog draft could not be saved: " + e.Message, false);
            }
            catch (ApiErrorException e) when (e.StatusCode == 400)
            { return JobItemOutcome.Fail("Catalog draft failed validation: " + e.Message, false); }
        }

        /// <summary>Parse strict, bounded parameters and require reviewed permissive source text.</summary>
        /// <param name="json">Parameters JSON.</param>
        /// <returns>Validated parameters.</returns>
        /// <exception cref="ArgumentException">A required input is missing or exceeds its bounds.</exception>
        /// <exception cref="JsonException">JSON is malformed or contains unknown fields.</exception>
        public static CatalogDraftParameters Parse(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new ArgumentException("Supply reviewed source text and topic names for catalog draft generation.");
            CatalogDraftParameters p = JsonSerializer.Deserialize<CatalogDraftParameters>(json, _Strict) ?? throw new ArgumentException("Catalog parameters must be an object.");
            if (p.Topics == null || p.Topics.Count < 1 || p.Topics.Count > 20 || p.Topics.Any(t => String.IsNullOrWhiteSpace(t) || t.Trim().Length < 2 || t.Length > 160) ||
                p.Topics.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.Topics.Count) throw new ArgumentException("Supply 1..20 distinct topic names of 2..160 characters.");
            if (p.Kind != "Exercise" && p.Kind != "Technique") throw new ArgumentException("Kind must be Exercise or Technique.");
            if (p.SourceText == null || p.SourceText.Trim().Length < 40 || p.SourceText.Length > 10000 || p.MaxOutputTokens < 256 || p.MaxOutputTokens > 12000)
                throw new ArgumentException("Source text must be 40..10000 characters and maxOutputTokens 256..12000.");
            if (!p.SourceReviewed || p.Source == null || !_Licenses.Contains(p.Source.License, StringComparer.Ordinal) || String.IsNullOrWhiteSpace(p.Source.Title) ||
                p.Source.Title.Length > 200 || p.Source.Attribution?.Length > 2000 || p.Source.Url?.Length > 2000 ||
                p.Source.License == "CC-BY-4.0" && String.IsNullOrWhiteSpace(p.Source.Attribution) ||
                !Uri.TryCreate(p.Source.Url, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)
                throw new ArgumentException("Supply an administrator-reviewed HTTPS source with title and CC0-1.0, CC-BY-4.0 or PublicDomain license.");
            return p;
        }

        private static async Task ValidateConfigurationAsync(JobRunContext context, CatalogDraftParameters p, CancellationToken token)
        {
            if (!context.Services.Property.Property.Features.Catalog) throw new ArgumentException("The catalog feature is disabled.");
            if (p.Program != null && !(await context.Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false)).Any(option => option.Key == p.Program))
                throw new ArgumentException("Use a configured directory program key.");
        }
        private static string Fingerprint(string value) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant(); }
    }
}
