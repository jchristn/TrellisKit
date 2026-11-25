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

    internal sealed class ExerciseCatalogMethods : MethodsBase, IExerciseCatalogMethods
    {
        internal ExerciseCatalogMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId) { }

        public async Task<CatalogEntry?> ReadAsync(string key, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            DataTable table = await QueryAsync("SELECT e.* FROM catalogentries e WHERE " + Scope("e") + " AND e.deletedutc IS NULL AND (e.id = " + D.Str(key) + " OR lower(e.slug) = " + D.Str(key.ToLowerInvariant()) + ") ORDER BY CASE WHEN e.id = " + D.Str(key) + " THEN 0 ELSE 1 END LIMIT 1;", token).ConfigureAwait(false);
            List<CatalogEntry> entries = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillAsync(entries, token).ConfigureAwait(false);
            return entries.FirstOrDefault();
        }

        public async Task<string?> DuplicateNameAsync(IReadOnlyList<string> names,CancellationToken token=default)
        {
            if(names.Count<1||names.Count>31||names.Any(n=>String.IsNullOrWhiteSpace(n)||n.Length>160))return null;
            string list=D.InList(names.Select(n=>n.Trim().ToLowerInvariant()));
            DataTable rows=await QueryAsync("SELECT e.id FROM catalogentries e WHERE "+Scope("e")+" AND e.deletedutc IS NULL AND (lower(e.name) IN "+list+" OR EXISTS(SELECT 1 FROM catalogentryvalues v WHERE "+Scope("v")+" AND v.entryid=e.id AND v.field='aliases' AND lower(v.value) IN "+list+")) ORDER BY e.id LIMIT 1;",token).ConfigureAwait(false);
            return rows.Rows.Count==0?null:RowReader.GetString(rows.Rows[0],"id");
        }

        public async Task<EnumerationResult<CatalogEntry>> EnumerateAsync(EnumerationRequest request, bool publishedOnly, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { "e.deletedutc IS NULL" };
            if (publishedOnly) conditions.Add("e.status = 'Published' AND e.moderationstate = 'None'");
            foreach (string field in new[] { "kind", "program", "difficulty", "status" })
            {
                string? value = request.Query.FilterString(field);
                if (value != null) conditions.Add("e." + field + " = " + D.Str(value));
            }
            string? family=request.Query.FilterString("familyKey");if(family!=null)conditions.Add("e.familykey="+D.Str(family));
            foreach(string key in new[]{"attireKey","levelKey","notationSystemKey"}){string? value=request.Query.FilterString(key);if(value!=null)conditions.Add("e."+key.ToLowerInvariant()+"="+D.Str(value));}
            string? language=request.Query.FilterString("nameLanguage");if(language!=null)conditions.Add("EXISTS(SELECT 1 FROM cataloglocalizednames cn WHERE "+Scope("cn")+" AND cn.entryid=e.id AND cn.languagetag="+D.Str(CatalogLanguageTags.Normalize(language))+")");
            foreach (string field in new[] { "muscle", "equipment" })
            {
                string? value = request.Query.FilterString(field);
                if (value != null) conditions.Add("EXISTS (SELECT 1 FROM catalogentryvalues cv WHERE " + Scope("cv") + " AND cv.entryid = e.id AND " + (field == "muscle" ? "cv.field IN ('primaryMuscles','secondaryMuscles')" : "cv.field = 'equipment'") + " AND cv.value = " + D.Str(value) + ")");
            }
            EnumerationResult<CatalogEntry> page = await EnumerateAsync(request, "e.*", "catalogentries e", conditions, Map, token).ConfigureAwait(false);
            await FillAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public Task<bool> SaveAsync(CatalogEntry entry, long? expectedRevision, bool replaceContent, List<CatalogRelationship>? relationships, CancellationToken token = default)
        { return SaveCoreAsync(entry,expectedRevision,replaceContent,relationships,null,token); }
        public Task<bool> SaveImportedAsync(CatalogEntry entry,long? expectedRevision,CatalogImportItem import,CancellationToken token=default)
        { return SaveCoreAsync(entry,expectedRevision,true,null,import,token); }
        private async Task<bool> SaveCoreAsync(CatalogEntry entry, long? expectedRevision, bool replaceContent, List<CatalogRelationship>? relationships,CatalogImportItem? import,CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(entry);
            Dictionary<string, string> values = Values(entry);
            List<string> sql = new List<string>();
            if (expectedRevision == null)
                sql.Add("INSERT INTO catalogentries (propertyid, id, " + String.Join(", ", values.Keys) + ") VALUES (" + Pid + ", " + D.Str(entry.Id) + ", " + String.Join(", ", values.Values) + ");");
            else sql.Add("UPDATE catalogentries SET " + String.Join(", ", values.Select(v => v.Key + " = " + v.Value)) + " WHERE " + Scope() + " AND id = " + D.Str(entry.Id) + ";");
            if (replaceContent)
            {
                sql.Add("DELETE FROM cataloglocalizednames WHERE "+Scope()+" AND entryid="+D.Str(entry.Id)+";");
                List<CatalogLocalizedName> localized=entry.ModalityDetails?.LocalizedNames??new List<CatalogLocalizedName>();
                for(int i=0;i<localized.Count;i++)sql.Add("INSERT INTO cataloglocalizednames(propertyid,entryid,position,languagetag,text) VALUES("+Pid+","+D.Str(entry.Id)+","+D.Num(i)+","+D.Str(localized[i].LanguageTag)+","+D.Str(localized[i].Text)+");");
                sql.Add("DELETE FROM catalogentryvalues WHERE " + Scope() + " AND entryid = " + D.Str(entry.Id) + ";");
                sql.Add("DELETE FROM catalogsources WHERE " + Scope() + " AND entryid = " + D.Str(entry.Id) + ";");
                foreach (KeyValuePair<string, List<string>> field in CatalogEntryFields.Lists(entry))
                    for (int i = 0; i < field.Value.Count; i++) sql.Add("INSERT INTO catalogentryvalues (propertyid,entryid,field,position,value) VALUES (" + Pid + ", " + D.Str(entry.Id) + ", " + D.Str(field.Key) + ", " + D.Num(i) + ", " + D.Str(field.Value[i]) + ");");
                for (int i = 0; i < entry.Sources.Count; i++)
                {
                    CatalogSource source = entry.Sources[i];
                    sql.Add("INSERT INTO catalogsources (propertyid,entryid,position,url,title,license,attribution,method,model,retrievedutc) VALUES (" + Pid + ", " + D.Str(entry.Id) + ", " + D.Num(i) + ", " + D.Str(source.Url) + ", " + D.Str(source.Title) + ", " + D.Str(source.License) + ", " + D.Str(source.Attribution) + ", " + D.Str(source.Method) + ", " + D.Str(source.Model) + ", " + D.Ts(source.RetrievedUtc) + ");");
                }
                sql.AddRange(MetadataStatements(TargetTypeEnum.CatalogEntry, entry.Id, entry.Labels, entry.Tags, entry.UpdatedUtc));
            }
            if (relationships != null)
            {
                string ids = "SELECT id FROM catalogrelationships WHERE " + Scope() + " AND sourceid = " + D.Str(entry.Id);
                sql.Add("DELETE FROM labels WHERE " + Scope() + " AND targettype = 'CatalogRelationship' AND targetid IN (" + ids + ");");
                sql.Add("DELETE FROM tags WHERE " + Scope() + " AND targettype = 'CatalogRelationship' AND targetid IN (" + ids + ");");
                sql.Add("DELETE FROM catalogrelationships WHERE " + Scope() + " AND sourceid = " + D.Str(entry.Id) + ";");
                foreach (CatalogRelationship edge in relationships)
                {
                    sql.Add("INSERT INTO catalogrelationships (propertyid,id,sourceid,targetid,kind,note,createdutc) VALUES (" + Pid + ", " + D.Str(edge.Id) + ", " + D.Str(entry.Id) + ", " + D.Str(edge.TargetId) + ", " + D.Str(edge.Kind) + ", " + D.Str(edge.Note) + ", " + D.Ts(edge.CreatedUtc) + ");");
                    sql.AddRange(MetadataStatements(TargetTypeEnum.CatalogRelationship, edge.Id, edge.Labels, edge.Tags, entry.UpdatedUtc));
                }
            }
            if(import!=null)
            {
                if(entry.Status!="Draft"||entry.ModerationState!=ModerationStateEnum.None)throw new ArgumentException("Imports may write only unmoderated drafts.",nameof(entry));
                string itemScope=Scope()+" AND sourceid="+D.Str(import.SourceId)+" AND itemkey="+D.Str(import.ItemKey);
                string sourceGuard="SELECT 1 FROM catalogimportsources WHERE "+Scope()+" AND id="+D.Str(import.SourceId)+" AND revision="+D.Num(import.SourceRevision)+" AND enabled=TRUE AND reviewedutc IS NOT NULL FOR UPDATE";
                string entryGuard=expectedRevision==null?"NOT EXISTS(SELECT 1 FROM catalogimportitems WHERE "+itemScope+")":"EXISTS(SELECT 1 FROM catalogentries WHERE "+Scope()+" AND id="+D.Str(entry.Id)+" AND revision="+D.Num(expectedRevision.Value)+" AND deletedutc IS NULL AND status='Draft' AND moderationstate='None' FOR UPDATE) AND EXISTS(SELECT 1 FROM catalogimportitems WHERE "+itemScope+" AND entryid="+D.Str(entry.Id)+" AND entryrevision="+D.Num(expectedRevision.Value)+")";
                sql.Add("INSERT INTO catalogimportitems(propertyid,sourceid,itemkey,entryid,sourcehash,entryrevision,sourcerevision,updatedutc) VALUES("+Pid+","+D.Str(import.SourceId)+","+D.Str(import.ItemKey)+","+D.Str(entry.Id)+","+D.Str(import.SourceHash)+","+D.Num(entry.Revision)+","+D.Num(import.SourceRevision)+","+D.Ts(entry.UpdatedUtc)+") ON CONFLICT(propertyid,sourceid,itemkey) DO UPDATE SET sourcehash=EXCLUDED.sourcehash,entryrevision=EXCLUDED.entryrevision,sourcerevision=EXCLUDED.sourcerevision,updatedutc=EXCLUDED.updatedutc;");
                string importBlock="BEGIN IF EXISTS("+sourceGuard+") THEN IF "+entryGuard+" THEN "+String.Join(" ",sql)+" PERFORM set_config('trelliskit.catalog_saved','true',true); END IF; END IF; END";
                DataTable imported=await UniqueTransactionAsync(new List<string>{"SELECT set_config('trelliskit.catalog_saved','false',true);","DO "+D.Str(importBlock)+";","SELECT current_setting('trelliskit.catalog_saved') AS saved;"},"A catalog entry with this slug",token).ConfigureAwait(false);
                return imported.Rows.Count>0&&RowReader.GetString(imported.Rows[0],"saved")=="true";
            }
            if (expectedRevision == null)
            {
                await UniqueTransactionAsync(sql, "A catalog entry with this slug", token).ConfigureAwait(false);
                return true;
            }
            string guard = "SELECT 1 FROM catalogentries WHERE " + Scope() + " AND id = " + D.Str(entry.Id) + " AND deletedutc IS NULL AND revision = " + D.Num(expectedRevision.Value) + " FOR UPDATE";
            string block = "BEGIN IF EXISTS (" + guard + ") THEN " + String.Join(" ", sql) + " PERFORM set_config('trelliskit.catalog_saved','true',true); END IF; END";
            DataTable result = await UniqueTransactionAsync(new List<string> { "SELECT set_config('trelliskit.catalog_saved','false',true);", "DO " + D.Str(block) + ";", "SELECT current_setting('trelliskit.catalog_saved') AS saved;" }, "A catalog entry with this slug", token).ConfigureAwait(false);
            return result.Rows.Count > 0 && RowReader.GetString(result.Rows[0], "saved") == "true";
        }

        public async Task<EnumerationResult<CatalogRelationship>> RelationshipsAsync(EnumerationRequest request, string sourceId, bool publishedOnly, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { "r.sourceid = " + D.Str(sourceId), "target.deletedutc IS NULL" };
            if (publishedOnly) conditions.Add("target.status = 'Published' AND target.moderationstate = 'None'");
            string? kind = request.Query.FilterString("kind");
            if (kind != null) conditions.Add("r.kind = " + D.Str(kind));
            EnumerationResult<CatalogRelationship> page = await EnumerateAsync(request, "r.*, target.name AS targetname, target.slug AS targetslug, target.kind AS targetkind", "catalogrelationships r JOIN catalogentries target ON target.propertyid = r.propertyid AND target.id = r.targetid", conditions, r => new CatalogRelationship { Id = RowReader.GetString(r, "id"), SourceId = RowReader.GetString(r, "sourceid"), TargetId = RowReader.GetString(r, "targetid"), Kind = RowReader.GetString(r, "kind"), Note = RowReader.GetNullableString(r, "note"), CreatedUtc = RowReader.GetDateTime(r, "createdutc"), TargetName = RowReader.GetString(r, "targetname"), TargetSlug = RowReader.GetString(r, "targetslug"), TargetKind = RowReader.GetString(r, "targetkind") }, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await Driver.Metadata.ReadManyAsync(TargetTypeEnum.CatalogRelationship, page.Objects.Select(e => e.Id), token).ConfigureAwait(false);
            foreach (CatalogRelationship edge in page.Objects) { edge.Labels = metadata[edge.Id].Labels; edge.Tags = metadata[edge.Id].Tags; }
            return page;
        }

        private async Task FillAsync(List<CatalogEntry> entries, CancellationToken token)
        {
            if (entries.Count == 0) return;
            Dictionary<string, CatalogEntry> byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
            string ids = D.InList(byId.Keys);
            DataTable rows = await QueryAsync("SELECT * FROM catalogentryvalues WHERE " + Scope() + " AND entryid IN " + ids + " ORDER BY entryid, field, position;", token).ConfigureAwait(false);
            foreach (DataRow row in rows.Rows)
                if (CatalogEntryFields.Lists(byId[RowReader.GetString(row, "entryid")]).TryGetValue(RowReader.GetString(row, "field"), out List<string>? list)) list.Add(RowReader.GetString(row, "value"));
            rows = await QueryAsync("SELECT * FROM catalogsources WHERE " + Scope() + " AND entryid IN " + ids + " ORDER BY entryid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in rows.Rows) byId[RowReader.GetString(row, "entryid")].Sources.Add(new CatalogSource { Url = RowReader.GetNullableString(row, "url"), Title = RowReader.GetString(row, "title"), License = RowReader.GetString(row, "license"), Attribution = RowReader.GetNullableString(row, "attribution"), Method = RowReader.GetString(row, "method"), Model = RowReader.GetNullableString(row, "model"), RetrievedUtc = RowReader.GetNullableDateTime(row, "retrievedutc") });
            rows=await QueryAsync("SELECT * FROM cataloglocalizednames WHERE "+Scope()+" AND entryid IN "+ids+" ORDER BY entryid,position;",token).ConfigureAwait(false);
            foreach(DataRow row in rows.Rows)byId[RowReader.GetString(row,"entryid")].ModalityDetails!.LocalizedNames.Add(new CatalogLocalizedName{LanguageTag=RowReader.GetString(row,"languagetag"),Text=RowReader.GetString(row,"text")});
            Dictionary<string, ObjectMetadata> metadata = await Driver.Metadata.ReadManyAsync(TargetTypeEnum.CatalogEntry, byId.Keys, token).ConfigureAwait(false);
            foreach (CatalogEntry entry in entries) { entry.Labels = metadata[entry.Id].Labels; entry.Tags = metadata[entry.Id].Tags; }
        }

        private List<string> MetadataStatements(TargetTypeEnum type, string id, List<string> labels, Dictionary<string, string> tags, DateTime now)
        {
            string where = " WHERE " + Scope() + " AND targettype = " + D.Enum(type) + " AND targetid = " + D.Str(id) + ";";
            List<string> sql = new List<string> { "DELETE FROM labels" + where, "DELETE FROM tags" + where };
            foreach (string label in labels) sql.Add("INSERT INTO labels (propertyid,id,targettype,targetid,label,labelnormalized,createdutc) VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate("lbl_")) + ", " + D.Enum(type) + ", " + D.Str(id) + ", " + D.Str(label) + ", " + D.Str(MetadataRules.Normalize(label)) + ", " + D.Ts(now) + ");");
            foreach (KeyValuePair<string, string> tag in tags) sql.Add("INSERT INTO tags (propertyid,id,targettype,targetid,tagkey,tagkeynormalized,tagvalue,createdutc,updatedutc) VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate("tag_")) + ", " + D.Enum(type) + ", " + D.Str(id) + ", " + D.Str(tag.Key) + ", " + D.Str(MetadataRules.Normalize(tag.Key)) + ", " + D.Str(tag.Value) + ", " + D.Ts(now) + ", " + D.Ts(now) + ");");
            return sql;
        }

        private Dictionary<string,string> Values(CatalogEntry e)
        {
            return new Dictionary<string,string>
            {
                { "kind", D.Str(e.Kind) },
                { "apparatuskey", D.Str(e.ApparatusSetup?.ApparatusKey) },{ "apparatusmanufacturer", D.Str(e.ApparatusSetup?.Manufacturer) },{ "apparatusmodel", D.Str(e.ApparatusSetup?.Model) },{ "apparatussetuptext", D.Str(e.ApparatusSetup?.SetupText) },{ "apparatusspringsetuptext", D.Str(e.ApparatusSetup?.SpringSetupText) },
                { "familykey", D.Str(e.ModalityDetails?.FamilyKey) },
                { "attirekey", D.Str(e.ModalityDetails?.AttireKey) },{ "levelkey", D.Str(e.ModalityDetails?.LevelKey) },{ "notationsystemkey", D.Str(e.ModalityDetails?.NotationSystemKey) },{ "notationtext", D.Str(e.ModalityDetails?.NotationText) },
                { "name", D.Str(e.Name) },
                { "slug", D.Str(e.Slug) },
                { "summary", D.Str(e.Summary) },
                { "program", D.Str(e.Program) },
                { "difficulty", D.Str(e.Difficulty) },
                { "movementpattern", D.Str(e.MovementPattern) },
                { "plane", D.Str(e.Plane) },
                { "laterality", D.Str(e.Laterality) },
                { "techniquetype", D.Str(e.TechniqueType) },
                { "fromposition", D.Str(e.FromPosition) },
                { "toposition", D.Str(e.ToPosition) },
                { "status", D.Str(e.Status) },
                { "moderationstate", D.Enum(e.ModerationState) },
                { "createdbyuserid", D.Str(e.CreatedByUserId) },
                { "reviewedbyuserid", D.Str(e.ReviewedByUserId) },
                { "reviewnote", D.Str(e.ReviewNote) },
                { "reviewedutc", D.Ts(e.ReviewedUtc) },
                { "publishedutc", D.Ts(e.PublishedUtc) },
                { "deletedutc", D.Ts(e.DeletedUtc) },
                { "createdutc", D.Ts(e.CreatedUtc) },
                { "updatedutc", D.Ts(e.UpdatedUtc) },
                { "revision", D.Num(e.Revision) }
            };
        }

        private static CatalogEntry Map(DataRow row)
        {
            return new CatalogEntry
            {
                Id = RowReader.GetString(row,"id"),
                ApparatusSetup=new CatalogApparatusSetup{ApparatusKey=RowReader.GetNullableString(row,"apparatuskey"),Manufacturer=RowReader.GetNullableString(row,"apparatusmanufacturer"),Model=RowReader.GetNullableString(row,"apparatusmodel"),SetupText=RowReader.GetNullableString(row,"apparatussetuptext"),SpringSetupText=RowReader.GetNullableString(row,"apparatusspringsetuptext")},
                ModalityDetails=new CatalogModalityDetails{FamilyKey=RowReader.GetNullableString(row,"familykey"),AttireKey=RowReader.GetNullableString(row,"attirekey"),LevelKey=RowReader.GetNullableString(row,"levelkey"),NotationSystemKey=RowReader.GetNullableString(row,"notationsystemkey"),NotationText=RowReader.GetNullableString(row,"notationtext")},
                Revision = RowReader.GetLong(row,"revision"),
                Kind = RowReader.GetString(row,"kind"),
                Name = RowReader.GetString(row,"name"),
                Slug = RowReader.GetString(row,"slug"),
                Summary = RowReader.GetNullableString(row,"summary"),
                Program = RowReader.GetNullableString(row,"program"),
                Difficulty = RowReader.GetString(row,"difficulty"),
                MovementPattern = RowReader.GetNullableString(row,"movementpattern"),
                Plane = RowReader.GetNullableString(row,"plane"),
                Laterality = RowReader.GetNullableString(row,"laterality"),
                TechniqueType = RowReader.GetNullableString(row,"techniquetype"),
                FromPosition = RowReader.GetNullableString(row,"fromposition"),
                ToPosition = RowReader.GetNullableString(row,"toposition"),
                Status = RowReader.GetString(row,"status"),
                ModerationState = Enum.Parse<ModerationStateEnum>(RowReader.GetString(row,"moderationstate")),
                CreatedByUserId = RowReader.GetNullableString(row,"createdbyuserid"),
                ReviewedByUserId = RowReader.GetNullableString(row,"reviewedbyuserid"),
                ReviewNote = RowReader.GetNullableString(row,"reviewnote"),
                ReviewedUtc = RowReader.GetNullableDateTime(row,"reviewedutc"),
                PublishedUtc = RowReader.GetNullableDateTime(row,"publishedutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row,"deletedutc"),
                CreatedUtc = RowReader.GetDateTime(row,"createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row,"updatedutc"),
            };
        }
    }
}
