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
    using TrellisKit.Core.Responses;

    internal abstract class DirectoryConfigMethodsBase : MethodsBase, IDirectoryConfigMethods
    {
        protected DirectoryConfigMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<DirectoryConfig?> ReadAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM directoryconfig WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new DirectoryConfig
            {
                VenueLabel = RowReader.GetString(row, "venuelabel"),
                VenueLabelPlural = RowReader.GetString(row, "venuelabelplural"),
                InstructorLabel = RowReader.GetString(row, "instructorlabel"),
                InstructorLabelPlural = RowReader.GetString(row, "instructorlabelplural"),
                InstructorRankFieldKey = RowReader.GetNullableString(row, "instructorrankfieldkey"),
                SubmissionsEnabled = RowReader.GetBool(row, "submissionsenabled"),
                ClaimsEnabled = RowReader.GetBool(row, "claimsenabled"),
                SourceHash = RowReader.GetNullableString(row, "sourcehash"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        public async Task WriteAsync(DirectoryConfig config, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(config);
            await QueryAsync("INSERT INTO directoryconfig (propertyid, venuelabel, venuelabelplural, instructorlabel, instructorlabelplural, instructorrankfieldkey, submissionsenabled, claimsenabled, sourcehash, updatedutc) VALUES (" +
                Pid + ", " + D.Str(config.VenueLabel) + ", " + D.Str(config.VenueLabelPlural) + ", " + D.Str(config.InstructorLabel) + ", " + D.Str(config.InstructorLabelPlural) + ", " + D.Str(config.InstructorRankFieldKey) + ", " +
                D.Bool(config.SubmissionsEnabled) + ", " + D.Bool(config.ClaimsEnabled) + ", " + D.Str(config.SourceHash) + ", " + D.Ts(config.UpdatedUtc) + ") ON CONFLICT (propertyid) DO UPDATE SET venuelabel = EXCLUDED.venuelabel, " +
                "venuelabelplural = EXCLUDED.venuelabelplural, instructorlabel = EXCLUDED.instructorlabel, instructorlabelplural = EXCLUDED.instructorlabelplural, instructorrankfieldkey = EXCLUDED.instructorrankfieldkey, " +
                "submissionsenabled = EXCLUDED.submissionsenabled, claimsenabled = EXCLUDED.claimsenabled, sourcehash = EXCLUDED.sourcehash, updatedutc = EXCLUDED.updatedutc;", token).ConfigureAwait(false);
        }

        public async Task<List<DirectoryProgram>> ProgramsAsync(bool includeRetired, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM directoryprograms WHERE " + Scope() + (includeRetired ? String.Empty : " AND isretired = " + D.Bool(false)) + " ORDER BY sortorder, id LIMIT 200;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<DirectoryProgram?> ReadProgramAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM directoryprograms WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<DirectoryProgram> CreateProgramAsync(DirectoryProgram program, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(program);
            RequireText(program.Id, nameof(program.Id));
            await UniqueAsync("INSERT INTO directoryprograms (propertyid, id, key, label, sortorder, isretired, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(program.Id) + ", " + D.Str(program.Key) + ", " +
                D.Str(program.Label) + ", " + D.Num(program.SortOrder) + ", " + D.Bool(program.IsRetired) + ", " + D.Ts(program.CreatedUtc) + ", " + D.Ts(program.UpdatedUtc) + ");", "A program with this key", token).ConfigureAwait(false);
            return (await ReadProgramAsync(program.Id, token).ConfigureAwait(false))!;
        }

        public async Task<DirectoryProgram?> UpdateProgramAsync(DirectoryProgram program, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(program);
            RequireText(program.Id, nameof(program.Id));
            await QueryAsync("UPDATE directoryprograms SET label = " + D.Str(program.Label) + ", sortorder = " + D.Num(program.SortOrder) + ", isretired = " + D.Bool(program.IsRetired) + ", updatedutc = " + D.Ts(program.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(program.Id) + ";", token).ConfigureAwait(false);
            return await ReadProgramAsync(program.Id, token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteProgramAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = new List<string>(Driver.Metadata.DeleteStatements(TargetTypeEnum.DirectoryProgram, id))
            {
                "DELETE FROM directoryprograms WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> ProgramInUseAsync(string key, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM venueprograms vp JOIN venues v ON " + Scope("v") + " AND v.id = vp.venueid AND v.deletedutc IS NULL WHERE " + Scope("vp") +
                " AND lower(vp.programkey) = " + D.Str(key.ToLowerInvariant()) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<int> ReorderProgramsAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return 0;
            List<string> statements = new List<string>();
            for (int i = 0; i < ids.Count; i++)
                statements.Add("UPDATE directoryprograms SET sortorder = " + D.Num((i + 1) * 10) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(ids[i]) + ";");
            statements.Add("SELECT COUNT(*) AS cnt FROM directoryprograms WHERE " + Scope() + " AND id IN " + D.InList(ids) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public Task<EnumerationResult<DirectoryProgram>> EnumerateProgramsAsync(EnumerationRequest request, bool includeRetired, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (!includeRetired) conditions.Add("p.isretired = " + D.Bool(false));
            return EnumerateAsync(request, "p.*", "directoryprograms p", conditions, Map, token);
        }

        private static DirectoryProgram Map(DataRow row)
        {
            return new DirectoryProgram
            {
                Id = RowReader.GetString(row, "id"),
                Key = RowReader.GetString(row, "key"),
                Label = RowReader.GetString(row, "label"),
                SortOrder = RowReader.GetInt(row, "sortorder"),
                IsRetired = RowReader.GetBool(row, "isretired"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
