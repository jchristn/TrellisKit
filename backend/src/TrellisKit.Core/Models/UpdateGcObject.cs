namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A stored live update object that may become unreferenced (table updategcobjects): a private source map of a
    /// deleted draft update, or a content asset image under app-content/. UpdateAssetGcWorker deletes the object once
    /// nothing references it and it waited out updates.assetGcRetentionDays since it was queued.
    /// </summary>
    public class UpdateGcObject
    {
        #region Public-Members

        /// <summary>
        /// Source map kind.
        /// </summary>
        public const string SourceMapKind = "SourceMap";

        /// <summary>
        /// Content asset kind.
        /// </summary>
        public const string ContentAssetKind = "ContentAsset";

        /// <summary>
        /// SourceMap or ContentAsset.
        /// </summary>
        public string Kind { get; set; } = SourceMapKind;

        /// <summary>
        /// SHA-256 hex of the object.
        /// </summary>
        public string Sha256Hex { get; set; } = String.Empty;

        /// <summary>
        /// Object key in the update store.
        /// </summary>
        public string StorageKey { get; set; } = String.Empty;

        /// <summary>
        /// Size in bytes (0 when unknown).
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// When the object was queued or last found referenced.
        /// </summary>
        public DateTime QueuedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
