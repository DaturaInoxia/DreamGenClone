namespace DreamGenClone.CorpusRunner;

public sealed record RunnerOptions(
    string RepositoryRoot,
    string CorpusPath,
    string ConfigurationDatabasePath,
    string OutputPath,
    int Iterations,
    bool KeepWorkingDatabase,
    string? SelectedCaseId = null,
    string? TargetStage = null)
{
    public static RunnerOptions Parse(string[] args, string? currentDirectory = null, string? executableDirectory = null, DateTime? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var root = RepositoryRootLocator.Find(currentDirectory, executableDirectory);
        string? corpus = null;
        string? configDb = null;
        string? output = null;
        var iterations = 1;
        var keep = false;
        string? selectedCaseId = null;
        string? targetStage = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--corpus": corpus = RequireValue(args, ref index, "--corpus"); break;
                case "--config-db": configDb = RequireValue(args, ref index, "--config-db"); break;
                case "--output": output = RequireValue(args, ref index, "--output"); break;
                case "--case": selectedCaseId = RequireValue(args, ref index, "--case"); break;
                case "--stage": targetStage = ParseStage(RequireValue(args, ref index, "--stage")); break;
                case "--iterations":
                    var value = RequireValue(args, ref index, "--iterations");
                    if (!int.TryParse(value, out iterations) || iterations < 1)
                        throw new RunnerOptionsException("runner_iterations_invalid", "--iterations must be a positive integer.");
                    break;
                case "--keep-working-db": keep = true; break;
                default: throw new RunnerOptionsException("runner_argument_unknown", $"Unknown argument '{args[index]}'.");
            }
        }

        corpus ??= Path.Combine(root, "specs", "Planning", "B-100-progressive-scene-beat-pipeline", "fixtures", "corpus.json");
        configDb ??= Path.Combine(root, "DreamGenClone.Web", "data", "dreamgenclone.dev.db");
        output ??= Path.Combine(root, "artifacts", "tmp", $"b100-corpus-report-{(utcNow ?? DateTime.UtcNow):yyyyMMddTHHmmssZ}.json");
        return new RunnerOptions(root, Resolve(root, corpus), Resolve(root, configDb), Resolve(root, output), iterations, keep, selectedCaseId, targetStage);
    }

    private static string ParseStage(string value)
        => BenchmarkStages.All.FirstOrDefault(stage => string.Equals(stage, value, StringComparison.OrdinalIgnoreCase))
            ?? throw new RunnerOptionsException(
                "runner_stage_invalid",
                $"--stage must be one of: {string.Join(", ", BenchmarkStages.All)}.");

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new RunnerOptionsException("runner_argument_value_missing", $"{option} requires a value.");
        return args[index];
    }

    private static string Resolve(string root, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
}

public static class RepositoryRootLocator
{
    public static string Find(string? currentDirectory = null, string? executableDirectory = null)
    {
        foreach (var start in new[] { currentDirectory ?? Environment.CurrentDirectory, executableDirectory ?? AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new RunnerOptionsException("repository_root_not_found", "Could not locate DreamGenClone.sln from the current directory or executable path.");
    }
}

public sealed class RunnerOptionsException : Exception
{
    public RunnerOptionsException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

public sealed record RunnerDatabasePlan(string ConfigurationDatabasePath, string WorkingDatabasePath)
{
    public static RunnerDatabasePlan Create(string configurationDatabasePath, string caseId, int iteration)
    {
        var config = Path.GetFullPath(configurationDatabasePath);
        var safeCaseId = string.Concat(caseId.Select(character => char.IsLetterOrDigit(character) ? character : '-'));
        var working = Path.Combine(Path.GetTempPath(), $"dreamgenclone-b100-{safeCaseId}-{iteration}-{Guid.NewGuid():N}.db");
        if (string.Equals(config, working, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The live configuration database cannot be used as a pipeline working database.");
        return new RunnerDatabasePlan(config, working);
    }
}