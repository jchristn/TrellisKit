namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Resolves a saved target through the property's <see cref="ContentTargetRegistry"/>: the target type of the same
    /// name, served by whichever provider registered it (<see cref="ContentTargetProvider.SaveSummaryAsync"/>). A type
    /// without a registered provider (for example a catalog that is not present) is not supported, so its saves show as
    /// unavailable until a provider registers.
    /// </summary>
    public class ContentTargetSaveResolver : SaveTargetResolver
    {
        #region Private-Members

        private readonly ContentTargetRegistry _Registry;
        private readonly string _TypeName;
        private readonly Func<bool> _Enabled;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="registry">Content target registry.</param>
        /// <param name="typeName">Content target type name (for example Lesson or CatalogEntry).</param>
        /// <param name="enabled">The feature switch of the type.</param>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        public ContentTargetSaveResolver(ContentTargetRegistry registry, string typeName, Func<bool> enabled)
        {
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (String.IsNullOrEmpty(typeName)) throw new ArgumentNullException(nameof(typeName));
            _TypeName = typeName;
            _Enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool IsSupported()
        {
            return _Enabled() && Provider(out TargetTypeEnum _) != null;
        }

        /// <inheritdoc />
        public override async Task<FavoriteTargetSummary?> ResolveAsync(ContentActor actor, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ContentTargetProvider? provider = Provider(out TargetTypeEnum type);
            if (provider == null) return null;
            return await provider.SaveSummaryAsync(actor, type, targetId, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private ContentTargetProvider? Provider(out TargetTypeEnum type)
        {
            if (!Enum.TryParse(_TypeName, false, out type) || !Enum.IsDefined(type)) return null;
            return _Registry.Find(type);
        }

        #endregion
    }
}
