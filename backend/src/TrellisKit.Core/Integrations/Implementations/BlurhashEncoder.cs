namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Text;

    /// <summary>
    /// Blurhash encoder (https://blurha.sh algorithm): a DCT of the image in linear light, quantized and encoded in
    /// base 83. Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class BlurhashEncoder
    {
        #region Private-Members

        private const string _Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode unpremultiplied RGBA pixels.
        /// </summary>
        /// <param name="rgba">Pixels, 4 bytes per pixel, row major.</param>
        /// <param name="width">Width. Minimum 1.</param>
        /// <param name="height">Height. Minimum 1.</param>
        /// <param name="componentsX">Horizontal components, 1 to 9. Default 4.</param>
        /// <param name="componentsY">Vertical components, 1 to 9. Default 3.</param>
        /// <returns>Blurhash string (28 characters for 4x3).</returns>
        /// <exception cref="ArgumentNullException">The pixels are null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A dimension or component count is out of range, or the buffer is too small.</exception>
        public static string Encode(byte[] rgba, int width, int height, int componentsX = 4, int componentsY = 3)
        {
            ArgumentNullException.ThrowIfNull(rgba);
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be 1 or more.");
            if (componentsX < 1 || componentsX > 9 || componentsY < 1 || componentsY > 9) throw new ArgumentOutOfRangeException(nameof(componentsX), "Components must be 1 to 9.");
            if (rgba.Length < (long)width * height * 4) throw new ArgumentOutOfRangeException(nameof(rgba), "The pixel buffer is smaller than width times height times 4.");

            double[] linear = new double[256];
            for (int i = 0; i < 256; i++) linear[i] = SrgbToLinear(i);

            double[,,] factors = new double[componentsY, componentsX, 3];
            for (int j = 0; j < componentsY; j++)
            {
                for (int i = 0; i < componentsX; i++)
                {
                    double normalisation = (i == 0 && j == 0) ? 1 : 2;
                    double r = 0, g = 0, b = 0;
                    for (int y = 0; y < height; y++)
                    {
                        double basisY = Math.Cos(Math.PI * j * y / height);
                        int row = y * width * 4;
                        for (int x = 0; x < width; x++)
                        {
                            double basis = Math.Cos(Math.PI * i * x / width) * basisY;
                            int p = row + x * 4;
                            r += basis * linear[rgba[p]];
                            g += basis * linear[rgba[p + 1]];
                            b += basis * linear[rgba[p + 2]];
                        }
                    }
                    double scale = normalisation / (width * height);
                    factors[j, i, 0] = r * scale;
                    factors[j, i, 1] = g * scale;
                    factors[j, i, 2] = b * scale;
                }
            }

            StringBuilder hash = new StringBuilder();
            hash.Append(Encode83((componentsX - 1) + (componentsY - 1) * 9, 1));

            double maximumValue;
            if (componentsX * componentsY > 1)
            {
                double actualMaximum = 0;
                for (int j = 0; j < componentsY; j++)
                {
                    for (int i = 0; i < componentsX; i++)
                    {
                        if (i == 0 && j == 0) continue;
                        for (int c = 0; c < 3; c++) actualMaximum = Math.Max(actualMaximum, Math.Abs(factors[j, i, c]));
                    }
                }
                int quantisedMaximum = (int)Math.Max(0, Math.Min(82, Math.Floor(actualMaximum * 166 - 0.5)));
                maximumValue = (quantisedMaximum + 1) / 166.0;
                hash.Append(Encode83(quantisedMaximum, 1));
            }
            else
            {
                maximumValue = 1;
                hash.Append(Encode83(0, 1));
            }

            int dc = (LinearToSrgb(factors[0, 0, 0]) << 16) + (LinearToSrgb(factors[0, 0, 1]) << 8) + LinearToSrgb(factors[0, 0, 2]);
            hash.Append(Encode83(dc, 4));

            for (int j = 0; j < componentsY; j++)
            {
                for (int i = 0; i < componentsX; i++)
                {
                    if (i == 0 && j == 0) continue;
                    int quantR = QuantiseAc(factors[j, i, 0], maximumValue);
                    int quantG = QuantiseAc(factors[j, i, 1], maximumValue);
                    int quantB = QuantiseAc(factors[j, i, 2], maximumValue);
                    hash.Append(Encode83(quantR * 19 * 19 + quantG * 19 + quantB, 2));
                }
            }
            return hash.ToString();
        }

        #endregion

        #region Private-Methods

        private static int QuantiseAc(double value, double maximumValue)
        {
            return (int)Math.Max(0, Math.Min(18, Math.Floor(SignPow(value / maximumValue, 0.5) * 9 + 9.5)));
        }

        private static double SignPow(double value, double exponent)
        {
            return Math.CopySign(Math.Pow(Math.Abs(value), exponent), value);
        }

        private static double SrgbToLinear(int value)
        {
            double v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static int LinearToSrgb(double value)
        {
            double v = Math.Clamp(value, 0, 1);
            if (v <= 0.0031308) return (int)Math.Round(v * 12.92 * 255 + 0.5, MidpointRounding.ToZero);
            return (int)Math.Round((1.055 * Math.Pow(v, 1 / 2.4) - 0.055) * 255 + 0.5, MidpointRounding.ToZero);
        }

        private static string Encode83(int value, int length)
        {
            char[] result = new char[length];
            for (int i = 1; i <= length; i++)
            {
                int digit = (value / (int)Math.Pow(83, length - i)) % 83;
                result[i - 1] = _Characters[digit];
            }
            return new string(result);
        }

        #endregion
    }
}
