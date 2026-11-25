namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Resolves saved forum threads with the forums' own visibility rules (forum audience and moderator scope,
    /// moderation state). Not supported when features.forums is off.
    /// </summary>
    public class ThreadSaveResolver : SaveTargetResolver
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
        public ThreadSaveResolver(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool IsSupported()
        {
            return _Services.Property.Property.Features.Forums;
        }

        /// <inheritdoc />
        public override async Task<FavoriteTargetSummary?> ResolveAsync(ContentActor actor, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (String.IsNullOrEmpty(targetId)) return null;
            ForumThread? thread = await _Services.Database.Threads.ReadAsync(targetId, token).ConfigureAwait(false);
            if (thread == null || thread.DeletedUtc != null || thread.PurgedUtc != null) return null;
            ThreadContext? context = await _Services.Forums.Access.LoadAsync(thread.Id, token).ConfigureAwait(false);
            if (context == null || ForumAccessService.StateOf(context.Thread) != ThreadStateEnum.Published || !ForumAccessService.CanSeeThread(context, actor))
                return FavoriteTargetSummary.Unavailable();
            return new FavoriteTargetSummary { Available = true, Title = thread.Title, Href = ForumNotificationService.ThreadPath(thread.Id, null) };
        }

        #endregion
    }
}
