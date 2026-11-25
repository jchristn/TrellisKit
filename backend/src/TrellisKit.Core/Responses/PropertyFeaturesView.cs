namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// The features object of GET /api/v1/property: the property's feature switches from property.json plus
    /// engine-computed capability flags. search is the effective value (features.search and the deployment setting
    /// search.enabled).
    /// </summary>
    public class PropertyFeaturesView : PropertyFeatures
    {
        #region Public-Members

        /// <summary>
        /// Where the mobile app shows assistants when ai is on: Menu (a profile menu entry, the default) or Tab
        /// (setting ai.mobilePlacement; docs/AI_CONTRACT.md resolved decision 15).
        /// </summary>
        public string AiPlacement { get; set; } = "Menu";

        /// <summary>
        /// Whether verified ranks, curricula, and attendance are on (server setting ranks.enabled; docs/TRELLISKIT.md
        /// section 15). Default false.
        /// </summary>
        public bool Ranks { get; set; } = false;

        /// <summary>
        /// Whether clients show verified or self-reported labels next to Rank fields (server setting
        /// ranks.showVerificationLabels). Default true.
        /// </summary>
        public bool RankVerificationLabels { get; set; } = true;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the view from the property's feature switches.
        /// </summary>
        /// <param name="features">Feature switches.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The features are null.</exception>
        public static PropertyFeaturesView From(PropertyFeatures features)
        {
            ArgumentNullException.ThrowIfNull(features);
            return new PropertyFeaturesView
            {
                SignupCodes = features.SignupCodes,
                Directory = features.Directory,
                Events = features.Events,
                Library = features.Library,
                Messaging = features.Messaging,
                PublicSamples = features.PublicSamples,
                Forums = features.Forums,
                Search = features.Search,
                Support = features.Support,
                Ai = features.Ai
            };
        }

        #endregion
    }
}
