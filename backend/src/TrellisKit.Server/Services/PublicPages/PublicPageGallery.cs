namespace TrellisKit.Server.Services.PublicPages
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    public partial class PublicPageService
    {
        /// <summary>Re-check public page and moderation state before each gallery file request.</summary>
        public async Task<bool> GalleryMediaVisibleAsync(MediaAttachment media, CancellationToken token = default)
        {
            if (!Config().Enabled || media.IsDeleted || media.IsPrivate || media.IsRetained || media.Status != MediaStatusEnum.Ready) return false;
            if (media.AttachedTargetType == TargetTypeEnum.VenueMedia)
            {
                VenueMediaItem? item = await _Services.Database.DirectoryListings.Media.ReadAsync(media.AttachedTargetId ?? String.Empty, token).ConfigureAwait(false);
                return item != null && item.MediaId == media.Id && item.IsLive &&
                    await _Services.Directory.Contributions.MemberMediaEnabledAsync(token).ConfigureAwait(false) &&
                    VenueHasPage(await _Services.Database.DirectoryListings.Venues.ReadAsync(item.VenueId, token).ConfigureAwait(false));
            }
            if (media.AttachedTargetType == TargetTypeEnum.ProfileMedia)
            {
                ProfileMediaItem? item = await _Services.Database.ProfileMedia.ReadAsync(media.AttachedTargetId ?? String.Empty, token).ConfigureAwait(false);
                if (item == null || item.MediaId != media.Id || !item.IsLive) return false;
                ProfilePage? page = await _Services.Database.PublicPages.ReadProfileAsync(item.ProfileUserId, token).ConfigureAwait(false);
                User? user = await _Services.Database.Users.ReadAsync(item.ProfileUserId, token).ConfigureAwait(false);
                return page != null && page.Enabled && !String.IsNullOrWhiteSpace(page.Slug) && user != null && !user.IsDeleted && !user.IsBanned;
            }
            return false;
        }

        private async Task<string> GalleryHtmlAsync(TargetTypeEnum type, string ownerId, CancellationToken token)
        {
            List<KeyValuePair<string, string>> items;
            bool more;
            EnumerationQuery query = new EnumerationQuery { MaxResults = 24 };
            if (type == TargetTypeEnum.ProfileMedia)
            {
                EnumerationRequest request = _Services.Enumeration.Prepare(query, ProfileMediaCollections.Items, false, ownerId);
                EnumerationResult<ProfileMediaItem> page = await _Services.Database.ProfileMedia.EnumerateAsync(request, new ProfileMediaFilter { ProfileUserId = ownerId }, token).ConfigureAwait(false);
                items = page.Objects.Select(i => new KeyValuePair<string, string>(i.Id, i.MediaId)).ToList();
                more = !page.EndOfResults;
            }
            else
            {
                if (!await _Services.Directory.Contributions.MemberMediaEnabledAsync(token).ConfigureAwait(false)) return String.Empty;
                EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.VenueMedia, false, ownerId);
                EnumerationResult<VenueMediaItem> page = await _Services.Database.DirectoryListings.Media.EnumerateAsync(request, new VenueContributionFilter { VenueId = ownerId }, token).ConfigureAwait(false);
                items = page.Objects.Select(i => new KeyValuePair<string, string>(i.Id, i.MediaId)).ToList();
                more = !page.EndOfResults;
            }
            string memberPath = type == TargetTypeEnum.ProfileMedia ? "/app/members/" + ownerId : "/app/venues/" + ownerId;
            StringBuilder html = new StringBuilder("<section class=\"card\"><h2>" + PublicPageRenderer.Attr(PublicPageText.Multimedia) + "</h2><ul class=\"photos\">");
            int count = 0;
            foreach (KeyValuePair<string, string> item in items)
            {
                MediaAttachment? media = await _Services.Database.Media.ReadAsync(item.Value, token).ConfigureAwait(false);
                if (media == null || !await GalleryMediaVisibleAsync(media, token).ConfigureAwait(false)) continue;
                List<MediaVariant> variants = await _Services.Database.Media.ReadVariantsAsync(media.Id, token).ConfigureAwait(false);
                MediaView view = _Services.Media.Urls.Build(media, variants, null, file => "/api/v1/media/gallery/" + media.Id + "/" + file);
                string? image = view.Image?.Variants.Values.Select(v => v.WebpUrl ?? v.JpegUrl).FirstOrDefault(u => u != null);
                if (image == null && view.Video == null) continue;
                html.Append("<li>");
                if (image != null) html.Append(PublicPageRenderer.Img(image, view.AltText ?? PublicPageText.Photo, "photo", true));
                else
                {
                    html.Append("<video controls playsinline preload=\"none\" aria-label=\"").Append(PublicPageRenderer.Attr(view.AltText ?? PublicPageText.Video))
                        .Append("\" poster=\"").Append(PublicPageRenderer.Attr(view.Video!.PosterUrl ?? String.Empty)).Append("\" data-hls=\"")
                        .Append(PublicPageRenderer.Attr(view.Video.HlsUrl ?? String.Empty)).Append("\"");
                    string? source = view.Video.Mp4Url ?? view.Video.HlsUrl;
                    if (source != null) html.Append(" src=\"").Append(PublicPageRenderer.Attr(source)).Append("\"");
                    html.Append("></video>");
                }
                html.Append("<p>").Append(Link(memberPath + "?reportMedia=" + Uri.EscapeDataString(item.Key), PublicPageText.ReportMedia)).Append("</p></li>");
                count++;
            }
            html.Append("</ul>");
            if (count == 0) html.Append("<p>").Append(PublicPageRenderer.Attr(PublicPageText.NoMedia)).Append("</p>");
            html.Append("<p>").Append(Link(memberPath, more ? PublicPageText.MoreMedia : PublicPageText.ManageMedia)).Append("</p></section>");
            return html.ToString();
        }
    }
}
