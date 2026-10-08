using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Measures and normalizes a produced clip through the model's configured ffmpeg binary (B-152, S6).
/// </summary>
/// <remarks>
/// The implementation is the harness's own recipe (<c>helpers/h3-local-host/run-h3-ref2va-proof.py</c>,
/// <c>normalize_loudness</c>): <c>loudnorm=I=&lt;target&gt;:TP=-1.5:LRA=11</c> with the audio rate pinned to 48 kHz,
/// because loudnorm otherwise resamples to 96 kHz. The video stream is copied, so normalization never re-encodes the
/// picture.
///
/// <para>
/// Every invocation goes through the configured path and fails fast when that file does not exist. Nothing is
/// resolved from PATH: a guessed binary is exactly the kind of hidden fallback that would let a render "succeed"
/// while shipping audio nobody can hear.
/// </para>
/// </remarks>
public sealed partial class SceneVideoAudioProcessor : IVideoAudioProcessor
{
    private readonly ILogger<SceneVideoAudioProcessor> _logger;

    public SceneVideoAudioProcessor(ILogger<SceneVideoAudioProcessor> logger)
    {
        _logger = logger;
    }

    public async Task<SceneVideoAudioEvidence> ProbeAsync(
        string absolutePath, string ffmpegPath, CancellationToken cancellationToken = default)
    {
        RequireBinary(ffmpegPath);
        var (video, audio, duration) = await ProbeStreamsAsync(ffmpegPath, absolutePath, cancellationToken);
        var loudness = audio
            ? await TryMeasureLoudnessAsync(ffmpegPath, absolutePath, cancellationToken)
            : null;

        return new SceneVideoAudioEvidence(
            VideoStreamPresent: video,
            AudioStreamPresent: audio,
            DurationSeconds: duration,
            IntegratedLoudnessLufs: loudness,
            Notes: BuildNotes(video, audio, loudness));
    }

    public async Task<(string NormalizedAbsolutePath, SceneVideoAudioEvidence Evidence)> NormalizeAndVerifyAsync(
        string absolutePath,
        string ffmpegPath,
        double targetLufs,
        CancellationToken cancellationToken = default)
    {
        RequireBinary(ffmpegPath);

        var directory = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException($"Video path '{absolutePath}' has no directory.");
        var stem = Path.GetFileNameWithoutExtension(absolutePath);
        var extension = Path.GetExtension(absolutePath);
        var destination = Path.Combine(directory, $"{stem}_norm{extension}");

        var arguments =
            $"-y -v error -i \"{absolutePath}\" -af \"loudnorm=I={targetLufs.ToString(CultureInfo.InvariantCulture)}:TP=-1.5:LRA=11\" "
            + $"-ar 48000 -c:v copy -c:a aac -b:a 192k \"{destination}\"";

        var (exitCode, _, standardError) = await RunAsync(ffmpegPath, arguments, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Loudness normalization failed (ffmpeg exit {exitCode}) for '{Path.GetFileName(absolutePath)}': "
                + $"{Truncate(standardError)}. The render is kept as it was; nothing is delivered un-normalized.");
        }

        _logger.LogInformation(
            "Scene video normalized to {TargetLufs} LUFS: {Source} -> {Destination}",
            targetLufs, absolutePath, destination);

