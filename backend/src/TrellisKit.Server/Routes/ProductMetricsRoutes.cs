namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>Opt-in, bounded product event ingestion, with anonymous signup.started only.</summary>
    public sealed class ProductMetricsRoutes
    {
        private readonly PropertyDirectory _Directory;
        /// <summary>Construct routes for the property directory.</summary>
        public ProductMetricsRoutes(PropertyDirectory directory) { _Directory = directory ?? throw new ArgumentNullException(nameof(directory)); }
        /// <summary>Register the public ingestion endpoint.</summary>
        public void Register(Webserver server)
        {
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/metrics/events", RecordAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Record a declared product event", "Product metrics", "Disabled by default. Anonymous signup.started or authenticated property-declared events only. No member text or identifier is accepted. Rate limited per IP and member.")
                    .Body("{ name, dimensions }").Resp(204,"Recorded or collection disabled").Resp(400,"ValidationFailed").Resp(401,"Unauthorized").Resp(429,"RateLimited"));
        }
        private async Task RecordAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            s.Limits.EnforceIp(RequestInfo.From(ctx, s.Settings.Rest).SourceIp, "product-events", 30, RateLimitService.Minute);
            if (ctx.Request.ContentLength > 4096) throw new TrellisKit.Core.Exceptions.ApiErrorException(413,"TooLarge","Event bodies are limited to 4096 bytes.");
            ProductMetricEvent payload = RouteHelper.ReadJson<ProductMetricEvent>(ctx);
            string? memberId = null;
            string? authorization = ctx.Request.RetrieveHeaderValue("Authorization");
            if (authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            {
                SessionPrincipal? principal = await s.Tokens.ValidateAccessTokenAsync(authorization.Substring(7).Trim(), ctx.Token).ConfigureAwait(false);
                if (principal?.PrincipalKind == TrellisKit.Core.Enums.PrincipalKindEnum.Member) memberId = principal.UserId;
            }
            if (memberId != null) s.Limits.Enforce("product-events-member:" + memberId, 60, RateLimitService.Minute);
            await new ProductMetricEventService(s).RecordAsync(payload, memberId, ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
        }
    }
}
