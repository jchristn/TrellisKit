namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Identifier, settings model, filter, and row reader behavior.
    /// </summary>
    public static class ModelSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Model";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Models and settings", new[]
            {
                Case("IdGenerator.PrefixAndLength", "Identifiers carry the prefix and fixed length", ct =>
                {
                    string id = IdGenerator.GenerateRequestHistoryId();
                    Check.True(id.StartsWith(Constants.RequestHistoryIdPrefix, StringComparison.Ordinal), "Prefix missing: " + id);
                    Check.Equal(Constants.IdLength, id.Length, "Identifier length");
                    return Task.CompletedTask;
                }),
                Case("IdGenerator.UniqueUnderLoad", "10000 identifiers generated in parallel are unique", ct =>
                {
                    string[] ids = new string[10000];
                    System.Threading.Tasks.Parallel.For(0, ids.Length, i => ids[i] = IdGenerator.GenerateUserId());
                    Check.Equal(ids.Length, ids.Distinct().Count(), "Distinct identifiers");
                    return Task.CompletedTask;
                }),
                Case("IdGenerator.EmptyPrefixThrows", "An empty prefix is rejected", ct =>
                {
                    Check.Throws<ArgumentNullException>(() => IdGenerator.Generate(String.Empty), "Generate(empty)");
                    return Task.CompletedTask;
                }),
                Case("Settings.Clamping", "Numeric settings clamp to their documented ranges", ct =>
                {
                    RequestHistorySettings history = new RequestHistorySettings { RetentionDays = 0, MaxRequestBodyBytes = -5, PruneIntervalMinutes = 100000 };
                    Check.Equal(1, history.RetentionDays, "RetentionDays minimum");
                    Check.Equal(0, history.MaxRequestBodyBytes, "MaxRequestBodyBytes minimum");
                    Check.Equal(1440, history.PruneIntervalMinutes, "PruneIntervalMinutes maximum");
                    RestSettings rest = new RestSettings { Port = 70000 };
                    Check.Equal(65535, rest.Port, "Port maximum");
                    return Task.CompletedTask;
                }),
                Case("Settings.NullSectionsRejected", "Null settings sections and blank required strings are rejected", ct =>
                {
                    AppSettings settings = new AppSettings();
                    Check.Throws<ArgumentNullException>(() => settings.Database = null!, "Database = null");
                    Check.Throws<ArgumentNullException>(() => settings.Rest.Hostname = " ", "Hostname = blank");
                    Check.Throws<ArgumentException>(() => settings.Telemetry.OtlpEndpoint = "not a uri", "OtlpEndpoint invalid");
                    return Task.CompletedTask;
                }),
                Case("Cors.ResolveOrigin", "CORS echoes allowed origins only", ct =>
                {
                    CorsSettings cors = new CorsSettings { AllowedOrigins = new List<string> { "https://example.test/", " https://www.example.test " } };
                    Check.Equal("https://example.test", cors.ResolveAllowedOrigin("https://example.test"), "Allowed origin");
                    Check.Equal("https://WWW.example.test", cors.ResolveAllowedOrigin("https://WWW.example.test"), "Case-insensitive match echoes the request origin exactly");
                    Check.True(cors.ResolveAllowedOrigin("https://evil.example") == null, "Disallowed origin must resolve to null");
                    Check.True(cors.ResolveAllowedOrigin(null) == null, "Missing origin must resolve to null");
                    CorsSettings any = new CorsSettings { AllowedOrigins = new List<string> { "*" } };
                    Check.Equal("https://x.example", any.ResolveAllowedOrigin("https://x.example"), "Wildcard echoes origin");
                    return Task.CompletedTask;
                }),
                Case("Filter.Normalization", "Filters normalize method, blank values, and paging bounds (maxResults clamping)", ct =>
                {
                    RequestHistoryFilter filter = new RequestHistoryFilter { Method = " get ", PathContains = "  ", BucketMinutes = 0 };
                    Check.Equal("GET", filter.Method, "Method");
                    Check.True(filter.PathContains == null, "Blank PathContains becomes null");
                    Check.Equal(1, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = -3 }, PhaseOneCollections.RequestHistory, new EnumerationSettings(), true).MaxResults, "maxResults minimum");
                    Check.Equal(500, EnumerationNormalizer.Normalize(new EnumerationQuery { MaxResults = 5000 }, PhaseOneCollections.RequestHistory, new EnumerationSettings(), true).MaxResults, "maxResults admin maximum");
                    Check.Equal(1, filter.BucketMinutes, "BucketMinutes minimum");
                    return Task.CompletedTask;
                }),
                Case("RowReader.ProviderNeutral", "Row reader accepts SQLite text and native types", ct =>
                {
                    DataTable table = new DataTable();
                    table.Columns.Add("b1", typeof(long));
                    table.Columns.Add("b2", typeof(bool));
                    table.Columns.Add("t1", typeof(string));
                    table.Columns.Add("t2", typeof(DateTime));
                    table.Columns.Add("n", typeof(string));
                    table.Rows.Add(1L, false, "2026-11-11T10:00:00.000Z", new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc), DBNull.Value);
                    DataRow row = table.Rows[0];
                    Check.True(RowReader.GetBool(row, "b1"), "Integer 1 is true");
                    Check.True(!RowReader.GetBool(row, "b2"), "Native false");
                    Check.Equal(RowReader.GetDateTime(row, "t1"), RowReader.GetDateTime(row, "t2"), "Text and native timestamps");
                    Check.Equal(DateTimeKind.Utc, RowReader.GetDateTime(row, "t1").Kind, "Timestamp kind");
                    Check.True(RowReader.GetNullableString(row, "n") == null, "DBNull is null");
                    Check.Equal(0, RowReader.GetInt(row, "missing"), "Missing column defaults");
                    return Task.CompletedTask;
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<System.Threading.CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit" });
        }

        #endregion
    }
}
