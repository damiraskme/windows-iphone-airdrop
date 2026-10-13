using System.IO;

namespace ClipboardPhonePc
{
    public sealed class FileReceiver
    {
        private const string FallbackFileName = "file";
        private const string FallbackImageName = "image";
        private const string ZoneStream = ":Zone.Identifier";
        private const string ZoneContent = "[ZoneTransfer]\r\nZoneId=3\r\n";
        private const string ReservedNamePrefix = "_";
        private const long MaxClipboardImageBytes = 64 * 1024 * 1024;

        private static readonly TimeSpan TempLifetime = TimeSpan.FromDays(1);

        private static readonly Dictionary<string, string> ClipboardImageExtensions = new()
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/gif"] = ".gif",
            ["image/bmp"] = ".bmp",
            ["image/tiff"] = ".tif"
        };

        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private readonly AppSettings _settings;

        public FileReceiver(AppSettings settings)
        {
            _settings = settings;
        }

        public async Task<TransferItem> ReceiveAsync(Stream body, string requestedName, string contentType)
        {
            var name = SanitizeName(requestedName);
            var imageExtension = ClipboardImageExtensions.GetValueOrDefault(contentType);
            var fileName = name.Length == 0 ? (imageExtension is null ? FallbackFileName : FallbackImageName + imageExtension)
                : imageExtension is not null && !Path.HasExtension(name) ? name + imageExtension
                : name;

            var path = string.Empty;

            try
            {
                await using var file = CreateUnique(_settings.SaveFiles ? PrepareSaveDirectory() : PrepareTempDirectory(), fileName);
                path = file.Name;

                await body.CopyToAsync(file);
            }
            catch
            {
                Discard(path);
                throw;
            }

            MarkDownloaded(path);
            return ToReceivedItem(path, name);
        }

        private TransferItem ToReceivedItem(string path, string name)
        {
            if (new FileInfo(path).Length > MaxClipboardImageBytes || ImageProbe.GetExtension(path) is not { } extension)
            {
                return TransferItem.FromFile(path);
            }

            var imageName = name.Length == 0 ? FallbackImageName + extension
                : Path.HasExtension(name) ? name
                : name + extension;
            var data = File.ReadAllBytes(path);

            string? imagePath = null;
            if (!_settings.SaveFiles)
            {
                Discard(path);
            }
            else if (Path.GetFileName(path) != imageName)
            {
                imagePath = MoveUnique(path, imageName);
            }
            else
            {
                imagePath = path;
            }

            return TransferItem.FromImage(imageName, TransferItem.ContentTypeOf(imageName), data, imagePath);
        }

        private static void MarkDownloaded(string path)
        {
            try
            {
                File.WriteAllText(path + ZoneStream, ZoneContent);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }

        private static void Discard(string path)
        {
            if (path.Length == 0)
            {
                return;
            }

            try
            {
                File.Delete(path);

                var directory = Path.GetDirectoryName(path)!;
                if (Path.GetDirectoryName(directory) == AppPaths.TempDirectory)
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static string PrepareTempDirectory()
        {
            AppPaths.PruneTempDirectory(TempLifetime);
            return AppPaths.CreateTempItemDirectory();
        }

        private string PrepareSaveDirectory()
        {
            var directory = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? AppPaths.DefaultSaveDirectory : _settings.SaveFolder;

            try
            {
                Directory.CreateDirectory(directory);
                return directory;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Save folder \"{directory}\" is not available, using \"{AppPaths.DefaultSaveDirectory}\"");
                Directory.CreateDirectory(AppPaths.DefaultSaveDirectory);
                return AppPaths.DefaultSaveDirectory;
            }
        }

        private static string SanitizeName(string name)
        {
            var fileName = Path.GetFileName(name);
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            fileName = fileName.Trim().TrimEnd('.');

            return ReservedNames.Contains(Path.GetFileNameWithoutExtension(fileName)) ? ReservedNamePrefix + fileName : fileName;
        }

        private static FileStream CreateUnique(string directory, string name)
        {
            for (var index = 0; ; index++)
            {
                var path = NumberedPath(directory, name, index);

                try
                {
                    return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException) when (File.Exists(path))
                {
                }
            }
        }

        private static string MoveUnique(string source, string name)
        {
            var directory = Path.GetDirectoryName(source)!;

            for (var index = 0; ; index++)
            {
                var path = NumberedPath(directory, name, index);

                try
                {
                    File.Move(source, path);
                    return path;
                }
                catch (IOException) when (File.Exists(path))
                {
                }
            }
        }

        private static string NumberedPath(string directory, string name, int index)
        {
            return Path.Combine(directory, index == 0
                ? name
                : $"{Path.GetFileNameWithoutExtension(name)} ({index}){Path.GetExtension(name)}");
        }
    }
}
