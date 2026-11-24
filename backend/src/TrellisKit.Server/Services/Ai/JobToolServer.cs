namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// The job-scoped TrellisKit MCP server of one harness call (docs/HARNESSES.md): a Voltaic Streamable HTTP MCP
    /// server on 127.0.0.1 and a free port, serving only the tools of one job item's agent call and accepting only that
    /// call's random bearer token. Loopback clients only, no CORS, no legacy JSON-RPC endpoints, and no diagnostic tools.
    /// Every tool call goes through <see cref="AgentToolSet.InvokeAsync"/>; Voltaic first checks the arguments against
    /// the tool's schema and answers a tool error (isError) for invalid ones, and an unknown tool is a JSON-RPC error.
    /// Dispose stops the server; the token is useless afterward. Thread safety: safe for concurrent tool calls.
    /// </summary>
    public sealed class JobToolServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// MCP server name the harness sees (its tools appear as mcp__trelliskit__* in Claude Code).
        /// </summary>
        public const string ServerName = "trelliskit";

        /// <summary>
        /// Loopback host the server binds and the harness connects to.
        /// </summary>
        public const string Host = "127.0.0.1";

        /// <summary>
        /// Streamable HTTP URL, for example http://127.0.0.1:52011/mcp.
        /// </summary>
        public string Url
        {
            get { return "http://" + Host + ":" + Port.ToString(CultureInfo.InvariantCulture) + "/mcp"; }
        }

        /// <summary>
        /// Base URL without the path (for clients that keep the path separately, such as Mux).
        /// </summary>
        public string BaseUrl
        {
            get { return "http://" + Host + ":" + Port.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Port.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// The bearer token of this call (43 URL-safe characters).
        /// </summary>
        public string Token { get; }

        /// <summary>
        /// The tools served.
        /// </summary>
        public AgentToolSet Tools { get; }

        #endregion

        #region Private-Members

        private const int _StartAttempts = 5;
        private const int _ReadyTimeoutMs = 3000;
        private readonly McpHttpServer _Server;
        private readonly CancellationTokenSource _Stop;
        private readonly Task _Run;
        private readonly byte[] _Expected;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private JobToolServer(McpHttpServer server, CancellationTokenSource stop, Task run, int port, string token, AgentToolSet tools)
        {
            _Server = server;
            _Stop = stop;
            _Run = run;
            Port = port;
            Token = token;
            Tools = tools;
            _Expected = Encoding.UTF8.GetBytes("Bearer " + token);
        }

        /// <summary>
        /// Start a server for one agent call.
        /// </summary>
        /// <param name="tools">The call's tools.</param>
        /// <param name="instructions">Server instructions shown to the harness, or null.</param>
        /// <param name="logger">Logger, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The running server.</returns>
        /// <exception cref="ArgumentNullException">tools is null.</exception>
        /// <exception cref="InvalidOperationException">No loopback port could be bound.</exception>
        public static async Task<JobToolServer> StartAsync(AgentToolSet tools, string? instructions, AppLogger? logger, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(tools);
            string secret = NewToken();
            for (int attempt = 0; attempt < _StartAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                int port = FreePort();
                McpHttpServer server = Build(tools, port, instructions, logger);
                JobToolServer? started = null;
                CancellationTokenSource stop = new CancellationTokenSource();
                Task run = Task.Run(() => server.StartAsync(stop.Token), CancellationToken.None);
                JobToolServer candidate = new JobToolServer(server, stop, run, port, secret, tools);
                server.AuthenticationHandler = candidate.AuthenticateAsync;
                if (await ReadyAsync(port, run, token).ConfigureAwait(false)) started = candidate;
                if (started != null) return started;
                candidate.Dispose();
            }
            throw new InvalidOperationException("No loopback port could be bound for the job tool server.");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stop the server.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try
            {
                _Stop.Cancel();
                _Server.Stop();
            }
            catch (Exception e) when (e is ObjectDisposedException || e is InvalidOperationException || e is HttpListenerException)
            {
                // Already stopped.
            }
            try
            {
                _Run.Wait(2000);
            }
            catch (AggregateException)
            {
                // The accept loop ends with the listener.
            }
            _Server.Dispose();
            _Stop.Dispose();
        }

        #endregion

        #region Private-Methods

        private static McpHttpServer Build(AgentToolSet tools, int port, string? instructions, AppLogger? logger)
        {
            McpHttpServer server = new McpHttpServer(Host, port, "/rpc", "/events", false, "/mcp")
            {
                EnableLegacyEndpoints = false,
                RestrictToLoopbackClients = true,
                EnableCors = false,
                ServerName = ServerName,
                ServerInstructions = instructions,
                MaxSessions = 16,
                MaxSessionsPerClient = 16,
                IncludeToolExceptionMessages = false
            };
            if (logger != null) server.Log += (sender, message) => logger.Debug("[JobToolServer] " + message);
            foreach (AgentTool tool in tools.Tools)
            {
                string name = tool.Name;
                Voltaic.Mcp.ToolDefinition definition = new Voltaic.Mcp.ToolDefinition
                {
                    Name = name,
                    Description = tool.Description,
                    InputSchema = tools.Schema(name)
                };
                server.RegisterTool(definition, async (RpcParameters? args, RpcCallContext? context, CancellationToken ct) =>
                {
                    AgentToolResult result = await tools.InvokeAsync(name, args?.RawJson, ct).ConfigureAwait(false);
                    McpToolCallResult reply = McpToolCallResult.FromText(result.Message);
                    if (!result.Accepted) reply.IsError = true;
                    return reply;
                });
            }
            return server;
        }

        private Task<AuthenticationResult> AuthenticateAsync(HttpListenerRequest request)
        {
            string presented = request.Headers["Authorization"] ?? String.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(presented.Trim());
            if (bytes.Length == _Expected.Length && CryptographicOperations.FixedTimeEquals(bytes, _Expected))
                return Task.FromResult(new AuthenticationResult { IsAuthenticated = true, Principal = "jobitem:" + (Tools.JobItemId ?? "none") });
            return Task.FromResult(new AuthenticationResult { IsAuthenticated = false, StatusCode = 401, ErrorMessage = "This token is not valid for this job tool server." });
        }

        private static string NewToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        private static async Task<bool> ReadyAsync(int port, Task run, CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(_ReadyTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (run.IsCompleted) return false;
                using (TcpClient client = new TcpClient())
                {
                    try
                    {
                        await client.ConnectAsync(IPAddress.Loopback, port, token).ConfigureAwait(false);
                        // A listener that failed to bind ends its accept loop at once; another process may own the port.
                        await Task.Delay(20, token).ConfigureAwait(false);
                        return !run.IsCompleted;
                    }
                    catch (SocketException)
                    {
                        // Not listening yet.
                    }
                }
                await Task.Delay(25, token).ConfigureAwait(false);
            }
            return false;
        }

        #endregion
    }
}
