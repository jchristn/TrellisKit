namespace TrellisKit.Server.Services
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Account export section "profile": the member's Me payload (fixed-bound lists only).
    /// </summary>
    public sealed class ProfileExportSection : IAccountExportSection
    {
        #region Public-Members

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public string Name
        {
            get { return "profile"; }
        }

        #endregion

        #region Private-Members

        private readonly MemberViewBuilder _Views;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the section.
        /// </summary>
        /// <param name="views">Member view builder.</param>
        /// <exception cref="ArgumentNullException">The builder is null.</exception>
        public ProfileExportSection(MemberViewBuilder views)
        {
            _Views = views ?? throw new ArgumentNullException(nameof(views));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write the profile object.
        /// </summary>
        /// <param name="writer">Writer positioned after the property name.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The writer is null.</exception>
        public async Task WriteAsync(Utf8JsonWriter writer, string userId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(writer);
            MeResponse me = await _Views.BuildMeAsync(userId, token).ConfigureAwait(false);
            if (_Views.ProfileValues != null)
            {
                // The export carries every value the member has, disabled fields included, except staff-only fields.
                TrellisKit.Core.Models.ProfileValueSet set = await _Views.ProfileValues.ReadAsync(userId, token).ConfigureAwait(false);
                me.Profile = await _Views.ProfileValues.MapAsync(set, TrellisKit.Core.Enums.ProfileMapKindEnum.Export, TrellisKit.Core.Enums.ProfileViewerEnum.Self, token).ConfigureAwait(false);
            }
            JsonSerializer.Serialize(writer, me, ApiJson.Api);
        }

        #endregion
    }
}
