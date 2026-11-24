namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Anonymous health routes used by load balancers, container healthchecks, and the apps.
    /// </summary>
    public class HealthRoutes
    {
        #region Public-Members

        /// <summary>
        /// Whether the task is draining (realtime shutdown). While true, health reports Draining and readiness returns 503.
        /// Default: never draining.
        /// </summary>
        public Func<bool> Draining { get; set; } = () => false;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly string _Version;
        private readonly DateTime _StartedUtc;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate health routes.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="version">Server version.</param>
        /// <param name="startedUtc">UTC start time.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public HealthRoutes(DatabaseDriverBase database, string version, DateTime startedUtc)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Version = version ?? throw new ArgumentNullException(nameof(version));
            _StartedUtc = startedUtc;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);

            server.Routes.PreAuthentication.Static.Add(
                HttpMethod.GET, Constants.ApiPrefix + "/health", GetHealthAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Server health", "Health")
                    .WithDescription("Liveness. Returns 200 when the database is reachable, 503 otherwise. Anonymous.")
                    .Returns("Health")
                    .WithResponse(503, OpenApiResponseMetadata.Create("Unhealthy")));

            server.Routes.PreAuthentication.Static.Add(
                HttpMethod.GET, Constants.ApiPrefix + "/health/ready", GetReadyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Server readiness", "Health")
                    .WithDescription("Readiness. Returns 200 when the database is reachable and every migration is applied. Anonymous.")
                    .Returns("Health")
                    .WithResponse(503, OpenApiResponseMetadata.Create("Not ready")));
        }

        #endregion

        #region Private-Methods

        private async Task GetHealthAsync(HttpContextBase ctx)
        {
            bool reachable = await _Database.PingAsync(ctx.Token).ConfigureAwait(false);
            HealthResponse body = BuildResponse(!reachable ? HealthStatusEnum.Unhealthy : Draining() ? HealthStatusEnum.Draining : HealthStatusEnum.Healthy);
            await RouteHelper.SendJsonAsync(ctx, reachable ? 200 : 503, body).ConfigureAwait(false);
        }

        private async Task GetReadyAsync(HttpContextBase ctx)
        {
            bool reachable = await _Database.PingAsync(ctx.Token).ConfigureAwait(false);
            bool migrated = reachable
                && await _Database.GetAppliedSchemaVersionAsync(ctx.Token).ConfigureAwait(false) >= _Database.LatestSchemaVersion;
            bool draining = Draining();
            HealthResponse body = BuildResponse(!reachable ? HealthStatusEnum.Unhealthy : draining ? HealthStatusEnum.Draining : migrated ? HealthStatusEnum.Healthy : HealthStatusEnum.Degraded);
            await RouteHelper.SendJsonAsync(ctx, reachable && migrated && !draining ? 200 : 503, body).ConfigureAwait(false);
        }

        private HealthResponse BuildResponse(HealthStatusEnum status)
        {
            return new HealthResponse
            {
                Status = status,
                Version = _Version,
                StartedUtc = _StartedUtc,
                UptimeSeconds = Math.Round((DateTime.UtcNow - _StartedUtc).TotalSeconds, 1),
                Database = _Database.DatabaseType
            };
        }

        #endregion
    }
}
