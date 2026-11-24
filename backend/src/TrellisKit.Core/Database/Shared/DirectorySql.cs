namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;

    /// <summary>
    /// SQL fragments of the directory method sets: the Haversine distance, box predicates, guard statements, and
    /// nullable double reads.
    /// </summary>
    internal static class DirectorySql
    {
        internal const string GuardPrefix = "tk_guard:";

        internal static string Haversine(SqlDialect d, double lat, double lng, string alias)
        {
            string la = d.Num(lat);
            string lo = d.Num(lng);
            return "(" + d.Num(GeoMath.EarthRadiusKm) + " * 2 * asin(sqrt(LEAST(1.0, power(sin(radians(" + alias + ".lat - " + la + ") / 2), 2) + cos(radians(" + la + ")) * cos(radians(" + alias + ".lat)) * power(sin(radians(" +
                alias + ".lng - " + lo + ") / 2), 2)))))";
        }

        internal static string DistanceKey(SqlDialect d, double lat, double lng, string alias)
        {
            return "CAST(ROUND(CAST(" + Haversine(d, lat, lng, alias) + " * 1000000 AS NUMERIC)) AS BIGINT)";
        }

        internal static string Box(SqlDialect d, GeoBox box, string latExpression, string lngExpression)
        {
            string lat = latExpression + " BETWEEN " + d.Num(box.South) + " AND " + d.Num(box.North);
            string lng = box.West <= box.East
                ? lngExpression + " BETWEEN " + d.Num(box.West) + " AND " + d.Num(box.East)
                : "(" + lngExpression + " >= " + d.Num(box.West) + " OR " + lngExpression + " <= " + d.Num(box.East) + ")";
            return "(" + lat + " AND " + lng + ")";
        }

        internal static string MapCoordinate(string approximateCondition, string column)
        {
            return "(CASE WHEN " + approximateCondition + " THEN CAST(ROUND(CAST(" + column + " AS NUMERIC), 2) AS DOUBLE PRECISION) ELSE " + column + " END)";
        }

        internal static string ClusterQuery(SqlDialect d, string latExpression, string lngExpression, string groupLat, string groupLng, string idColumn, string from, string where, double cellDegrees, int limit)
        {
            string c = d.Num(Math.Max(1e-9, cellDegrees));
            return "SELECT floor(" + groupLat + " / " + c + ") AS gridlat, floor(" + groupLng + " / " + c + ") AS gridlng, COUNT(*) AS cnt, AVG(" + latExpression + ") AS clat, AVG(" + lngExpression + ") AS clng, MIN(" + latExpression + ") AS south, MAX(" + latExpression + ") AS north, MIN(" + lngExpression +
                ") AS west, MAX(" + lngExpression + ") AS east, MIN(" + idColumn + ") AS firstid FROM " + from + " WHERE " + where + " GROUP BY floor(" + groupLat + " / " + c + "), floor(" + groupLng + " / " + c +
                ") ORDER BY COUNT(*) DESC, MIN(" + idColumn + ") LIMIT " + d.Num(Math.Max(1, limit)) + ";";
        }

        internal static List<MapCluster> Clusters(DataTable table)
        {
            List<MapCluster> result = new List<MapCluster>();
            foreach (DataRow row in table.Rows)
            {
                int count = (int)RowReader.GetLong(row, "cnt");
                result.Add(new MapCluster
                {
                    GridLatitude = RowReader.GetLong(row, "gridlat"),
                    GridLongitude = RowReader.GetLong(row, "gridlng"),
                    Count = count,
                    Lat = RowReader.GetDouble(row, "clat"),
                    Lng = RowReader.GetDouble(row, "clng"),
                    South = RowReader.GetDouble(row, "south"),
                    North = RowReader.GetDouble(row, "north"),
                    West = RowReader.GetDouble(row, "west"),
                    East = RowReader.GetDouble(row, "east"),
                    ItemId = count == 1 ? RowReader.GetString(row, "firstid") : null
                });
            }
            return result;
        }

        internal static string Guard(string condition, string code)
        {
            return "DO $tkguard$ BEGIN IF NOT (" + condition + ") THEN RAISE EXCEPTION '" + GuardPrefix + code + "'; END IF; END $tkguard$;";
        }

        internal static string? GuardCode(Exception exception)
        {
            for (Exception? e = exception; e != null; e = e.InnerException)
            {
                int at = e.Message.IndexOf(GuardPrefix, StringComparison.Ordinal);
                if (at < 0) continue;
                string rest = e.Message.Substring(at + GuardPrefix.Length);
                int end = 0;
                while (end < rest.Length && (Char.IsLetterOrDigit(rest[end]) || rest[end] == '_')) end++;
                return rest.Substring(0, end);
            }
            return null;
        }

        internal static double? NullableDouble(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column)) return null;
            object value = row[column];
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        internal static string NullableNum(SqlDialect d, double? value)
        {
            return value == null ? "NULL" : d.Num(value.Value);
        }

        internal static string Positions(IEnumerable<string> values)
        {
            return String.Join(",", values);
        }

        internal static List<string> Distinct(IEnumerable<string> ids)
        {
            return ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
