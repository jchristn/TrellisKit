namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Helpers for MultiProperty tests: the platform host and bootstrap administrator, platform calls, and a second
    /// test-local property (Second Example, second.test, test.second.trelliskit) registered through the platform API
    /// from the sample document. Never a real property.
    /// </summary>
    internal static class MultiPropertyKit
    {
        internal const string PlatformHost = "platform.test";

        internal const string PlatformEmail = "root@platform.test";

        internal const string PlatformPassword = "platform horse battery";

        internal const string SecondWeb = "second.test";

        internal const string SecondApi = "api.second.test";

        internal const string SecondIosApp = "test.second.trelliskit";

        internal const string SecondAndroidApp = "test.second.trelliskit.android";

        /// <summary>
        /// Configure a MultiProperty deployment with the platform host and a bootstrap administrator with a password.
        /// </summary>
        internal static void Multi(AppSettings settings)
        {
            settings.Deployment.Mode = DeploymentModeEnum.MultiProperty;
            settings.Platform.Hosts = new List<string> { PlatformHost };
            settings.Platform.BootstrapAdminEmail = PlatformEmail;
            settings.Platform.BootstrapAdminPassword = PlatformPassword;
        }

        /// <summary>
        /// Send a platform request (platform host, no property header).
        /// </summary>
        internal static async Task<ApiResult> PlatformAsync(HttpFixture f, HttpMethod method, string path, string? bearer, object? body, CancellationToken ct)
        {
            string? property = f.PropertyId;
            string? host = f.HostHeader;
            f.PropertyId = null;
            f.HostHeader = PlatformHost;
            try
            {
                return await Api.SendAsync(f, method, path, bearer, body, ct, "web").ConfigureAwait(false);
            }
            finally
            {
                f.PropertyId = property;
                f.HostHeader = host;
            }
        }

        /// <summary>
        /// Send a platform request and check its status.
        /// </summary>
        internal static async Task<JsonElement> PlatformExpectAsync(HttpFixture f, HttpMethod method, string path, string? bearer, object? body, HttpStatusCode expected, CancellationToken ct)
        {
            ApiResult result = await PlatformAsync(f, method, path, bearer, body, ct).ConfigureAwait(false);
            Check.Equal(expected, result.Status, method + " " + path + " status (body: " + result.Text + ")");
            return result.Body;
        }

        /// <summary>
        /// Sign the bootstrap platform administrator in.
        /// </summary>
        internal static async Task<string> PlatformLoginAsync(HttpFixture f, CancellationToken ct)
        {
            JsonElement session = await PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/auth/login", null, new { email = PlatformEmail, password = PlatformPassword }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return Api.Str(session, "accessToken");
        }

        /// <summary>
        /// The sample property document with the second property's name, slug, domains, and app ids.
        /// </summary>
        internal static JsonObject SecondDocument()
        {
            JsonObject document = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(TestProperty.SampleDirectory, PropertyLoader.PropertyFileName)))!;
            document["name"] = "Second Example";
            document["shortName"] = "Second";
            document["slug"] = "second";
            document["domains"] = new JsonObject { ["web"] = SecondWeb, ["api"] = SecondApi, ["media"] = "media.second.test", ["redirects"] = new JsonArray() };
            JsonObject mobile = (JsonObject)document["mobile"]!;
            mobile["bundleId"] = SecondIosApp;
            mobile["androidPackage"] = SecondAndroidApp;
            return document;
        }

        /// <summary>
        /// Register and enable the second property; returns its generated id.
        /// </summary>
        internal static async Task<string> CreateSecondAsync(HttpFixture f, string platformToken, CancellationToken ct)
        {
            JsonElement created = await PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties", platformToken,
                new { document = SecondDocument(), labels = new[] { "test-fixture" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string id = Api.Str(created, "id");
            Check.True(TrellisKit.Core.Helpers.PropertyIds.IsValid(id), "Generated opaque id " + id);
            Check.Equal("Disabled", Api.Str(created, "status"), "New properties start Disabled");
            await PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + id + "/enable", platformToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return id;
        }
    }
}
