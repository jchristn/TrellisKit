namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json.Nodes;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Builds the isolated launch of a harness call (docs/HARNESSES.md), after Armada's captain launch isolation
    /// (~/Code/Armada CaptainLaunchIsolationPlanner and ArmadaMcpConfigBuilder): the job-scoped MCP server is the only
    /// MCP server the harness loads, its home and configuration are a throwaway directory, and its environment holds
    /// nothing of the server's but an allowlist.
    /// <list type="bullet">
    /// <item><description>Claude Code: --strict-mcp-config with an --mcp-config file, --setting-sources project,local (no user settings or hooks), and --allowedTools mcp__trelliskit (plus WebSearch and WebFetch for web research).</description></item>
    /// <item><description>Codex: a CODEX_HOME holding config.toml with the server and its Authorization header.</description></item>
    /// <item><description>Gemini CLI: HOME with .gemini/settings.json (the server trusted, so its tools run without a prompt) and the working directory marked trusted (Gemini CLI disables MCP servers in untrusted folders).</description></item>
    /// <item><description>Cursor CLI: HOME with .cursor/mcp.json and --approve-mcps.</description></item>
    /// <item><description>Mux: --strict-mcp-config with an --mcp-config file; MUX_CONFIG_DIR keeps pointing at the worker's Mux endpoints.</description></item>
    /// <item><description>OpenCode and Pi: no job tools (OpenCode's restricted permission rules deny every MCP tool, and Pi runs with --no-mcp); they can still answer plain prompts.</description></item>
    /// </list>
    /// Thread safety: stateless.
    /// </summary>
    public static class HarnessLaunchPlanner
    {
        #region Public-Members

        /// <summary>
        /// Variables every harness keeps from the server environment: search path, locale, terminal, proxies, and CA
        /// bundles.
        /// </summary>
        public static readonly IReadOnlyList<string> CommonVariables = new List<string>
        {
            "PATH", "LANG", "LANGUAGE", "LC_ALL", "LC_CTYPE", "TZ", "TERM", "SYSTEMROOT", "COMSPEC", "PATHEXT", "WINDIR",
            "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "no_proxy",
            "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS", "DOTNET_ROOT"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Why a runtime cannot run calls with job tools, or null when it can.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>Reason or null.</returns>
        public static string? ToolsUnsupportedReason(AiExecutorRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AiExecutorRuntimeEnum.ClaudeCode:
                case AiExecutorRuntimeEnum.Codex:
                case AiExecutorRuntimeEnum.GeminiCli:
                case AiExecutorRuntimeEnum.CursorCli:
                case AiExecutorRuntimeEnum.Mux:
                    return null;
                case AiExecutorRuntimeEnum.OpenCode:
                    return "OpenCode's restricted permission rules deny every MCP tool, so it cannot reach job tools";
                case AiExecutorRuntimeEnum.Pi:
                    return "Pi runs with --no-mcp in Restricted and ReadOnly modes, so it cannot reach job tools";
                default:
                    return "not a harness runtime";
            }
        }

        /// <summary>
        /// The vendor credential variables a runtime keeps (API keys and business accounts; never consumer logins, which
        /// live in the worker's own home and are not visible to the throwaway home).
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>Variable names.</returns>
        public static List<string> CredentialVariables(AiExecutorRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AiExecutorRuntimeEnum.ClaudeCode:
                    return new List<string> { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_BASE_URL", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "AWS_REGION", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN", "CLOUD_ML_REGION", "ANTHROPIC_VERTEX_PROJECT_ID" };
                case AiExecutorRuntimeEnum.Codex:
                    return new List<string> { "OPENAI_API_KEY", "CODEX_API_KEY", "OPENAI_BASE_URL", "OPENAI_ORGANIZATION", "OPENAI_PROJECT" };
                case AiExecutorRuntimeEnum.GeminiCli:
                    return new List<string> { "GEMINI_API_KEY", "GOOGLE_API_KEY", "GOOGLE_GEMINI_BASE_URL", "GOOGLE_GENAI_USE_VERTEXAI", "GOOGLE_CLOUD_PROJECT", "GOOGLE_CLOUD_LOCATION" };
                case AiExecutorRuntimeEnum.CursorCli:
                    return new List<string> { "CURSOR_API_KEY" };
                case AiExecutorRuntimeEnum.Mux:
                    return new List<string> { "MUX_CONFIG_DIR" };
                case AiExecutorRuntimeEnum.OpenCode:
                    return new List<string> { "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "OPENROUTER_API_KEY" };
                case AiExecutorRuntimeEnum.Pi:
                    return new List<string> { "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "GEMINI_API_KEY" };
                default:
                    return new List<string>();
            }
        }

        /// <summary>
        /// The ways a runtime can authenticate from a harness profile (docs/HARNESSES.md, "Harness profiles"). Every
        /// runtime can use the worker's environment; Claude Code also takes a stored API key, OAuth token, or gateway
        /// auth token; Codex, Gemini CLI, and Cursor CLI take a stored API key. Mux, OpenCode, and Pi read their own
        /// provider configuration, so they use the environment only.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>Credential types, Environment first.</returns>
        public static List<HarnessCredentialTypeEnum> CredentialTypes(AiExecutorRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AiExecutorRuntimeEnum.ClaudeCode:
                    return new List<HarnessCredentialTypeEnum> { HarnessCredentialTypeEnum.Environment, HarnessCredentialTypeEnum.ApiKey, HarnessCredentialTypeEnum.OAuthToken, HarnessCredentialTypeEnum.AuthToken };
                case AiExecutorRuntimeEnum.Codex:
                case AiExecutorRuntimeEnum.GeminiCli:
                case AiExecutorRuntimeEnum.CursorCli:
                    return new List<HarnessCredentialTypeEnum> { HarnessCredentialTypeEnum.Environment, HarnessCredentialTypeEnum.ApiKey };
                default:
                    return new List<HarnessCredentialTypeEnum> { HarnessCredentialTypeEnum.Environment };
            }
        }

        /// <summary>
        /// The environment variables a stored credential of a profile is passed in: ANTHROPIC_API_KEY,
        /// CLAUDE_CODE_OAUTH_TOKEN, or ANTHROPIC_AUTH_TOKEN for Claude Code; CODEX_API_KEY and OPENAI_API_KEY for Codex
        /// (codex exec reads the first, the interactive CLI the second); GEMINI_API_KEY for Gemini CLI; CURSOR_API_KEY
        /// for Cursor CLI.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <param name="type">Credential type.</param>
        /// <returns>Variable names; empty for Environment and for a type the runtime does not take.</returns>
        public static List<string> StoredCredentialVariables(AiExecutorRuntimeEnum runtime, HarnessCredentialTypeEnum type)
        {
            if (type == HarnessCredentialTypeEnum.Environment || !CredentialTypes(runtime).Contains(type)) return new List<string>();
            switch (runtime)
            {
                case AiExecutorRuntimeEnum.ClaudeCode:
                    if (type == HarnessCredentialTypeEnum.OAuthToken) return new List<string> { "CLAUDE_CODE_OAUTH_TOKEN" };
                    if (type == HarnessCredentialTypeEnum.AuthToken) return new List<string> { "ANTHROPIC_AUTH_TOKEN" };
                    return new List<string> { "ANTHROPIC_API_KEY" };
                case AiExecutorRuntimeEnum.Codex:
                    return new List<string> { "CODEX_API_KEY", "OPENAI_API_KEY" };
                case AiExecutorRuntimeEnum.GeminiCli:
                    return new List<string> { "GEMINI_API_KEY" };
                case AiExecutorRuntimeEnum.CursorCli:
                    return new List<string> { "CURSOR_API_KEY" };
                default:
                    return new List<string>();
            }
        }

        /// <summary>
        /// The environment variable a profile's base URL is passed in: ANTHROPIC_BASE_URL for Claude Code,
        /// OPENAI_BASE_URL for Codex, GOOGLE_GEMINI_BASE_URL for Gemini CLI.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>Variable name, or null when the runtime takes no base URL.</returns>
        public static string? BaseUrlVariable(AiExecutorRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AiExecutorRuntimeEnum.ClaudeCode: return "ANTHROPIC_BASE_URL";
                case AiExecutorRuntimeEnum.Codex: return "OPENAI_BASE_URL";
                case AiExecutorRuntimeEnum.GeminiCli: return "GOOGLE_GEMINI_BASE_URL";
                default: return null;
            }
        }

        /// <summary>
        /// Apply a harness profile's credential and base URL to a process environment built by
        /// <see cref="Environment"/>. A stored credential replaces every vendor credential variable the worker's
        /// environment holds for that runtime (so a stored OAuth token is never shadowed by an inherited API key), and
        /// is set in the variables of <see cref="StoredCredentialVariables"/>. A base URL is set in
        /// <see cref="BaseUrlVariable"/>. With the Environment type and no base URL nothing changes.
        /// </summary>
        /// <param name="environment">Environment overrides to change (null values remove a variable).</param>
        /// <param name="runtime">Runtime.</param>
        /// <param name="type">Credential type of the profile.</param>
        /// <param name="credential">Decrypted credential, or null.</param>
        /// <param name="baseUrl">Base URL of the profile, or null.</param>
        /// <exception cref="ArgumentNullException">environment is null.</exception>
        public static void ApplyProfile(Dictionary<string, string?> environment, AiExecutorRuntimeEnum runtime, HarnessCredentialTypeEnum type, string? credential, string? baseUrl)
        {
            ArgumentNullException.ThrowIfNull(environment);
            string? urlVariable = BaseUrlVariable(runtime);
            List<string> targets = StoredCredentialVariables(runtime, type);
            if (targets.Count > 0 && !String.IsNullOrEmpty(credential))
            {
                foreach (string name in CredentialVariables(runtime)) environment[name] = null;
                foreach (string name in targets) environment[name] = credential;
            }
            if (urlVariable != null && !String.IsNullOrWhiteSpace(baseUrl)) environment[urlVariable] = baseUrl.Trim();
        }

        /// <summary>
        /// The scrubbed environment of a harness process: every inherited variable not on the allowlist (common
        /// variables, the runtime's credential variables, and the extra names) is removed, and HOME, TMPDIR, and the
        /// XDG directories point into the sandbox.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <param name="sandbox">Sandbox.</param>
        /// <param name="extraNames">Extra names to keep (jobs.harnessPassEnvironment), or null.</param>
        /// <param name="inherited">The environment the process would inherit (the server's), or null to read it.</param>
        /// <returns>Overrides for the process environment (null values remove).</returns>
        /// <exception cref="ArgumentNullException">sandbox is null.</exception>
        public static Dictionary<string, string?> Environment(AiExecutorRuntimeEnum runtime, HarnessSandbox sandbox, string? extraNames, IDictionary? inherited = null)
        {
            ArgumentNullException.ThrowIfNull(sandbox);
            HashSet<string> keep = new HashSet<string>(CommonVariables, StringComparer.OrdinalIgnoreCase);
            foreach (string name in CredentialVariables(runtime)) keep.Add(name);
            foreach (string name in (extraNames ?? String.Empty).Split(new[] { ',', ' ', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!name.StartsWith("TK_", StringComparison.OrdinalIgnoreCase)) keep.Add(name);
            }
            Dictionary<string, string?> result = new Dictionary<string, string?>(StringComparer.Ordinal);
            IDictionary source = inherited ?? System.Environment.GetEnvironmentVariables();
            foreach (DictionaryEntry entry in source)
            {
                string? name = entry.Key as string;
                if (String.IsNullOrEmpty(name) || keep.Contains(name)) continue;
                result[name] = null;
            }
            if (runtime == AiExecutorRuntimeEnum.Mux && !source.Contains("MUX_CONFIG_DIR"))
            {
                // Mux reads its endpoints from ~/.mux; the throwaway HOME would hide them, so name the real folder.
                string? home = source["HOME"] as string ?? source["USERPROFILE"] as string;
                if (!String.IsNullOrEmpty(home)) result["MUX_CONFIG_DIR"] = Path.Combine(home, ".mux");
            }
            result["HOME"] = sandbox.Home;
            result["USERPROFILE"] = sandbox.Home;
            result["TMPDIR"] = sandbox.Temp;
            result["TMP"] = sandbox.Temp;
            result["TEMP"] = sandbox.Temp;
            result["XDG_CONFIG_HOME"] = Path.Combine(sandbox.Home, ".config");
            result["XDG_CACHE_HOME"] = Path.Combine(sandbox.Home, ".cache");
            result["XDG_DATA_HOME"] = Path.Combine(sandbox.Home, ".local", "share");
            result["XDG_STATE_HOME"] = Path.Combine(sandbox.Home, ".local", "state");
            result["NO_COLOR"] = "1";
            if (runtime == AiExecutorRuntimeEnum.ClaudeCode)
            {
                result["CLAUDE_CONFIG_DIR"] = Path.Combine(sandbox.Home, ".claude");
                result["DISABLE_AUTOUPDATER"] = "1";
                result["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
            }
            if (runtime == AiExecutorRuntimeEnum.Codex) result["CODEX_HOME"] = Path.Combine(sandbox.Home, ".codex");
            if (runtime == AiExecutorRuntimeEnum.GeminiCli)
            {
                // Gemini CLI disables MCP servers in folders it does not trust; the throwaway working directory is trusted.
                result["GEMINI_CLI_TRUST_WORKSPACE"] = "true";
                result["GEMINI_CLI_TRUSTED_FOLDERS_PATH"] = Path.Combine(sandbox.Home, ".gemini", "trustedFolders.json");
            }
            return result;
        }

        /// <summary>
        /// Build the launch plan of a call.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <param name="sandbox">Sandbox.</param>
        /// <param name="server">The job tool server, or null for a call without job tools.</param>
        /// <param name="allowWebResearch">Whether Claude Code pre-approves WebSearch and WebFetch.</param>
        /// <returns>Plan (arguments and files; the environment comes from <see cref="Environment"/>).</returns>
        /// <exception cref="ArgumentNullException">sandbox is null.</exception>
        public static HarnessLaunchPlan Plan(AiExecutorRuntimeEnum runtime, HarnessSandbox sandbox, JobToolServer? server, bool allowWebResearch)
        {
            ArgumentNullException.ThrowIfNull(sandbox);
            HarnessLaunchPlan plan = new HarnessLaunchPlan();
            if (server != null) plan.UnsupportedReason = ToolsUnsupportedReason(runtime);
            if (!plan.Supported) return plan;
            switch (runtime)
            {
                case AiExecutorRuntimeEnum.ClaudeCode:
                    {
                        string path = Path.Combine(sandbox.Config, "mcp.json");
                        plan.Files[path] = server == null ? "{\"mcpServers\":{}}" : KeyedServers(server, false);
                        plan.ExtraArguments["--strict-mcp-config"] = null;
                        plan.ExtraArguments["--mcp-config"] = path;
                        plan.ExtraArguments["--setting-sources"] = "project,local";
                        List<string> allowed = new List<string>();
                        if (server != null) allowed.Add("mcp__" + JobToolServer.ServerName);
                        if (allowWebResearch)
                        {
                            allowed.Add("WebSearch");
                            allowed.Add("WebFetch");
                        }
                        if (allowed.Count > 0) plan.ExtraArguments["--allowedTools"] = String.Join(",", allowed);
                        break;
                    }
                case AiExecutorRuntimeEnum.Codex:
                    plan.Files[Path.Combine(sandbox.Home, ".codex", "config.toml")] = server == null ? String.Empty : CodexToml(server);
                    break;
                case AiExecutorRuntimeEnum.GeminiCli:
                    plan.Files[Path.Combine(sandbox.Home, ".gemini", "settings.json")] = server == null ? "{\"mcpServers\":{}}" : KeyedServers(server, true);
                    plan.Files[Path.Combine(sandbox.Home, ".gemini", "trustedFolders.json")] = new JsonObject { [sandbox.Work] = "TRUST_FOLDER" }.ToJsonString();
                    break;
                case AiExecutorRuntimeEnum.CursorCli:
                    plan.Files[Path.Combine(sandbox.Home, ".cursor", "mcp.json")] = server == null ? "{\"mcpServers\":{}}" : KeyedServers(server, false);
                    if (server != null) plan.ExtraArguments["--approve-mcps"] = null;
                    break;
                case AiExecutorRuntimeEnum.Mux:
                    {
                        string path = Path.Combine(sandbox.Config, "mux-mcp.json");
                        plan.Files[path] = server == null ? "{\"servers\":[]}" : MuxServers(server);
                        plan.ExtraArguments["--strict-mcp-config"] = null;
                        plan.ExtraArguments["--mcp-config"] = path;
                        break;
                    }
                default:
                    break;
            }
            return plan;
        }

        #endregion

        #region Private-Methods

        private static string KeyedServers(JobToolServer server, bool trust)
        {
            JsonObject entry = new JsonObject
            {
                ["type"] = "http",
                ["url"] = server.Url,
                ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + server.Token }
            };
            if (trust) entry["trust"] = true;
            JsonObject root = new JsonObject { ["mcpServers"] = new JsonObject { [JobToolServer.ServerName] = entry } };
            return root.ToJsonString();
        }

        private static string MuxServers(JobToolServer server)
        {
            JsonObject entry = new JsonObject
            {
                ["name"] = JobToolServer.ServerName,
                ["transport"] = "http",
                ["url"] = server.BaseUrl,
                ["mcpPath"] = "/mcp",
                ["auth"] = new JsonObject { ["type"] = "bearer", ["bearerToken"] = server.Token }
            };
            JsonObject root = new JsonObject { ["servers"] = new JsonArray(entry) };
            return root.ToJsonString();
        }

        private static string CodexToml(JobToolServer server)
        {
            return "[mcp_servers." + JobToolServer.ServerName + "]\n" +
                "url = \"" + server.Url + "\"\n" +
                "http_headers = { \"Authorization\" = \"Bearer " + server.Token + "\" }\n";
        }

        #endregion
    }
}
