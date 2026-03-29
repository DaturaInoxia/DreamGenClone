namespace DreamGenClone.Tests.StoryParser;

internal static class StoryParserTestHelpers
{
    internal static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "specs"))
                && File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for StoryParser tests.");
    }
}
