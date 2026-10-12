using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace ClipboardPhonePc
{
    public static class ImageProbe
    {
        private const long MaxPixels = 100_000_000;

        public static string? GetExtension(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);

                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth == 0)
                {
                    return null;
                }

                if ((long)decoder.Frames[0].PixelWidth * decoder.Frames[0].PixelHeight > MaxPixels)
                {
                    AppLog.Warn($"Image \"{Path.GetFileName(path)}\" is too large to preview, kept as file");
                    return null;
                }

                return decoder.CodecInfo?.FileExtensions.Split(',')[0].Trim().ToLowerInvariant() ?? string.Empty;
            }
            catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or ArgumentException or COMException)
            {
                return null;
            }
        }
    }
}
