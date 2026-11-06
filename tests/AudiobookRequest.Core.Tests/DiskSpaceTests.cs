using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Core.Tests;

public class DiskSpaceTests
{
    [Fact]
    public void GetFreeBytes_ReportsSpaceForAFolderThatDoesNotExistYet()
    {
        var missing = Path.Combine(Path.GetTempPath(), "abr-disk-test-" + Guid.NewGuid().ToString("N"), "nested");

        var free = DiskSpace.GetFreeBytes(missing);

        Assert.NotNull(free);
        Assert.True(free > 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetFreeBytes_BlankPathIsUnknown(string? path) => Assert.Null(DiskSpace.GetFreeBytes(path));
}
