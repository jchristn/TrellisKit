namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Resolves saved feed posts with the feed's own visibility rules (audience, locked stubs, moderation, blocks).
    /// </summary>
    public class FeedPostSaveResolver : SaveTargetResolver
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public FeedPostSaveResolver(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool IsSupported()
        {
            return true;
        }

        /// <inheritdoc />
        public override async Task<FavoriteTargetSummary?> ResolveAsync(ContentActor actor, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (String.IsNullOrEmpty(targetId)) return null;
            FeedPost? post = await _Services.Database.FeedPosts.ReadAsync(targetId, token).ConfigureAwait(false);
            if (post == null || post.DeletedUtc != null || post.PurgedUtc != null) return null;
            try
            {
                await _Services.ContentAccess.RequireVisiblePostAsync(actor, post.Id, token).ConfigureAwait(false);
            }
            catch (ApiErrorException)
            {
                return FavoriteTargetSummary.Unavailable();
            }
            if (!String.IsNullOrEmpty(actor.UserId) && (await _Services.Blocks.RelatedAsync(actor.UserId, token).ConfigureAwait(false)).Contains(post.AuthorUserId))
                return FavoriteTargetSummary.Unavailable();
            string title = !String.IsNullOrWhiteSpace(post.Title) ? post.Title.Trim() : post.Excerpt;
            return new FavoriteTargetSummary { Available = true, Title = title, Href = "/feed/" + post.Id };
        }

        #endregion
    }
}
