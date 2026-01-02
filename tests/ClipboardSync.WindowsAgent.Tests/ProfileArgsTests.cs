using ClipboardSync.WindowsAgent.Settings;

namespace ClipboardSync.WindowsAgent.Tests;

public sealed class ProfileArgsTests
{
    [Fact]
    public void TryGetProfileName_ReturnsNull_WhenMissing()
    {
        Assert.Null(ProfileArgs.TryGetProfileName(Array.Empty<string>()));
        Assert.Null(ProfileArgs.TryGetProfileName(new[] { "--other", "x" }));
    }

    [Fact]
    public void TryGetProfileName_ReturnsNull_WhenFlagHasNoValue()
    {
        Assert.Null(ProfileArgs.TryGetProfileName(new[] { "--profile" }));
        Assert.Null(ProfileArgs.TryGetProfileName(new[] { "--profile", "" }));
        Assert.Null(ProfileArgs.TryGetProfileName(new[] { "--profile", "   " }));
    }

    [Fact]
    public void TryGetProfileName_ReturnsValue_CaseInsensitive()
    {
        Assert.Equal("A", ProfileArgs.TryGetProfileName(new[] { "--profile", "A" }));
        Assert.Equal("B", ProfileArgs.TryGetProfileName(new[] { "--PROFILE", "B" }));
    }
}


