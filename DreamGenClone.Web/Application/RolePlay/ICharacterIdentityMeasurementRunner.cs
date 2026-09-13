namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>The raw result of one invocation of the eye-validation tool.</summary>
public sealed record CharacterIdentityMeasurementRun(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs the approved eye-validation tool (<c>tools/eye-validation/measure_iris.py</c>) as a
/// subprocess. Abstracted so the Validate step is testable without a Python interpreter.
/// </summary>
public interface ICharacterIdentityMeasurementRunner
{
    Task<CharacterIdentityMeasurementRun> RunAsync(
        string interpreterPath,
        string scriptPath,
        string imagePath,
        CancellationToken cancellationToken = default);
}
