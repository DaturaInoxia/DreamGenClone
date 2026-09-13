using System.Diagnostics;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Default runner: launches the configured interpreter against the approved tool script.</summary>
public sealed class ProcessCharacterIdentityMeasurementRunner : ICharacterIdentityMeasurementRunner
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromMinutes(3);

    public async Task<CharacterIdentityMeasurementRun> RunAsync(
        string interpreterPath,
        string scriptPath,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            // Normalise separators so a configured "d:/.../python.exe" still launches on Windows.
            FileName = Path.GetFullPath(interpreterPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--json");
        startInfo.ArgumentList.Add(imagePath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start the eye-validation interpreter '{interpreterPath}'.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ToolTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process already exited between the timeout and the kill.
            }

            throw new InvalidOperationException(
                $"The eye-validation tool did not finish within {ToolTimeout.TotalMinutes:0} minute(s).");
        }

        return new CharacterIdentityMeasurementRun(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }
}
