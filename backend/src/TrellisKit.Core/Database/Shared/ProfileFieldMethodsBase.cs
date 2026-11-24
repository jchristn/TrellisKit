namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared profile field definition methods (tables profilefields, profilefieldoptions, profilefieldsubfields).
    /// </summary>
    internal abstract class ProfileFieldMethodsBase : MethodsBase, IProfileFieldMethods
    {
        private const string _Duplicate = "A profile field with this key";

        protected ProfileFieldMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<ProfileField>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM profilefields WHERE " + Scope() + " ORDER BY profileorder, createdutc, id LIMIT 200;", token).ConfigureAwait(false);
            List<ProfileField> fields = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await AttachChildrenAsync(fields, token).ConfigureAwait(false);
            return fields;
        }

        public async Task<ProfileField?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM profilefields WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            List<ProfileField> fields = new List<ProfileField> { Map(table.Rows[0]) };
            await AttachChildrenAsync(fields, token).ConfigureAwait(false);
            return fields[0];
        }

        public async Task<EnumerationResult<ProfileField>> EnumerateAsync(EnumerationRequest request, ProfileFieldFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Type != null) conditions.Add("f.type = " + D.Enum(filter.Type.Value));
            if (filter.CollectAt != null) conditions.Add("f.collectat = " + D.Enum(filter.CollectAt.Value));
            if (filter.Visibility != null) conditions.Add("f.visibility = " + D.Enum(filter.Visibility.Value));
            if (filter.Enabled != null) conditions.Add("f.enabled = " + D.Bool(filter.Enabled.Value));
            if (filter.BuiltIn != null) conditions.Add("f.builtin = " + D.Bool(filter.BuiltIn.Value));
            if (filter.Filterable != null) conditions.Add("f.filterable = " + D.Bool(filter.Filterable.Value));
            if (filter.ShowOnCard != null) conditions.Add("f.showoncard = " + D.Bool(filter.ShowOnCard.Value));
            EnumerationResult<ProfileField> page = await EnumerateAsync(request, "f.*", "profilefields f", conditions, Map, token).ConfigureAwait(false);
            await AttachChildrenAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public async Task CreateAsync(ProfileField field, string? userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(field);
            List<string> statements = new List<string> { Insert(field, userId) };
            statements.AddRange(ChildInserts(field));
            await UniqueTransactionAsync(statements, _Duplicate, token).ConfigureAwait(false);
        }

        public async Task UpdateAsync(ProfileField field, string? userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(field);
            ProfileFieldValidation v = field.Validation ?? new ProfileFieldValidation();
            List<string> statements = new List<string>
            {
                "UPDATE profilefields SET key = " + D.Str(field.Key) + ", label = " + D.Str(field.Label) + ", helptext = " + D.Str(field.HelpText ?? String.Empty) +
                ", placeholder = " + D.Str(field.Placeholder ?? String.Empty) + ", type = " + D.Enum(field.Type) + ", collectat = " + D.Enum(field.CollectAt) +
                ", editableby = " + D.Enum(field.EditableBy) + ", visibility = " + D.Enum(field.Visibility) + ", showonprofile = " + D.Bool(field.ShowOnProfile) +
                ", profileorder = " + D.Num(field.ProfileOrder) + ", showoncard = " + D.Bool(field.ShowOnCard) + ", searchable = " + D.Bool(field.Searchable) +
                ", filterable = " + D.Bool(field.Filterable) + ", enabled = " + D.Bool(field.Enabled) + ", " + ValidationAssignments(v) +
                ", levellabel = " + D.Str(field.Rank?.LevelLabel) + ", maxlevel = " + Num(field.Rank == null ? null : field.Rank.MaxLevel) +
                ", revision = revision + 1, updatedbyuserid = " + D.Str(userId) + ", updatedutc = " + D.Ts(field.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(field.Id) + ";",
                "DELETE FROM profilefieldoptions WHERE " + Scope() + " AND fieldid = " + D.Str(field.Id) + ";",
                "DELETE FROM profilefieldsubfields WHERE " + Scope() + " AND fieldid = " + D.Str(field.Id) + ";"
            };
            statements.AddRange(ChildInserts(field));
            await UniqueTransactionAsync(statements, _Duplicate, token).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await TransactionAsync(new List<string>
            {
                "DELETE FROM profilefieldoptions WHERE " + Scope() + " AND fieldid = " + D.Str(id) + ";",
                "DELETE FROM profilefieldsubfields WHERE " + Scope() + " AND fieldid = " + D.Str(id) + ";",
                "DELETE FROM labels WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.ProfileField) + " AND targetid = " + D.Str(id) + ";",
                "DELETE FROM tags WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.ProfileField) + " AND targetid = " + D.Str(id) + ";",
                "DELETE FROM profilefields WHERE " + Scope() + " AND id = " + D.Str(id) + ";"
            }, token).ConfigureAwait(false);
        }

        public async Task ReorderAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> statements = new List<string>();
            for (int i = 0; i < ids.Count; i++)
                statements.Add("UPDATE profilefields SET profileorder = " + D.Num(i * 10) + ", revision = revision + 1, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(ids[i]) + ";");
            if (statements.Count > 0) await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public Task<long> CountCustomAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM profilefields WHERE " + Scope() + " AND builtin = " + D.Bool(false) + ";", token);
        }

        public async Task<string> RevisionAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT COUNT(*) AS cnt, COALESCE(SUM(revision), 0) AS rev, MAX(updatedutc) AS latest FROM profilefields WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return "0";
            DataRow row = table.Rows[0];
            DateTime? latest = RowReader.GetNullableDateTime(row, "latest");
            return RowReader.GetLong(row, "cnt").ToString(CultureInfo.InvariantCulture) + "." + RowReader.GetLong(row, "rev").ToString(CultureInfo.InvariantCulture) + "." +
                (latest == null ? "0" : latest.Value.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        private async Task AttachChildrenAsync(List<ProfileField> fields, CancellationToken token)
        {
            if (fields.Count == 0) return;
            List<string> ids = fields.Select(f => f.Id).ToList();
            DataTable options = await QueryAsync("SELECT * FROM profilefieldoptions WHERE " + Scope() + " AND fieldid IN " + D.InList(ids) + " ORDER BY fieldid, position, id LIMIT 25000;", token).ConfigureAwait(false);
            DataTable subs = await QueryAsync("SELECT * FROM profilefieldsubfields WHERE " + Scope() + " AND fieldid IN " + D.InList(ids) + " ORDER BY fieldid, position, id LIMIT 2000;", token).ConfigureAwait(false);
            Dictionary<string, ProfileField> byId = fields.ToDictionary(f => f.Id, StringComparer.Ordinal);
            Dictionary<string, ProfileSubField> subById = new Dictionary<string, ProfileSubField>(StringComparer.Ordinal);
            foreach (DataRow row in subs.Rows)
            {
                ProfileSubField sub = new ProfileSubField
                {
                    Id = RowReader.GetString(row, "id"),
                    Key = RowReader.GetString(row, "key"),
                    Label = RowReader.GetString(row, "label"),
                    Type = RowReader.GetEnum(row, "type", ProfileSubFieldTypeEnum.Text),
                    Required = RowReader.GetBool(row, "required"),
                    MaxLength = RowReader.GetNullableInt(row, "maxlength"),
                    MinValue = NullableDouble(row, "minvalue"),
                    MaxValue = NullableDouble(row, "maxvalue"),
                    NotInFuture = RowReader.GetBool(row, "notinfuture"),
                    RankFieldKey = RowReader.GetNullableString(row, "rankfieldkey"),
                    WithLevel = RowReader.GetBool(row, "withlevel"),
                    Position = RowReader.GetInt(row, "position")
                };
                subById[sub.Id] = sub;
                if (byId.TryGetValue(RowReader.GetString(row, "fieldid"), out ProfileField? owner)) owner.SubFields.Add(sub);
            }
            foreach (DataRow row in options.Rows)
            {
                ProfileFieldOption option = new ProfileFieldOption
                {
                    Id = RowReader.GetString(row, "id"),
                    Key = RowReader.GetString(row, "key"),
                    Label = RowReader.GetString(row, "label"),
                    Color = RowReader.GetNullableString(row, "color"),
                    MaxLevel = RowReader.GetNullableInt(row, "maxlevel"),
                    FreeText = RowReader.GetBool(row, "freetext"),
                    Retired = RowReader.GetBool(row, "retired"),
                    SubFieldId = RowReader.GetNullableString(row, "subfieldid")
                };
                if (option.SubFieldId != null)
                {
                    if (subById.TryGetValue(option.SubFieldId, out ProfileSubField? sub)) sub.Options.Add(option);
                }
                else if (byId.TryGetValue(RowReader.GetString(row, "fieldid"), out ProfileField? owner)) owner.Options.Add(option);
            }
        }

        private string Insert(ProfileField f, string? userId)
        {
            ProfileFieldValidation v = f.Validation ?? new ProfileFieldValidation();
            return "INSERT INTO profilefields (propertyid, id, key, builtin, label, helptext, placeholder, type, collectat, editableby, visibility, showonprofile, profileorder, showoncard, searchable, filterable, enabled, " +
                "required, minlength, maxlength, minvalue, maxvalue, integeronly, pattern, patternmessage, minentries, maxentries, mindate, maxdate, notinfuture, levellabel, maxlevel, valuecount, revision, createdbyuserid, updatedbyuserid, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(f.Id) + ", " + D.Str(f.Key) + ", " + D.Bool(f.BuiltIn) + ", " + D.Str(f.Label) + ", " + D.Str(f.HelpText ?? String.Empty) + ", " + D.Str(f.Placeholder ?? String.Empty) + ", " +
                D.Enum(f.Type) + ", " + D.Enum(f.CollectAt) + ", " + D.Enum(f.EditableBy) + ", " + D.Enum(f.Visibility) + ", " + D.Bool(f.ShowOnProfile) + ", " + D.Num(f.ProfileOrder) + ", " +
                D.Bool(f.ShowOnCard) + ", " + D.Bool(f.Searchable) + ", " + D.Bool(f.Filterable) + ", " + D.Bool(f.Enabled) + ", " +
                D.Bool(v.Required) + ", " + D.Num(v.MinLength) + ", " + D.Num(v.MaxLength) + ", " + Num(v.MinValue) + ", " + Num(v.MaxValue) + ", " + D.Bool(v.IntegerOnly) + ", " +
                D.Str(v.Pattern) + ", " + D.Str(v.PatternMessage) + ", " + D.Num(v.MinEntries) + ", " + D.Num(v.MaxEntries) + ", " + D.Str(v.MinDate) + ", " + D.Str(v.MaxDate) + ", " + D.Bool(v.NotInFuture) + ", " +
                D.Str(f.Rank?.LevelLabel) + ", " + Num(f.Rank == null ? null : f.Rank.MaxLevel) + ", 0, 1, " + D.Str(userId) + ", " + D.Str(userId) + ", " + D.Ts(f.CreatedUtc) + ", " + D.Ts(f.UpdatedUtc) + ");";
        }

        private string ValidationAssignments(ProfileFieldValidation v)
        {
            return "required = " + D.Bool(v.Required) + ", minlength = " + D.Num(v.MinLength) + ", maxlength = " + D.Num(v.MaxLength) + ", minvalue = " + Num(v.MinValue) +
                ", maxvalue = " + Num(v.MaxValue) + ", integeronly = " + D.Bool(v.IntegerOnly) + ", pattern = " + D.Str(v.Pattern) + ", patternmessage = " + D.Str(v.PatternMessage) +
                ", minentries = " + D.Num(v.MinEntries) + ", maxentries = " + D.Num(v.MaxEntries) + ", mindate = " + D.Str(v.MinDate) + ", maxdate = " + D.Str(v.MaxDate) +
                ", notinfuture = " + D.Bool(v.NotInFuture);
        }

        private List<string> ChildInserts(ProfileField f)
        {
            List<string> statements = new List<string>();
            for (int i = 0; i < f.Options.Count; i++) statements.Add(OptionInsert(f, f.Options[i], null, i));
            for (int i = 0; i < f.SubFields.Count; i++)
            {
                ProfileSubField s = f.SubFields[i];
                if (String.IsNullOrEmpty(s.Id)) s.Id = IdGenerator.Generate(Constants.ProfileSubFieldIdPrefix);
                s.Position = i;
                statements.Add("INSERT INTO profilefieldsubfields (propertyid, id, fieldid, key, label, type, required, maxlength, minvalue, maxvalue, notinfuture, rankfieldkey, withlevel, position, createdutc, updatedutc) VALUES (" +
                    Pid + ", " + D.Str(s.Id) + ", " + D.Str(f.Id) + ", " + D.Str(s.Key) + ", " + D.Str(s.Label) + ", " + D.Enum(s.Type) + ", " + D.Bool(s.Required) + ", " + D.Num(s.MaxLength) + ", " +
                    Num(s.MinValue) + ", " + Num(s.MaxValue) + ", " + D.Bool(s.NotInFuture) + ", " + D.Str(s.RankFieldKey) + ", " + D.Bool(s.WithLevel) + ", " + D.Num(i) + ", " + D.Ts(f.UpdatedUtc) + ", " + D.Ts(f.UpdatedUtc) + ");");
                for (int j = 0; j < s.Options.Count; j++) statements.Add(OptionInsert(f, s.Options[j], s.Id, j));
            }
            return statements;
        }

        private string OptionInsert(ProfileField f, ProfileFieldOption o, string? subFieldId, int position)
        {
            if (String.IsNullOrEmpty(o.Id)) o.Id = IdGenerator.Generate(Constants.ProfileFieldOptionIdPrefix);
            o.SubFieldId = subFieldId;
            return "INSERT INTO profilefieldoptions (propertyid, id, fieldid, subfieldid, key, label, color, maxlevel, freetext, retired, position, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(o.Id) + ", " + D.Str(f.Id) + ", " + D.Str(subFieldId) + ", " + D.Str(o.Key) + ", " + D.Str(o.Label) + ", " + D.Str(o.Color) + ", " + D.Num(o.MaxLevel) + ", " +
                D.Bool(o.FreeText) + ", " + D.Bool(o.Retired) + ", " + D.Num(position) + ", " + D.Ts(f.UpdatedUtc) + ", " + D.Ts(f.UpdatedUtc) + ");";
        }

        private string Num(double? value)
        {
            return value == null ? "NULL" : D.Num(value.Value);
        }

        private string Num(int? value)
        {
            return D.Num(value);
        }

        private static double? NullableDouble(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == null || row[column] == DBNull.Value) return null;
            return Convert.ToDouble(row[column], CultureInfo.InvariantCulture);
        }

        private static ProfileField Map(DataRow row)
        {
            int? maxLevel = RowReader.GetNullableInt(row, "maxlevel");
            string? levelLabel = RowReader.GetNullableString(row, "levellabel");
            ProfileFieldTypeEnum type = RowReader.GetEnum(row, "type", ProfileFieldTypeEnum.Text);
            return new ProfileField
            {
                Id = RowReader.GetString(row, "id"),
                Key = RowReader.GetString(row, "key"),
                BuiltIn = RowReader.GetBool(row, "builtin"),
                Label = RowReader.GetString(row, "label"),
                HelpText = RowReader.GetNullableString(row, "helptext") ?? String.Empty,
                Placeholder = RowReader.GetNullableString(row, "placeholder") ?? String.Empty,
                Type = type,
                Rank = type == ProfileFieldTypeEnum.Rank ? new ProfileRankSettings { LevelLabel = levelLabel ?? String.Empty, MaxLevel = maxLevel ?? 0 } : null,
                Validation = new ProfileFieldValidation
                {
                    Required = RowReader.GetBool(row, "required"),
                    MinLength = RowReader.GetNullableInt(row, "minlength"),
                    MaxLength = RowReader.GetNullableInt(row, "maxlength"),
                    MinValue = NullableDouble(row, "minvalue"),
                    MaxValue = NullableDouble(row, "maxvalue"),
                    IntegerOnly = RowReader.GetBool(row, "integeronly"),
                    Pattern = RowReader.GetNullableString(row, "pattern"),
                    PatternMessage = RowReader.GetNullableString(row, "patternmessage"),
                    MinEntries = RowReader.GetNullableInt(row, "minentries"),
                    MaxEntries = RowReader.GetNullableInt(row, "maxentries"),
                    MinDate = RowReader.GetNullableString(row, "mindate"),
                    MaxDate = RowReader.GetNullableString(row, "maxdate"),
                    NotInFuture = RowReader.GetBool(row, "notinfuture")
                },
                CollectAt = RowReader.GetEnum(row, "collectat", ProfileCollectAtEnum.Profile),
                EditableBy = RowReader.GetEnum(row, "editableby", ProfileEditableByEnum.Member),
                Visibility = RowReader.GetEnum(row, "visibility", ProfileVisibilityEnum.Public),
                ShowOnProfile = RowReader.GetBool(row, "showonprofile"),
                ProfileOrder = RowReader.GetInt(row, "profileorder"),
                ShowOnCard = RowReader.GetBool(row, "showoncard"),
                Searchable = RowReader.GetBool(row, "searchable"),
                Filterable = RowReader.GetBool(row, "filterable"),
                Enabled = RowReader.GetBool(row, "enabled"),
                ValueCount = RowReader.GetLong(row, "valuecount"),
                Revision = RowReader.GetLong(row, "revision"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
