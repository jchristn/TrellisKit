namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>Property-scoped reviewed training catalog, reusing library authors and demonstration lessons.</summary>
    public sealed partial class ExerciseCatalogService
    {
        private readonly ApplicationServices _Services;
        private static readonly string[] _Kinds = { "Exercise", "Technique" };
        private static readonly string[] _Difficulties = { "Beginner", "Intermediate", "Advanced", "AllLevels" };
        private static readonly string[] _Relations = { "Regression", "Progression", "Substitution", "Transition", "Prerequisite", "Counterpose" };
        private static readonly HashSet<string> _Licenses = new HashSet<string>(new[] { "CC0-1.0", "CC-BY-4.0", "PublicDomain", "MIT", "Unlicense", "Original" }, StringComparer.OrdinalIgnoreCase);

        /// <summary>Instantiate for one property.</summary>
        public ExerciseCatalogService(ApplicationServices services) { _Services = services ?? throw new ArgumentNullException(nameof(services)); }

        /// <summary>Public controlled vocabularies and relationship kinds.</summary>
        public async Task<object> ConfigAsync(CancellationToken token = default)
        {
            Available();
            PropertyLibrarySection config = _Services.Property.Property.LibrarySection ?? new PropertyLibrarySection();
            return new { programs = await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false), muscles = config.CatalogMuscles, equipment = config.CatalogEquipment, kinds = _Kinds, difficulties = _Difficulties, relationshipKinds = _Relations, descriptorProfiles = DescriptorProfiles(config) };
        }

        /// <summary>Bounded enumeration; public/member routes expose published entries only.</summary>
        public async Task<EnumerationResult<CatalogEntryView>> ListAsync(ContentActor actor, EnumerationQuery? query, bool admin, CancellationToken token = default)
        {
            Available(); _Services.Media.EnforceRead(actor); if (admin) LibraryAccess.RequireStaff(actor);
            query??=new EnumerationQuery();ValidateDescriptorFilters(query);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, admin ? ExerciseCatalogCollections.AdminEntries : ExerciseCatalogCollections.Entries, admin, admin ? "admin-catalog" : "catalog",100);
            EnumerationResult<CatalogEntry> page = await _Services.Database.ExerciseCatalog.EnumerateAsync(request, !admin, token).ConfigureAwait(false);
            List<CatalogEntryView> views = new List<CatalogEntryView>();
            foreach (CatalogEntry entry in page.Objects) views.Add(await ViewAsync(entry, actor, admin, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>Read by identifier or current slug.</summary>
        public async Task<CatalogEntryView> GetAsync(ContentActor actor, string key, bool admin, CancellationToken token = default)
        {
            Available(); _Services.Media.EnforceRead(actor); if (admin) LibraryAccess.RequireStaff(actor);
            CatalogEntry entry = await RequireAsync(key, token).ConfigureAwait(false);
            if (!admin && (entry.Status != "Published" || entry.ModerationState != ModerationStateEnum.None)) throw ApiErrorException.NotFound("Catalog entry");
            return await ViewAsync(entry, actor, admin, token).ConfigureAwait(false);
        }

        /// <summary>Create a validated draft. Generation jobs use this same authoring boundary.</summary>
        public async Task<CatalogEntryView> CreateAsync(ContentActor actor, RequestInfo info, CatalogEntryRequest request, CancellationToken token = default)
        {
            Available(); LibraryAccess.RequireStaff(actor);
            CatalogEntry entry = Clone<CatalogEntry>(request);
            entry.Id = IdGenerator.Generate("cex_"); entry.CreatedByUserId = actor.UserId; entry.CreatedUtc = entry.UpdatedUtc = DateTime.UtcNow;
            await ValidateAsync(entry, false, token).ConfigureAwait(false);
            WordFilterResult filtered = await CheckContentAsync(actor,entry,token).ConfigureAwait(false);
            await SaveAsync(entry, null, true, null, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filtered, TargetTypeEnum.CatalogEntry, entry.Id, entry.CreatedByUserId, "Library", "*", token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info,"catalog.create",TargetTypeEnum.CatalogEntry.ToString(),entry.Id,entry.Name,token).ConfigureAwait(false);
            return await ViewAsync(entry, actor, true, token).ConfigureAwait(false);
        }

        /// <summary>Replace draft content using its revision; reviewed edits require a new review.</summary>
        public async Task<CatalogEntryView> UpdateAsync(ContentActor actor, string key, CatalogEntryRequest request, CancellationToken token = default, RequestInfo? info = null)
        {
            Available(); LibraryAccess.RequireStaff(actor);
            CatalogEntry previous = await RequireAsync(key, token).ConfigureAwait(false);
            Revision(previous, request.ExpectedRevision);
            CatalogEntry entry = Clone<CatalogEntry>(request);
            entry.Id = previous.Id; entry.CreatedUtc = previous.CreatedUtc; entry.CreatedByUserId = previous.CreatedByUserId; entry.Revision = previous.Revision + 1; entry.UpdatedUtc = DateTime.UtcNow;
            entry.ModerationState = previous.ModerationState;
            entry.ModalityDetails=request.ModalityDetails??previous.ModalityDetails;
            entry.ApparatusSetup=request.ApparatusSetup??previous.ApparatusSetup;
            await ValidateAsync(entry, false, token).ConfigureAwait(false);
            WordFilterResult filtered = await CheckContentAsync(actor,entry,token).ConfigureAwait(false);
            await SaveAsync(entry, previous.Revision, true, null, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filtered, TargetTypeEnum.CatalogEntry, entry.Id, entry.CreatedByUserId, "Library", "*", token).ConfigureAwait(false);
            if (info != null) await _Services.Audit.WriteAsync(info,"catalog.update",TargetTypeEnum.CatalogEntry.ToString(),entry.Id,entry.Name,token).ConfigureAwait(false);
            return await ViewAsync(entry, actor, true, token).ConfigureAwait(false);
        }

        /// <summary>Submit, explicitly approve licensing/content, reject, archive, or soft delete a revision.</summary>
        public async Task<CatalogEntryView> ActionAsync(ContentActor actor, string key, string action, CatalogActionRequest request, CancellationToken token = default, RequestInfo? info = null)
        {
            Available(); LibraryAccess.RequireStaff(actor);
            if ((action == "approve" || action == "reject") && !actor.IsSystemAdmin) throw new ApiErrorException(403, "Forbidden", "A system administrator must review catalog entries.");
            CatalogEntry entry = await RequireAsync(key, token).ConfigureAwait(false);
            Revision(entry, request.ExpectedRevision);
            if ((action == "submit" || action == "approve") && entry.ModerationState != ModerationStateEnum.None)
                throw new ApiErrorException(409, "InvalidTransition", "Restore this entry through moderation before submitting it for publication.");
            if (request.Note?.Length > 1000) throw ApiErrorException.Field("note", "Use at most 1000 characters.");
            if (action == "submit" && entry.Status != "Draft" && entry.Status != "Rejected" || (action == "approve" || action == "reject") && entry.Status != "InReview")
                throw new ApiErrorException(409, "InvalidTransition", "This action does not apply to the current workflow state.");
            if (action == "submit" || action == "approve") await ValidateAsync(entry, true, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            switch (action)
            {
                case "submit": entry.Status = "InReview"; break;
                case "approve": entry.Status = "Published"; entry.PublishedUtc = now; entry.ReviewedUtc = now; entry.ReviewedByUserId = actor.UserId; break;
                case "reject": entry.Status = "Rejected"; entry.ReviewedUtc = now; entry.ReviewedByUserId = actor.UserId; break;
                case "archive": entry.Status = "Archived"; break;
                case "delete": entry.Status = "Archived"; entry.DeletedUtc = now; break;
                default: throw ApiErrorException.Field("action", "Unknown action.");
            }
            entry.ReviewNote = request.Note; entry.UpdatedUtc = now; entry.Revision++;
            await SaveAsync(entry, request.ExpectedRevision, false, null, token).ConfigureAwait(false);
            if (info != null) await _Services.Audit.WriteAsync(info,"catalog." + action,TargetTypeEnum.CatalogEntry.ToString(),entry.Id,entry.Name,token).ConfigureAwait(false);
            return await ViewAsync(entry, actor, true, token).ConfigureAwait(false);
        }

        /// <summary>Bounded relationships, hiding unpublished or removed targets from public routes.</summary>
        public async Task<EnumerationResult<CatalogRelationship>> RelationshipsAsync(ContentActor actor, string key, EnumerationQuery? query, bool admin, CancellationToken token = default)
        {
            CatalogEntryView source = await GetAsync(actor, key, admin, token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query ?? new EnumerationQuery(), ExerciseCatalogCollections.Relationships, admin, "catalog-relationships",100);
            EnumerationResult<CatalogRelationship> page = await _Services.Database.ExerciseCatalog.RelationshipsAsync(request, source.Id, !admin, token).ConfigureAwait(false);
            if (!admin) foreach (CatalogRelationship edge in page.Objects) { edge.Labels = edge.Labels.Where(l => !MetadataRules.IsStaff(l)).ToList(); edge.Tags = edge.Tags.Where(t => !MetadataRules.IsStaff(t.Key)).ToDictionary(t => t.Key, t => t.Value); }
            return page;
        }

        /// <summary>Atomically replace outgoing relationships and require renewed source review.</summary>
        public async Task<CatalogEntryView> SetRelationshipsAsync(ContentActor actor, string key, CatalogRelationshipsRequest request, CancellationToken token = default, RequestInfo? info = null)
        {
            Available(); LibraryAccess.RequireStaff(actor);
            CatalogEntry entry = await RequireAsync(key, token).ConfigureAwait(false); Revision(entry, request.ExpectedRevision);
            if (request.Relationships == null || request.Relationships.Count > 100) throw ApiErrorException.Field("relationships", "Provide at most 100 relationships.");
            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string?> text = new Dictionary<string, string?>();
            foreach (CatalogRelationship edge in request.Relationships)
            {
                if (edge == null || !_Relations.Contains(edge.Kind) || edge.Note?.Length > 1000) throw ApiErrorException.Field("relationships", "Provide a supported kind and note of at most 1000 characters.");
                CatalogEntry target = await RequireAsync(edge.TargetId, token).ConfigureAwait(false);
                if (target.Id == entry.Id || !unique.Add(edge.Kind + ":" + target.Id)) throw ApiErrorException.Field("relationships", "Self references and duplicate relationships are not allowed.");
                edge.TargetId = target.Id; edge.Id = IdGenerator.Generate("cre_"); edge.SourceId = entry.Id; edge.CreatedUtc = DateTime.UtcNow;
                Dictionary<string,string> errors = new Dictionary<string,string>(); edge.Labels = MetadataRules.CleanLabels(edge.Labels,"labels",errors); edge.Tags = MetadataRules.CleanTags(edge.Tags,"tags",errors); if (errors.Count > 0) throw ApiErrorException.Validation(errors);
                text["relationships[" + text.Count + "].note"] = edge.Note;
            }
            WordFilterResult filtered = await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.CatalogEntry, text, token).ConfigureAwait(false);
            entry.Status = "Draft"; entry.PublishedUtc = null; entry.ReviewedUtc = null; entry.ReviewedByUserId = null; entry.ReviewNote = null; entry.UpdatedUtc = DateTime.UtcNow; entry.Revision++;
            await SaveAsync(entry, request.ExpectedRevision, false, request.Relationships, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filtered, TargetTypeEnum.CatalogEntry, entry.Id, entry.CreatedByUserId, "Library", "*", token).ConfigureAwait(false);
            if (info != null) await _Services.Audit.WriteAsync(info,"catalog.relationships",TargetTypeEnum.CatalogEntry.ToString(),entry.Id,request.Relationships.Count + " outgoing relationships",token).ConfigureAwait(false);
            return await ViewAsync(entry, actor, true, token).ConfigureAwait(false);
        }

        private async Task ValidateAsync(CatalogEntry entry, bool publishable, CancellationToken token)
        {
            Dictionary<string,string> errors = new Dictionary<string,string>();
            entry.Name = (entry.Name ?? String.Empty).Trim(); entry.Slug = (entry.Slug ?? String.Empty).Trim().ToLowerInvariant();
            if (entry.Name.Length < 2 || entry.Name.Length > 160) errors["name"] = "Use 2 to 160 characters.";
            if (!PublicSlugRules.IsValid(entry.Slug)) errors["slug"] = PublicSlugRules.Message;
            if (!_Kinds.Contains(entry.Kind)) errors["kind"] = "Choose Exercise or Technique.";
            if (!_Difficulties.Contains(entry.Difficulty)) errors["difficulty"] = "Choose a supported difficulty.";
            if (entry.Summary?.Length > 2000) errors["summary"] = "Use at most 2000 characters.";
            foreach (string? value in new[] { entry.MovementPattern,entry.Plane,entry.Laterality,entry.TechniqueType,entry.FromPosition,entry.ToPosition }) if (value?.Length > 100) errors["descriptors"] = "Use at most 100 characters per descriptor.";
            foreach (KeyValuePair<string,List<string>> field in CatalogEntryFields.Lists(entry))
            {
                int maxCount = field.Key == "instructions" ? 50 : field.Key == "lessonIds" ? 20 : 30;
                int maxLength = field.Key == "aliases" ? 100 : field.Key == "primaryMuscles" || field.Key == "secondaryMuscles" || field.Key == "equipment" ? 64 : 1000;
                if (field.Value == null || field.Value.Count > maxCount || field.Value.Any(v => String.IsNullOrWhiteSpace(v) || v.Length > maxLength)) errors[field.Key] = "Provide at most " + maxCount + " nonempty values of at most " + maxLength + " characters.";
                else { for (int i = 0; i < field.Value.Count; i++) field.Value[i] = field.Value[i].Trim(); if (field.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() != field.Value.Count) errors[field.Key] = "Duplicate values are not allowed."; }
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            PropertyLibrarySection config = _Services.Property.Property.LibrarySection ?? new PropertyLibrarySection();
            ValidateModalityDetails(entry,config,errors);
            ValidateApparatus(entry,config,errors);
            if (entry.PrimaryMuscles.Concat(entry.SecondaryMuscles).Any(v => !config.CatalogMuscles.Any(o => o.Key == v))) errors["primaryMuscles"] = "Use configured muscle keys.";
            if (entry.Equipment.Any(v => !config.CatalogEquipment.Any(o => o.Key == v))) errors["equipment"] = "Use configured equipment keys.";
            if (!String.IsNullOrEmpty(entry.Program) && !(await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false)).Any(p => p.Key == entry.Program)) errors["program"] = "Use a configured program key.";
            foreach (string id in entry.LessonIds) if (await _Services.Database.Library.Catalog.ReadLessonAsync(id, token).ConfigureAwait(false) == null) errors["lessonIds"] = "A referenced lesson does not exist in this property.";
            if (entry.Sources == null || entry.Sources.Count > 20) errors["sources"] = "Provide at most 20 source references.";
            else foreach (CatalogSource source in entry.Sources)
            {
                if (source == null || String.IsNullOrWhiteSpace(source.Title) || source.Title.Length > 200 || String.IsNullOrWhiteSpace(source.License) || source.License.Length > 80 || source.Attribution?.Length > 2000 || source.Model?.Length > 200 || !new[] { "Manual", "Model", "Import" }.Contains(source.Method)) { errors["sources"] = "Source title, license, provenance method or attribution is invalid."; continue; }
                if (source.Url != null && (source.Url.Length > 2000 || !Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo))) errors["sources"] = "Use absolute HTTPS source URLs without credentials.";
                if (publishable && (!_Licenses.Contains(source.License) || !source.License.Equals("Original",StringComparison.OrdinalIgnoreCase) && String.IsNullOrWhiteSpace(source.Url) || source.License.Equals("CC-BY-4.0",StringComparison.OrdinalIgnoreCase) && String.IsNullOrWhiteSpace(source.Attribution))) errors["sources"] = "Publication needs reviewed permissive source licenses, URLs, and required attribution.";
            }
            if (publishable && (entry.Instructions.Count == 0 || String.IsNullOrWhiteSpace(entry.Program) || entry.Sources?.Count == 0 || entry.Kind == "Exercise" && entry.PrimaryMuscles.Count == 0)) errors["publication"] = "Provide instructions, a program, licensed sources, and primary muscles for exercises before review.";
            entry.Labels = MetadataRules.CleanLabels(entry.Labels,"labels",errors); entry.Tags = MetadataRules.CleanTags(entry.Tags,"tags",errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        private async Task<CatalogEntryView> ViewAsync(CatalogEntry entry, ContentActor actor, bool admin, CancellationToken token)
        {
            CatalogEntryView view = Clone<CatalogEntryView>(entry);
            view.Viewer = new CatalogEntryViewer { CanEdit = actor.IsLibraryStaff, CanSubmit = actor.IsLibraryStaff && entry.ModerationState == ModerationStateEnum.None && (entry.Status == "Draft" || entry.Status == "Rejected"), CanReview = actor.IsSystemAdmin && entry.ModerationState == ModerationStateEnum.None && entry.Status == "InReview" };
            if (!admin)
            {
                view.ReviewNote = null; view.ReviewedByUserId = null; view.CreatedByUserId = null;
                view.Labels = view.Labels.Where(l => !MetadataRules.IsStaff(l)).ToList(); view.Tags = view.Tags.Where(t => !MetadataRules.IsStaff(t.Key)).ToDictionary(t => t.Key,t => t.Value);
                List<string> visible = new List<string>();
                foreach (string id in view.LessonIds)
                {
                    try { Lesson lesson = await _Services.Library.Lessons.RequireVisibleAsync(actor,id,token).ConfigureAwait(false); if (lesson.Status == LibraryStatusEnum.Published && LibraryAccess.CanRead(actor,lesson.Audience)) visible.Add(id); }
                    catch (ApiErrorException) { }
                }
                view.LessonIds = visible;
            }
            return view;
        }

        private async Task<CatalogEntry> RequireAsync(string key, CancellationToken token) { return await _Services.Database.ExerciseCatalog.ReadAsync(key, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Catalog entry"); }
        private static T Clone<T>(object value) { return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!; }
        private void Available() { if (!_Services.Property.Property.Features.Catalog) throw new ApiErrorException(404,"NotAvailable","The catalog is unavailable."); }
        private static void Revision(CatalogEntry entry, long? expected) { if (expected == null || expected < 1) throw ApiErrorException.Field("expectedRevision","A positive revision is required."); if (entry.Revision != expected) throw new ApiErrorException(409,"CatalogChanged","The entry changed. Reload before saving."); }
        private Task<WordFilterResult> CheckContentAsync(ContentActor actor,CatalogEntry entry,CancellationToken token)
        {
            Dictionary<string,string?> fields=new Dictionary<string,string?> { { "name",entry.Name }, { "summary",entry.Summary } };
            foreach (KeyValuePair<string,List<string>> field in CatalogEntryFields.Lists(entry)) fields[field.Key]=String.Join("\n",field.Value);
            fields["apparatusSetup.apparatusKey"]=entry.ApparatusSetup?.ApparatusKey;fields["apparatusSetup.manufacturer"]=entry.ApparatusSetup?.Manufacturer;fields["apparatusSetup.model"]=entry.ApparatusSetup?.Model;fields["apparatusSetup.setupText"]=entry.ApparatusSetup?.SetupText;fields["apparatusSetup.springSetupText"]=entry.ApparatusSetup?.SpringSetupText;
            fields["modalityDetails.familyKey"]=entry.ModalityDetails?.FamilyKey;
            fields["modalityDetails.attireKey"]=entry.ModalityDetails?.AttireKey;fields["modalityDetails.levelKey"]=entry.ModalityDetails?.LevelKey;fields["modalityDetails.notationSystemKey"]=entry.ModalityDetails?.NotationSystemKey;fields["modalityDetails.notationText"]=entry.ModalityDetails?.NotationText;
            if(entry.ModalityDetails!=null)for(int i=0;i<entry.ModalityDetails.LocalizedNames.Count;i++)fields["modalityDetails.localizedNames["+i+"]"]=entry.ModalityDetails.LocalizedNames[i].Text;
            fields["descriptors"] = String.Join("\n", new[] { entry.MovementPattern, entry.Plane, entry.Laterality, entry.TechniqueType, entry.FromPosition, entry.ToPosition });
            for (int i = 0; i < entry.Sources.Count; i++) fields["sources[" + i + "]"] = String.Join("\n", new[] { entry.Sources[i].Title, entry.Sources[i].Url, entry.Sources[i].License, entry.Sources[i].Attribution });
            return _Services.WordFilter.CheckAsync(actor,TargetTypeEnum.CatalogEntry,fields,token);
        }
        private async Task SaveAsync(CatalogEntry entry, long? revision, bool content, List<CatalogRelationship>? edges, CancellationToken token)
        {
            try { if (!await _Services.Database.ExerciseCatalog.SaveAsync(entry,revision,content,edges,token).ConfigureAwait(false)) throw new ApiErrorException(409,"CatalogChanged","The entry changed. Reload before saving."); }
            catch (DuplicateRecordException) { throw new ApiErrorException(409,"SlugInUse","Another entry uses this slug."); }
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.CatalogEntry,entry.Id,token).ConfigureAwait(false);
        }
    }
}
