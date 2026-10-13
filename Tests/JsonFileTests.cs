using System.IO;
using Xunit;

namespace ClipboardPhonePc.Tests
{
    public sealed class JsonFileTests : TestEnvironment
    {
        public sealed record Sample(string Name, int Count);

        private static string FilePath => Path.Combine(AppPaths.DataDirectory, "sample.json");

        [Fact]
        public void MissingFileLoadsNothing()
        {
            Assert.Null(JsonFile.Load<Sample>(FilePath, out var state));
            Assert.Equal(JsonFileState.Missing, state);
        }

        [Fact]
        public void SavedValueLoadsBack()
        {
            JsonFile.Save(FilePath, new Sample("first", 1));

            Assert.Equal(new Sample("first", 1), JsonFile.Load<Sample>(FilePath, out var state));
            Assert.Equal(JsonFileState.Loaded, state);
        }

        [Fact]
        public void DamagedFileIsRestoredFromBackup()
        {
            JsonFile.Save(FilePath, new Sample("first", 1));
            JsonFile.Save(FilePath, new Sample("second", 2));
            File.WriteAllText(FilePath, "{ broken");

            Assert.Equal(new Sample("first", 1), JsonFile.Load<Sample>(FilePath, out var state));
            Assert.Equal(JsonFileState.Restored, state);
            Assert.Equal(new Sample("first", 1), JsonFile.Load<Sample>(FilePath, out var again));
            Assert.Equal(JsonFileState.Loaded, again);
        }

        [Fact]
        public void DamagedFileWithoutBackupIsSetAside()
        {
            File.WriteAllText(FilePath, "{ broken");

            Assert.Null(JsonFile.Load<Sample>(FilePath, out var state));
            Assert.Equal(JsonFileState.Damaged, state);
            Assert.True(File.Exists(FilePath + ".bad"));
        }
    }
}
