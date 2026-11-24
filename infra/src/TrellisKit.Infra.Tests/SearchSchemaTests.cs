namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;
    using Xunit;

    /// <summary>
    /// Phase 3 search: db-bootstrap lets the application role use the Verbex search schema with least privilege.
    /// </summary>
    public class SearchSchemaTests
    {
        /// <summary>
        /// The bootstrap SQL creates the search schema owned by the application role and grants CREATE on the
        /// database (needed by the server's CREATE SCHEMA IF NOT EXISTS), never ownership of the database or
        /// superuser, database-creation, or role-creation rights.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void BootstrapGrantsTheSearchSchemaWithLeastPrivilege(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Assert.Equal("search", env.Stacks.Config.Database.SearchSchema);

            JsonElement bootstrap = TemplateReader.ApiContainer(env, env.Stacks.Api, ApiStack.BootstrapContainerName);
            Dictionary<string, string> environment = bootstrap.GetProperty("Environment").EnumerateArray()
                .ToDictionary(e => e.GetProperty("Name").GetString() ?? string.Empty, e => e.GetProperty("Value").ValueKind == JsonValueKind.String ? e.GetProperty("Value").GetString() ?? string.Empty : e.GetProperty("Value").GetRawText());
            Assert.Equal("search", environment["SEARCH_SCHEMA"]);

            string sql = environment["BOOTSTRAP_SQL"];
            Assert.Contains("GRANT CONNECT, TEMPORARY, CREATE ON DATABASE :\"db\" TO :\"app_user\";", sql);
            Assert.Contains("CREATE SCHEMA IF NOT EXISTS :\"search_schema\" AUTHORIZATION :\"app_user\";", sql);
            Assert.Contains("GRANT USAGE, CREATE ON SCHEMA :\"search_schema\" TO :\"app_user\";", sql);
            Assert.DoesNotContain("OWNER", sql);
            Assert.DoesNotContain(" CREATEDB", sql);
            Assert.Contains("search_schema=\"$SEARCH_SCHEMA\"", bootstrap.GetProperty("Command")[0].GetString());
        }

        /// <summary>
        /// The search schema name is a validated identifier.
        /// </summary>
        [Fact]
        public void SearchSchemaMustBeAnIdentifier()
        {
            DatabaseSettings settings = new DatabaseSettings();
            settings.SearchSchema = "forum_search";
            Assert.Equal("forum_search", settings.SearchSchema);
            Assert.Throws<System.ArgumentException>(() => settings.SearchSchema = "search; DROP");
            Assert.Throws<System.ArgumentException>(() => settings.SearchSchema = "Search");
        }
    }
}
