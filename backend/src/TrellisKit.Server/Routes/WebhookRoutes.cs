namespace TrellisKit.Server.Routes
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Store webhooks: anonymous, signature-verified, idempotent. The body is read exactly as received. Webhooks never
    /// read X-TK-Property, X-TK-App, or the host (docs/MULTIPROPERTY_CONTRACT.md section 2.5): Stripe resolves from its
    /// per-property URL, Apple and Google from the bundle id or package in the notification (the property's own
    /// verifier then checks the signature), and MediaConvert from the media row that owns the job id. A notification
    /// that matches no property returns 200 Ignored and stores nothing.
    /// </summary>
    public class WebhookRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/webhooks";
        private const string _Tag = "Webhooks";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public WebhookRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
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
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, _Base + "/stripe", StripeLegacyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Stripe webhook (single-property URL)", _Tag, "SingleProperty mode only (404 NotAvailable in MultiProperty; use /webhooks/stripe/{propertyId}). Verified with the Stripe-Signature header. Not called by clients.")
                    .Resp(200, "Processed, ignored, or duplicate").Resp(400, "InvalidSignature").Resp(404, "NotAvailable").Resp(500, "WebhookFailed (retried)").Resp(503, "IntegrationNotConfigured"));
            server.Routes.PreAuthentication.Parameter.Add(HttpMethod.POST, _Base + "/stripe/{propertyId}", StripeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Stripe webhook (per property)", _Tag, "Each property's Stripe account posts to its own URL. Verified with that property's Stripe-Signature secret. Not called by clients.")
                    .PathParam("propertyId", "Property id").Resp(200, "Processed, ignored, or duplicate").Resp(400, "InvalidSignature").Resp(404, "PropertyNotFound").Resp(500, "WebhookFailed (retried)").Resp(503, "IntegrationNotConfigured"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, _Base + "/apple", AppleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("App Store Server Notifications V2", _Tag, "Body { signedPayload }. Not called by clients.")
                    .Resp(200, "Processed, ignored, or duplicate").Resp(400, "InvalidSignature").Resp(500, "WebhookFailed (retried)").Resp(503, "IntegrationNotConfigured"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, _Base + "/google", GoogleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Google Play Real-Time Developer Notifications", _Tag, "Pub/Sub push with an OIDC bearer token. Not called by clients.")
                    .Resp(200, "Processed, ignored, or duplicate").Resp(401, "InvalidSignature").Resp(400, "InvalidNotification").Resp(500, "WebhookFailed (retried)").Resp(503, "IntegrationNotConfigured"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, _Base + "/mediaconvert", MediaConvertAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("MediaConvert job state webhook", _Tag, "Amazon SNS HTTPS delivery of MediaConvert Job State Change events (signature, signing certificate host, and media.snsTopicArn are verified; SubscriptionConfirmation is confirmed). Not called by clients.")
                    .Resp(200, "Processed, ignored, or duplicate").Resp(400, "InvalidSignature"));
        }

        #endregion

        #region Private-Methods

        private static async Task<string> RawAsync(HttpContextBase ctx)
        {
            byte[] data = await ctx.Request.ReadBodyAsync(ctx.Token).ConfigureAwait(false) ?? Array.Empty<byte>();
            return Encoding.UTF8.GetString(data);
        }

        private async Task StripeLegacyAsync(HttpContextBase ctx)
        {
            PropertyRuntime? runtime = _Directory.Mode == DeploymentModeEnum.SingleProperty ? _Directory.Default : null;
            if (runtime == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotAvailable", "Use the per-property Stripe webhook URL /api/v1/webhooks/stripe/{propertyId}.").ConfigureAwait(false);
                return;
            }
            string raw = await RawAsync(ctx).ConfigureAwait(false);
            MessageResponse response = await AlertingAsync(runtime, "Stripe webhook", () => runtime.Services.Webhooks.HandleStripeAsync(raw, ctx.Request.RetrieveHeaderValue("Stripe-Signature"), ctx.Token)).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task StripeAsync(HttpContextBase ctx)
        {
            PropertyRuntime? runtime = _Directory.Find(RouteHelper.Param(ctx, "propertyId"));
            if (runtime == null || runtime.Record.Status == PropertyStatusEnum.Deleting || runtime.Record.Status == PropertyStatusEnum.Deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "PropertyNotFound", "No property matches this webhook URL.").ConfigureAwait(false);
                return;
            }
            string raw = await RawAsync(ctx).ConfigureAwait(false);
            MessageResponse response = await AlertingAsync(runtime, "Stripe webhook", () => runtime.Services.Webhooks.HandleStripeAsync(raw, ctx.Request.RetrieveHeaderValue("Stripe-Signature"), ctx.Token)).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task AppleAsync(HttpContextBase ctx)
        {
            string raw = await RawAsync(ctx).ConfigureAwait(false);
            PropertyRuntime? runtime = StoreRuntime(AppPlatformEnum.Ios, AppleBundleId(raw));
            if (runtime == null)
            {
                await IgnoredAsync(ctx, "apple").ConfigureAwait(false);
                return;
            }
            MessageResponse response = await AlertingAsync(runtime, "App Store webhook", () => runtime.Services.Webhooks.HandleAppleAsync(raw, ctx.Token)).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task GoogleAsync(HttpContextBase ctx)
        {
            string raw = await RawAsync(ctx).ConfigureAwait(false);
            PropertyRuntime? runtime = StoreRuntime(AppPlatformEnum.Android, GooglePackageName(raw));
            if (runtime == null)
            {
                await IgnoredAsync(ctx, "google").ConfigureAwait(false);
                return;
            }
            MessageResponse response = await AlertingAsync(runtime, "Google Play webhook", () => runtime.Services.Webhooks.HandleGoogleAsync(raw, ctx.Request.RetrieveHeaderValue("Authorization"), ctx.Token)).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private static async Task<MessageResponse> AlertingAsync(PropertyRuntime runtime, string integration, Func<Task<MessageResponse>> handler)
        {
            // Integration failures alert the property's staff (rate-limited by email.integrationAlertMinutes).
            try
            {
                return await handler().ConfigureAwait(false);
            }
            catch (ApiErrorException e) when (e.StatusCode >= 500 || e.Error == "InvalidSignature")
            {
                await runtime.Services.Email.AlertIntegrationAsync(integration, e.Error + ": " + e.Message).ConfigureAwait(false);
                throw;
            }
        }

        private PropertyRuntime? StoreRuntime(AppPlatformEnum platform, string? appId)
        {
            if (_Directory.Mode == DeploymentModeEnum.SingleProperty) return _Directory.Default;
            PropertyRuntime? runtime = _Directory.FindByApp(platform, appId);
            if (runtime == null || runtime.Record.Status == PropertyStatusEnum.Deleting || runtime.Record.Status == PropertyStatusEnum.Deleted) return null;
            return runtime;
        }

        private static async Task IgnoredAsync(HttpContextBase ctx, string source)
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.PropertyResolutions, "source", "webhook", "outcome", "webhook_unmatched");
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.Webhooks, "source", source, "outcome", "ignored");
            await RouteHelper.SendJsonAsync(ctx, 200, new MessageResponse { Message = "Ignored." }).ConfigureAwait(false);
        }

        private static string? AppleBundleId(string raw)
        {
            // Routing only: the property's verifier checks the JWS chain and the bundle id before anything is stored.
            try
            {
                using (JsonDocument body = JsonDocument.Parse(raw))
                {
                    if (!body.RootElement.TryGetProperty("signedPayload", out JsonElement signed) || signed.ValueKind != JsonValueKind.String) return null;
                    string[] parts = (signed.GetString() ?? String.Empty).Split('.');
                    if (parts.Length != 3) return null;
                    using (JsonDocument payload = JsonDocument.Parse(Base64Url(parts[1])))
                    {
                        if (!payload.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object) return null;
                        return data.TryGetProperty("bundleId", out JsonElement bundle) && bundle.ValueKind == JsonValueKind.String ? bundle.GetString() : null;
                    }
                }
            }
            catch (Exception e) when (e is JsonException || e is FormatException)
            {
                return null;
            }
        }

        private static string? GooglePackageName(string raw)
        {
            // Routing only: the property's verifier checks the Pub/Sub OIDC token before anything is stored.
            try
            {
                using (JsonDocument body = JsonDocument.Parse(raw))
                {
                    if (!body.RootElement.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object) return null;
                    if (!message.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.String) return null;
                    using (JsonDocument decoded = JsonDocument.Parse(Convert.FromBase64String(data.GetString() ?? String.Empty)))
                    {
                        return decoded.RootElement.TryGetProperty("packageName", out JsonElement package) && package.ValueKind == JsonValueKind.String ? package.GetString() : null;
                    }
                }
            }
            catch (Exception e) when (e is JsonException || e is FormatException)
            {
                return null;
            }
        }

        private static byte[] Base64Url(string text)
        {
            string padded = text.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }
            return Convert.FromBase64String(padded);
        }

        private async Task MediaConvertAsync(HttpContextBase ctx)
        {
            string raw = await RawAsync(ctx).ConfigureAwait(false);
            SnsMessage message;
            try
            {
                message = await Any().Integrations.SnsVerifier.VerifyAsync(raw, ctx.Token).ConfigureAwait(false);
            }
            catch (WebhookSignatureException e)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.Webhooks, "source", "mediaconvert", "outcome", "invalid_signature");
                await RouteHelper.SendErrorAsync(ctx, 400, "InvalidSignature", e.Message).ConfigureAwait(false);
                return;
            }
            string outcome = "ignored";
            try
            {
                if (message.Type == "SubscriptionConfirmation")
                {
                    await Any().Integrations.SnsVerifier.ConfirmSubscriptionAsync(message, ctx.Token).ConfigureAwait(false);
                    outcome = "subscribed";
                }
                else if (message.Type == "Notification" && TryReadJobEvent(message.Message, out string jobId, out string? mediaId, out VideoJobStateEnum state))
                {
                    string? owner = await _Directory.Database.Registry.FindMediaJobPropertyAsync(jobId, ctx.Token).ConfigureAwait(false);
                    PropertyRuntime? runtime = _Directory.Find(owner);
                    if (runtime != null && runtime.Record.Status != PropertyStatusEnum.Deleting)
                    {
                        bool changed = await runtime.Services.Media.Pipeline.HandleJobStateAsync(jobId, mediaId, state, ctx.Token).ConfigureAwait(false);
                        outcome = changed ? "processed" : "duplicate";
                    }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Any().Logger.Error("[WebhookRoutes] MediaConvert notification " + message.MessageId + " failed", e);
                outcome = "error";
            }
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.Webhooks, "source", "mediaconvert", "outcome", outcome);
            await RouteHelper.SendJsonAsync(ctx, 200, new MessageResponse { Message = outcome }).ConfigureAwait(false);
        }

        private ApplicationServices Any()
        {
            // The SNS verifier and logger are deployment-level (media settings); any property's instance serves.
            PropertyRuntime? runtime = _Directory.Default ?? _Directory.Runtimes.FirstOrDefault();
            if (runtime == null) throw new IntegrationNotConfiguredException("No property is registered.");
            return runtime.Services;
        }

        private static bool TryReadJobEvent(string message, out string jobId, out string? mediaId, out VideoJobStateEnum state)
        {
            jobId = String.Empty;
            mediaId = null;
            state = VideoJobStateEnum.Running;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(message))
                {
                    if (!doc.RootElement.TryGetProperty("detail", out JsonElement detail) || detail.ValueKind != JsonValueKind.Object) return false;
                    if (!detail.TryGetProperty("jobId", out JsonElement job) || job.ValueKind != JsonValueKind.String) return false;
                    jobId = job.GetString() ?? String.Empty;
                    string status = detail.TryGetProperty("status", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? String.Empty : String.Empty;
                    state = MediaConvertTranscoder.MapState(status);
                    if (detail.TryGetProperty("userMetadata", out JsonElement meta) && meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("mediaId", out JsonElement id) && id.ValueKind == JsonValueKind.String)
                        mediaId = id.GetString();
                    return jobId.Length > 0 && state != VideoJobStateEnum.Running;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        #endregion
    }
}
