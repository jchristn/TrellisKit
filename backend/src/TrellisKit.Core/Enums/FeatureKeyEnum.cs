namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Feature keys that a PartiallyGated property may list in access.gatedFeatures (docs/ACCESS_MODELS_CONTRACT.md
    /// section 1). In property.json and the API they are written in lower camel case (feed, members, forums).
    /// </summary>
    public enum FeatureKeyEnum
    {
        /// <summary>
        /// Feed posts and their comments, reactions, and media.
        /// </summary>
        Feed,

        /// <summary>
        /// Member profiles, mention search, and the member directory.
        /// </summary>
        Members,

        /// <summary>
        /// Forums (reserved until the forum routes exist).
        /// </summary>
        Forums,

        /// <summary>
        /// Library (reserved).
        /// </summary>
        Library,

        /// <summary>
        /// Events (reserved).
        /// </summary>
        Events,

        /// <summary>
        /// Directory (reserved).
        /// </summary>
        Directory,

        /// <summary>
        /// Messaging (reserved).
        /// </summary>
        Messaging,

        /// <summary>
        /// AI assistants (reserved).
        /// </summary>
        Assistants,

        /// <summary>
        /// Global search (POST /api/v1/search/enumerate), gated independently of forums.
        /// </summary>
        Search,

        /// <summary>
        /// Ideas board.
        /// </summary>
        Ideas,

        /// <summary>
        /// The member Training hub. Its access is the union of its sections' access.
        /// </summary>
        Training
    }
}
