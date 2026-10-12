using System.IO;
using System.Windows;
using System.Windows.Documents;

namespace ClipboardPhonePc
{
    public static class RtfText
    {
        private static readonly byte[] Signature = "{\\rtf"u8.ToArray();

        public static int SignatureLength => Signature.Length;

        public static bool HasSignature(ReadOnlySpan<byte> data)
        {
            return data.StartsWith(Signature);
        }

        public static async Task<string> ToPlainTextAsync(byte[] rtf)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var document = new FlowDocument();
                var range = new TextRange(document.ContentStart, document.ContentEnd);

                using var stream = new MemoryStream(rtf);
                range.Load(stream, DataFormats.Rtf);
                return range.Text.TrimEnd('\r', '\n');
            });
        }
    }
}
