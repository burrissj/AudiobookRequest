namespace AudiobookRequest.Core.Audio;

/// <summary>
/// Combines one or more source audio files into a single tagged .m4b, embedding chapters (one per
/// source file, when there's more than one) and cover art (when a cover image is given).
/// </summary>
public interface IAudioCombiner
{
    /// <param name="requireAac">
    /// Make sure the audio in the result is AAC. Without it, MP3 and other sources are copied into the .m4b as they are,
    /// which plays on most things but isn't a proper audiobook file; with it, anything that isn't already AAC/ALAC is re-encoded.
    /// </param>
    Task CombineAsync(IReadOnlyList<string> sourceFiles, string outputPath, BookTags tags, string? coverImagePath, bool embedTags = true, CancellationToken ct = default, bool requireAac = false);
}
