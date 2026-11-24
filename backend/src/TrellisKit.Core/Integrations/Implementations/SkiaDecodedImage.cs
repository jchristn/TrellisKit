namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using SkiaSharp;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// A decoded image held as an sRGB SkiaSharp bitmap. Thread safety: not safe for concurrent use.
    /// </summary>
    public sealed class SkiaDecodedImage : IDecodedImage
    {
        #region Public-Members

        /// <inheritdoc />
        public int Width
        {
            get { return Bitmap.Width; }
        }

        /// <inheritdoc />
        public int Height
        {
            get { return Bitmap.Height; }
        }

        #endregion

        #region Internal-Members

        internal SKBitmap Bitmap { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        internal SkiaDecodedImage(SKBitmap bitmap)
        {
            Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Release the pixels.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            Bitmap.Dispose();
        }

        #endregion
    }
}
