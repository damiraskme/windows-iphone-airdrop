using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace BlueDrop
{
    public static class QrCode
    {
        private const int PixelsPerModule = 8;

        public static BitmapSource Create(string text)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            using var code = new PngByteQRCode(data);
            using var stream = new MemoryStream(code.GetGraphic(PixelsPerModule));

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
