using ClipboardSync.WindowsAgent.Tray;

namespace ClipboardSync.WindowsAgent.Tests;

public sealed class RestartHelperTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("", "\"\"")]
    public void QuoteArg_QuotesAsExpected(string input, string expected)
    {
        Assert.Equal(expected, RestartHelper.QuoteArg(input));
    }

    [Fact]
    public void CreateRestartStartInfo_ReturnsProcessStartInfo()
    {
        var psi = RestartHelper.CreateRestartStartInfo();
        Assert.False(string.IsNullOrWhiteSpace(psi.FileName));
        // We don't assert exact args because it depends on the test runner.
        Assert.True(psi.UseShellExecute);
    }
}


