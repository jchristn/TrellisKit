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

    /// <summary>
    /// Shared access configuration methods (tables propertyaccess and propertyaccessfeatures).
    /// </summary>
    internal abstract class PropertyAccessMethodsBase : MethodsBase, IPropertyAccessMethods
    {
        protected PropertyAccessMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<AccessPolicy?> ReadAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM propertyaccess WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            DataTable features = await QueryAsync("SELECT feature FROM propertyaccessfeatures WHERE " + Scope() + " ORDER BY feature LIMIT 50;", token).ConfigureAwait(false);
            return new AccessPolicy
            {
                Model = RowReader.GetEnum(row, "model", AccessModelEnum.FullyGated),
                AnonymousBrowsing = RowReader.GetBool(row, "anonymousbrowsing"),
                DefaultAudience = RowReader.GetNullableEnum<ContentAudienceEnum>(row, "defaultaudience"),
                GatedFeatures = AccessPolicy.Normalize(features.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "feature"))),
                SourceHash = RowReader.GetNullableString(row, "sourcehash"),
                Revision = RowReader.GetInt(row, "revision"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        public async Task WriteAsync(AccessPolicy policy, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(policy);
            List<string> statements = new List<string>
            {
                "INSERT INTO propertyaccess (propertyid, model, anonymousbrowsing, defaultaudience, sourcehash, revision, updatedbyuserid, createdutc, updatedutc) VALUES (" +
                    Pid + ", " + D.Enum(policy.Model) + ", " + D.Bool(policy.AnonymousBrowsing) + ", " + (policy.DefaultAudience == null ? "NULL" : D.Enum(policy.DefaultAudience.Value)) + ", " +
                    D.Str(policy.SourceHash) + ", " + D.Num(policy.Revision) + ", " + D.Str(policy.UpdatedByUserId) + ", " + D.Ts(policy.CreatedUtc) + ", " + D.Ts(policy.UpdatedUtc) +
                    ") ON CONFLICT (propertyid) DO UPDATE SET model = EXCLUDED.model, anonymousbrowsing = EXCLUDED.anonymousbrowsing, defaultaudience = EXCLUDED.defaultaudience, " +
                    "sourcehash = EXCLUDED.sourcehash, revision = EXCLUDED.revision, updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;",
                "DELETE FROM propertyaccessfeatures WHERE " + Scope() + ";"
            };
            foreach (FeatureKeyEnum feature in policy.GatedFeatures.Distinct())
                statements.Add("INSERT INTO propertyaccessfeatures (propertyid, feature, createdutc) VALUES (" + Pid + ", " + D.Str(AccessPolicy.FeatureKey(feature)) + ", " + D.Ts(policy.UpdatedUtc) + ");");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }
    }
}
