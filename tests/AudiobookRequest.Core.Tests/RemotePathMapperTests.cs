using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Core.Tests;

public class RemotePathMapperTests
{
    [Fact]
    public void ToLocalPath_SwapsConfiguredPrefix()
    {
        var result = RemotePathMapper.ToLocalPath(
            @"C:\audiobooks\Sarah A. Parker\When the Moon Hatched\file.m4b",
            @"C:\audiobooks",
            @"\\192.168.1.181\Media Server\Books\AudioBooks");

        Assert.Equal(@"\\192.168.1.181\Media Server\Books\AudioBooks\Sarah A. Parker\When the Moon Hatched\file.m4b", result);
    }

    [Fact]
    public void ToLocalPath_IsCaseInsensitiveAndToleratesTrailingSlashOnPrefixes()
    {
        var result = RemotePathMapper.ToLocalPath(
            @"c:\AUDIOBOOKS\Author\file.m4b",
            @"C:\audiobooks\",
            @"\\host\share\");

        Assert.Equal(@"\\host\share\Author\file.m4b", result);
    }

    [Theory]
    [InlineData(null, @"\\host\share")]
    [InlineData(@"C:\audiobooks", null)]
    [InlineData("", @"\\host\share")]
    public void ToLocalPath_ReturnsInputUnchanged_WhenEitherPrefixIsNotConfigured(string? chaptarrPrefix, string? localPrefix)
    {
        const string path = @"C:\audiobooks\Author\file.m4b";

        var result = RemotePathMapper.ToLocalPath(path, chaptarrPrefix, localPrefix);

        Assert.Equal(path, result);
    }

    [Fact]
    public void ToLocalPath_ReturnsInputUnchanged_WhenPathDoesNotStartWithConfiguredPrefix()
    {
        const string path = @"D:\somewhere\else\file.m4b";

        var result = RemotePathMapper.ToLocalPath(path, @"C:\audiobooks", @"\\host\share");

        Assert.Equal(path, result);
    }
}
