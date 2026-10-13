using System.IO;
using Xunit;

namespace ClipboardPhonePc.Tests
{
    public sealed class DeviceStoreTests : TestEnvironment
    {
        [Fact]
        public void IssuedKeyAuthorizesItsDevice()
        {
            var devices = new DeviceStore();
            var key = devices.Add("Phone", out var device);

            Assert.Equal(device.Id, devices.Authorize(key)?.Id);
            Assert.Null(devices.Authorize(key + "x"));
            Assert.Null(devices.Authorize(string.Empty));
        }

        [Fact]
        public void FirstUseIsAnnounced()
        {
            var devices = new DeviceStore();
            var key = devices.Add("Phone", out _);
            var changes = 0;
            devices.Changed += () => changes++;

            devices.Authorize(key);
            devices.Authorize(key);

            Assert.Equal(1, changes);
            Assert.NotNull(devices.GetAll().Single().LastUsedAt);
        }

        [Fact]
        public void KeyIsNotStored()
        {
            var devices = new DeviceStore();
            var key = devices.Add("Phone", out _);

            Assert.DoesNotContain(key, File.ReadAllText(AppPaths.DevicesFile));
        }

        [Fact]
        public void SameNameGetsNumber()
        {
            var devices = new DeviceStore();
            devices.Add("Phone", out var first);
            devices.Add("phone", out var second);
            devices.Add("Phone", out var third);
            devices.Add(string.Empty, out var unnamed);

            Assert.Equal("Phone", first.Name);
            Assert.Equal("phone (1)", second.Name);
            Assert.Equal("Phone (2)", third.Name);
            Assert.Equal(string.Empty, unnamed.Name);
        }

        [Fact]
        public void DevicesSurviveRestart()
        {
            var key = new DeviceStore().Add("Phone", out _);

            Assert.NotNull(new DeviceStore().Authorize(key));
        }

        [Fact]
        public void RemovedDeviceDoesNotReturnFromBackup()
        {
            var devices = new DeviceStore();
            var kept = devices.Add("Keep", out _);
            var removed = devices.Add("Remove", out var device);

            devices.Remove(device.Id);
            File.WriteAllText(AppPaths.DevicesFile, "[ broken");
            var restored = new DeviceStore();

            Assert.NotNull(restored.Authorize(kept));
            Assert.Null(restored.Authorize(removed));
        }
    }
}
