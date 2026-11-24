namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>
    /// Offline suites for the external integrations (identity providers, app stores, Stripe, and SMTP).
    /// </summary>
    public static class IntegrationSuites
    {
        #region Public-Members

        /// <summary>
        /// All integration suites, with fresh descriptors on each access.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new[]
                {
                    AppleSignInSuite.Build(),
                    GoogleSignInSuite.Build(),
                    FacebookSignInSuite.Build(),
                    AppStoreSuite.Build(),
                    GooglePlaySuite.Build(),
                    StripeSuite.Build(),
                    EmailSuite.Build(),
                    ExpoPushSuite.Build()
                };
            }
        }

        #endregion
    }
}
