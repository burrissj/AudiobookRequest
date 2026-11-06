using AudiobookRequest.Core.Audio;

namespace AudiobookRequest.Core.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task CancellingStopsTheProgramInsteadOfLeavingItRunning()
    {
        // A program that would write a marker file a couple of seconds from now, if it were left to finish.
        var marker = Path.Combine(Path.GetTempPath(), "abr-runner-" + Guid.NewGuid().ToString("N") + ".txt");
        var script = $"Start-Sleep -Seconds 3; Set-Content -LiteralPath '{marker}' -Value done";
        using var cancel = new CancellationTokenSource();
        try
        {
            var run = new ProcessRunner().RunAsync("powershell", ["-NoProfile", "-NonInteractive", "-Command", script], cancel.Token);
            await Task.Delay(700);
            cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

            await Task.Delay(4500);   // long enough for the program to have finished, had it been left running
            Assert.False(File.Exists(marker), "the program kept running after it was cancelled");
        }
        finally
        {
            try { File.Delete(marker); } catch { /* scratch */ }
        }
    }

    [Fact]
    public async Task AProgramThatFinishesReturnsItsOutputAndExitCode()
    {
        var result = await new ProcessRunner().RunAsync("powershell", ["-NoProfile", "-NonInteractive", "-Command", "Write-Output hello; exit 3"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
    }
}
