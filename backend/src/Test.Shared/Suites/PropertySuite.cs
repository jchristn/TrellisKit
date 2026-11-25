namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Property loading and validation: the sample property loads, the directory resolves by path, every validation
    /// rule refuses a broken file with a message naming the problem, and brand asset names are traversal safe.
    /// </summary>
    public static class PropertySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Property";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                SuiteCases.Unit(Id, "Loader.Sample", "The sample property loads and validates with its brand files", ct =>
                {
                    PropertyConfiguration property = TestProperty.LoadSample();
                    Check.Equal(TestProperty.SampleId, property.Id, "Id");
                    Check.Equal("example", property.EffectiveSlug, "Slug");
                    Check.True(property.Name.Length > 0 && property.Membership.PriceUsdCents > 0, "Name and price");
                    Check.True(property.Profile.Rank.Enabled && property.Profile.Rank.Values.Count > 0, "Ranks");
                    Check.True(Directory.Exists(property.Directory), "Directory recorded");
                    Check.Equal(0, PropertyValidator.Validate(property).Count, "No problems");
                    Check.True(property.FindRank(property.Profile.Rank.Values[0].Key.ToUpperInvariant())?.Key == property.Profile.Rank.Values[0].Key, "Rank lookup is case-insensitive and canonical");
                    Check.True(property.FindRank("NoSuchRank") == null && property.FindGoal(null) == null, "Unknown keys");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Mobile.NavigationRoundTrip", "Configured navigation survives property parsing and public response serialization; omitted configuration stays null", ct =>
                {
                    PropertyConfiguration property = TestProperty.LoadSample();
                    Check.True(property.Mobile.Navigation != null, "Sample declares generic navigation");
                    Check.Equal("members", property.Mobile.Navigation!.DrawerItems![2], "Members is a top-level destination");
                    PublicPropertyResponse response = new PublicPropertyResponse { Mobile = property.Mobile };
                    JsonElement serialized = JsonSerializer.SerializeToElement(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    JsonElement navigation = serialized.GetProperty("mobile").GetProperty("navigation");
                    Check.Equal(4, navigation.GetProperty("shortcuts").GetArrayLength(), "Four default shortcuts");
                    JsonObject document = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(TestProperty.SampleDirectory, PropertyLoader.PropertyFileName)))!;
                    Obj(document, "mobile").Remove("navigation");
                    PropertyConfiguration legacy = PropertyLoader.Parse(document.ToJsonString(), "test");
                    Check.True(legacy.Mobile.Navigation == null, "Legacy property leaves client defaults intact");
                    Check.True(new PropertyMobileNavigation().AccountMenu == null && new PropertyMobileNavigation().Labels == null, "Missing account menu and labels are not empty values");
                    Obj(document, "mobile")["navigation"] = new JsonObject
                    {
                        ["drawerItems"] = new JsonArray("forums", "members"),
                        ["shortcuts"] = new JsonArray("feed", "directory", "events", "messages"),
                        ["accountMenu"] = new JsonArray("profile", "settings", "about"),
                        ["labels"] = new JsonObject { ["directory"] = "Explore" }
                    };
                    PropertyConfiguration placed = PropertyLoader.Parse(document.ToJsonString(), "test");
                    Check.Equal(0, PropertyValidator.Validate(placed).Count, "Account menu and labels accepted");
                    JsonElement placedNavigation = JsonSerializer.SerializeToElement(new PublicPropertyResponse { Mobile = placed.Mobile }, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("mobile").GetProperty("navigation");
                    Check.Equal("settings", placedNavigation.GetProperty("accountMenu")[1].GetString(), "Account menu order survives serialization");
                    Check.Equal("Explore", placedNavigation.GetProperty("labels").GetProperty("directory").GetString(), "Label overrides keep their destination keys");
                    property.Mobile.Navigation.Shortcuts = new List<string>();
                    Check.Equal(0, PropertyValidator.Validate(property).Count, "Drawer-only navigation is valid");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Loader.Resolve", "The directory resolves from an absolute path, a relative path searched upward, or the sample default", ct =>
                {
                    string sample = TestProperty.SampleDirectory;
                    string[] roots = new[] { AppContext.BaseDirectory };
                    Check.Equal(sample, PropertyLoader.ResolveDirectory(null, roots), "Default is the sample property");
                    Check.Equal(sample, PropertyLoader.ResolveDirectory(sample, roots), "Absolute path");
                    Check.Equal(sample, PropertyLoader.ResolveDirectory(PropertyLoader.SampleRelativePath, roots), "Relative path found upward");
                    string missing = Path.Combine(TestEnvironment.TempRoot, "no-such-property-" + Guid.NewGuid().ToString("N"));
                    PropertyConfigurationException absent = Check.Throws<PropertyConfigurationException>(() => PropertyLoader.ResolveDirectory(missing, roots), "Missing absolute");
                    Check.True(absent.Message.Contains(missing, StringComparison.Ordinal) && absent.Message.Contains("TK_PROPERTY_DIR", StringComparison.Ordinal), "Message names the path and the variable");
                    Check.Throws<PropertyConfigurationException>(() => PropertyLoader.ResolveDirectory("no/such/relative/property", roots), "Missing relative");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Loader.FileErrors", "A missing file, an empty file, and invalid JSON are refused with the file name", ct =>
                {
                    string directory = TestProperty.CopySample();
                    try
                    {
                        string file = Path.Combine(directory, PropertyLoader.PropertyFileName);
                        File.WriteAllText(file, "{ not json");
                        PropertyConfigurationException invalid = Check.Throws<PropertyConfigurationException>(() => PropertyLoader.Load(directory), "Invalid JSON");
                        Check.True(invalid.Message.Contains("not valid JSON", StringComparison.Ordinal), "Invalid JSON message");
                        File.WriteAllText(file, "   ");
                        Check.Throws<PropertyConfigurationException>(() => PropertyLoader.Load(directory), "Empty file");
                        File.Delete(file);
                        PropertyConfigurationException missing = Check.Throws<PropertyConfigurationException>(() => PropertyLoader.Load(directory), "Missing file");
                        Check.True(missing.Message.Contains(file, StringComparison.Ordinal), "Missing file named");
                    }
                    finally
                    {
                        TestProperty.Delete(directory);
                    }
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Loader.MissingBrandFile", "A brand image named in property.json but absent from brand/ is refused", ct =>
                {
                    string directory = TestProperty.CopySample();
                    try
                    {
                        File.Delete(Path.Combine(directory, PropertyLoader.BrandFolder, "icon.png"));
                        PropertyConfigurationException e = Check.Throws<PropertyConfigurationException>(() => PropertyLoader.Load(directory), "Missing icon");
                        Check.True(e.Message.Contains("brand.icon", StringComparison.Ordinal), "Problem names brand.icon: " + e.Message);
                    }
                    finally
                    {
                        TestProperty.Delete(directory);
                    }
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Assets.TraversalSafe", "Only plain file names directly under brand/ resolve", ct =>
                {
                    PropertyConfiguration property = TestProperty.LoadSample();
                    Check.True(PropertyLoader.ResolveAsset(property, "logo.png") != null, "Logo resolves");
                    foreach (string name in new[] { "../property.json", "..", ".", "..\\property.json", "brand/logo.png", "/etc/passwd", ".hidden", "logo.png/..", "a..png", "", "missing.png", "%2e%2e" })
                        Check.True(PropertyLoader.ResolveAsset(property, name) == null, "Rejected: \"" + name + "\"");
                    Check.True(PropertyLoader.IsSafeAssetName("icon.ico") && !PropertyLoader.IsSafeAssetName("../icon.ico"), "Safe names");
                    return Task.CompletedTask;
                })
            };

            foreach (KeyValuePair<string, Action<JsonObject>> failure in Failures())
            {
                string expected = failure.Key;
                Action<JsonObject> mutate = failure.Value;
                cases.Add(SuiteCases.Unit(Id, "Validator." + Slug(expected), "property.json with a bad " + expected + " is refused naming it", ct =>
                {
                    ExpectRejected(mutate, expected);
                    return Task.CompletedTask;
                }));
            }

            return new TestSuiteDescriptor(Id, "Property configuration", cases);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, Action<JsonObject>> Failures()
        {
            return new Dictionary<string, Action<JsonObject>>
            {
                ["mobile.navigation.drawerItems[1]"] = j => Obj(Obj(j, "mobile"), "navigation")["drawerItems"] = new JsonArray("members", "members"),
                ["mobile.navigation.drawerItems[0]"] = j => Obj(Obj(j, "mobile"), "navigation")["drawerItems"] = new JsonArray("unknown"),
                ["mobile.navigation.shortcuts[0]"] = j => Obj(Obj(j, "mobile"), "navigation")["shortcuts"] = new JsonArray("/admin"),
                ["mobile.navigation.shortcuts[1]"] = j => Obj(Obj(j, "mobile"), "navigation")["shortcuts"] = new JsonArray("feed", "feed"),
                ["mobile.navigation.shortcuts"] = j => Obj(Obj(j, "mobile"), "navigation")["shortcuts"] = new JsonArray("feed", "forums", "members", "messages", "profile"),
                ["mobile.navigation.accountMenu[1]"] = j => Obj(Obj(j, "mobile"), "navigation")["accountMenu"] = new JsonArray("profile", "profile"),
                ["mobile.navigation.accountMenu[0]"] = j => Obj(Obj(j, "mobile"), "navigation")["accountMenu"] = new JsonArray("/admin"),
                ["mobile.navigation.labels.nowhere"] = j => Obj(Obj(j, "mobile"), "navigation")["labels"] = new JsonObject { ["nowhere"] = "Explore" },
                ["mobile.navigation.labels.directory"] = j => Obj(Obj(j, "mobile"), "navigation")["labels"] = new JsonObject { ["directory"] = new string('x', 25) },
                ["mobile.navigation.labels.feed"] = j => Obj(Obj(j, "mobile"), "navigation")["labels"] = new JsonObject { ["feed"] = " " },
                ["schemaVersion"] = j => j["schemaVersion"] = 2,
                ["id"] = j => j["id"] = "Not A Valid Id",
                ["name"] = j => j["name"] = " ",
                ["shortName"] = j => j.Remove("shortName"),
                ["domains.web"] = j => Obj(j, "domains").Remove("web"),
                ["domains.api"] = j => Obj(j, "domains")["api"] = "https://api.example.test/path",
                ["contact.supportEmail"] = j => Obj(j, "contact")["supportEmail"] = "not-an-email",
                ["contact.emailFromAddress"] = j => Obj(j, "contact")["emailFromAddress"] = "",
                ["brand.logo"] = j => Obj(j, "brand")["logo"] = "../secret.png",
                ["brand.colors.light.primary"] = j => Obj(Obj(Obj(j, "brand"), "colors"), "light")["primary"] = "red",
                ["brand.colors.dark"] = j => Obj(Obj(j, "brand"), "colors").Remove("dark"),
                ["membership.priceUsdCents"] = j => Obj(j, "membership")["priceUsdCents"] = 0,
                ["membership.interval"] = j => Obj(j, "membership")["interval"] = "Week",
                ["membership.storeProductId"] = j => Obj(j, "membership")["storeProductId"] = "",
                ["membership.minimumAge"] = j => Obj(j, "membership")["minimumAge"] = 5,
                ["profile.rank.values"] = j => Obj(Obj(j, "profile"), "rank")["values"] = new JsonArray(),
                ["profile.rank.values[1].key"] = j => ((JsonObject)Arr(Obj(Obj(j, "profile"), "rank"), "values")[1]!)["key"] = Arr(Obj(Obj(j, "profile"), "rank"), "values")[0]!["key"]!.GetValue<string>(),
                ["profile.rank.values[0].color"] = j => ((JsonObject)Arr(Obj(Obj(j, "profile"), "rank"), "values")[0]!)["color"] = "#12345",
                ["profile.rank.maxLevel"] = j => Obj(Obj(j, "profile"), "rank")["maxLevel"] = -1,
                ["profile.rankHistory"] = j => Obj(Obj(j, "profile"), "rank")["enabled"] = false,
                ["profile.rankHistory.maxEntries"] = j => Obj(Obj(j, "profile"), "rankHistory")["maxEntries"] = 0,
                ["profile.goals.values[1].key"] = j => ((JsonObject)Arr(Obj(Obj(j, "profile"), "goals"), "values")[1]!)["key"] = Arr(Obj(Obj(j, "profile"), "goals"), "values")[0]!["key"]!.GetValue<string>(),
                ["reactions[1].key"] = j => ((JsonObject)Arr(j, "reactions")[1]!)["key"] = Arr(j, "reactions")[0]!["key"]!.GetValue<string>(),
                ["reactions[0].emoji"] = j => ((JsonObject)Arr(j, "reactions")[0]!)["emoji"] = "",
                ["profile"] = j => j["profile"] = null
            };
        }

        private static void ExpectRejected(Action<JsonObject> mutate, string expected)
        {
            string directory = TestProperty.CopySample();
            try
            {
                string file = Path.Combine(directory, PropertyLoader.PropertyFileName);
                JsonObject json = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
                mutate(json);
                File.WriteAllText(file, json.ToJsonString());
                PropertyConfigurationException e = Check.Throws<PropertyConfigurationException>(() => PropertyLoader.Load(directory), "Load with a bad " + expected);
                Check.True(e.Problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), "A problem names " + expected + " (got: " + e.Message + ")");
                Check.True(e.Message.Contains(file, StringComparison.Ordinal), "Message names the file");
            }
            finally
            {
                TestProperty.Delete(directory);
            }
        }

        private static JsonObject Obj(JsonObject parent, string name)
        {
            return (JsonObject)parent[name]!;
        }

        private static JsonArray Arr(JsonObject parent, string name)
        {
            return (JsonArray)parent[name]!;
        }

        private static string Slug(string path)
        {
            return new string(path.Select(c => Char.IsLetterOrDigit(c) ? c : '_').ToArray());
        }

        #endregion
    }
}
