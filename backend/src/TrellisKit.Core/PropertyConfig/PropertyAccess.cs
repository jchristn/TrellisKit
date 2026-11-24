namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The access section of property.json (docs/TRELLISKIT.md section 7, docs/ACCESS_MODELS_CONTRACT.md section 1). It
    /// seeds the property's stored access configuration; after seeding, administrators edit it in the admin portal and
    /// in SingleProperty a change of this section on disk wins again (as profile fields do). Absent means FullyGated.
    /// </summary>
    public class PropertyAccess
    {
        #region Public-Members

        /// <summary>
        /// Access model. Default FullyGated.
        /// </summary>
        public AccessModelEnum Model { get; set; } = AccessModelEnum.FullyGated;

        /// <summary>
        /// Whether signed-out callers may read Public objects through the normal content routes. Ignored in
        /// FullyGated. Default false.
        /// </summary>
        public bool AnonymousBrowsing { get; set; } = false;

        /// <summary>
        /// Feature keys (lower camel case, for example feed, members) that need a membership to write in. Used only in
        /// PartiallyGated. Never null; default empty.
        /// </summary>
        public List<string> GatedFeatures
        {
            get { return _GatedFeatures; }
            set { _GatedFeatures = value ?? new List<string>(); }
        }

        /// <summary>
        /// Audience of new objects, or null for the model default (Members, or SignedIn in Open). Members is rejected
        /// in Open.
        /// </summary>
        public ContentAudienceEnum? DefaultAudience { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _GatedFeatures = new List<string>();

        #endregion
    }
}
