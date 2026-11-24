namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;
    using Xunit;

    /// <summary>
    /// Phase 4 infrastructure: document media headers on the media CDN, the opt-in download rendition of the job
    /// template, opt-in Amazon Location resources, and the optional Partio settings.
    /// </summary>
    public class DocumentsLocationAndPartioTests
    {
        private static readonly Lazy<ConfiguredEnvironment> _Configured = new Lazy<ConfiguredEnvironment>(() => new ConfiguredEnvironment("dev", config =>
        {
            config.Location.PlaceIndexEnabled = true;
            config.Location.MapEnabled = true;
            config.Location.DataProvider = "Here";
            config.Partio.BaseUrl = "https://partio.example.test";
        }));

        /// <summary>
        /// PDFs (signed and public copies) get Content-Disposition attachment, Content-Security-Policy sandbox, and
        /// nosniff; the public copy needs no signed cookies.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void DocumentsDownloadSandboxed(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            KeyValuePair<string, JsonElement> policy = env.Json(env.Stacks.Media).GetProperty("Resources").EnumerateObject()
                .Where(r => r.Value.GetProperty("Type").GetString() == "AWS::CloudFront::ResponseHeadersPolicy")
                .Select(r => new KeyValuePair<string, JsonElement>(r.Name, r.Value.GetProperty("Properties").GetProperty("ResponseHeadersPolicyConfig")))
                .Single(r => r.Value.GetProperty("Name").GetString() == env.Stacks.Config.ResourceName("media-documents"));
            JsonElement header = Assert.Single(policy.Value.GetProperty("CustomHeadersConfig").GetProperty("Items").EnumerateArray());
            Assert.Equal("Content-Disposition", header.GetProperty("Header").GetString());
            Assert.Equal("attachment", header.GetProperty("Value").GetString());
            Assert.True(header.GetProperty("Override").GetBoolean());
            JsonElement security = policy.Value.GetProperty("SecurityHeadersConfig");
            Assert.Equal("sandbox", security.GetProperty("ContentSecurityPolicy").GetProperty("ContentSecurityPolicy").GetString());
            Assert.True(security.GetProperty("ContentTypeOptions").GetProperty("Override").GetBoolean());

            JsonElement config = TemplateReader.DistributionConfig(env, env.Stacks.Media);
            JsonElement signed = TemplateReader.Behavior(config, "/*/media/*.pdf");
            JsonElement open = TemplateReader.Behavior(config, "/*/public/media/*.pdf");
            Assert.Equal(policy.Key, signed.GetProperty("ResponseHeadersPolicyId").GetProperty("Ref").GetString());
            Assert.Equal(policy.Key, open.GetProperty("ResponseHeadersPolicyId").GetProperty("Ref").GetString());
            Assert.True(signed.TryGetProperty("TrustedKeyGroups", out JsonElement _));
            Assert.False(open.TryGetProperty("TrustedKeyGroups", out JsonElement _));
        }

        /// <summary>
        /// The download rendition is opt-in and appended as output group 2 (download_720p.mp4); groups 0 and 1 stay HLS
        /// and the poster.
        /// </summary>
        [Fact]
        public void DownloadRenditionIsAppendedAfterHlsAndPoster()
        {
            MediaSettings media = new MediaSettings();
            Assert.False(media.DownloadRendition);
            Assert.Equal(2, ((object[])MediaConvertJobTemplate.BuildSettings(media)["OutputGroups"]).Length);

            media.DownloadRendition = true;
            object[] groups = (object[])MediaConvertJobTemplate.BuildSettings(media)["OutputGroups"];
            Assert.Equal(3, groups.Length);
            Assert.Equal(MediaConvertJobTemplate.HlsGroupName, ((Dictionary<string, object>)groups[0])["Name"]);
            Assert.Equal(MediaConvertJobTemplate.PosterGroupName, ((Dictionary<string, object>)groups[1])["Name"]);
            Dictionary<string, object> download = (Dictionary<string, object>)groups[2];
            Assert.Equal(MediaConvertJobTemplate.DownloadGroupName, download["Name"]);
            Dictionary<string, object> output = (Dictionary<string, object>)Assert.Single((object[])download["Outputs"]);
            Assert.Equal("download_720p.mp4", "download" + output["NameModifier"] + "." + output["Extension"]);
            Assert.Equal("MP4", ((Dictionary<string, object>)output["ContainerSettings"])["Container"]);
            Assert.Equal(720, ((Dictionary<string, object>)output["VideoDescription"])["Height"]);
            Assert.DoesNotContain("Destination", JsonSerializer.Serialize(groups));
        }

        /// <summary>
        /// Without a location section no Location resource or geo permission exists.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void LocationIsOffByDefault(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            foreach (Stack stack in env.Stacks.All)
            {
                foreach (JsonProperty resource in env.Json(stack).GetProperty("Resources").EnumerateObject())
                {
                    Assert.False((resource.Value.GetProperty("Type").GetString() ?? String.Empty).StartsWith("AWS::Location::", StringComparison.Ordinal));
                }
            }

            Assert.DoesNotContain("geo:", String.Join("\n", env.Resources(env.Stacks.Api, "AWS::IAM::Policy").Select(p => p.GetRawText())));
            Assert.Empty(env.Stacks.Api!.Location);
        }

        /// <summary>
        /// An opted-in property gets a place index (configured provider, Storage) and a map, with exactly the search and
        /// map actions on those resources, and outputs naming the admin settings.
        /// </summary>
        [Fact]
        public void LocationResourcesAndPermissions()
        {
            ConfiguredEnvironment env = _Configured.Value;
            EnvironmentConfig config = env.Stacks.Config;
            JsonElement index = env.Resources(env.Stacks.Api, "AWS::Location::PlaceIndex").Single().GetProperty("Properties");
            Assert.Equal(config.ResourceName("places"), index.GetProperty("IndexName").GetString());
            Assert.Equal("Here", index.GetProperty("DataSource").GetString());
            Assert.Equal("Storage", index.GetProperty("DataSourceConfiguration").GetProperty("IntendedUse").GetString());
            JsonElement map = env.Resources(env.Stacks.Api, "AWS::Location::Map").Single().GetProperty("Properties");
            Assert.Equal(config.ResourceName("map"), map.GetProperty("MapName").GetString());
            Assert.Equal("VectorEsriNavigation", map.GetProperty("Configuration").GetProperty("Style").GetString());

            List<JsonElement> statements = env.Resources(env.Stacks.Api, "AWS::IAM::Policy")
                .SelectMany(p => p.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement").EnumerateArray()).ToList();
            JsonElement search = statements.Single(s => s.TryGetProperty("Sid", out JsonElement sid) && sid.GetString() == "LocationSearch");
            Assert.Equal("geo:SearchPlaceIndexForText", search.GetProperty("Action").GetString());
            Assert.Contains("PlaceIndex", search.GetProperty("Resource").GetRawText());
            JsonElement mapStatement = statements.Single(s => s.TryGetProperty("Sid", out JsonElement sid) && sid.GetString() == "LocationMap");
            List<string> actions = mapStatement.GetProperty("Action").EnumerateArray().Select(a => a.GetString() ?? String.Empty).ToList();
            Assert.Equal(LocationResources.MapActions.ToList(), actions);
            Assert.All(actions, a => Assert.StartsWith("geo:GetMap", a));

            JsonElement outputs = env.Json(env.Stacks.Api).GetProperty("Outputs");
            Assert.Equal(config.ResourceName("places"), outputs.GetProperty("PlaceIndexName").GetProperty("Value").GetString());
            Assert.Contains("directory.geocoder.placeIndexName", outputs.GetProperty("PlaceIndexName").GetProperty("Description").GetString());
            Assert.Contains("/maps/" + config.ResourceName("map") + "/style-descriptor", outputs.GetProperty("MapStyleUrl").GetProperty("Value").GetString());

            // No directory setting is ever passed as an environment variable.
            Assert.DoesNotContain(TemplateReader.ServerEnvironment(env, env.Stacks.Api).Keys, k => k.Contains("GEOCODER", StringComparison.Ordinal) || k.Contains("MAP_", StringComparison.Ordinal));
        }

        /// <summary>
        /// A place index without a map grants no map actions.
        /// </summary>
        [Fact]
        public void PlaceIndexAloneGrantsNoMapActions()
        {
            ConfiguredEnvironment env = new ConfiguredEnvironment("prod", config => config.Location.PlaceIndexEnabled = true);
            string policies = String.Join("\n", env.Resources(env.Stacks.Api, "AWS::IAM::Policy").Select(p => p.GetRawText()));
            Assert.Contains("geo:SearchPlaceIndexForText", policies);
            Assert.DoesNotContain("geo:GetMap", policies);
            Assert.Empty(env.Resources(env.Stacks.Api, "AWS::Location::Map"));
        }

        /// <summary>
        /// The location section is parsed and validated.
        /// </summary>
        [Fact]
        public void LocationSettingsAreValidated()
        {
            using (JsonDocument document = JsonDocument.Parse("{\"location\":{\"placeIndex\":true,\"dataProvider\":\"Grab\",\"map\":false}}"))
            {
                LocationSettings parsed = LocationSettings.FromJson(document.RootElement, "infra.json");
                Assert.True(parsed.PlaceIndexEnabled);
                Assert.False(parsed.MapEnabled);
                Assert.Equal("Grab", parsed.DataProvider);
                Assert.Equal("Storage", parsed.IntendedUse);
            }

            using (JsonDocument document = JsonDocument.Parse("{\"location\":{\"dataProvider\":\"Nominatim\"}}"))
            {
                JsonElement root = document.RootElement;
                Assert.Throws<ArgumentException>(() => LocationSettings.FromJson(root, "infra.json"));
            }

            using (JsonDocument document = JsonDocument.Parse("{\"location\":{\"placeIndex\":\"yes\"}}"))
            {
                JsonElement root = document.RootElement;
                Assert.Throws<ArgumentException>(() => LocationSettings.FromJson(root, "infra.json"));
            }

            LocationSettings settings = new LocationSettings();
            Assert.False(settings.AnyEnabled);
            Assert.Throws<ArgumentException>(() => settings.IntendedUse = "Forever");
            Assert.Throws<ArgumentException>(() => settings.MapStyle = "not a style");
        }

        /// <summary>
        /// Partio is emitted only when configured: the base URL as a variable and the key from {prefix}/partio.
        /// </summary>
        [Fact]
        public void PartioIsEmittedOnlyWhenConfigured()
        {
            SynthesizedEnvironment plain = Environments.Dev;
            Assert.DoesNotContain(ServerEnvironmentKeys.PartioBaseUrl, TemplateReader.ServerEnvironment(plain, plain.Stacks.Api).Keys);
            Assert.DoesNotContain(ServerEnvironmentKeys.PartioApiKey, TemplateReader.ServerSecrets(plain, plain.Stacks.Api));
            Assert.Null(plain.Stacks.Api!.PartioSecret);

            ConfiguredEnvironment env = _Configured.Value;
            Assert.Equal("https://partio.example.test", TemplateReader.ServerEnvironment(env, env.Stacks.Api)[ServerEnvironmentKeys.PartioBaseUrl]);
            Assert.Contains(ServerEnvironmentKeys.PartioApiKey, TemplateReader.ServerSecrets(env, env.Stacks.Api));
            Assert.DoesNotContain(ServerEnvironmentKeys.PartioApiKey, TemplateReader.ServerEnvironment(env, env.Stacks.Api).Keys);
            Assert.Contains(env.Resources(env.Stacks.Api, "AWS::SecretsManager::Secret"),
                s => s.GetProperty("Properties").GetProperty("Name").GetString() == env.Stacks.Config.ResourcePath("partio"));

            Assert.Throws<ArgumentException>(() => new PartioSettings().BaseUrl = "http://partio.example.test");
            using (JsonDocument document = JsonDocument.Parse("{\"partio\":{\"baseUrl\":\"https://partio.example.test/\"}}"))
            {
                Assert.True(PartioSettings.FromJson(document.RootElement, "infra.json").Enabled);
            }
        }
    }
}
