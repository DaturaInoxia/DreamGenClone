using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Hosting;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>One measurement run's parsed values plus the tool's raw output, kept verbatim as evidence.</summary>
public sealed record CharacterIdentityMeasurementResult(
    CharacterIdentityEyeMeasurement Measurement,
    string RawOutput);

/// <summary>
/// The ONE owner of running the approved measurement tool (<c>tools/eye-validation/measure_iris.py</c>).
/// The identity Validate step and the head-aware crop both go through it, so the interpreter resolution,
/// the script location, the parsing and the fail-fast messages exist once.
/// </summary>
public interface ICharacterIdentityMeasurementService
{
    /// <summary>
    /// Measures the image at the given absolute path. Fails fast naming <c>EyeToolPythonPath</c> when the
    /// interpreter is not configured. Returns the tool's parsed values; its <c>Head</c> is null when the
    /// tool found no face mesh.
    /// </summary>
    Task<CharacterIdentityMeasurementResult> MeasureFileAsync(
        string imagePath, CancellationToken cancellationToken = default);
}

public sealed class CharacterIdentityMeasurementService : ICharacterIdentityMeasurementService
{
    private const string ToolRelativePath = "tools/eye-validation/measure_iris.py";

    private readonly IImageWorkflowTemplateService _templates;
    private readonly ICharacterIdentityMeasurementRunner _runner;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CharacterIdentityMeasurementService> _logger;

    public CharacterIdentityMeasurementService(
        IImageWorkflowTemplateService templates,
        ICharacterIdentityMeasurementRunner runner,
        IHostEnvironment environment,
        ILogger<CharacterIdentityMeasurementService> logger)
    {
        _templates = templates;
        _runner = runner;
        _environment = environment;
        _logger = logger;
    }

    public async Task<CharacterIdentityMeasurementResult> MeasureFileAsync(
        string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new InvalidOperationException("A measurement requires the absolute path of the image to measure.");

        var settings = await _templates.ResolveSettingsAsync(null, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.EyeToolPythonPath))
        {
            throw new InvalidOperationException(
                "Missing required configuration 'EyeToolPythonPath': the eye-validation interpreter path "
                + "must be configured in the global reference workflow settings before it can run.");
        }

        var interpreterPath = settings.EyeToolPythonPath.Trim();
        var scriptPath = ResolveToolScriptPath();
        _logger.LogInformation(
            "Running the approved measurement tool: ImagePath={ImagePath}, Interpreter={Interpreter}",
            imagePath, interpreterPath);

        var run = await _runner.RunAsync(interpreterPath, scriptPath, imagePath, cancellationToken);
        return new CharacterIdentityMeasurementResult(
            CharacterIdentityMeasurementParser.Parse(run), run.StandardOutput);
    }

    private string ResolveToolScriptPath()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, ".."));
        var scriptPath = Path.GetFullPath(Path.Combine(repoRoot, ToolRelativePath));
        if (!File.Exists(scriptPath))
            throw new InvalidOperationException($"The approved eye-validation tool was not found at '{scriptPath}'.");

        return scriptPath;
    }
}
