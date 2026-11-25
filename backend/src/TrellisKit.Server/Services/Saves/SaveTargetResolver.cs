namespace TrellisKit.Server.Services
{
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Resolves one saved target type for the saves service (REST_API.md, Saves): whether the type is available in
    /// this property, whether a target exists, and what the caller may see of it. Register a resolver with
    /// <see cref="FavoriteService.RegisterResolver"/> to replace the engine default of a type. Implementations must be
    /// thread safe.
    /// </summary>
    public abstract class SaveTargetResolver
    {
        #region Public-Methods

        /// <summary>
        /// Whether saves of this type work in the property now (its content exists in the engine build and its feature
        /// is on). When false, saving is 404 NotAvailable and saved rows show as unavailable.
        /// </summary>
        /// <returns>True when supported.</returns>
        public abstract bool IsSupported();

        /// <summary>
        /// The summary of a target: null when it does not exist or was deleted; Available false (with no title, image,
        /// or link) when the caller cannot open it.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary or null.</returns>
        public abstract Task<FavoriteTargetSummary?> ResolveAsync(ContentActor actor, string targetId, CancellationToken token = default);

        #endregion
    }
}
