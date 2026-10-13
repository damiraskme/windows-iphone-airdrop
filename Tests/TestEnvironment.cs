using System.IO;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ClipboardPhonePc.Tests
{
    public abstract class TestEnvironment : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ClipboardPhonePc.Tests", Guid.NewGuid().ToString("N"));

        protected TestEnvironment()
        {
            AppPaths.DataDirectory = Path.Combine(_root, "data");
            AppPaths.TempDirectory = Path.Combine(_root, "temp");
            SaveDirectory = Path.Combine(_root, "saved");

            Directory.CreateDirectory(AppPaths.DataDirectory);
            Directory.CreateDirectory(SaveDirectory);
        }

        protected string SaveDirectory { get; }

        public virtual void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
