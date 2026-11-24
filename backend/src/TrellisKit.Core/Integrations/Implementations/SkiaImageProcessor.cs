namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using SkiaSharp;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// SkiaSharp image processor (MIT). Decodes JPEG, PNG, and WebP into sRGB pixels, applies the EXIF orientation
    /// to the pixels (codec origin, or the JPEG APP1 tag read directly when the codec reports none), and encodes
    /// outputs from pixels only, so no EXIF, GPS, XMP, IPTC, or maker notes are carried over. SkiaSharp's JPEG encoder
    /// writes baseline (not progressive) JPEG; there is no progressive option in SkiaSharp 3.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class SkiaImageProcessor : IImageProcessor
    {
        #region Public-Members

        /// <summary>
        /// Largest edge of the downscaled copy used for blurhash. Default 64. Minimum 8, maximum 256.
        /// </summary>
        public int BlurhashSampleEdge
        {
            get { return _BlurhashSampleEdge; }
            set { _BlurhashSampleEdge = Math.Clamp(value, 8, 256); }
        }

        #endregion

        #region Private-Members

        private int _BlurhashSampleEdge = 64;
        private static readonly SKSamplingOptions _Sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IDecodedImage Decode(byte[] data, long maxPixels)
        {
            ArgumentNullException.ThrowIfNull(data);
            using (SKData skData = SKData.CreateCopy(data))
            using (SKCodec? codec = SKCodec.Create(skData))
            {
                if (codec == null) throw new MediaProcessingException(MediaFailureReasonEnum.Corrupt, "The image could not be decoded.");
                SKEncodedImageFormat format = codec.EncodedFormat;
                if (format != SKEncodedImageFormat.Jpeg && format != SKEncodedImageFormat.Png && format != SKEncodedImageFormat.Webp)
                    throw new MediaProcessingException(MediaFailureReasonEnum.UnsupportedFormat, "Only JPEG, PNG, and WebP images are accepted.");
                int width = codec.Info.Width;
                int height = codec.Info.Height;
                if (width < 1 || height < 1) throw new MediaProcessingException(MediaFailureReasonEnum.Corrupt, "The image has no pixels.");
                if ((long)width * height > maxPixels) throw new MediaProcessingException(MediaFailureReasonEnum.ImageTooLarge, "The image has more than " + maxPixels + " pixels.");

                SKImageInfo info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
                SKBitmap bitmap = new SKBitmap(info);
                SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    bitmap.Dispose();
                    throw new MediaProcessingException(MediaFailureReasonEnum.Corrupt, "The image could not be decoded (" + result + ").");
                }

                int orientation = (int)codec.EncodedOrigin;
                if (orientation <= 1 && format == SKEncodedImageFormat.Jpeg) orientation = ExifOrientationReader.Read(data);
                if (orientation <= 1 || orientation > 8) return new SkiaDecodedImage(bitmap);
                using (bitmap)
                {
                    return new SkiaDecodedImage(Orient(bitmap, orientation));
                }
            }
        }

        /// <inheritdoc />
        public IDecodedImage Resize(IDecodedImage image, int maxLongEdge)
        {
            SKBitmap source = Unwrap(image);
            if (maxLongEdge < 1) throw new ArgumentOutOfRangeException(nameof(maxLongEdge), "The long edge must be 1 or more.");
            int longEdge = Math.Max(source.Width, source.Height);
            int width = source.Width;
            int height = source.Height;
            if (longEdge > maxLongEdge)
            {
                double scale = maxLongEdge / (double)longEdge;
                width = Math.Max(1, (int)Math.Round(source.Width * scale));
                height = Math.Max(1, (int)Math.Round(source.Height * scale));
            }
            SKBitmap target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
            using (SKCanvas canvas = new SKCanvas(target))
            using (SKImage sourceImage = SKImage.FromBitmap(source))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawImage(sourceImage, new SKRect(0, 0, width, height), _Sampling);
            }
            return new SkiaDecodedImage(target);
        }

        /// <inheritdoc />
        public EncodedImage EncodeWebp(IDecodedImage image, int quality)
        {
            SKBitmap source = Unwrap(image);
            using (SKImage skImage = SKImage.FromBitmap(source))
            using (SKData data = skImage.Encode(SKEncodedImageFormat.Webp, Math.Clamp(quality, 1, 100)))
            {
                return new EncodedImage { Data = data.ToArray(), Format = "webp", ContentType = "image/webp", Width = source.Width, Height = source.Height };
            }
        }

        /// <inheritdoc />
        public EncodedImage EncodeJpeg(IDecodedImage image, int quality)
        {
            SKBitmap source = Unwrap(image);
            using (SKBitmap opaque = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb())))
            {
                using (SKCanvas canvas = new SKCanvas(opaque))
                {
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
                }
                using (SKImage skImage = SKImage.FromBitmap(opaque))
                using (SKData data = skImage.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100)))
                {
                    return new EncodedImage { Data = data.ToArray(), Format = "jpeg", ContentType = "image/jpeg", Width = source.Width, Height = source.Height };
                }
            }
        }

        /// <inheritdoc />
        public string Blurhash(IDecodedImage image)
        {
            Unwrap(image);
            using (IDecodedImage small = Resize(image, _BlurhashSampleEdge))
            {
                SKBitmap bitmap = Unwrap(small);
                byte[] rgba = new byte[bitmap.Width * bitmap.Height * 4];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        SKColor color = bitmap.GetPixel(x, y);
                        int p = (y * bitmap.Width + x) * 4;
                        rgba[p] = color.Red;
                        rgba[p + 1] = color.Green;
                        rgba[p + 2] = color.Blue;
                        rgba[p + 3] = color.Alpha;
                    }
                }
                return BlurhashEncoder.Encode(rgba, bitmap.Width, bitmap.Height, 4, 3);
            }
        }

        #endregion

        #region Private-Methods

        private static SKBitmap Unwrap(IDecodedImage image)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image is not SkiaDecodedImage skia) throw new ArgumentException("The image was not decoded by this processor.", nameof(image));
            return skia.Bitmap;
        }

        private static SKBitmap Orient(SKBitmap source, int orientation)
        {
            int w = source.Width;
            int h = source.Height;
            bool swap = orientation >= 5;
            int newWidth = swap ? h : w;
            int newHeight = swap ? w : h;
            SKMatrix matrix;
            switch (orientation)
            {
                case 2:
                    matrix = new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1);
                    break;
                case 3:
                    matrix = new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1);
                    break;
                case 4:
                    matrix = new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1);
                    break;
                case 5:
                    matrix = new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1);
                    break;
                case 6:
                    matrix = new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1);
                    break;
                case 7:
                    matrix = new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1);
                    break;
                default:
                    matrix = new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1);
                    break;
            }
            SKBitmap target = new SKBitmap(new SKImageInfo(newWidth, newHeight, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
            using (SKCanvas canvas = new SKCanvas(target))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.SetMatrix(matrix);
                canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
            }
            return target;
        }

        #endregion
    }
}
