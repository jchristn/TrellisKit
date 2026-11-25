namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;

    public sealed partial class ExerciseCatalogService
    {
        /// <summary>Validate and apply an imported Draft with atomic source-review, entry-revision and provenance guards. A false result preserves concurrent human edits or review revocations. Dry runs validate only.</summary>
        public async Task<bool> ApplyImportAsync(ContentActor actor,CatalogEntryRequest request,CatalogEntry? previous,CatalogImportItem import,bool dryRun,CancellationToken token=default)
        {
            Available();if(!actor.IsSystemAdmin)throw new ApiErrorException(403,"Forbidden","Catalog import requires a system administrator.");
            CatalogEntry entry=Clone<CatalogEntry>(request);entry.Id=previous?.Id??IdGenerator.Generate("cex_");entry.CreatedUtc=previous?.CreatedUtc??DateTime.UtcNow;entry.CreatedByUserId=previous?.CreatedByUserId??actor.UserId;entry.UpdatedUtc=DateTime.UtcNow;entry.Revision=(previous?.Revision??0)+1;
            if(previous!=null&&(previous.Status!="Draft"||previous.ModerationState!=ModerationStateEnum.None||previous.Revision!=import.EntryRevision))return false;
            entry.ModalityDetails=request.ModalityDetails??previous?.ModalityDetails;
            entry.ApparatusSetup=request.ApparatusSetup??previous?.ApparatusSetup;
            await ValidateAsync(entry,false,token).ConfigureAwait(false);
            WordFilterResult filtered=await CheckContentAsync(actor,entry,token).ConfigureAwait(false);
            if(dryRun)return true;
            if(!await _Services.Database.ExerciseCatalog.SaveImportedAsync(entry,previous?.Revision,import,token).ConfigureAwait(false))return false;
            import.EntryId=entry.Id;import.EntryRevision=entry.Revision;
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.CatalogEntry,entry.Id,token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filtered,TargetTypeEnum.CatalogEntry,entry.Id,entry.CreatedByUserId,"Library","*",token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(new RequestInfo{UserAgent="Job catalog.importSources"},"catalog.import","CatalogEntry",entry.Id,"Reviewed source "+import.SourceId,token).ConfigureAwait(false);return true;
        }
    }
}
