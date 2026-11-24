namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    internal abstract class AiHarnessMethodsBase : MethodsBase, IAiHarnessMethods
    {
        protected AiHarnessMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<AiHarnessProfile>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM aiharnessprofiles WHERE " + Scope() + " ORDER BY runtime LIMIT 50;", token).ConfigureAwait(false);
            List<AiHarnessProfile> result = new List<AiHarnessProfile>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        public async Task<AiHarnessProfile?> ReadAsync(AiExecutorRuntimeEnum runtime, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM aiharnessprofiles WHERE " + Scope() + " AND runtime = " + D.Enum(runtime) + " LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public Task SaveAsync(AiHarnessProfile p, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(p);
            return QueryAsync("INSERT INTO aiharnessprofiles (propertyid, runtime, model, permissionmode, timeoutseconds, maxtoolcalls, allowwebresearch, baseurl, credentialtype, credentialsecret, " +
                "updatedbyuserid, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Enum(p.Runtime) + ", " + D.Str(p.Model) + ", " + D.Str(p.PermissionMode) + ", " + D.Num(p.TimeoutSeconds) + ", " +
                D.Num(p.MaxToolCalls) + ", " + D.Bool(p.AllowWebResearch) + ", " + D.Str(p.BaseUrl) + ", " + D.Enum(p.CredentialType) + ", " + D.Str(p.CredentialSecret) + ", " + D.Str(p.UpdatedByUserId) + ", " +
                D.Ts(p.CreatedUtc) + ", " + D.Ts(p.UpdatedUtc) + ") ON CONFLICT (propertyid, runtime) DO UPDATE SET model = EXCLUDED.model, permissionmode = EXCLUDED.permissionmode, " +
                "timeoutseconds = EXCLUDED.timeoutseconds, maxtoolcalls = EXCLUDED.maxtoolcalls, allowwebresearch = EXCLUDED.allowwebresearch, baseurl = EXCLUDED.baseurl, " +
                "credentialtype = EXCLUDED.credentialtype, credentialsecret = EXCLUDED.credentialsecret, updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;", token);
        }

        private static AiHarnessProfile Map(DataRow row)
        {
            return new AiHarnessProfile
            {
                Runtime = RowReader.GetEnum(row, "runtime", AiExecutorRuntimeEnum.ClaudeCode),
                Model = RowReader.GetNullableString(row, "model"),
                PermissionMode = RowReader.GetNullableString(row, "permissionmode"),
                TimeoutSeconds = RowReader.GetNullableInt(row, "timeoutseconds"),
                MaxToolCalls = RowReader.GetNullableInt(row, "maxtoolcalls"),
                AllowWebResearch = RowReader.GetBool(row, "allowwebresearch"),
                BaseUrl = RowReader.GetNullableString(row, "baseurl"),
                CredentialType = RowReader.GetEnum(row, "credentialtype", HarnessCredentialTypeEnum.Environment),
                CredentialSecret = RowReader.GetNullableString(row, "credentialsecret"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
