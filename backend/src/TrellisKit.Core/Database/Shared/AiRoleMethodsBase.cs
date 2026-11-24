namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    internal abstract class AiRoleMethodsBase : MethodsBase, IAiRoleMethods
    {
        protected AiRoleMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Dictionary<string, AiTaskRoleConfig>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable roles = await QueryAsync("SELECT * FROM aitaskroles WHERE " + Scope() + " ORDER BY role LIMIT 200;", token).ConfigureAwait(false);
            DataTable executors = await QueryAsync("SELECT * FROM aitaskroleexecutors WHERE " + Scope() + " ORDER BY role, position LIMIT 2000;", token).ConfigureAwait(false);
            Dictionary<string, AiTaskRoleConfig> result = new Dictionary<string, AiTaskRoleConfig>(StringComparer.Ordinal);
            foreach (DataRow row in roles.Rows)
            {
                string? effort = RowReader.GetNullableString(row, "reasoningeffort");
                result[RowReader.GetString(row, "role")] = new AiTaskRoleConfig
                {
                    Role = RowReader.GetString(row, "role"),
                    ReasoningEffort = effort != null && Enum.TryParse(effort, out ModelReasoningEffortEnum e) ? e : null,
                    AvailabilityPolicy = Enum.TryParse(RowReader.GetString(row, "availabilitypolicy"), out AiAvailabilityPolicyEnum a) ? a : AiAvailabilityPolicyEnum.FallThrough,
                    ExhaustionPolicy = Enum.TryParse(RowReader.GetString(row, "exhaustionpolicy"), out AiExhaustionPolicyEnum x) ? x : AiExhaustionPolicyEnum.Fail,
                    UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                    UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
                };
            }
            foreach (DataRow row in executors.Rows)
            {
                if (!result.TryGetValue(RowReader.GetString(row, "role"), out AiTaskRoleConfig? config)) continue;
                config.Executors.Add(new AiExecutor
                {
                    Position = RowReader.GetInt(row, "position"),
                    Runtime = Enum.TryParse(RowReader.GetString(row, "runtime"), out AiExecutorRuntimeEnum r) ? r : AiExecutorRuntimeEnum.ApiEndpoint,
                    EndpointId = RowReader.GetNullableString(row, "endpointid"),
                    Model = RowReader.GetNullableString(row, "model"),
                    Enabled = RowReader.GetBool(row, "enabled")
                });
            }

            return result;
        }

        public Task SaveAsync(AiTaskRoleConfig config, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(config);
            RequireText(config.Role, nameof(config.Role));
            if (config.Executors.Count > 8) throw new ArgumentOutOfRangeException(nameof(config), "At most 8 executors per role.");
            List<string> statements = new List<string>
            {
                "INSERT INTO aitaskroles (propertyid, role, reasoningeffort, availabilitypolicy, exhaustionpolicy, updatedbyuserid, updatedutc) VALUES (" + Pid + ", " + D.Str(config.Role) + ", " +
                    D.Str(config.ReasoningEffort?.ToString()) + ", " + D.Enum(config.AvailabilityPolicy) + ", " + D.Enum(config.ExhaustionPolicy) + ", " + D.Str(config.UpdatedByUserId) + ", " +
                    D.Ts(config.UpdatedUtc ?? DateTime.UtcNow) + ") ON CONFLICT (propertyid, role) DO UPDATE SET reasoningeffort = EXCLUDED.reasoningeffort, availabilitypolicy = EXCLUDED.availabilitypolicy, " +
                    "exhaustionpolicy = EXCLUDED.exhaustionpolicy, updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;",
                "DELETE FROM aitaskroleexecutors WHERE " + Scope() + " AND role = " + D.Str(config.Role) + ";"
            };
            int position = 0;
            foreach (AiExecutor e in config.Executors)
            {
                statements.Add("INSERT INTO aitaskroleexecutors (propertyid, role, position, runtime, endpointid, model, enabled) VALUES (" + Pid + ", " + D.Str(config.Role) + ", " + D.Num(position++) + ", " +
                    D.Enum(e.Runtime) + ", " + D.Str(e.EndpointId) + ", " + D.Str(e.Model) + ", " + D.Bool(e.Enabled) + ");");
            }

            return TransactionAsync(statements, token);
        }
    }
}
