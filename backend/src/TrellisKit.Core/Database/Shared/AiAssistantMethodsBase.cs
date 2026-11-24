namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared assistant methods.
    /// </summary>
    internal abstract class AiAssistantMethodsBase : MethodsBase, IAiAssistantMethods
    {
        private const string _Columns =
            "id, name, namenormalized, description, enabled, title, avataremoji, avatarmediaid, systemprompt, temperature, topp, audience, knowledgeenabled, knowledgemode, " +
            "maxcontextitems, maxcontextcharacters, citations, messagespermemberperday, messagespervisitorperday, maxmessagesperconversation, maxtokensperreply, maxmessagecharacters, " +
            "conversationretentiondays, streaming, showreasoning, disclaimer, embedenabled, embedlauncherlabel, embedposition, moderationmode, monthlybudgetusd, budgethardstop, " +
            "toolsenabled, maxtooliterations, maxtoolcallsperturn, toolcalltimeoutms, maxtooloutputcharacters, maxtokensperturn, showtoolactivity, deletedutc, createdbyuserid, createdutc, updatedutc";

        private const string _ItemColumns = "assistantid, targettype, targetid, sourceid, title, contenthash, audience, chunkcount, status, error, indexedutc, updatedutc";

        protected AiAssistantMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Assistant> CreateAsync(Assistant assistant, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            assistant.PropertyId = PropertyId;
            List<string> statements = new List<string> { Insert(assistant) };
            statements.AddRange(ChildStatements(assistant));
            await UniqueTransactionAsync(statements, "An assistant with that name", token).ConfigureAwait(false);
            return assistant;
        }

        public async Task<Assistant?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM aiassistants WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            List<Assistant> list = new List<Assistant> { Map(table.Rows[0]) };
            await AttachChildrenAsync(list, true, token).ConfigureAwait(false);
            return list[0];
        }

        public async Task<Assistant?> ReadAnyAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM aiassistants WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            List<Assistant> list = new List<Assistant> { Map(table.Rows[0]) };
            await AttachChildrenAsync(list, true, token).ConfigureAwait(false);
            return list[0];
        }

        public async Task<List<Assistant>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct().Take(100).ToList();
            if (list.Count == 0) return new List<Assistant>();
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM aiassistants WHERE " + Scope() + " AND id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            List<Assistant> assistants = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await AttachChildrenAsync(assistants, false, token).ConfigureAwait(false);
            return assistants;
        }

        public async Task<Assistant> UpdateAsync(Assistant assistant, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            Assistant a = assistant;
            string sql = "UPDATE aiassistants SET name = " + D.Str(a.Name) + ", namenormalized = " + D.Str(AiSql.Normalize(a.Name)) + ", description = " + D.Str(a.Description) +
                ", enabled = " + D.Bool(a.Enabled) + ", title = " + D.Str(a.Title) + ", avataremoji = " + D.Str(a.AvatarEmoji) + ", avatarmediaid = " + D.Str(a.AvatarMediaId) +
                ", systemprompt = " + D.Str(a.SystemPrompt) + ", temperature = " + AiSql.Dbl(D, a.Temperature) + ", topp = " + AiSql.Dbl(D, a.TopP) + ", audience = " + D.Enum(a.Audience) +
                ", knowledgeenabled = " + D.Bool(a.KnowledgeEnabled) + ", knowledgemode = " + D.Enum(a.KnowledgeMode) + ", maxcontextitems = " + D.Num(a.MaxContextItems) +
                ", maxcontextcharacters = " + D.Num(a.MaxContextCharacters) + ", citations = " + D.Bool(a.Citations) + ", messagespermemberperday = " + D.Num(a.MessagesPerMemberPerDay) +
                ", messagespervisitorperday = " + D.Num(a.MessagesPerVisitorPerDay) + ", maxmessagesperconversation = " + D.Num(a.MaxMessagesPerConversation) +
                ", maxtokensperreply = " + D.Num(a.MaxTokensPerReply) + ", maxmessagecharacters = " + D.Num(a.MaxMessageCharacters) + ", conversationretentiondays = " + D.Num(a.ConversationRetentionDays) +
                ", streaming = " + D.Bool(a.Streaming) + ", showreasoning = " + D.Bool(a.ShowReasoning) + ", disclaimer = " + D.Str(a.Disclaimer) + ", embedenabled = " + D.Bool(a.EmbedEnabled) +
                ", embedlauncherlabel = " + D.Str(a.EmbedLauncherLabel) + ", embedposition = " + D.Str(a.EmbedPosition) + ", moderationmode = " + D.Enum(a.ModerationMode) +
                ", monthlybudgetusd = " + AiSql.Dbl(D, a.MonthlyBudgetUsd) + ", budgethardstop = " + D.Bool(a.BudgetHardStop) + ", toolsenabled = " + D.Bool(a.ToolsEnabled) + ", maxtooliterations = " + D.Num(a.MaxToolIterations) +
                ", maxtoolcallsperturn = " + D.Num(a.MaxToolCallsPerTurn) + ", toolcalltimeoutms = " + D.Num(a.ToolCallTimeoutMs) + ", maxtooloutputcharacters = " + D.Num(a.MaxToolOutputCharacters) +
                ", maxtokensperturn = " + D.Num(a.MaxTokensPerTurn) + ", showtoolactivity = " + D.Bool(a.ShowToolActivity) + ", updatedutc = " + D.Ts(a.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(a.Id) + " AND deletedutc IS NULL;";
            List<string> statements = new List<string> { sql };
            statements.AddRange(DeleteChildStatements(a.Id));
            statements.AddRange(ChildStatements(a));
            await UniqueTransactionAsync(statements, "An assistant with that name", token).ConfigureAwait(false);
            return assistant;
        }

        public async Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = new List<string>(DeleteChildStatements(id))
            {
                "DELETE FROM aiknowledgeitems WHERE " + Scope() + " AND assistantid = " + D.Str(id) + ";",
                "DELETE FROM aiknowledgeindexes WHERE " + Scope() + " AND assistantid = " + D.Str(id) + ";",
                "UPDATE aiassistants SET deletedutc = " + D.Ts(nowUtc) + ", enabled = " + D.Bool(false) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND deletedutc IS NULL RETURNING id;"
            };
            DataTable result = await TransactionAsync(statements, token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public async Task<EnumerationResult<Assistant>> EnumerateAsync(EnumerationRequest request, AiEnumerationFilter filter, IReadOnlyCollection<string>? audiences, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string> { "a.deletedutc IS NULL" };
            if (audiences != null)
            {
                conditions.Add("a.enabled = " + D.Bool(true));
                conditions.Add("a.audience IN " + D.InList(audiences));
            }
            if (filter.Enabled != null) conditions.Add("a.enabled = " + D.Bool(filter.Enabled.Value));
            if (filter.Audience != null) conditions.Add("a.audience = " + D.Enum(filter.Audience.Value));
            if (filter.EmbedEnabled != null) conditions.Add("a.embedenabled = " + D.Bool(filter.EmbedEnabled.Value));
            if (!String.IsNullOrEmpty(filter.EndpointId))
                conditions.Add("EXISTS (SELECT 1 FROM aiassistantmodels fm WHERE fm.propertyid = a.propertyid AND fm.assistantid = a.id AND fm.endpointid = " + D.Str(filter.EndpointId) + ")");
            string select = String.Join(", ", _Columns.Split(',').Select(c => "a." + c.Trim()));
            EnumerationResult<Assistant> page = await EnumerateAsync(request, select, "aiassistants a", conditions, Map, token).ConfigureAwait(false);
            await AttachChildrenAsync(page.Objects, true, token).ConfigureAwait(false);
            return page;
        }

        public async Task<List<Assistant>> ListForIndexingAsync(int limit, string? afterId, CancellationToken token = default)
        {
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM aiassistants WHERE " + Scope() + " AND deletedutc IS NULL AND knowledgeenabled = " + D.Bool(true) + after +
                " AND EXISTS (SELECT 1 FROM aiassistantmodels im WHERE im.propertyid = " + Pid + " AND im.assistantid = aiassistants.id AND im.purpose = " + D.Enum(AssistantPurposeEnum.Embedding) + ")" +
                " ORDER BY id LIMIT " + Math.Clamp(limit, 1, 200) + ";", token).ConfigureAwait(false);
            List<Assistant> list = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await AttachChildrenAsync(list, true, token).ConfigureAwait(false);
            return list;
        }

        public async Task<List<Assistant>> ListPageAsync(int limit, string? afterId, CancellationToken token = default)
        {
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM aiassistants WHERE " + Scope() + after + " ORDER BY id LIMIT " + Math.Clamp(limit, 1, 500) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<AiKnowledgeIndex?> ReadIndexAsync(string assistantId, CancellationToken token = default)
        {
            RequireText(assistantId, nameof(assistantId));
            DataTable table = await QueryAsync("SELECT assistantid, endpointid, collectionid, dimensions, status, itemcount, chunkcount, lastrunutc, lastsuccessutc, lasterror, reindexrequested, createdutc, updatedutc " +
                "FROM aiknowledgeindexes WHERE " + Scope() + " AND assistantid = " + D.Str(assistantId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new AiKnowledgeIndex
            {
                AssistantId = RowReader.GetString(row, "assistantid"),
                EndpointId = RowReader.GetNullableString(row, "endpointid"),
                CollectionId = RowReader.GetNullableString(row, "collectionid"),
                Dimensions = RowReader.GetNullableInt(row, "dimensions"),
                Status = RowReader.GetEnum(row, "status", AiKnowledgeIndexStatusEnum.NotConfigured),
                ItemCount = RowReader.GetInt(row, "itemcount"),
                ChunkCount = RowReader.GetInt(row, "chunkcount"),
                LastRunUtc = RowReader.GetNullableDateTime(row, "lastrunutc"),
                LastSuccessUtc = RowReader.GetNullableDateTime(row, "lastsuccessutc"),
                LastError = RowReader.GetNullableString(row, "lasterror"),
                ReindexRequested = RowReader.GetBool(row, "reindexrequested"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        public Task UpsertIndexAsync(AiKnowledgeIndex index, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(index);
            AiKnowledgeIndex i = index;
            return QueryAsync("INSERT INTO aiknowledgeindexes (propertyid, assistantid, endpointid, collectionid, dimensions, status, itemcount, chunkcount, lastrunutc, lastsuccessutc, lasterror, reindexrequested, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(i.AssistantId) + ", " + D.Str(i.EndpointId) + ", " + D.Str(i.CollectionId) + ", " + D.Num(i.Dimensions) + ", " + D.Enum(i.Status) + ", " + D.Num(i.ItemCount) + ", " +
                D.Num(i.ChunkCount) + ", " + D.Ts(i.LastRunUtc) + ", " + D.Ts(i.LastSuccessUtc) + ", " + D.Str(i.LastError) + ", " + D.Bool(i.ReindexRequested) + ", " + D.Ts(i.CreatedUtc) + ", " + D.Ts(i.UpdatedUtc) + ") " +
                "ON CONFLICT (propertyid, assistantid) DO UPDATE SET endpointid = EXCLUDED.endpointid, collectionid = EXCLUDED.collectionid, dimensions = EXCLUDED.dimensions, status = EXCLUDED.status, " +
                "itemcount = EXCLUDED.itemcount, chunkcount = EXCLUDED.chunkcount, lastrunutc = EXCLUDED.lastrunutc, lastsuccessutc = EXCLUDED.lastsuccessutc, lasterror = EXCLUDED.lasterror, " +
                "reindexrequested = EXCLUDED.reindexrequested, updatedutc = EXCLUDED.updatedutc;", token);
        }

        public async Task<List<AiKnowledgeItem>> ListItemsAsync(string assistantId, int limit, CancellationToken token = default)
        {
            RequireText(assistantId, nameof(assistantId));
            DataTable table = await QueryAsync("SELECT " + _ItemColumns + " FROM aiknowledgeitems WHERE " + Scope() + " AND assistantid = " + D.Str(assistantId) + " ORDER BY targettype, targetid LIMIT " +
                Math.Clamp(limit, 1, 100000) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapItem).ToList();
        }

        public Task UpsertItemAsync(AiKnowledgeItem item, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            AiKnowledgeItem k = item;
            return QueryAsync("INSERT INTO aiknowledgeitems (propertyid, " + _ItemColumns + ") VALUES (" + Pid + ", " + D.Str(k.AssistantId) + ", " + D.Str(k.TargetType) + ", " + D.Str(k.TargetId) + ", " +
                D.Str(k.SourceId) + ", " + D.Str(k.Title) + ", " + D.Str(k.ContentHash) + ", " + D.Str(k.Audience) + ", " + D.Num(k.ChunkCount) + ", " + D.Enum(k.Status) + ", " + D.Str(k.Error) + ", " +
                D.Ts(k.IndexedUtc) + ", " + D.Ts(k.UpdatedUtc) + ") ON CONFLICT (propertyid, assistantid, targettype, targetid) DO UPDATE SET sourceid = EXCLUDED.sourceid, title = EXCLUDED.title, " +
                "contenthash = EXCLUDED.contenthash, audience = EXCLUDED.audience, chunkcount = EXCLUDED.chunkcount, status = EXCLUDED.status, error = EXCLUDED.error, indexedutc = EXCLUDED.indexedutc, " +
                "updatedutc = EXCLUDED.updatedutc;", token);
        }

        public Task DeleteKnowledgeAsync(string assistantId, CancellationToken token = default)
        {
            RequireText(assistantId, nameof(assistantId));
            return TransactionAsync(new List<string>
            {
                "DELETE FROM aiknowledgeitems WHERE " + Scope() + " AND assistantid = " + D.Str(assistantId) + ";",
                "DELETE FROM aiknowledgeindexes WHERE " + Scope() + " AND assistantid = " + D.Str(assistantId) + ";"
            }, token);
        }

        public Task<EnumerationResult<AiKnowledgeItem>> EnumerateItemsAsync(EnumerationRequest request, string assistantId, AiEnumerationFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            RequireText(assistantId, nameof(assistantId));
            List<string> conditions = new List<string> { "k.assistantid = " + D.Str(assistantId) };
            if (filter.ItemStatus != null) conditions.Add("k.status = " + D.Enum(filter.ItemStatus.Value));
            if (!String.IsNullOrEmpty(filter.TargetType)) conditions.Add("k.targettype = " + D.Str(filter.TargetType));
            string select = String.Join(", ", _ItemColumns.Split(',').Select(c => "k." + c.Trim()));
            return EnumerateAsync(request, select, "aiknowledgeitems k", conditions, MapItem, token);
        }

        public async Task<List<AssistantToolSwitch>> ListToolSwitchesAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT toolname, disabled, reason, updatedbyuserid, updatedutc FROM aitoolswitches WHERE " + Scope() + " ORDER BY toolname LIMIT 500;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new AssistantToolSwitch
            {
                ToolName = RowReader.GetString(row, "toolname"),
                Disabled = RowReader.GetBool(row, "disabled"),
                Reason = RowReader.GetNullableString(row, "reason"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            }).ToList();
        }

        public Task UpsertToolSwitchAsync(AssistantToolSwitch toolSwitch, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(toolSwitch);
            RequireText(toolSwitch.ToolName, nameof(toolSwitch));
            AssistantToolSwitch t = toolSwitch;
            return QueryAsync("INSERT INTO aitoolswitches (propertyid, toolname, disabled, reason, updatedbyuserid, updatedutc) VALUES (" + Pid + ", " + D.Str(t.ToolName) + ", " + D.Bool(t.Disabled) + ", " +
                D.Str(t.Reason) + ", " + D.Str(t.UpdatedByUserId) + ", " + D.Ts(t.UpdatedUtc) + ") ON CONFLICT (propertyid, toolname) DO UPDATE SET disabled = EXCLUDED.disabled, reason = EXCLUDED.reason, " +
                "updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;", token);
        }

        private async Task AttachChildrenAsync(List<Assistant> assistants, bool allLists, CancellationToken token)
        {
            if (assistants.Count == 0) return;
            string ids = D.InList(assistants.Select(a => a.Id));
            Dictionary<string, Assistant> byId = assistants.ToDictionary(a => a.Id, StringComparer.Ordinal);
            DataTable models = await QueryAsync("SELECT assistantid, purpose, endpointid FROM aiassistantmodels WHERE " + Scope() + " AND assistantid IN " + ids + ";", token).ConfigureAwait(false);
            foreach (DataRow row in models.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "assistantid"), out Assistant? a)) continue;
                if (Enum.TryParse(RowReader.GetString(row, "purpose"), out AssistantPurposeEnum purpose)) a.Models[purpose] = RowReader.GetString(row, "endpointid");
            }
            if (!allLists) return;
            DataTable prompts = await QueryAsync("SELECT assistantid, prompt FROM aiassistantstarterprompts WHERE " + Scope() + " AND assistantid IN " + ids + " ORDER BY assistantid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in prompts.Rows)
                if (byId.TryGetValue(RowReader.GetString(row, "assistantid"), out Assistant? a)) a.StarterPrompts.Add(RowReader.GetString(row, "prompt"));
            DataTable origins = await QueryAsync("SELECT assistantid, origin FROM aiassistantembedorigins WHERE " + Scope() + " AND assistantid IN " + ids + " ORDER BY assistantid, origin;", token).ConfigureAwait(false);
            foreach (DataRow row in origins.Rows)
                if (byId.TryGetValue(RowReader.GetString(row, "assistantid"), out Assistant? a)) a.EmbedOrigins.Add(RowReader.GetString(row, "origin"));
            DataTable tools = await QueryAsync("SELECT assistantid, toolname FROM aiassistanttools WHERE " + Scope() + " AND assistantid IN " + ids + " ORDER BY assistantid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in tools.Rows)
                if (byId.TryGetValue(RowReader.GetString(row, "assistantid"), out Assistant? a)) a.AllowedTools.Add(RowReader.GetString(row, "toolname"));
            DataTable sources = await QueryAsync("SELECT id, assistantid, position, targettype, labelmatch, maxitems FROM aiknowledgesources WHERE " + Scope() + " AND assistantid IN " + ids +
                " ORDER BY assistantid, position;", token).ConfigureAwait(false);
            Dictionary<string, AssistantKnowledgeSource> sourceById = new Dictionary<string, AssistantKnowledgeSource>(StringComparer.Ordinal);
            foreach (DataRow row in sources.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "assistantid"), out Assistant? a)) continue;
                AssistantKnowledgeSource source = new AssistantKnowledgeSource
                {
                    Id = RowReader.GetString(row, "id"),
                    AssistantId = a.Id,
                    Position = RowReader.GetInt(row, "position"),
                    TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.FeedPost),
                    LabelMatch = RowReader.GetEnum(row, "labelmatch", LabelMatchEnum.All),
                    MaxItems = RowReader.GetInt(row, "maxitems")
                };
                a.Sources.Add(source);
                sourceById[source.Id] = source;
            }
            if (sourceById.Count == 0) return;
            string sourceIds = D.InList(sourceById.Keys);
            DataTable labels = await QueryAsync("SELECT sourceid, label FROM aiknowledgesourcelabels WHERE " + Scope() + " AND sourceid IN " + sourceIds + " ORDER BY sourceid, labelnormalized;", token).ConfigureAwait(false);
            foreach (DataRow row in labels.Rows)
                if (sourceById.TryGetValue(RowReader.GetString(row, "sourceid"), out AssistantKnowledgeSource? s)) s.Labels.Add(RowReader.GetString(row, "label"));
            DataTable tags = await QueryAsync("SELECT sourceid, tagkey, tagvalue FROM aiknowledgesourcetags WHERE " + Scope() + " AND sourceid IN " + sourceIds + " ORDER BY sourceid, tagkeynormalized;", token).ConfigureAwait(false);
            foreach (DataRow row in tags.Rows)
                if (sourceById.TryGetValue(RowReader.GetString(row, "sourceid"), out AssistantKnowledgeSource? s)) s.Tags[RowReader.GetString(row, "tagkey")] = RowReader.GetString(row, "tagvalue");
        }

        private List<string> DeleteChildStatements(string assistantId)
        {
            string id = D.Str(assistantId);
            return new List<string>
            {
                "DELETE FROM aiassistantmodels WHERE " + Scope() + " AND assistantid = " + id + ";",
                "DELETE FROM aiassistantstarterprompts WHERE " + Scope() + " AND assistantid = " + id + ";",
                "DELETE FROM aiassistantembedorigins WHERE " + Scope() + " AND assistantid = " + id + ";",
                "DELETE FROM aiassistanttools WHERE " + Scope() + " AND assistantid = " + id + ";",
                "DELETE FROM aiknowledgesourcelabels WHERE " + Scope() + " AND sourceid IN (SELECT s.id FROM aiknowledgesources s WHERE s.propertyid = " + Pid + " AND s.assistantid = " + id + ");",
                "DELETE FROM aiknowledgesourcetags WHERE " + Scope() + " AND sourceid IN (SELECT s.id FROM aiknowledgesources s WHERE s.propertyid = " + Pid + " AND s.assistantid = " + id + ");",
                "DELETE FROM aiknowledgesources WHERE " + Scope() + " AND assistantid = " + id + ";"
            };
        }

        private List<string> ChildStatements(Assistant a)
        {
            List<string> statements = new List<string>();
            string id = D.Str(a.Id);
            foreach (KeyValuePair<AssistantPurposeEnum, string> model in a.Models.Where(m => !String.IsNullOrEmpty(m.Value)))
                statements.Add("INSERT INTO aiassistantmodels (propertyid, assistantid, purpose, endpointid) VALUES (" + Pid + ", " + id + ", " + D.Enum(model.Key) + ", " + D.Str(model.Value) + ");");
            int position = 0;
            foreach (string prompt in a.StarterPrompts)
                statements.Add("INSERT INTO aiassistantstarterprompts (propertyid, assistantid, position, prompt) VALUES (" + Pid + ", " + id + ", " + D.Num(position++) + ", " + D.Str(prompt) + ");");
            foreach (string origin in a.EmbedOrigins.Distinct(StringComparer.OrdinalIgnoreCase))
                statements.Add("INSERT INTO aiassistantembedorigins (propertyid, assistantid, origin) VALUES (" + Pid + ", " + id + ", " + D.Str(origin) + ");");
            position = 0;
            foreach (string tool in a.AllowedTools.Distinct(StringComparer.OrdinalIgnoreCase))
                statements.Add("INSERT INTO aiassistanttools (propertyid, assistantid, toolname, position) VALUES (" + Pid + ", " + id + ", " + D.Str(tool) + ", " + D.Num(position++) + ");");
            position = 0;
            foreach (AssistantKnowledgeSource source in a.Sources)
            {
                source.AssistantId = a.Id;
                source.Position = position;
                statements.Add("INSERT INTO aiknowledgesources (propertyid, id, assistantid, position, targettype, labelmatch, maxitems) VALUES (" + Pid + ", " + D.Str(source.Id) + ", " + id + ", " +
                    D.Num(position++) + ", " + D.Enum(source.TargetType) + ", " + D.Enum(source.LabelMatch) + ", " + D.Num(source.MaxItems) + ");");
                foreach (string label in source.Labels.Distinct(StringComparer.OrdinalIgnoreCase))
                    statements.Add("INSERT INTO aiknowledgesourcelabels (propertyid, sourceid, labelnormalized, label) VALUES (" + Pid + ", " + D.Str(source.Id) + ", " + D.Str(AiSql.Normalize(label)) + ", " + D.Str(label) + ");");
                foreach (KeyValuePair<string, string> tag in source.Tags)
                    statements.Add("INSERT INTO aiknowledgesourcetags (propertyid, sourceid, tagkeynormalized, tagkey, tagvalue) VALUES (" + Pid + ", " + D.Str(source.Id) + ", " + D.Str(AiSql.Normalize(tag.Key)) + ", " +
                        D.Str(tag.Key) + ", " + D.Str(tag.Value ?? String.Empty) + ") ON CONFLICT DO NOTHING;");
            }
            return statements;
        }

        private string Insert(Assistant a)
        {
            return "INSERT INTO aiassistants (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(a.Id) + ", " + D.Str(a.Name) + ", " + D.Str(AiSql.Normalize(a.Name)) + ", " + D.Str(a.Description) + ", " +
                D.Bool(a.Enabled) + ", " + D.Str(a.Title) + ", " + D.Str(a.AvatarEmoji) + ", " + D.Str(a.AvatarMediaId) + ", " + D.Str(a.SystemPrompt) + ", " + AiSql.Dbl(D, a.Temperature) + ", " +
                AiSql.Dbl(D, a.TopP) + ", " + D.Enum(a.Audience) + ", " + D.Bool(a.KnowledgeEnabled) + ", " + D.Enum(a.KnowledgeMode) + ", " + D.Num(a.MaxContextItems) + ", " + D.Num(a.MaxContextCharacters) + ", " +
                D.Bool(a.Citations) + ", " + D.Num(a.MessagesPerMemberPerDay) + ", " + D.Num(a.MessagesPerVisitorPerDay) + ", " + D.Num(a.MaxMessagesPerConversation) + ", " + D.Num(a.MaxTokensPerReply) + ", " +
                D.Num(a.MaxMessageCharacters) + ", " + D.Num(a.ConversationRetentionDays) + ", " + D.Bool(a.Streaming) + ", " + D.Bool(a.ShowReasoning) + ", " + D.Str(a.Disclaimer) + ", " +
                D.Bool(a.EmbedEnabled) + ", " + D.Str(a.EmbedLauncherLabel) + ", " + D.Str(a.EmbedPosition) + ", " + D.Enum(a.ModerationMode) + ", " + AiSql.Dbl(D, a.MonthlyBudgetUsd) + ", " +
                D.Bool(a.BudgetHardStop) + ", " + D.Bool(a.ToolsEnabled) + ", " + D.Num(a.MaxToolIterations) + ", " + D.Num(a.MaxToolCallsPerTurn) + ", " + D.Num(a.ToolCallTimeoutMs) + ", " +
                D.Num(a.MaxToolOutputCharacters) + ", " + D.Num(a.MaxTokensPerTurn) + ", " + D.Bool(a.ShowToolActivity) + ", " + D.Ts(a.DeletedUtc) + ", " + D.Str(a.CreatedByUserId) + ", " + D.Ts(a.CreatedUtc) + ", " + D.Ts(a.UpdatedUtc) + ");";
        }

        private Assistant Map(DataRow row)
        {
            return new Assistant
            {
                PropertyId = PropertyId,
                Id = RowReader.GetString(row, "id"),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetNullableString(row, "description"),
                Enabled = RowReader.GetBool(row, "enabled"),
                Title = RowReader.GetString(row, "title"),
                AvatarEmoji = RowReader.GetNullableString(row, "avataremoji"),
                AvatarMediaId = RowReader.GetNullableString(row, "avatarmediaid"),
                SystemPrompt = RowReader.GetString(row, "systemprompt"),
                Temperature = AiSql.NullableDouble(row, "temperature"),
                TopP = AiSql.NullableDouble(row, "topp"),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                KnowledgeEnabled = RowReader.GetBool(row, "knowledgeenabled"),
                KnowledgeMode = RowReader.GetEnum(row, "knowledgemode", AssistantKnowledgeModeEnum.Keyword),
                MaxContextItems = RowReader.GetInt(row, "maxcontextitems"),
                MaxContextCharacters = RowReader.GetInt(row, "maxcontextcharacters"),
                Citations = RowReader.GetBool(row, "citations"),
                MessagesPerMemberPerDay = RowReader.GetInt(row, "messagespermemberperday"),
                MessagesPerVisitorPerDay = RowReader.GetInt(row, "messagespervisitorperday"),
                MaxMessagesPerConversation = RowReader.GetInt(row, "maxmessagesperconversation"),
                MaxTokensPerReply = RowReader.GetInt(row, "maxtokensperreply"),
                MaxMessageCharacters = RowReader.GetInt(row, "maxmessagecharacters"),
                ConversationRetentionDays = RowReader.GetInt(row, "conversationretentiondays"),
                Streaming = RowReader.GetBool(row, "streaming"),
                ShowReasoning = RowReader.GetBool(row, "showreasoning"),
                Disclaimer = RowReader.GetNullableString(row, "disclaimer"),
                EmbedEnabled = RowReader.GetBool(row, "embedenabled"),
                EmbedLauncherLabel = RowReader.GetNullableString(row, "embedlauncherlabel"),
                EmbedPosition = RowReader.GetString(row, "embedposition"),
                ModerationMode = RowReader.GetEnum(row, "moderationmode", AssistantModerationModeEnum.Block),
                MonthlyBudgetUsd = AiSql.NullableDouble(row, "monthlybudgetusd"),
                BudgetHardStop = RowReader.GetBool(row, "budgethardstop"),
                ToolsEnabled = RowReader.GetBool(row, "toolsenabled"),
                MaxToolIterations = RowReader.GetInt(row, "maxtooliterations"),
                MaxToolCallsPerTurn = RowReader.GetInt(row, "maxtoolcallsperturn"),
                ToolCallTimeoutMs = RowReader.GetInt(row, "toolcalltimeoutms"),
                MaxToolOutputCharacters = RowReader.GetInt(row, "maxtooloutputcharacters"),
                MaxTokensPerTurn = RowReader.GetInt(row, "maxtokensperturn"),
                ShowToolActivity = RowReader.GetBool(row, "showtoolactivity"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static AiKnowledgeItem MapItem(DataRow row)
        {
            return new AiKnowledgeItem
            {
                AssistantId = RowReader.GetString(row, "assistantid"),
                TargetType = RowReader.GetString(row, "targettype"),
                TargetId = RowReader.GetString(row, "targetid"),
                SourceId = RowReader.GetNullableString(row, "sourceid"),
                Title = RowReader.GetNullableString(row, "title"),
                ContentHash = RowReader.GetNullableString(row, "contenthash"),
                Audience = RowReader.GetNullableString(row, "audience"),
                ChunkCount = RowReader.GetInt(row, "chunkcount"),
                Status = RowReader.GetEnum(row, "status", AiKnowledgeItemStatusEnum.Pending),
                Error = RowReader.GetNullableString(row, "error"),
                IndexedUtc = RowReader.GetNullableDateTime(row, "indexedutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