        var evidence = await ProbeAsync(destination, ffmpegPath, cancellationToken);
        return (destination, evidence);
    }

    public async Task ExtractFinalFrameAsync(
        string absolutePath,
        string destinationAbsolutePath,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        RequireBinary(ffmpegPath);

        if (string.IsNullOrWhiteSpace(destinationAbsolutePath))
        {
            throw new InvalidOperationException(
                "Extracting the final frame needs a destination path; nothing was written.");
        }

        var directory = Path.GetDirectoryName(destinationAbsolutePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // -sseof seeks 0.1 s before EOF so the LAST decodable frame is the one taken, and -update 1 makes the image
        // muxer OVERWRITE a single file instead of writing a numbered sequence into the folder.
        var arguments =
            $"-hide_banner -loglevel error -y -sseof -0.1 -i \"{absolutePath}\" -frames:v 1 -update 1 "
            + $"\"{destinationAbsolutePath}\"";

        var (exitCode, _, standardError) = await RunAsync(ffmpegPath, arguments, cancellationToken);
        if (exitCode != 0 || !File.Exists(destinationAbsolutePath))
        {
            throw new InvalidOperationException(
                $"Extracting the final frame of '{Path.GetFileName(absolutePath)}' failed (ffmpeg exit {exitCode}): "
                + $"{Truncate(standardError)}. A continuation has nothing to anchor to without that frame.");
        }

        _logger.LogInformation(
            "Source frame extracted: {Source} -> {Destination}",
            Path.GetFileName(absolutePath), destinationAbsolutePath);
    }

    public async Task ExtractAudioAsync(
        string absolutePath,
        string destinationAbsolutePath,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        RequireBinary(ffmpegPath);

        var directory = Path.GetDirectoryName(destinationAbsolutePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 32 kHz stereo PCM matches what the generated clips already carry, so carrying a track forward does not
        // resample it a second time.
        var arguments =
            $"-hide_banner -loglevel error -y -i \"{absolutePath}\" -vn -acodec pcm_s16le -ar 32000 -ac 2 "
            + $"\"{destinationAbsolutePath}\"";

        var (exitCode, _, standardError) = await RunAsync(ffmpegPath, arguments, cancellationToken);
        if (exitCode != 0 || !File.Exists(destinationAbsolutePath))
        {
            throw new InvalidOperationException(
                $"Extracting the audio of '{Path.GetFileName(absolutePath)}' failed (ffmpeg exit {exitCode}): "
                + $"{Truncate(standardError)}. Choose frame-only continuation, or fix the source clip's audio.");
        }

        _logger.LogInformation(
            "Source audio extracted: {Source} -> {Destination}",
            Path.GetFileName(absolutePath), destinationAbsolutePath);
    }

    public async Task<SceneVideoDriftMetrics> MeasureDriftAsync(        string absolutePath,
        string? sourceFrameAbsolutePath,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        RequireBinary(ffmpegPath);

        var notes = new List<string>();

        double? luminanceMean = null;
        double? luminanceLow = null;
        double? luminanceHigh = null;
        double? saturationMean = null;
        double? edgeEnergy = null;

        // One pass over the clip yields the colour statistics; a second pass over an edge map yields the sharpness
        // proxy. Both read frame metadata rather than a summary, so a metric missing from either read is simply
        // absent rather than guessed.
        var statsArguments =
            $"-hide_banner -v quiet -i \"{absolutePath}\" -vf \"signalstats,metadata=print:file=-\" -f null -";
        var (statsExit, statsOutput, _) = await RunAsync(ffmpegPath, statsArguments, cancellationToken);
        if (statsExit != 0)
        {
            notes.Add("the colour statistics could not be read");
        }
        else
        {
            luminanceMean = MeanOf(statsOutput, "lavfi.signalstats.YAVG");
            luminanceLow = MeanOf(statsOutput, "lavfi.signalstats.YLOW");
            luminanceHigh = MeanOf(statsOutput, "lavfi.signalstats.YHIGH");
            saturationMean = MeanOf(statsOutput, "lavfi.signalstats.SATAVG");
        }

        edgeEnergy = await MeasureEdgeEnergyAsync(ffmpegPath, absolutePath, cancellationToken);
        if (edgeEnergy is null)
        {
            notes.Add("the sharpness proxy could not be measured");
        }

        double? edgeEnergyRatio = null;
        if (edgeEnergy is not null && !string.IsNullOrWhiteSpace(sourceFrameAbsolutePath))
        {
            var sourceEdge = await MeasureEdgeEnergyAsync(ffmpegPath, sourceFrameAbsolutePath!, cancellationToken);
            if (sourceEdge is > 0)
            {
                edgeEnergyRatio = edgeEnergy / sourceEdge;
            }
            else
            {
                notes.Add("the source frame's sharpness proxy was zero, so no ratio is reported");
            }
        }

        var trebleToBass = await MeasureTrebleToBassRatioAsync(ffmpegPath, absolutePath, cancellationToken);
        if (trebleToBass is null)
        {
            notes.Add("the audio treble/bass ratio could not be measured (no audio, or astats produced none)");
        }

        return new SceneVideoDriftMetrics(
            LuminanceMean: Round(luminanceMean),
            LuminanceLowPercentile: Round(luminanceLow),
            LuminanceHighPercentile: Round(luminanceHigh),
            SaturationMean: Round(saturationMean),
            NearBlackPercent: null,
            EdgeEnergyMean: Round(edgeEnergy),
            EdgeEnergyRatioToSource: Round(edgeEnergyRatio),
            AudioTrebleToBassRatio: Round(trebleToBass),
            Notes: notes.Count == 0
                ? "Measured with ffmpeg signalstats (luma percentiles, chroma), edgedetect (sharpness proxy) and "
                  + "astats (treble/bass). Evidence only - drift is reported, never gated on."
                : string.Join("; ", notes));
    }

    /// <summary>
    /// Mean edge coverage, from ffmpeg's <c>edgedetect</c> map. This is a SHARPNESS PROXY, not the Laplacian
    /// variance the community reports: it is only meaningful as a ratio between two renders of comparable framing,
    /// which is exactly how the drift record uses it.
    /// </summary>
    private static async Task<double?> MeasureEdgeEnergyAsync(
        string ffmpegPath, string path, CancellationToken cancellationToken)
    {
        var arguments =
            $"-hide_banner -v quiet -i \"{path}\" -vf \"format=gray,edgedetect=low=0.05:high=0.2,"
            + "signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=-\" -f null -";
        var (exitCode, output, _) = await RunAsync(ffmpegPath, arguments, cancellationToken);
        return exitCode == 0 ? MeanOf(output, "lavfi.signalstats.YAVG") : null;
    }

    /// <summary>
    /// High-frequency RMS over low-frequency RMS. The documented chained-audio failure is a loss of about a third
    /// of the treble per join, which is invisible in a waveform - so it is measured as a band ratio instead.
    /// </summary>
    private static async Task<double?> MeasureTrebleToBassRatioAsync(
        string ffmpegPath, string path, CancellationToken cancellationToken)
    {
        var treble = await MeasureBandRmsDecibelsAsync(ffmpegPath, path, "highpass=f=4000", cancellationToken);
        var bass = await MeasureBandRmsDecibelsAsync(ffmpegPath, path, "lowpass=f=300", cancellationToken);
        if (treble is null || bass is null)
        {
            return null;
        }

        // RMS levels are reported in dBFS; the linear ratio is 10^((treble - bass) / 20).
        return Math.Pow(10, (treble.Value - bass.Value) / 20.0);
    }

    private static async Task<double?> MeasureBandRmsDecibelsAsync(
        string ffmpegPath, string path, string filter, CancellationToken cancellationToken)
    {
        var arguments =
            $"-hide_banner -v info -i \"{path}\" -af \"{filter},astats=measure_overall=RMS_level\" -f null -";
        var (exitCode, _, standardError) = await RunAsync(ffmpegPath, arguments, cancellationToken);
        if (exitCode != 0)
        {
            return null;
        }

        // astats prints a per-channel block and then an Overall block; the OVERALL value is the last one printed.
        var matches = RmsLevelRegex().Matches(standardError);
        if (matches.Count == 0)
        {
            return null;
        }

        return double.TryParse(
            matches[^1].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Averages every frame's value of one metadata key, or null when the key never appeared.</summary>
    private static double? MeanOf(string output, string key)
    {
        var matches = Regex.Matches(output, $"{Regex.Escape(key)}\\s*=\\s*(-?\\d+(?:\\.\\d+)?)");
        if (matches.Count == 0)
        {
            return null;
        }

        var total = 0.0;
        var counted = 0;
        foreach (Match match in matches)
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                total += value;
                counted++;
            }
        }

        return counted == 0 ? null : total / counted;
    }

    private static double? Round(double? value) =>
        value is null ? null : Math.Round(value.Value, 4, MidpointRounding.AwayFromZero);

    private static void RequireBinary(string ffmpegPath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new InvalidOperationException(
                "No ffmpeg path is configured for this video model, so the mandatory loudness normalization cannot "
                + "run. Set 'FfmpegPath' in the model's MiniMaxH3Ref2VA qualification (DbQuery 'h3-video-configure' "
                + "seeds it). Nothing was delivered un-normalized.");
        }

        if (!File.Exists(ffmpegPath))
        {
            throw new InvalidOperationException(
                $"The configured ffmpeg binary '{ffmpegPath}' does not exist on this machine, so the mandatory "
                + "loudness normalization cannot run. Point 'FfmpegPath' at a real ffmpeg (this machine has no system "
                + "ffmpeg; the repo venv's imageio-ffmpeg binary is the usual choice). Nothing was delivered "
                + "un-normalized.");
        }
    }

    private async Task<(bool Video, bool Audio, double? Duration)> ProbeStreamsAsync(
        string ffmpegPath, string absolutePath, CancellationToken cancellationToken)
    {
        // ffmpeg exits non-zero with no output target, which is expected: the stream report goes to stderr.
        var (_, _, standardError) = await RunAsync(
            ffmpegPath, $"-hide_banner -i \"{absolutePath}\"", cancellationToken);

        var video = standardError.Contains("Video:", StringComparison.Ordinal);
        var audio = standardError.Contains("Audio:", StringComparison.Ordinal);

        double? duration = null;
        var durationMatch = DurationRegex().Match(standardError);
        if (durationMatch.Success
            && TimeSpan.TryParse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var parsed))
        {
            duration = parsed.TotalSeconds;
        }

        return (video, audio, duration);
    }

    private async Task<double?> TryMeasureLoudnessAsync(
        string ffmpegPath, string absolutePath, CancellationToken cancellationToken)
    {
        var arguments =
            $"-hide_banner -i \"{absolutePath}\" -af loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json -f null -";
        var (_, _, standardError) = await RunAsync(ffmpegPath, arguments, cancellationToken);

        var match = InputLoudnessRegex().Match(standardError);
        if (match.Success
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lufs))
        {
            return lufs;
        }

        _logger.LogDebug(
            "Integrated loudness could not be read from ffmpeg output for {Path}: {Output}",
            absolutePath, Truncate(standardError));
        return null;
    }

    private static string? BuildNotes(bool video, bool audio, double? loudness)
    {
        var notes = new List<string>();
        if (!video)
        {
            notes.Add("no video stream was found in the produced file");
        }

        if (!audio)
        {
            notes.Add("no audio stream was found in the produced file (the clip will play silent)");
        }
        else if (loudness is null)
        {
            notes.Add("an audio stream is present but its loudness could not be measured");
        }

        return notes.Count == 0 ? null : string.Join("; ", notes);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value.Trim() : value[..500].Trim() + "...";

    [GeneratedRegex(@"Duration:\s*(\d{2}:\d{2}:\d{2}\.\d+)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex("\"input_i\"\\s*:\\s*\"(-?\\d+(?:\\.\\d+)?)\"")]
    private static partial Regex InputLoudnessRegex();

    /// <summary>astats' overall RMS line. The Overall block is printed last, so the final match is the clip's.</summary>
    [GeneratedRegex(@"RMS level dB:\s*(-?\d+(?:\.\d+)?)")]
    private static partial Regex RmsLevelRegex();
}
