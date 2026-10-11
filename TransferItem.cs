using System.IO;
using Microsoft.AspNetCore.StaticFiles;

namespace BlueDrop
{
    public enum TransferKind
    {
        Text,
        Image,
        File
    }

    public sealed record TransferItem(
        TransferKind Kind,
        string Name,
        string ContentType,
        string? Text = null,
        byte[]? Data = null,
        string? FilePath = null)
    {
        private const int PreviewLength = 300;
        private const string DefaultContentType = "application/octet-stream";
        private static readonly FileExtensionContentTypeProvider ContentTypes = new();

        public string Preview
        {
            get
            {
                if (Kind != TransferKind.Text)
                {
                    return Name;
                }

                var text = Text!.Trim();
                return text.Length > PreviewLength ? text[..PreviewLength] : text;
            }
        }

        public static TransferItem FromText(string text)
        {
            return new TransferItem(TransferKind.Text, string.Empty, "text/plain", Text: text);
        }

        public static TransferItem FromImage(string name, string contentType, byte[] data)
        {
            return new TransferItem(TransferKind.Image, name, contentType, Data: data);
        }

        public static TransferItem FromFile(string path)
        {
            var contentType = ContentTypes.TryGetContentType(path, out var known) ? known : DefaultContentType;
            return new TransferItem(TransferKind.File, Path.GetFileName(path), contentType, FilePath: path);
        }
    }
}
