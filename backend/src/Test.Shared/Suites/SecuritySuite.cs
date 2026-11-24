namespace Test.Shared.Suites
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Touchstone.Core;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Admin token authentication and header redaction.
    /// </summary>
    public static class SecuritySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Security";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Authentication and redaction", new[]
            {
                Case("AdminToken.Accepts", "The configured bearer token authenticates as admin", ct =>
                {
                    using (LoggingModule module = TestLogging.CreateModule())
                    {
                        AuthenticationService auth = new AuthenticationService(new AuthSettings { AdminToken = "tk_token_for_tests_0000000000000001" }, null, new AuthorizationService(TestProperty.LoadSample().Membership.MinimumAge), new AppLogger(module));
                        RequestContext? context = auth.Authenticate("Bearer tk_token_for_tests_0000000000000001");
                        Check.True(context != null && context.IsAuthenticated && context.IsAdmin, "Authenticated admin");
                        Check.Equal("AdminToken", context!.Scheme, "Scheme");
                        Check.True(auth.Authenticate("bearer   tk_token_for_tests_0000000000000001  ") != null, "Scheme is case-insensitive and whitespace tolerant");
                    }
                    return Task.CompletedTask;
                }),
                Case("AdminToken.Rejects", "Wrong, missing, malformed, and empty-configured tokens are rejected", ct =>
                {
                    using (LoggingModule module = TestLogging.CreateModule())
                    {
                        AuthSettings settings = new AuthSettings { AdminToken = "tk_token_for_tests_0000000000000001" };
                        AuthenticationService auth = new AuthenticationService(settings, null, new AuthorizationService(TestProperty.LoadSample().Membership.MinimumAge), new AppLogger(module));
                        Check.True(auth.Authenticate(null) == null, "Missing header");
                        Check.True(auth.Authenticate("Bearer ") == null, "Empty token");
                        Check.True(auth.Authenticate("Basic abc") == null, "Wrong scheme");
                        Check.True(auth.Authenticate("Bearer tk_token_for_tests_0000000000000002") == null, "Wrong token");
                        Check.True(auth.Authenticate("tk_token_for_tests_0000000000000001") == null, "No scheme");
                        settings.AdminToken = String.Empty;
                        Check.True(auth.Authenticate("Bearer ") == null, "Empty configured token never matches");
                    }
                    return Task.CompletedTask;
                }),
                Case("AdminToken.RotationIsLive", "A rotated token takes effect without restarting the service", ct =>
                {
                    using (LoggingModule module = TestLogging.CreateModule())
                    {
                        AuthSettings settings = new AuthSettings { AdminToken = "tk_old_token_000000000000000000001" };
                        AuthenticationService auth = new AuthenticationService(settings, null, new AuthorizationService(TestProperty.LoadSample().Membership.MinimumAge), new AppLogger(module));
                        settings.AdminToken = "tk_new_token_000000000000000000001";
                        Check.True(auth.Authenticate("Bearer tk_old_token_000000000000000000001") == null, "Old token rejected");
                        Check.True(auth.Authenticate("Bearer tk_new_token_000000000000000000001") != null, "New token accepted");
                    }
                    return Task.CompletedTask;
                }),
                Case("Redaction.Headers", "Secret-bearing headers are redacted from request history", ct =>
                {
                    foreach (string name in new[] { "Authorization", "authorization", "Cookie", "Set-Cookie", "Proxy-Authorization", "X-Api-Key", "x-apikey", "X-Refresh-Token", "X-Password", "X-Client-Secret" })
                    {
                        Check.True(RequestHistoryCaptureService.ShouldRedact(name), name + " must be redacted");
                    }
                    foreach (string name in new[] { "Content-Type", "User-Agent", "Accept", "X-Request-Id" })
                    {
                        Check.True(!RequestHistoryCaptureService.ShouldRedact(name), name + " must not be redacted");
                    }
                    return Task.CompletedTask;
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit", "security" });
        }

        #endregion
    }
}
