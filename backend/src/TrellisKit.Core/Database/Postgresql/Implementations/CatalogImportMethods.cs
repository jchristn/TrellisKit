namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal sealed class CatalogImportMethods : MethodsBase, ICatalogImportMethods
    {
        internal CatalogImportMethods(DatabaseDriverBase driver,string? propertyId) : base(driver,PostgresqlDialect.Instance,propertyId) { }
        public async Task<CatalogImportSource?> ReadAsync(string id,CancellationToken token=default)
        {
            DataTable rows=await QueryAsync("SELECT * FROM catalogimportsources WHERE "+Scope()+" AND id="+D.Str(id)+";",token).ConfigureAwait(false);
            if(rows.Rows.Count==0)return null;
            CatalogImportSource source=Map(rows.Rows[0]);await FillAsync(new[]{source},token).ConfigureAwait(false);return source;
        }
        public async Task<EnumerationResult<CatalogImportSource>> EnumerateAsync(EnumerationRequest request,CancellationToken token=default)
        {
            List<string> conditions=new List<string>();
            string? enabled=request.Query.FilterString("enabled"),reviewed=request.Query.FilterString("reviewed");
            if(enabled!=null)conditions.Add("s.enabled="+(enabled=="true"?"TRUE":"FALSE"));
            if(reviewed!=null)conditions.Add("s.reviewedutc IS "+(reviewed=="true"?"NOT ":"")+"NULL");
            EnumerationResult<CatalogImportSource> page=await EnumerateAsync(request,"s.*","catalogimportsources s",conditions,Map,token).ConfigureAwait(false);
            await FillAsync(page.Objects,token).ConfigureAwait(false);return page;
        }
        public async Task<bool> SaveAsync(CatalogImportSource source,long? expectedRevision,CancellationToken token=default)
        {
            Dictionary<string,string> values=new Dictionary<string,string>{{"name",D.Str(source.Name)},{"url",D.Str(source.Url)},{"license",D.Str(source.License)},{"attribution",D.Str(source.Attribution)},{"enabled",D.Bool(source.Enabled)},{"maxentries",D.Num(source.MaxEntries)},{"revision",D.Num(source.Revision)},{"reviewedutc",D.Ts(source.ReviewedUtc)},{"reviewedbyuserid",D.Str(source.ReviewedByUserId)},{"createdutc",D.Ts(source.CreatedUtc)},{"updatedutc",D.Ts(source.UpdatedUtc)}};
            List<string> sql=new List<string>();
            sql.Add(expectedRevision==null?"INSERT INTO catalogimportsources(propertyid,id,"+String.Join(",",values.Keys)+") VALUES("+Pid+","+D.Str(source.Id)+","+String.Join(",",values.Values)+");":"UPDATE catalogimportsources SET "+String.Join(",",values.Select(v=>v.Key+"="+v.Value))+" WHERE "+Scope()+" AND id="+D.Str(source.Id)+";");
            string where=" WHERE "+Scope()+" AND targettype='CatalogImportSource' AND targetid="+D.Str(source.Id)+";";
            sql.Add("DELETE FROM labels"+where);sql.Add("DELETE FROM tags"+where);
            foreach(string label in source.Labels)sql.Add("INSERT INTO labels(propertyid,id,targettype,targetid,label,labelnormalized,createdutc) VALUES("+Pid+","+D.Str(IdGenerator.Generate("lbl_"))+",'CatalogImportSource',"+D.Str(source.Id)+","+D.Str(label)+","+D.Str(MetadataRules.Normalize(label))+","+D.Ts(source.UpdatedUtc)+");");
            foreach(KeyValuePair<string,string> tag in source.Tags)sql.Add("INSERT INTO tags(propertyid,id,targettype,targetid,tagkey,tagkeynormalized,tagvalue,createdutc,updatedutc) VALUES("+Pid+","+D.Str(IdGenerator.Generate("tag_"))+",'CatalogImportSource',"+D.Str(source.Id)+","+D.Str(tag.Key)+","+D.Str(MetadataRules.Normalize(tag.Key))+","+D.Str(tag.Value)+","+D.Ts(source.UpdatedUtc)+","+D.Ts(source.UpdatedUtc)+");");
            if(expectedRevision==null){await UniqueTransactionAsync(sql,"A catalog import source with this URL",token).ConfigureAwait(false);return true;}
            string guard="SELECT 1 FROM catalogimportsources WHERE "+Scope()+" AND id="+D.Str(source.Id)+" AND revision="+D.Num(expectedRevision.Value)+" FOR UPDATE";
            DataTable result=await UniqueTransactionAsync(new List<string>{"SELECT set_config('trelliskit.source_saved','false',true);","DO "+D.Str("BEGIN IF EXISTS("+guard+") THEN "+String.Join(" ",sql)+" PERFORM set_config('trelliskit.source_saved','true',true); END IF; END")+";","SELECT current_setting('trelliskit.source_saved') AS saved;"},"A catalog import source with this URL",token).ConfigureAwait(false);
            return result.Rows.Count>0&&RowReader.GetString(result.Rows[0],"saved")=="true";
        }
        public async Task<List<CatalogImportSource>> PlanAsync(IReadOnlyList<string> ids,CancellationToken token=default)
        {
            if(ids.Count>20)throw new ArgumentException("At most 20 sources per run.",nameof(ids));
            DataTable rows=await QueryAsync("SELECT * FROM catalogimportsources WHERE "+Scope()+" AND enabled=TRUE AND reviewedutc IS NOT NULL"+(ids.Count==0?"":" AND id IN "+D.InList(ids))+" ORDER BY lastattemptutc NULLS FIRST,id LIMIT 20;",token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().Select(Map).ToList();
        }
        public Task TouchAsync(string id,DateTime now,CancellationToken token=default){return QueryAsync("UPDATE catalogimportsources SET lastattemptutc="+D.Ts(now)+" WHERE "+Scope()+" AND id="+D.Str(id)+";",token);}
        public async Task<CatalogImportItem?> ItemAsync(string sourceId,string key,CancellationToken token=default)
        {
            DataTable rows=await QueryAsync("SELECT * FROM catalogimportitems WHERE "+Scope()+" AND sourceid="+D.Str(sourceId)+" AND itemkey="+D.Str(key)+";",token).ConfigureAwait(false);
            if(rows.Rows.Count==0)return null;DataRow r=rows.Rows[0];return new CatalogImportItem{SourceId=sourceId,ItemKey=key,EntryId=RowReader.GetString(r,"entryid"),SourceHash=RowReader.GetString(r,"sourcehash"),EntryRevision=RowReader.GetLong(r,"entryrevision"),SourceRevision=RowReader.GetLong(r,"sourcerevision")};
        }
        private async Task FillAsync(IEnumerable<CatalogImportSource> sources,CancellationToken token)
        {
            foreach(CatalogImportSource source in sources){ObjectMetadata metadata=await Driver.Metadata.ReadAsync(TargetTypeEnum.CatalogImportSource,source.Id,token).ConfigureAwait(false);source.Labels=metadata.Labels;source.Tags=metadata.Tags;}
        }
        private static CatalogImportSource Map(DataRow r){return new CatalogImportSource{Id=RowReader.GetString(r,"id"),Name=RowReader.GetString(r,"name"),Url=RowReader.GetString(r,"url"),License=RowReader.GetString(r,"license"),Attribution=RowReader.GetNullableString(r,"attribution"),Enabled=RowReader.GetBool(r,"enabled"),MaxEntries=RowReader.GetInt(r,"maxentries"),Revision=RowReader.GetLong(r,"revision"),ReviewedUtc=RowReader.GetNullableDateTime(r,"reviewedutc"),ReviewedByUserId=RowReader.GetNullableString(r,"reviewedbyuserid"),LastAttemptUtc=RowReader.GetNullableDateTime(r,"lastattemptutc"),CreatedUtc=RowReader.GetDateTime(r,"createdutc"),UpdatedUtc=RowReader.GetDateTime(r,"updatedutc")};}
    }
}
