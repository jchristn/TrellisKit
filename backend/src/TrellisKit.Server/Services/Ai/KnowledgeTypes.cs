namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Which knowledge source target types an assistant may use (docs/AI_CONTRACT.md section 7.1,
    /// docs/PHASE4_CONTRACT.md section 5.11): FeedPost always; Lesson, Event, Series, and Venue while a content target
    /// provider serves the type and supplies knowledge for it.
    /// </summary>
    public static class KnowledgeTypes
    {
        #region Public-Members

        /// <summary>
        /// The Phase 4 knowledge types served through content target providers.
        /// </summary>
        public static readonly IReadOnlyList<TargetTypeEnum> ProviderTypes = new[] { TargetTypeEnum.Lesson, TargetTypeEnum.Event, TargetTypeEnum.Series, TargetTypeEnum.Venue };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a provider knowledge type is available for a property.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="targetType">Target type.</param>
        /// <returns>True when available.</returns>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static bool IsAvailable(ApplicationServices services, TargetTypeEnum targetType)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (!ProviderTypes.Contains(targetType)) return false;
            ContentTargetProvider? provider = services.ContentTargets.Find(targetType);
            return provider != null && provider.ProvidesKnowledge(targetType);
        }

        /// <summary>
        /// Every knowledge type available for a property, FeedPost first.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <returns>Type names.</returns>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static List<string> Available(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<string> names = new List<string> { TargetTypeEnum.FeedPost.ToString() };
            names.AddRange(ProviderTypes.Where(t => IsAvailable(services, t)).Select(t => t.ToString()));
            return names;
        }

        #endregion
    }
}
