namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;

    /// <summary>
    /// Which engine palette presets a property's members may choose (table propertypalettechoices; docs/TRELLISKIT.md
    /// section 2.1). Without a stored row every preset is allowed (mode All, revision 0).
    /// Thread safety: not thread safe; snapshots served to requests are never mutated.
    /// </summary>
    public class MemberPaletteChoices
    {
        #region Public-Members

        /// <summary>
        /// All, None, or Selected.
        /// </summary>
        public MemberPaletteModeEnum Mode { get; set; } = MemberPaletteModeEnum.All;

        /// <summary>
        /// Allowed preset ids when Mode is Selected (catalog order), else empty. Fixed-bound: the catalog size.
        /// </summary>
        public List<string> PresetIds
        {
            get { return _PresetIds; }
            set { _PresetIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// Revision, 0 until an administrator changes the choices, then incremented by every change. Part of the
        /// GET /api/v1/property ETag.
        /// </summary>
        public int Revision
        {
            get { return _Revision; }
            set { _Revision = value < 0 ? 0 : value; }
        }

        /// <summary>
        /// Who changed the choices last, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When the row was first stored (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the choices last changed (UTC), or null when never changed.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _PresetIds = new List<string>();
        private int _Revision = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether members may choose a preset.
        /// </summary>
        /// <param name="presetId">Preset id, or null.</param>
        /// <returns>True when the id names a catalog preset that the mode allows.</returns>
        public bool Allows(string? presetId)
        {
            if (String.IsNullOrEmpty(presetId) || BrandPresetCatalog.Find(presetId) == null) return false;
            switch (Mode)
            {
                case MemberPaletteModeEnum.All: return true;
                case MemberPaletteModeEnum.Selected: return _PresetIds.Contains(presetId, StringComparer.Ordinal);
                default: return false;
            }
        }

        /// <summary>
        /// The presets members may choose, in catalog order (copies).
        /// </summary>
        /// <returns>Presets.</returns>
        public List<BrandPreset> Allowed()
        {
            return BrandPresetCatalog.All.Where(p => Allows(p.Id)).Select(p => p.Clone()).ToList();
        }

        /// <summary>
        /// A copy of these choices.
        /// </summary>
        /// <returns>Copy.</returns>
        public MemberPaletteChoices Clone()
        {
            return new MemberPaletteChoices
            {
                Mode = Mode,
                PresetIds = _PresetIds.ToList(),
                Revision = _Revision,
                UpdatedByUserId = UpdatedByUserId,
                CreatedUtc = CreatedUtc,
                UpdatedUtc = UpdatedUtc
            };
        }

        #endregion
    }
}
