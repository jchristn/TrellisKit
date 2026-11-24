namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// A fake Claude Code CLI for the harness suites: a POSIX shell script that PolyPrompt's ClaudeCodeClient launches
    /// as its executable. It records its arguments, standard input, working directory, and environment next to the
    /// script, reads the job-scoped MCP server URL and token from the --mcp-config file it was given, calls the MCP
    /// server with curl (initialize, notifications/initialized, a valid tools/call, and an invalid one), and prints the
    /// stream-json lines Claude Code prints (system/init, two assistant messages with text and a tool use, and a result
    /// with usage, cache tokens, and cost).
    /// </summary>
    public sealed class FakeHarnessCli : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Model the fake reports.
        /// </summary>
        public const string Model = "claude-fake-model";

        /// <summary>
        /// Cost the fake reports, in US dollars.
        /// </summary>
        public const decimal CostUsd = 0.0425m;

        /// <summary>
        /// Prompt tokens the fake reports.
        /// </summary>
        public const int PromptTokens = 1200;

        /// <summary>
        /// Completion tokens the fake reports.
        /// </summary>
        public const int CompletionTokens = 300;

        /// <summary>
        /// Cache read tokens the fake reports.
        /// </summary>
        public const int CacheReadTokens = 900;

        /// <summary>
        /// Cache creation tokens the fake reports.
        /// </summary>
        public const int CacheCreationTokens = 150;

        /// <summary>
        /// Session identifier the fake reports.
        /// </summary>
        public const string SessionId = "fake-session";

        /// <summary>
        /// Path of the script.
        /// </summary>
        public string ScriptPath { get; }

        /// <summary>
        /// Whether the script was launched (its argument capture exists).
        /// </summary>
        public bool Launched
        {
            get { return File.Exists(Capture("args")); }
        }

        #endregion

        #region Private-Members

        private readonly string _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Write the script.
        /// </summary>
        /// <param name="toolName">Job tool the fake calls.</param>
        /// <param name="validArgumentsJson">Arguments of the first (valid) call.</param>
        /// <param name="invalidArgumentsJson">Arguments of the second (invalid) call.</param>
        public FakeHarnessCli(string toolName, string validArgumentsJson, string invalidArgumentsJson)
        {
            _Directory = Path.Combine(Path.GetTempPath(), "tk-fake-harness-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(_Directory);
            ScriptPath = Path.Combine(_Directory, "claude");
            string capture = Path.Combine(_Directory, "capture");
            string headers = "-H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -H \"Authorization: $AUTH\"";
            string session = headers + " -H \"Mcp-Session-Id: $SID\" -H 'MCP-Protocol-Version: 2025-06-18'";
            List<string> lines = new List<string>
            {
                "#!/bin/sh",
                "CAP='" + capture + "'",
                "printf '%s\\n' \"$@\" > \"$CAP.args\"",
                "cat > \"$CAP.stdin\"",
                "pwd > \"$CAP.cwd\"",
                "env > \"$CAP.env\"",
                "CFG=''",
                "PREV=''",
                "for A in \"$@\"; do",
                "  if [ \"$PREV\" = '--mcp-config' ]; then CFG=\"$A\"; fi",
                "  PREV=\"$A\"",
                "done",
                "if [ -n \"$CFG\" ] && [ -f \"$CFG\" ]; then",
                "  URL=$(sed -n 's/.*\"url\":\"\\([^\"]*\\)\".*/\\1/p' \"$CFG\")",
                "  AUTH=$(sed -n 's/.*\"Authorization\":\"\\([^\"]*\\)\".*/\\1/p' \"$CFG\")",
                "  echo \"$URL\" > \"$CAP.url\"",
                "  curl -s -D \"$CAP.headers\" -o \"$CAP.init\" -X POST \"$URL\" " + headers + " --data '{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"fake-claude\",\"version\":\"1.0.0\"}}}'",
                "  SID=$(grep -i '^mcp-session-id:' \"$CAP.headers\" | sed 's/^[^:]*: *//' | tr -d '\\r\\n')",
                "  curl -s -o /dev/null -X POST \"$URL\" " + session + " --data '{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}'",
                "  curl -s -o \"$CAP.list\" -X POST \"$URL\" " + session + " --data '{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}'",
                "  curl -s -o \"$CAP.call1\" -X POST \"$URL\" " + session + " --data '{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"" + toolName + "\",\"arguments\":" + validArgumentsJson + "}}'",
                "  curl -s -o \"$CAP.call2\" -X POST \"$URL\" " + session + " --data '{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"" + toolName + "\",\"arguments\":" + invalidArgumentsJson + "}}'",
                "fi",
                "echo '{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"fake-session\",\"model\":\"" + Model + "\"}'",
                "echo '{\"type\":\"assistant\",\"message\":{\"id\":\"msg_fake_1\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Looking for community centers.\"},{\"type\":\"tool_use\",\"id\":\"toolu_fake_1\",\"name\":\"mcp__trelliskit__" + toolName + "\",\"input\":{}}]}}'",
                "sleep 0.05",
                "echo '{\"type\":\"assistant\",\"message\":{\"id\":\"msg_fake_2\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Reported one candidate.\"}]}}'",
                "echo '{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Reported one candidate.\",\"session_id\":\"fake-session\",\"usage\":{\"input_tokens\":" + PromptTokens + ",\"output_tokens\":" + CompletionTokens + ",\"cache_read_input_tokens\":" + CacheReadTokens + ",\"cache_creation_input_tokens\":" + CacheCreationTokens + "},\"total_cost_usd\":" + CostUsd.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}'",
                "exit 0"
            };
            File.WriteAllText(ScriptPath, String.Join("\n", lines) + "\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(ScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Path of one capture (args, stdin, cwd, env, url, init, list, call1, call2).
        /// </summary>
        /// <param name="name">Capture name.</param>
        /// <returns>Path.</returns>
        public string Capture(string name)
        {
            return Path.Combine(_Directory, "capture." + name);
        }

        /// <summary>
        /// Text of one capture, or empty when it does not exist.
        /// </summary>
        /// <param name="name">Capture name.</param>
        /// <returns>Text.</returns>
        public string Read(string name)
        {
            string path = Capture(name);
            return File.Exists(path) ? File.ReadAllText(path) : String.Empty;
        }

        /// <summary>
        /// The recorded arguments, one per element (a multi-line argument spans several lines).
        /// </summary>
        /// <returns>Lines.</returns>
        public List<string> ArgumentLines()
        {
            return Read("args").Split('\n').ToList();
        }

        /// <summary>
        /// Delete the script and its captures.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_Directory)) Directory.Delete(_Directory, true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }

        #endregion
    }
}
