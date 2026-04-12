using DreamGenClone.Infrastructure.StoryAnalysis;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class ThemeDefinitionServiceTests
{
    [Fact]
    public void Parser_Extracts_Metadata_And_RawContent()
    {
        var parser = new ThemeDefinitionParser();
        var markdown = """
# Theme Definition: Infidelity with Public Facade

## Theme Metadata

**ID:** `infidelity-public-facade`
**Label:** Infidelity with Public Facade
**Category:** Power
**Weight:** 4

## Description
Body text.
""";

        var result = parser.Parse("infidelity-public-facade.md", markdown);

        Assert.Equal("infidelity-public-facade", result.Id);
        Assert.Equal("Infidelity with Public Facade", result.Label);
        Assert.Equal("Power", result.Category);
        Assert.Equal(4, result.Weight);
        Assert.Contains("## Description", result.RawContent);
        Assert.Empty(result.ParseWarnings);
    }

    [Fact]
    public void Parser_Uses_Fallbacks_When_Metadata_Missing()
    {
        var parser = new ThemeDefinitionParser();
        var markdown = "# Empty";

        var result = parser.Parse("my-file.md", markdown);

        Assert.Equal("my-file", result.Id);
        Assert.Equal("my-file", result.Label);
        Assert.Equal("Uncategorized", result.Category);
        Assert.Equal(0, result.Weight);
        Assert.NotEmpty(result.ParseWarnings);
    }

    [Fact]
    public async Task Service_LoadAllAsync_Reads_All_Markdown_From_Default_Folder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dreamgen-theme-defs-" + Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        var webRoot = Path.Combine(workspaceRoot, "DreamGenClone.Web");
        var definitionsRoot = Path.Combine(workspaceRoot, "specs", "ThemeDefinitaions");

        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(definitionsRoot);

        var fileA = Path.Combine(definitionsRoot, "infidelity-public-facade.md");
        var fileB = Path.Combine(definitionsRoot, "infidelity-public-facade-discovery.md");

        await File.WriteAllTextAsync(fileA, "**ID:** `infidelity-public-facade`\n**Label:** Infidelity with Public Facade\n**Category:** Power\n**Weight:** 4");
        await File.WriteAllTextAsync(fileB, "**ID:** `infidelity-public-discovery`\n**Label:** Infidelity with Public Discovery\n**Category:** Power\n**Weight:** 4");

        var parser = new ThemeDefinitionParser();
        var env = new TestHostEnvironment(webRoot);
        var service = new ThemeDefinitionService(parser, env, NullLogger<ThemeDefinitionService>.Instance);

        var loaded = await service.LoadAllAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, x => x.Id == "infidelity-public-facade");
        Assert.Contains(loaded, x => x.Id == "infidelity-public-discovery");

        Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new NullFileProvider();
            ApplicationName = "DreamGenClone.Tests";
            EnvironmentName = Environments.Development;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; }

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
