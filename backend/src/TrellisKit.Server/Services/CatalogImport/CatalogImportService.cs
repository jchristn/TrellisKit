namespace TrellisKit.Server.Services.CatalogImport
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
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>Administrator-reviewed catalog source configuration. All changes revoke prior approval; no operation publishes catalog entries.</summary>
    public sealed class CatalogImportService
    {
        /// <summary>Outbound address policy for manifest fetches. Default <see cref="PublicAddressPolicy"/> (public addresses, ports 80 and 443).</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public ILinkPreviewAddressPolicy AddressPolicy { get { return _AddressPolicy; } set { _AddressPolicy = value ?? throw new ArgumentNullException(nameof(AddressPolicy)); } }
        /// <summary>DNS resolver used by the address policy. Default <see cref="SystemDnsResolver"/>.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public IDnsResolver Resolver { get { return _Resolver; } set { _Resolver = value ?? throw new ArgumentNullException(nameof(Resolver)); } }
        private ILinkPreviewAddressPolicy _AddressPolicy = new PublicAddressPolicy();
        private IDnsResolver _Resolver = new SystemDnsResolver();
        private readonly ApplicationServices _Services;
        private static readonly string[] _Licenses={"CC0-1.0","CC-BY-4.0","PublicDomain","Unlicense"};
        /// <summary>Create for one property.</summary>
        public CatalogImportService(ApplicationServices services){_Services=services??throw new ArgumentNullException(nameof(services));}
        /// <summary>Read a bounded metadata-filterable source page; requires SystemAdmin and library feature.</summary>
        public Task<EnumerationResult<CatalogImportSource>> EnumerateAsync(ContentActor actor,EnumerationQuery query,CancellationToken token=default)
        {
            RequireAdmin(actor);
            foreach(string key in new[]{"enabled","reviewed"}){string? value=query.FilterString(key);if(value!=null&&value!="true"&&value!="false")throw ApiErrorException.Field(key,"Use true or false.");}
            return _Services.Database.CatalogImports.EnumerateAsync(_Services.Enumeration.Prepare(query,ExerciseCatalogCollections.ImportSources,true,"catalog-import-sources",100),token);
        }
        /// <summary>Read one source or 404; requires SystemAdmin.</summary>
        public async Task<CatalogImportSource> GetAsync(ContentActor actor,string id,CancellationToken token=default)
        { RequireAdmin(actor);return await _Services.Database.CatalogImports.ReadAsync(id,token).ConfigureAwait(false)??throw ApiErrorException.NotFound("Catalog import source"); }
        /// <summary>Create or replace validated configuration, revoking review and requiring an expected revision for updates.</summary>
        public async Task<CatalogImportSource> SaveAsync(ContentActor actor,string? id,CatalogImportSourceRequest request,RequestInfo info,CancellationToken token=default)
        {
            RequireAdmin(actor);ArgumentNullException.ThrowIfNull(request);
            CatalogImportSource? previous=id==null?null:await GetAsync(actor,id,token).ConfigureAwait(false);
            if(previous!=null&&(request.ExpectedRevision==null||request.ExpectedRevision!=previous.Revision))throw Changed();
            CatalogImportSource source=JsonSerializer.Deserialize<CatalogImportSource>(JsonSerializer.Serialize(request))!;
            source.Id=previous?.Id??IdGenerator.Generate("cis_");source.Revision=(previous?.Revision??0)+1;source.CreatedUtc=previous?.CreatedUtc??DateTime.UtcNow;source.UpdatedUtc=DateTime.UtcNow;source.LastAttemptUtc=previous?.LastAttemptUtc;
            Validate(source);
            try{if(!await _Services.Database.CatalogImports.SaveAsync(source,previous?.Revision,token).ConfigureAwait(false))throw Changed();}
            catch(DuplicateRecordException){throw new ApiErrorException(409,"SourceUrlInUse","Another catalog source uses this URL.");}
            await _Services.Audit.WriteAsync(info,"catalog.importSource.save","CatalogImportSource",source.Id,source.Name,token).ConfigureAwait(false);return source;
        }
        /// <summary>Record explicit source licensing review against an unchanged revision. This authorizes import only, never publication.</summary>
        public async Task<CatalogImportSource> ReviewAsync(ContentActor actor,string id,CatalogImportReviewRequest request,RequestInfo info,CancellationToken token=default)
        {
            CatalogImportSource source=await GetAsync(actor,id,token).ConfigureAwait(false);
            if(!request.LicenseReviewed)throw ApiErrorException.Field("licenseReviewed","Explicitly review the manifest source and reuse license.");
            if(source.Revision!=request.ExpectedRevision)throw Changed();Validate(source);
            source.ReviewedUtc=source.UpdatedUtc=DateTime.UtcNow;source.ReviewedByUserId=actor.UserId;source.Revision++;
            if(!await _Services.Database.CatalogImports.SaveAsync(source,request.ExpectedRevision,token).ConfigureAwait(false))throw Changed();
            await _Services.Audit.WriteAsync(info,"catalog.importSource.review","CatalogImportSource",source.Id,source.License,token).ConfigureAwait(false);return source;
        }
        private void RequireAdmin(ContentActor actor)
        { if(!actor.IsSystemAdmin)throw new ApiErrorException(403,"Forbidden","A system administrator must manage catalog sources.");if(!_Services.Property.Property.Features.Catalog)throw new ApiErrorException(404,"NotAvailable","The catalog is unavailable."); }
        private static ApiErrorException Changed(){return new ApiErrorException(409,"CatalogSourceChanged","The source changed. Reload before saving or reviewing.");}
        private static void Validate(CatalogImportSource source)
        {
            Dictionary<string,string> errors=new Dictionary<string,string>();source.Name=(source.Name??String.Empty).Trim();source.Url=(source.Url??String.Empty).Trim();
            if(source.Name.Length<2||source.Name.Length>200)errors["name"]="Use 2..200 characters.";
            if(source.Url.Length>2000||!Uri.TryCreate(source.Url,UriKind.Absolute,out Uri? uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||uri.Fragment.Length>0||uri.Query.Length>0)errors["url"]="Use an absolute HTTPS URL without credentials, query secrets or fragments.";
            string? license=_Licenses.FirstOrDefault(l=>String.Equals(l,source.License,StringComparison.OrdinalIgnoreCase));
            if(license==null)errors["license"]="Choose CC0-1.0, CC-BY-4.0, PublicDomain or Unlicense.";else source.License=license;
            if(source.Attribution?.Length>2000||source.License=="CC-BY-4.0"&&String.IsNullOrWhiteSpace(source.Attribution))errors["attribution"]="Required for CC-BY-4.0, at most 2000 characters.";
            if(source.MaxEntries<1||source.MaxEntries>100)errors["maxEntries"]="Use 1..100.";
            source.Labels=MetadataRules.CleanLabels(source.Labels,"labels",errors);source.Tags=MetadataRules.CleanTags(source.Tags,"tags",errors);
            if(errors.Count>0)throw ApiErrorException.Validation(errors);
        }
    }
}
