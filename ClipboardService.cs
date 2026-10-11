using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BlueDrop
{
    public static class ClipboardService
    {
        private const string ImageName = "image.png";
        private const string ImageContentType = "image/png";

        public static TransferItem? Read()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var path = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(File.Exists);
                    if (path is not null)
                    {
                        return TransferItem.FromFile(path);
                    }
                }

                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (text.Length > 0)
                    {
                        return TransferItem.FromText(text);
                    }
                }

                if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image is not null)
                    {
                        return TransferItem.FromImage(ImageName, ImageContentType, EncodePng(image));
                    }
                }
            }
            catch (COMException)
            {
            }

            return null;
        }

        public static bool Write(TransferItem item)
        {
            try
            {
                switch (item.Kind)
                {
                    case TransferKind.Text:
                        Clipboard.SetText(item.Text!);
                        break;
                    case TransferKind.Image:
                        Clipboard.SetImage(DecodeImage(item.Data!));
                        break;
                    case TransferKind.File:
                        Clipboard.SetFileDropList([item.FilePath!]);
                        break;
                }

                return true;
            }
            catch (Exception e) when (e is COMException or NotSupportedException or FileFormatException)
            {
                return false;
            }
        }

        private static byte[] EncodePng(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static BitmapSource DecodeImage(byte[] data)
        {
            using var stream = new MemoryStream(data);
            return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
    }
}
