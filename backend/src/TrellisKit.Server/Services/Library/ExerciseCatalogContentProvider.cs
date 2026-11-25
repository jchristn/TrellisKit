namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>Catalog visibility, search, reports and moderation reuse the library content infrastructure.</summary>
    public sealed class ExerciseCatalogContentProvider : ContentTargetProvider
    {
        private readonly ApplicationServices _Services;
        /// <summary>Instantiate for one property.</summary>
        public ExerciseCatalogContentProvider(ApplicationServices services) { _Services = services; }
        /// <inheritdoc />
        public override IReadOnlyList<TargetTypeEnum> TargetTypes => new[] { TargetTypeEnum.CatalogEntry };
        /// <inheritdoc />
        public override string ScopeType(TargetTypeEnum type) => "Library";
        /// <inheritdoc />
        public override bool Moderates(ContentActor actor,string scope) => scope == "Library" && actor.IsLibraryStaff;
        /// <inheritdoc />
        public override string PathOf(TargetTypeEnum type,string id) => "/catalog/" + id;
        /// <inheritdoc />
        public override async Task<bool> IsVisibleAsync(ContentActor actor,TargetTypeEnum type,string id,CancellationToken token=default)
        {
            CatalogEntry? entry=await _Services.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false);
            return _Services.Property.Property.Features.Catalog && entry != null && (actor.IsLibraryStaff || entry.Status == "Published" && entry.ModerationState == ModerationStateEnum.None);
        }
        /// <inheritdoc />
        public override async Task<ModerationTarget?> LoadModerationTargetAsync(TargetTypeEnum type,string id,CancellationToken token=default)
        {
            CatalogEntry? entry=await _Services.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false);
            return entry == null ? null : new ModerationTarget { TargetType=TargetTypeEnum.CatalogEntry,TargetId=entry.Id,AuthorUserId=entry.CreatedByUserId ?? String.Empty,State=entry.ModerationState };
        }
        /// <inheritdoc />
        public override async Task SetModerationStateAsync(ModerationTarget target,ModerationStateEnum state,string? moderatorUserId,string? reason,CancellationToken token=default)
        {
            CatalogEntry entry=await _Services.Database.ExerciseCatalog.ReadAsync(target.TargetId,token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Catalog entry");
            long revision=entry.Revision; entry.ModerationState=state; entry.Revision++; entry.Status=state == ModerationStateEnum.None ? "Draft" : "Archived"; entry.UpdatedUtc=DateTime.UtcNow; entry.ReviewNote=reason; entry.ReviewedByUserId=moderatorUserId; entry.ReviewedUtc=entry.UpdatedUtc; entry.PublishedUtc=null;
            if (!await _Services.Database.ExerciseCatalog.SaveAsync(entry,revision,false,null,token).ConfigureAwait(false)) throw new ApiErrorException(409,"CatalogChanged","The catalog entry changed; reload before moderating.");
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.CatalogEntry,entry.Id,token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        public override Task<object> ModerationViewAsync(ContentActor actor,ModerationTarget target,CancellationToken token=default) => ReadModerationAsync(actor,target.TargetId,token);
        private async Task<object> ReadModerationAsync(ContentActor actor,string id,CancellationToken token) => await _Services.Library.ExerciseCatalog.GetAsync(actor,id,true,token).ConfigureAwait(false);
        /// <inheritdoc />
        public override async Task<SearchDocument?> SearchDocumentAsync(TargetTypeEnum type,string id,CancellationToken token=default)
        {
            CatalogEntry? entry=await _Services.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false);
            if (!_Services.Property.Property.Features.Catalog || entry == null || entry.Status != "Published" || entry.ModerationState != ModerationStateEnum.None) return null;
            SearchDocument document=new SearchDocument { Id=type + ":" + id, Content=String.Join("\n",new[] { entry.Name,entry.Summary ?? String.Empty,entry.ApparatusSetup?.ApparatusKey??String.Empty,entry.ApparatusSetup?.Manufacturer??String.Empty,entry.ApparatusSetup?.Model??String.Empty,entry.ApparatusSetup?.SetupText??String.Empty,entry.ApparatusSetup?.SpringSetupText??String.Empty,entry.ModalityDetails?.FamilyKey??String.Empty,entry.ModalityDetails?.AttireKey??String.Empty,entry.ModalityDetails?.LevelKey??String.Empty,entry.ModalityDetails?.NotationSystemKey??String.Empty,entry.ModalityDetails?.NotationText??String.Empty }.Concat(entry.ModalityDetails?.LocalizedNames.Select(n=>n.Text)??Enumerable.Empty<string>()).Concat(CatalogEntryFields.Lists(entry).Where(f => f.Key != "lessonIds").SelectMany(f => f.Value))),Labels=entry.Labels };
            foreach (KeyValuePair<string,string> tag in entry.Tags) document.Tags[tag.Key]=tag.Value;
            document.Tags["tk.type"]=type.ToString(); document.Tags["tk.audience"]="Public";
            return document;
        }
        /// <inheritdoc />
        public override async Task<SearchResultItem?> SearchResultAsync(ContentActor actor,TargetTypeEnum type,string id,IReadOnlyList<string> terms,CancellationToken token=default)
        {
            if (!await IsVisibleAsync(actor,type,id,token).ConfigureAwait(false)) return null;
            CatalogEntry? entry=await _Services.Database.ExerciseCatalog.ReadAsync(id,token).ConfigureAwait(false);
            if (entry == null || entry.Status != "Published" || entry.ModerationState != ModerationStateEnum.None) return null;
            SearchResultItem result = new SearchResultItem { Type=type.ToString(),Id=id,Title=entry.Name,Path="/catalog/" + entry.Slug,CreatedUtc=entry.PublishedUtc ?? entry.CreatedUtc };
            SearchSnippetView? snippet = SearchService.Snippet(entry.Summary ?? entry.Name,terms,"summary",null);
            if (snippet != null) result.Snippet = snippet;
            return result;
        }
    }
}
