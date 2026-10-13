using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ClipboardPhonePc.Tests
{
    public sealed class FileReceiverTests : TestEnvironment
    {
        private const string BinaryType = "application/octet-stream";

        private FileReceiver CreateReceiver(bool saveFiles = true)
        {
            return new FileReceiver(new AppSettings { SaveFiles = saveFiles, SaveFolder = SaveDirectory });
        }

        private static MemoryStream Body(string text)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(text));
        }

        private static MemoryStream Png()
        {
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            return stream;
        }

        [Theory]
        [InlineData("notes.txt", "notes.txt")]
        [InlineData("", "file")]
        [InlineData("..", "file")]
        [InlineData(".", "file")]
        [InlineData("CON.txt", "_CON.txt")]
        [InlineData("nul", "_nul")]
        [InlineData(@"..\..\evil.txt", "evil.txt")]
        [InlineData("a/b/inner.txt", "inner.txt")]
        [InlineData("what?.txt", "what_.txt")]
        public async Task NameIsMadeSafe(string requested, string expected)
        {
            var item = await CreateReceiver().ReceiveAsync(Body("data"), requested, BinaryType);

            Assert.Equal(TransferKind.File, item.Kind);
            Assert.Equal(Path.Combine(SaveDirectory, expected), item.FilePath);
            Assert.Equal("data", File.ReadAllText(item.FilePath!));
        }

        [Fact]
        public async Task SameNameGetsNumber()
        {
            var receiver = CreateReceiver();

            var first = await receiver.ReceiveAsync(Body("one"), "same.txt", BinaryType);
            var second = await receiver.ReceiveAsync(Body("two"), "same.txt", BinaryType);

            Assert.Equal("same.txt", first.Name);
            Assert.Equal("same (1).txt", second.Name);
            Assert.Equal("one", File.ReadAllText(first.FilePath!));
        }

        [Fact]
        public async Task SavedFileIsMarkedAsDownloaded()
        {
            var item = await CreateReceiver().ReceiveAsync(Body("MZ"), "tool.exe", BinaryType);

            Assert.Contains("ZoneId=3", File.ReadAllText(item.FilePath + ":Zone.Identifier"));
        }

        [Fact]
        public async Task PictureBecomesImage()
        {
            var item = await CreateReceiver().ReceiveAsync(Png(), "photo", "image/png");

            Assert.Equal(TransferKind.Image, item.Kind);
            Assert.Equal("photo.png", item.Name);
            Assert.True(File.Exists(Path.Combine(SaveDirectory, "photo.png")));
        }

        [Fact]
        public async Task FakePictureStaysFile()
        {
            var item = await CreateReceiver().ReceiveAsync(Body("not a picture"), string.Empty, "image/png");

            Assert.Equal(TransferKind.File, item.Kind);
            Assert.Equal("image.png", item.Name);
        }

        [Fact]
        public async Task RichTextIsKeptAsFile()
        {
            var item = await CreateReceiver().ReceiveAsync(Body(@"{\rtf1\ansi hello}"), "note.rtf", BinaryType);

            Assert.Equal(TransferKind.File, item.Kind);
        }

        [Fact]
        public async Task UnsavedFilesGetOwnFolders()
        {
            var receiver = CreateReceiver(false);

            var first = await receiver.ReceiveAsync(Body("one"), "same.txt", BinaryType);
            var second = await receiver.ReceiveAsync(Body("two"), "same.txt", BinaryType);

            Assert.True(File.Exists(first.FilePath));
            Assert.True(File.Exists(second.FilePath));
            Assert.NotEqual(Path.GetDirectoryName(first.FilePath), Path.GetDirectoryName(second.FilePath));
            Assert.StartsWith(AppPaths.TempDirectory, first.FilePath);
        }

        [Fact]
        public async Task UnsavedPictureLeavesNothingBehind()
        {
            var item = await CreateReceiver(false).ReceiveAsync(Png(), "photo", "image/png");

            Assert.Equal(TransferKind.Image, item.Kind);
            Assert.Empty(Directory.EnumerateFileSystemEntries(AppPaths.TempDirectory));
        }
    }
}
