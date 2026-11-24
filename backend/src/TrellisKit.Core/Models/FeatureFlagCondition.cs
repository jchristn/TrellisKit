namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One condition of a targeting rule (table featureflagconditions with featureflagconditionvalues).
    /// </summary>
    public class FeatureFlagCondition
    {
        /// <summary>
        /// Identifier (ffc_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Attribute (platform, client, appVersion, updateId, runtimeVersion, channel, signedIn, membership, userRole, userLabels, userTag, userId, profileField, country, date).
        /// </summary>
        public string Attribute { get; set; } = String.Empty;

        /// <summary>
        /// Operator (In, NotIn, SemverRange, Is, HasAny, HasAll, HasNone, Equals, HasKey, Before, After).
        /// </summary>
        public string Operator { get; set; } = String.Empty;

        /// <summary>
        /// Tag key or profile field key, or null.
        /// </summary>
        public string? Argument { get; set; } = null;

        /// <summary>
        /// Values.
        /// </summary>
        public List<string> Values { get; set; } = new List<string>();
    }
}
