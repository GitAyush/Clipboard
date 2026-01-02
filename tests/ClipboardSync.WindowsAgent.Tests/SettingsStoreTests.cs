using ClipboardSync.WindowsAgent.Settings;

namespace ClipboardSync.WindowsAgent.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void LoadOrCreateDefaults_CreatesFile_AndPersistsDeviceId()
    {
        var dir = NewTempDir();
        var store = new SettingsStore("test", dir);

        var s1 = store.LoadOrCreateDefaults();
        Assert.True(File.Exists(store.SettingsPath));
        Assert.NotEqual(Guid.Empty, s1.DeviceId);

        var s2 = store.LoadOrCreateDefaults();
        Assert.Equal(s1.DeviceId, s2.DeviceId);
    }

    [Fact]
    public void LoadOrCreateDefaults_BacksUpCorruptFile_AndRegenerates()
    {
        var dir = NewTempDir();
        var store = new SettingsStore("test", dir);

        Directory.CreateDirectory(dir);
        File.WriteAllText(store.SettingsPath, "{ this is not valid json ");

        var s = store.LoadOrCreateDefaults();
        Assert.NotEqual(Guid.Empty, s.DeviceId);
        Assert.True(File.Exists(store.SettingsPath + ".bak"));
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ClipboardSyncTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}


