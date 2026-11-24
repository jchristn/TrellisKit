namespace TrellisKit.Server.Routes
{
    using System;
    using System.Net;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// One-click email unsubscribe (Phase 2 contract 8.9, RFC 8058). Anonymous, PreAuthentication: the signed token in
    /// the query string identifies the member and category; no session is needed. POST (the List-Unsubscribe-Post
    /// target) returns JSON. GET (the link in the message) turns the preference off as well and returns a minimal HTML
    /// confirmation page with the property name; turning it off on GET is deliberate so the link works in one tap, and
    /// it is idempotent, so a mail scanner fetching the link only repeats the member's own opt-out. Invalid tokens return
    /// 400 (JSON InvalidToken on POST, an HTML page on GET). Limited per IP by rateLimits.unsubscribePerHour.
    /// </summary>
    public class EmailUnsubscribeRoutes
    {
        #region Private-Members

        private const string _Path = Constants.ApiPrefix + "/email/unsubscribe";
        private const string _Tag = "Email";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public EmailUnsubscribeRoutes(PropertyDirectory directory)
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
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, _Path, PostAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("One-click unsubscribe", _Tag, "RFC 8058 target of List-Unsubscribe-Post. Turns the token's category email off. Anonymous.")
                    .QueryParam("token", "Signed unsubscribe token from the email").Returns("{ unsubscribed, category }").Resp(400, "InvalidToken").Resp(429, "RateLimited"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, _Path, GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unsubscribe page", _Tag, "The link in the email: turns the category email off and shows a confirmation page. Anonymous.")
                    .QueryParam("token", "Signed unsubscribe token from the email").File("text/html", "Confirmation page").Resp(400, "Invalid link page").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private void Limit(HttpContextBase ctx)
        {
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            _Directory.For(ctx).Limits.EnforceIp(info.SourceIp, "email-unsubscribe", _Directory.For(ctx).Settings.RateLimits.UnsubscribePerHour, RateLimitService.Hour);
        }

        private async Task PostAsync(HttpContextBase ctx)
        {
            Limit(ctx);
            NotificationCategoryEnum category = await _Directory.For(ctx).FeedPostEmails.UnsubscribeAsync(RouteHelper.Query(ctx, "token"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new UnsubscribeResult { Unsubscribed = true, Category = category }).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            Limit(ctx);
            string name = WebUtility.HtmlEncode(_Directory.For(ctx).Property.Name);
            try
            {
                await _Directory.For(ctx).FeedPostEmails.UnsubscribeAsync(RouteHelper.Query(ctx, "token"), ctx.Token).ConfigureAwait(false);
            }
            catch (ApiErrorException e) when (e.StatusCode == 400)
            {
                await SendHtmlAsync(ctx, 400, name, "This unsubscribe link is not valid", "Change which emails you get in the notification settings of the app.").ConfigureAwait(false);
                return;
            }
            await SendHtmlAsync(ctx, 200, name, "You are unsubscribed", "You will no longer get emails about new posts. You can turn them back on in your notification settings.").ConfigureAwait(false);
        }

        private static async Task SendHtmlAsync(HttpContextBase ctx, int status, string encodedName, string heading, string text)
        {
            string html = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + encodedName +
                "</title></head><body style=\"font-family:-apple-system,Segoe UI,Helvetica,Arial,sans-serif;max-width:520px;margin:48px auto;padding:0 16px\"><p style=\"color:#555555\">" + encodedName +
                "</p><h1 style=\"font-size:22px\">" + WebUtility.HtmlEncode(heading) + "</h1><p>" + WebUtility.HtmlEncode(text) + "</p></body></html>";
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.Send(html, ctx.Token).ConfigureAwait(false);
        }

        #endregion
    }
}
