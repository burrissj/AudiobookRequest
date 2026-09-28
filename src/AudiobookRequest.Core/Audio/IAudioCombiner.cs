namespace AudiobookRequest.Core.Audio;

/// <summary>
/// Combines one or more source audio files into a single tagged .m4b, embedding chapters (one per
/// source file, when there's more than one) and cover art (when a cover image is given).
/// </summary>
public interface IAudioCombiner
{
    Task CombineAsync(IReadOnlyList<string> sourceFiles, string outputPath, BookTags tags, string? coverImagePath, CancellationToken ct = default);
}
