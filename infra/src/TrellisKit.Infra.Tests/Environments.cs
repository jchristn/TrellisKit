namespace TrellisKit.Infra.Tests
{
    using System;
    using System.IO;
    using System.Text.Json.Nodes;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// Lazily synthesized environments shared by every test (synthesis takes several seconds). dev and prod use the
    /// engine's neutral sample property (the default). dev-redirect and prod-redirect use a test-local copy of the
    /// sample with a vanity domain and a deploy/infra.json (accounts, alarm address, GitHub, Mailgun mail), to cover the redirect stack and property deploy settings.
    /// shared-dev and shared-prod synthesize the shared topology fixture (infra/fixtures/shared): the sample property
    /// (no vanity domain) plus a second fixture property with a vanity domain behind one shared API.
    /// </summary>
    public static class Environments
    {
        /// <summary>
        /// Vanity domain of the test-local redirect property.
        /// </summary>
        public static readonly string VanityDomain = "example-vanity.test";

        /// <summary>
        /// GitHub owner in the test-local deploy/infra.json.
        /// </summary>
        public static readonly string FixtureGitHubOwner = "fixture-owner";

        /// <summary>
        /// GitHub repository in the test-local deploy/infra.json.
        /// </summary>
        public static readonly string FixtureGitHubRepository = "fixture-repository";

        /// <summary>
        /// Production account in the test-local deploy/infra.json.
        /// </summary>
        public static readonly string FixtureProdAccount = "444444444444";

        /// <summary>
        /// Mailgun sending domain in the test-local deploy/infra.json mail section.
        /// </summary>
        public static readonly string FixtureMailgunDomain = "mg.example-fixture.test";

        /// <summary>
        /// Mailgun API base URL (EU) in the test-local deploy/infra.json mail section.
        /// </summary>
        public static readonly string FixtureMailgunBaseUrl = "https://api.eu.mailgun.net/v3/";

        /// <summary>
        /// The one account dev and prod share in the single-account fixtures (dev-single, prod-single, and the external
        /// DNS shared deployment).
        /// </summary>
        public static readonly string SingleAccount = "333333333333";

        /// <summary>
        /// Vanity domain of the single-account, external DNS property.
        /// </summary>
        public static readonly string SingleAccountVanityDomain = "example-single-vanity.test";

        /// <summary>
        /// Shared topology fixture, relative to the engine root (resolved like the deployment context value).
        /// </summary>
        public static readonly string SharedDeploymentFile = "infra/fixtures/shared/deployment.json";

        private static readonly Lazy<SynthesizedDeployment> _SharedDev = new Lazy<SynthesizedDeployment>(() => new SynthesizedDeployment("dev", SharedDeploymentFile));
        private static readonly Lazy<SynthesizedDeployment> _SharedProd = new Lazy<SynthesizedDeployment>(() => new SynthesizedDeployment("prod", SharedDeploymentFile));
        private static readonly Lazy<string> _RedirectPropertyDir = new Lazy<string>(CreateRedirectProperty);
        private static readonly Lazy<SynthesizedEnvironment> _Dev = new Lazy<SynthesizedEnvironment>(() => new SynthesizedEnvironment("dev", null));
        private static readonly Lazy<SynthesizedEnvironment> _Prod = new Lazy<SynthesizedEnvironment>(() => new SynthesizedEnvironment("prod", null));
        private static readonly Lazy<SynthesizedEnvironment> _DevRedirect = new Lazy<SynthesizedEnvironment>(() => new SynthesizedEnvironment("dev", RedirectPropertyDir));
        private static readonly Lazy<SynthesizedEnvironment> _ProdRedirect = new Lazy<SynthesizedEnvironment>(() => new SynthesizedEnvironment("prod", RedirectPropertyDir));
        private static readonly Lazy<string> _SingleAccountPropertyDir = new Lazy<string>(CreateSingleAccountProperty);
        private static readonly Lazy<SynthesizedEnvironment> _DevSingle = new Lazy<SynthesizedEnvironment>(() => new SynthesizedEnvironment("dev", SingleAccountPropertyDir));
        private static readonly Lazy<SynthesizedEnvironment> _ProdSingle = new Lazy<SynthesizedEnvironment>(() => new SynthesizedEnvironment("prod", SingleAccountPropertyDir));
        private static readonly Lazy<string> _ExternalSharedDeploymentFile = new Lazy<string>(CreateExternalSharedDeployment);
        private static readonly Lazy<SynthesizedDeployment> _SharedExternalDev = new Lazy<SynthesizedDeployment>(() => new SynthesizedDeployment("dev", ExternalSharedDeploymentFile));
        private static readonly Lazy<SynthesizedDeployment> _SharedExternalProd = new Lazy<SynthesizedDeployment>(() => new SynthesizedDeployment("prod", ExternalSharedDeploymentFile));

        /// <summary>
        /// The dev environment of the sample property.
        /// </summary>
        public static SynthesizedEnvironment Dev
        {
            get { return _Dev.Value; }
        }

        /// <summary>
        /// The prod environment of the sample property.
        /// </summary>
        public static SynthesizedEnvironment Prod
        {
            get { return _Prod.Value; }
        }

        /// <summary>
        /// Directory of the test-local property with a vanity domain and deploy settings.
        /// </summary>
        public static string RedirectPropertyDir
        {
            get { return _RedirectPropertyDir.Value; }
        }

        /// <summary>
        /// Directory of the test-local property whose dev and prod share one account and whose DNS is external (a copy of
        /// the sample with a vanity domain).
        /// </summary>
        public static string SingleAccountPropertyDir
        {
            get { return _SingleAccountPropertyDir.Value; }
        }

        /// <summary>
        /// Deployment file of the shared topology fixture with external DNS and one account for dev and prod.
        /// </summary>
        public static string ExternalSharedDeploymentFile
        {
            get { return _ExternalSharedDeploymentFile.Value; }
        }

        /// <summary>
        /// Look up an environment by name.
        /// </summary>
        /// <param name="name">dev, prod, dev-redirect, prod-redirect, dev-single, or prod-single.</param>
        /// <returns>The environment.</returns>
        /// <exception cref="ArgumentException">The name is unknown.</exception>
        public static SynthesizedEnvironment Get(string name)
        {
            if (name == "dev") return Dev;
            if (name == "prod") return Prod;
            if (name == "dev-redirect") return _DevRedirect.Value;
            if (name == "prod-redirect") return _ProdRedirect.Value;
            if (name == "dev-single") return _DevSingle.Value;
            if (name == "prod-single") return _ProdSingle.Value;
            throw new ArgumentException("Unknown environment " + name, nameof(name));
        }

        /// <summary>
        /// Look up a synthesized shared deployment by name.
        /// </summary>
        /// <param name="name">shared-dev, shared-prod, shared-external-dev, or shared-external-prod.</param>
        /// <returns>The deployment.</returns>
        /// <exception cref="ArgumentException">The name is unknown.</exception>
        public static SynthesizedDeployment GetShared(string name)
        {
            if (name == "shared-dev") return _SharedDev.Value;
            if (name == "shared-prod") return _SharedProd.Value;
            if (name == "shared-external-dev") return _SharedExternalDev.Value;
            if (name == "shared-external-prod") return _SharedExternalProd.Value;
            throw new ArgumentException("Unknown shared deployment " + name, nameof(name));
        }

        private static string CreateRedirectProperty()
        {
            string sample = PropertyDefinition.ResolveDirectory(null);
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "deploy"));

            JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(sample, PropertyDefinition.PropertyFileName)))!;
            root["domains"]!["redirects"] = new JsonArray(VanityDomain);
            File.WriteAllText(Path.Combine(directory, PropertyDefinition.PropertyFileName), root.ToJsonString());

            JsonObject deploy = new JsonObject
            {
                ["accounts"] = new JsonObject { ["prod"] = FixtureProdAccount },
                ["alarmEmail"] = "alarms@example.test",
                ["github"] = new JsonObject { ["owner"] = FixtureGitHubOwner, ["repository"] = FixtureGitHubRepository },
                ["mail"] = new JsonObject { ["provider"] = "Mailgun", ["mailgunDomain"] = FixtureMailgunDomain, ["mailgunBaseUrl"] = FixtureMailgunBaseUrl }
            };
            File.WriteAllText(Path.Combine(directory, PropertyDefinition.DeployFileName), deploy.ToJsonString());
            return directory;
        }

        private static string CreateSingleAccountProperty()
        {
            string sample = PropertyDefinition.ResolveDirectory(null);
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-single-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "deploy"));

            JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(sample, PropertyDefinition.PropertyFileName)))!;
            root["domains"]!["redirects"] = new JsonArray(SingleAccountVanityDomain);
            File.WriteAllText(Path.Combine(directory, PropertyDefinition.PropertyFileName), root.ToJsonString());

            JsonObject deploy = new JsonObject
            {
                ["accounts"] = new JsonObject { ["dev"] = SingleAccount, ["prod"] = SingleAccount },
                ["alarmEmail"] = "alarms@example.test",
                ["dns"] = new JsonObject { ["mode"] = "External" },
                ["github"] = new JsonObject { ["owner"] = FixtureGitHubOwner, ["repository"] = FixtureGitHubRepository },
                ["mail"] = new JsonObject { ["mailgunDomain"] = FixtureMailgunDomain }
            };
            File.WriteAllText(Path.Combine(directory, PropertyDefinition.DeployFileName), deploy.ToJsonString());
            return directory;
        }

        private static string CreateExternalSharedDeployment()
        {
            string fixtures = Path.Combine(PropertyDefinition.EngineRoot(), "infra", "fixtures", "shared");
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-shared-external-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            JsonObject deployment = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtures, "deployment.json")))!.AsObject();
            deployment["id"] = "fixture-external";
            deployment["properties"] = new JsonArray(PropertyDefinition.ResolveDirectory(null), Path.Combine(fixtures, "second"));
            deployment["accounts"] = new JsonObject { ["dev"] = SingleAccount, ["prod"] = SingleAccount };
            deployment["dns"] = new JsonObject { ["mode"] = "External" };
            string file = Path.Combine(directory, "deployment.json");
            File.WriteAllText(file, deployment.ToJsonString());
            return file;
        }
    }
}
