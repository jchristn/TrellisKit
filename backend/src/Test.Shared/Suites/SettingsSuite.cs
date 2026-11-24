namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// Settings store, environment overrides, and the runtime settings service.
    /// </summary>
    public static class SettingsSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Settings";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Runtime settings", new[]
            {
                Case("Store.FirstBootGeneratesToken", "First boot writes the file and generates an admin token", ct =>
                {
                    string path = NewPath();
                    SettingsStore store = new SettingsStore(path, name => null);
                    AppSettings settings = store.LoadEffective(out string? generated);
                    Check.True(generated != null && generated.StartsWith("tk_", StringComparison.Ordinal), "Token generated");
                    Check.Equal(generated, settings.Auth.AdminToken, "Effective token");
                    Check.True(File.Exists(path), "Settings file written");
                    store.LoadEffective(out string? second);
                    Check.True(second == null, "Second boot keeps the saved token");
                    return Task.CompletedTask;
                }),
                Case("Store.EnvironmentOverrides", "Environment variables override the file and are reported", ct =>
                {
                    Dictionary<string, string> env = new Dictionary<string, string> { { "TK_REST_PORT", "9123" }, { "TK_DB_TYPE", "postgresql" }, { "TK_ADMIN_TOKEN", "env_token_value_that_is_long_enough_0001" } };
                    SettingsStore store = new SettingsStore(NewPath(), name => env.TryGetValue(name, out string? v) ? v : null);
                    AppSettings settings = store.LoadEffective(out string? generated);
                    Check.True(generated == null, "No token generated when the environment supplies one");
                    Check.Equal(9123, settings.Rest.Port, "Port");
                    Check.Equal(DatabaseTypeEnum.Postgresql, settings.Database.Type, "Database type");
                    HashSet<string> overridden = store.ApplyEnvironment(new AppSettings());
                    Check.True(overridden.SetEquals(new[] { "rest.port", "database.type", "auth.adminToken" }), "Overridden keys: " + String.Join(",", overridden));
                    return Task.CompletedTask;
                }),
                Case("Store.InvalidEnvironmentFails", "An invalid environment value stops startup with a clear message", ct =>
                {
                    SettingsStore store = new SettingsStore(NewPath(), name => name == "TK_REST_PORT" ? "eighty" : null);
                    InvalidOperationException e = Check.Throws<InvalidOperationException>(() => store.LoadEffective(out string? _), "LoadEffective");
                    Check.True(e.Message.Contains("TK_REST_PORT", StringComparison.Ordinal), "Message names the variable: " + e.Message);
                    return Task.CompletedTask;
                }),
                Case("Store.InvalidFileFails", "A malformed settings file stops startup with a clear message", ct =>
                {
                    string path = NewPath();
                    File.WriteAllText(path, "{ not json");
                    SettingsStore store = new SettingsStore(path, name => null);
                    InvalidOperationException e = Check.Throws<InvalidOperationException>(() => store.LoadFile(), "LoadFile");
                    Check.True(e.Message.Contains(path, StringComparison.Ordinal), "Message names the file");
                    return Task.CompletedTask;
                }),
                Case("Service.DescribeHidesSecrets", "Describe never returns secret values", ct =>
                {
                    using (SettingsFixture fixture = SettingsFixture.Create(name => null))
                    {
                        SettingsResponse response = fixture.Service.Describe();
                        Check.Equal(SettingsRegistry.All.Count, response.Fields.Count, "Field count");
                        SettingField token = response.Fields.Single(f => f.Key == "auth.adminToken");
                        Check.True(token.Value == null, "Secret value omitted");
                        Check.Equal(true, token.IsSet, "Secret reported as set");
                        SettingField port = response.Fields.Single(f => f.Key == "rest.port");
                        Check.Equal(1L, port.Min, "Port min");
                        Check.Equal(SettingApplyModeEnum.Restart, port.ApplyMode, "Port apply mode");
                    }
                    return Task.CompletedTask;
                }),
                CaseAsync("Service.LiveChangeApplies", "A live change is saved and applied to the running server", async ct =>
                {
                    using (SettingsFixture fixture = SettingsFixture.Create(name => null))
                    {
                        await fixture.Service.UpdateAsync(Request(new Dictionary<string, object> { { "requestHistory.retentionDays", 7 }, { "rest.port", 9001 } }), ct).ConfigureAwait(false);
                        Check.Equal(7, fixture.Running.RequestHistory.RetentionDays, "Live value applied");
                        Check.Equal(8000, fixture.Running.Rest.Port, "Restart value not applied to the running server");
                        AppSettings saved = fixture.Store.LoadFile();
                        Check.Equal(7, saved.RequestHistory.RetentionDays, "Live value saved");
                        Check.Equal(9001, saved.Rest.Port, "Restart value saved");
                        Check.Equal(9001L, Convert.ToInt64(fixture.Service.Describe().Fields.Single(f => f.Key == "rest.port").Value), "Describe shows the saved value");
                    }
                }),
                CaseAsync("Service.ValidationIsAllOrNothing", "Any invalid value rejects the whole update with per-field errors", async ct =>
                {
                    using (SettingsFixture fixture = SettingsFixture.Create(name => name == "TK_REST_PORT" ? "8000" : null))
                    {
                        SettingsValidationException e = await Check.ThrowsAsync<SettingsValidationException>(
                            () => fixture.Service.UpdateAsync(Request(new Dictionary<string, object> { { "requestHistory.retentionDays", 14 }, { "requestHistory.maxRequestBodyBytes", -1 }, { "database.type", "Oracle" }, { "rest.port", 9000 }, { "auth.adminToken", "short" }, { "unknown.key", true } }), ct),
                            "UpdateAsync").ConfigureAwait(false);
                        Check.True(e.FieldErrors.ContainsKey("requestHistory.maxRequestBodyBytes"), "Range error");
                        Check.True(e.FieldErrors.ContainsKey("database.type"), "Enum error");
                        Check.True(e.FieldErrors["rest.port"].Contains("TK_REST_PORT", StringComparison.Ordinal), "Environment-locked field");
                        Check.True(e.FieldErrors.ContainsKey("auth.adminToken"), "Short token rejected");
                        Check.True(e.FieldErrors.ContainsKey("unknown.key"), "Unknown key");
                        Check.True(!e.FieldErrors.ContainsKey("requestHistory.retentionDays"), "Valid field has no error");
                        Check.Equal(30, fixture.Store.LoadFile().RequestHistory.RetentionDays, "Nothing saved");
                        Check.Equal(30, fixture.Running.RequestHistory.RetentionDays, "Nothing applied");
                    }
                }),
                CaseAsync("Service.SecretRotation", "Rotating the admin token applies live", async ct =>
                {
                    using (SettingsFixture fixture = SettingsFixture.Create(name => null))
                    {
                        string rotated = SettingsStore.GenerateToken();
                        await fixture.Service.UpdateAsync(Request(new Dictionary<string, object> { { "auth.adminToken", rotated } }), ct).ConfigureAwait(false);
                        Check.Equal(rotated, fixture.Running.Auth.AdminToken, "Running token");
                        Check.Equal(rotated, fixture.Store.LoadFile().Auth.AdminToken, "Saved token");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit" });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return Case(caseId, name, body);
        }

        private static string NewPath()
        {
            return Path.Combine(TestEnvironment.TempRoot, "settings-" + Guid.NewGuid().ToString("N") + ".json");
        }

        private static SettingsUpdateRequest Request(Dictionary<string, object> values)
        {
            SettingsUpdateRequest request = new SettingsUpdateRequest();
            foreach (KeyValuePair<string, object> pair in values)
            {
                request.Values[pair.Key] = JsonSerializer.SerializeToElement(pair.Value);
            }
            return request;
        }

        #endregion
    }
}
