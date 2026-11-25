namespace TrellisKit.Server.Services.CatalogImport
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.Json.Serialization.Metadata;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>Bounded scheduled JSON intake from explicitly reviewed sources. Never invokes models, fetches media or publishes. Safe retries preserve all human edits and moderation holds.</summary>
    public sealed class CatalogImportJobHandler : IJobHandler,IJobParameterValidator
    {
        /// <summary>Stable job key.</summary>
        public const string JobKey="catalog.importSources";
        /// <inheritdoc />
        public string Key=>JobKey;
        /// <inheritdoc />
        public JobHandlerDefaults Defaults { get; }=new JobHandlerDefaults{Name="Import reviewed catalog sources",Description="Fetch bounded licensed manifests and create or refresh untouched drafts. Publication always requires human review.",Enabled=false,IntervalType=JobIntervalTypeEnum.EveryNDays,IntervalValue=7,TimeOfDayMinutesUtc=180,MaxConcurrency=1,MaxAttempts=3,SupportsDryRun=true};
        private readonly CatalogManifestFetcher _Fetcher;
        /// <summary>Use a policy-enforcing client per fetch by default. An injected client supports local transport integration tests; its caller must enforce outbound address policy.</summary>
        /// <param name="client">Optional injected HTTP client.</param>
        public CatalogImportJobHandler(HttpClient? client=null){_Fetcher=new CatalogManifestFetcher(client);}
        private static readonly JsonSerializerOptions _Strict=new JsonSerializerOptions(ApiJson.Api){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=16};
        /// <inheritdoc />
        public void ValidateParameters(string? parameters){Parse(parameters);}
        /// <summary>Validate closed bounded source selection.</summary>
        public static CatalogImportParameters Parse(string? parameters)
        {
            CatalogImportParameters p=String.IsNullOrWhiteSpace(parameters)?new CatalogImportParameters():JsonSerializer.Deserialize<CatalogImportParameters>(parameters,_Strict)??throw new ArgumentException("Supply a source selection object.");
            if(p.SourceIds==null||p.SourceIds.Count>20||p.SourceIds.Any(id=>String.IsNullOrWhiteSpace(id)||id.Length>100)||p.SourceIds.Distinct(StringComparer.Ordinal).Count()!=p.SourceIds.Count)throw new ArgumentException("Supply at most 20 distinct source identifiers.");return p;
        }
        /// <inheritdoc />
        public async Task<List<JobItemPlan>> PlanAsync(JobRunContext context,CancellationToken token)
        {
            if(!context.Services.Property.Property.Features.Catalog)throw new ArgumentException("The catalog is disabled.");
            CatalogImportParameters p=Parse(context.Run.Parameters);
            List<CatalogImportSource> sources=await context.Services.Database.CatalogImports.PlanAsync(p.SourceIds,token).ConfigureAwait(false);
            if(p.SourceIds.Count>0&&sources.Count!=p.SourceIds.Count)throw new ArgumentException("Every selected source must be enabled and explicitly reviewed in this property.");
            return sources.Select(s=>new JobItemPlan(s.Id,s.Revision.ToString(CultureInfo.InvariantCulture))).ToList();
        }
        /// <inheritdoc />
        public async Task<JobItemOutcome> ExecuteAsync(JobItemContext context,CancellationToken token)
        {
            if(!context.Services.Property.Property.Features.Catalog)return JobItemOutcome.Fail("The catalog is disabled.",false);
            CatalogImportSource? source=await context.Services.Database.CatalogImports.ReadAsync(context.Item.TargetKey,token).ConfigureAwait(false);
            if(source==null||!source.Enabled||source.ReviewedUtc==null||!Int64.TryParse(context.Item.Payload,out long revision)||revision!=source.Revision)return JobItemOutcome.Fail("Source configuration or review changed; plan a new run.",false);
            if(!context.IsDryRun)await context.Services.Database.CatalogImports.TouchAsync(source.Id,context.NowUtc,token).ConfigureAwait(false);
            CatalogManifestResponse fetched=await _Fetcher.FetchAsync(new Uri(source.Url),context.Services.CatalogImports.AddressPolicy,context.Services.CatalogImports.Resolver,token).ConfigureAwait(false);
            if(fetched.Error!=null||fetched.Status<200||fetched.Status>=300||String.IsNullOrEmpty(fetched.Body))return JobItemOutcome.Fail("Manifest fetch failed: "+(fetched.Error??("HTTP "+fetched.Status)),fetched.Retryable);
            CatalogImportManifest? manifest;
            try{manifest=JsonSerializer.Deserialize<CatalogImportManifest>(fetched.Body,_Strict);}
            catch(JsonException){return JobItemOutcome.Fail("The source is not a valid version 1 catalog manifest.",false);}
            if(manifest==null||manifest.Version!=1||manifest.Entries==null||manifest.Entries.Count>source.MaxEntries||manifest.Entries.Any(r=>r==null||r.Entry==null||!Regex.IsMatch(r.Key??String.Empty,"^[a-zA-Z0-9_.-]{1,100}$"))||manifest.Entries.Select(r=>r.Key).Distinct(StringComparer.Ordinal).Count()!=manifest.Entries.Count)return JobItemOutcome.Fail("Manifest version, keys or entry bounds are invalid; no entries were imported.",false);
            ContentActor actor=new ContentActor{IsSystemAdmin=true,IsBreakGlass=true,IsStaff=true,HasMembership=true,AgeAttested=true,UserId=context.Run.RequestedByUserId,Access=context.Services.Access.Current};
            List<CatalogImportOutcome> results=new List<CatalogImportOutcome>();
            foreach(CatalogImportRecord record in manifest.Entries)
            {
                token.ThrowIfCancellationRequested();
                CatalogImportItem? prior=await context.Services.Database.CatalogImports.ItemAsync(source.Id,record.Key,token).ConfigureAwait(false);
                CatalogEntry? previous=prior==null?null:await context.Services.Database.ExerciseCatalog.ReadAsync(prior.EntryId,token).ConfigureAwait(false);
                CatalogImportOutcome outcome=new CatalogImportOutcome{Key=record.Key,EntryId=prior?.EntryId};results.Add(outcome);
                if(prior!=null&&(previous==null||previous.Revision!=prior.EntryRevision||previous.Status!="Draft"||previous.ModerationState!=ModerationStateEnum.None)){outcome.Outcome="Preserved";continue;}
                CatalogEntryRequest entry=record.Entry;
                if(prior==null)
                {
                    List<string> names=new List<string>{entry.Name};if(entry.Aliases!=null)names.AddRange(entry.Aliases);
                    string? duplicate=await context.Services.Database.ExerciseCatalog.DuplicateNameAsync(names,token).ConfigureAwait(false);
                    if(duplicate!=null){outcome.Outcome="DuplicateName";outcome.EntryId=duplicate;continue;}
                }
                entry.ModalityDetails??=previous?.ModalityDetails??new CatalogModalityDetails();
                entry.ApparatusSetup??=previous?.ApparatusSetup??new CatalogApparatusSetup();
                string identity=Hash(source.Id+":"+record.Key);
                entry.Slug=previous?.Slug??("import-"+identity.Substring(0,32));entry.ExpectedRevision=null;
                entry.LessonIds=new List<string>();entry.Labels=new List<string>{"import-draft"};entry.Tags=new Dictionary<string,string>{{"staff:importSource",source.Id},{"staff:importKey",record.Key}};
                entry.Sources=new List<CatalogSource>{new CatalogSource{Url=source.Url,Title=source.Name,License=source.License,Attribution=source.Attribution,Method="Import"}};
                string hash=Hash(JsonSerializer.Serialize(new{entry,sourceRevision=source.Revision},_Fingerprint));
                if(prior?.SourceHash==hash){outcome.Outcome="Unchanged";continue;}
                CatalogImportItem import=new CatalogImportItem{SourceId=source.Id,ItemKey=record.Key,SourceHash=hash,SourceRevision=source.Revision,EntryId=prior?.EntryId??String.Empty,EntryRevision=prior?.EntryRevision??0};
                entry.Sources[0].RetrievedUtc=context.NowUtc;
                try
                {
                    bool saved=await context.Services.Library.ExerciseCatalog.ApplyImportAsync(actor,entry,previous,import,context.IsDryRun,token).ConfigureAwait(false);
                    outcome.Outcome=!saved?"Preserved":context.IsDryRun?"Planned":previous==null?"CreatedDraft":"RefreshedDraft";outcome.EntryId=String.IsNullOrEmpty(import.EntryId)?null:import.EntryId;
                }
                catch(ApiErrorException e) when(e.StatusCode==400||e.StatusCode==409){outcome.Outcome="Invalid";outcome.ErrorCode=e.Error;}
                catch(DuplicateRecordException){outcome.Outcome="Preserved";outcome.ErrorCode="ConcurrentImport";}
            }
            return JobItemOutcome.Ok(ApiJson.Serialize(new{sourceId=source.Id,sourceRevision=source.Revision,dryRun=context.IsDryRun,entries=results}));
        }
        // Preserve earlier fingerprints when newer optional descriptors are absent. API responses still include explicit nulls.
        private static readonly JsonSerializerOptions _Fingerprint=FingerprintOptions();
        private static JsonSerializerOptions FingerprintOptions()
        {
            DefaultJsonTypeInfoResolver resolver=new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info=>
            {
                if(typeof(CatalogEntryData).IsAssignableFrom(info.Type))
                    foreach(JsonPropertyInfo property in info.Properties)
                        if(property.Name=="apparatusSetup")property.ShouldSerialize=(_,value)=>value is CatalogApparatusSetup setup&&new[]{setup.ApparatusKey,setup.Manufacturer,setup.Model,setup.SetupText,setup.SpringSetupText}.Any(v=>!String.IsNullOrWhiteSpace(v));
                if(info.Type!=typeof(CatalogModalityDetails))return;
                foreach(JsonPropertyInfo property in info.Properties)
                    if(property.Name=="attireKey"||property.Name=="levelKey"||property.Name=="notationSystemKey"||property.Name=="notationText")property.ShouldSerialize=(_,value)=>value!=null;
            });
            return new JsonSerializerOptions(ApiJson.Api){TypeInfoResolver=resolver};
        }
        private static string Hash(string value){return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();}
    }
}
