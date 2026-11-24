namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// SQL condition of one compiled profile filter clause (docs/MULTIPROPERTY_CONTRACT.md section 9.12), shared by the
    /// member directory and broadcast segments so both select members the same way.
    /// </summary>
    internal static class ProfileFilterSql
    {
        /// <summary>
        /// Build the condition.
        /// </summary>
        /// <param name="d">Dialect.</param>
        /// <param name="pid">SQL literal of the bound property id.</param>
        /// <param name="userColumn">The member id column of the outer query, for example u.id.</param>
        /// <param name="clause">Compiled clause.</param>
        /// <returns>Condition (never empty; an unknown operator matches nothing).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal static string Condition(SqlDialect d, string pid, string userColumn, ProfileFilterClause clause)
        {
            ArgumentNullException.ThrowIfNull(d);
            ArgumentNullException.ThrowIfNull(pid);
            ArgumentNullException.ThrowIfNull(userColumn);
            ArgumentNullException.ThrowIfNull(clause);
            string field = d.Str(clause.FieldId);
            string values = "SELECT 1 FROM profilevalues pv WHERE pv.propertyid = " + pid + " AND pv.userid = " + userColumn + " AND pv.fieldid = " + field;
            switch (clause.Operator)
            {
                case ProfileFilterOperatorEnum.HasValue:
                    string exists;
                    if (clause.FieldType == ProfileFieldTypeEnum.History)
                        exists = "EXISTS (SELECT 1 FROM profilehistoryentries ph WHERE ph.propertyid = " + pid + " AND ph.userid = " + userColumn + " AND ph.fieldid = " + field + ")";
                    else if (clause.FieldType == ProfileFieldTypeEnum.MultiSelect)
                        exists = "EXISTS (SELECT 1 FROM profilevalueoptions po WHERE po.propertyid = " + pid + " AND po.userid = " + userColumn + " AND po.fieldid = " + field + ")";
                    else exists = "EXISTS (" + values + ")";
                    return clause.Flag == false ? "NOT " + exists : exists;
                case ProfileFilterOperatorEnum.Any:
                    return "EXISTS (SELECT 1 FROM profilevalueoptions po WHERE po.propertyid = " + pid + " AND po.userid = " + userColumn + " AND po.fieldid = " + field + " AND po.optionkey IN " + d.InList(clause.Values) + ")";
                case ProfileFilterOperatorEnum.All:
                    return "(" + String.Join(" AND ", clause.Values.Select(v => "EXISTS (SELECT 1 FROM profilevalueoptions po WHERE po.propertyid = " + pid + " AND po.userid = " + userColumn + " AND po.fieldid = " + field + " AND po.optionkey = " + d.Str(v) + ")")) + ")";
                case ProfileFilterOperatorEnum.In:
                case ProfileFilterOperatorEnum.MinRank:
                case ProfileFilterOperatorEnum.MaxRank:
                    if (clause.Values.Count == 0) return "1 = 0";
                    return "EXISTS (" + values + " AND pv.valuetext IN " + d.InList(clause.Values) + ")";
                case ProfileFilterOperatorEnum.MinLevel:
                    return "EXISTS (" + values + " AND COALESCE(pv.valuelevel, 0) >= " + d.Num((long)(clause.Number ?? 0)) + ")";
                case ProfileFilterOperatorEnum.Prefix:
                    return "EXISTS (" + values + " AND " + d.StartsWith("pv.valuenormalized", clause.Values.FirstOrDefault() ?? String.Empty) + ")";
                case ProfileFilterOperatorEnum.Gte:
                case ProfileFilterOperatorEnum.Lte:
                    string op = clause.Operator == ProfileFilterOperatorEnum.Gte ? " >= " : " <= ";
                    if (clause.FieldType == ProfileFieldTypeEnum.Date) return "EXISTS (" + values + " AND pv.valuedate" + op + d.Str(clause.Values.FirstOrDefault() ?? String.Empty) + ")";
                    return "EXISTS (" + values + " AND pv.valuenumber" + op + d.Num(clause.Number ?? 0) + ")";
                case ProfileFilterOperatorEnum.Eq:
                    if (clause.FieldType == ProfileFieldTypeEnum.Boolean) return "EXISTS (" + values + " AND pv.valuebool = " + d.Bool(clause.Flag == true) + ")";
                    if (clause.FieldType == ProfileFieldTypeEnum.Number) return "EXISTS (" + values + " AND pv.valuenumber = " + d.Num(clause.Number ?? 0) + ")";
                    return "EXISTS (" + values + " AND pv.valuenormalized = " + d.Str(clause.Values.FirstOrDefault() ?? String.Empty) + ")";
                default:
                    return "1 = 0";
            }
        }
    }
}
