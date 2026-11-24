namespace TrellisKit.Server.Routes
{
    using System;
    using TrellisKit.Server.Services.Realtime;
    using WatsonWebserver;

    /// <summary>
    /// GET /api/v1/ws, the realtime WebSocket (docs/PHASE5_CONTRACT.md section 4). The route is a Watson WebSocket route
    /// in the pre-authentication group: authentication happens in the hello frame, and every pre-upgrade check runs in
    /// the server's pre-routing hook (<see cref="RealtimeGateway.CheckUpgradeAsync"/>). WebSocket routes do not appear in
    /// the OpenAPI document; REST_API.md and the Postman collection document the protocol.
    /// </summary>
    public class RealtimeRoutes
    {
        #region Private-Members

        private readonly RealtimeGateway _Gateway;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="gateway">Gateway.</param>
        /// <exception cref="ArgumentNullException">The gateway is null.</exception>
        public RealtimeRoutes(RealtimeGateway gateway)
        {
            _Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the WebSocket route.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            server.WebSocket(RealtimeGateway.Path, _Gateway.HandleAsync);
        }

        #endregion
    }
}
