namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The content target providers of one property (Phase 4 types plugged into comments, reactions, reports,
    /// moderation, and search). Providers register during module construction; reads are thread safe afterwards.
    /// </summary>
    public class ContentTargetRegistry
    {
        #region Public-Members

        /// <summary>
        /// Every registered provider, in registration order.
        /// </summary>
        public IReadOnlyList<ContentTargetProvider> Providers
        {
            get { return _Providers; }
        }

        /// <summary>
        /// Every target type served by a provider.
        /// </summary>
        public IReadOnlyCollection<TargetTypeEnum> TargetTypes
        {
            get { return _ByType.Keys; }
        }

        #endregion

        #region Private-Members

        private readonly List<ContentTargetProvider> _Providers = new List<ContentTargetProvider>();
        private readonly Dictionary<TargetTypeEnum, ContentTargetProvider> _ByType = new Dictionary<TargetTypeEnum, ContentTargetProvider>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a provider.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <exception cref="ArgumentNullException">The provider is null.</exception>
        /// <exception cref="InvalidOperationException">A type is already served by another provider.</exception>
        public void Register(ContentTargetProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            foreach (TargetTypeEnum type in provider.TargetTypes)
            {
                if (_ByType.TryGetValue(type, out ContentTargetProvider? existing) && !ReferenceEquals(existing, provider))
                    throw new InvalidOperationException("Target type " + type + " already has a content target provider.");
            }
            foreach (TargetTypeEnum type in provider.TargetTypes) _ByType[type] = provider;
            if (!_Providers.Contains(provider)) _Providers.Add(provider);
        }

        /// <summary>
        /// The provider of a type, or null.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>Provider or null.</returns>
        public ContentTargetProvider? Find(TargetTypeEnum targetType)
        {
            return _ByType.TryGetValue(targetType, out ContentTargetProvider? provider) ? provider : null;
        }

        /// <summary>
        /// The provider whose <see cref="ContentTargetProvider.ScopeType"/> is a moderation scope type, or null.
        /// </summary>
        /// <param name="scopeType">Scope type (Directory, Events, Library).</param>
        /// <returns>Provider or null.</returns>
        public ContentTargetProvider? ForScope(string? scopeType)
        {
            if (String.IsNullOrEmpty(scopeType)) return null;
            foreach (KeyValuePair<TargetTypeEnum, ContentTargetProvider> pair in _ByType)
            {
                if (String.Equals(pair.Value.ScopeType(pair.Key), scopeType, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }
            return null;
        }

        /// <summary>
        /// Remove the provider of a type (tests swap providers this way). A provider left without types is removed.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>The removed provider, or null when the type had none.</returns>
        public ContentTargetProvider? Unregister(TargetTypeEnum targetType)
        {
            if (!_ByType.TryGetValue(targetType, out ContentTargetProvider? provider)) return null;
            _ByType.Remove(targetType);
            if (!_ByType.Values.Any(p => ReferenceEquals(p, provider))) _Providers.Remove(provider);
            return provider;
        }

        /// <summary>
        /// The moderation scope entries a caller has through the registered providers: a scope type ("Directory",
        /// "Events", "Library") when the caller moderates the scope, or "{scope}/Comment" when the caller moderates only
        /// the comments under a type of the scope (forum moderators on library comments). Report queries read these
        /// entries (ModerationMethodsBase.ScopeCondition).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Scope entries (distinct, ordered).</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public List<string> ScopesFor(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            SortedSet<string> whole = new SortedSet<string>(StringComparer.Ordinal);
            SortedSet<string> comments = new SortedSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<TargetTypeEnum, ContentTargetProvider> pair in _ByType)
            {
                string scope = pair.Value.ScopeType(pair.Key);
                if (pair.Value.Moderates(actor, scope)) whole.Add(scope);
                else if (pair.Value.ModeratesComments(actor, pair.Key)) comments.Add(scope);
            }
            List<string> result = whole.ToList();
            result.AddRange(comments.Where(c => !whole.Contains(c)).Select(c => c + "/Comment"));
            return result;
        }

        /// <summary>
        /// Fill an actor's provider moderation facts (<see cref="ContentActor.ModeratedTargetScopes"/> and
        /// <see cref="ContentActor.ModeratedCommentHosts"/>). Called by <see cref="ActorService"/> for every resolved
        /// member.
        /// </summary>
        /// <param name="actor">Actor.</param>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public void Decorate(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            HashSet<string> scopes = new HashSet<string>(StringComparer.Ordinal);
            HashSet<TargetTypeEnum> hosts = new HashSet<TargetTypeEnum>();
            foreach (KeyValuePair<TargetTypeEnum, ContentTargetProvider> pair in _ByType)
            {
                string scope = pair.Value.ScopeType(pair.Key);
                if (pair.Value.Moderates(actor, scope)) scopes.Add(scope);
                if (pair.Value.ModeratesComments(actor, pair.Key)) hosts.Add(pair.Key);
            }
            actor.ModeratedTargetScopes = scopes;
            actor.ModeratedCommentHosts = hosts;
        }

        #endregion
    }
}
