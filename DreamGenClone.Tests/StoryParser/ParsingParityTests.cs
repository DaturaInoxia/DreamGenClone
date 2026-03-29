using AngleSharp.Html.Parser;
using DreamGenClone.Infrastructure.StoryParser;

namespace DreamGenClone.Tests.StoryParser;

public sealed class ParsingParityTests
{
    [Fact]
    public async Task Sample1_Page1_ContainsExpectedExtractedPrefix()
    {
        var root = StoryParserTestHelpers.FindRepoRoot();
        var htmlPath = Path.Combine(root, "specs", "StoryParser", "Sample1", "Story1-Page1.html");
        var expectedPath = Path.Combine(root, "specs", "StoryParser", "Sample1", "ExpectedPage1.txt");

        var html = await File.ReadAllTextAsync(htmlPath);
        var expected = await File.ReadAllTextAsync(expectedPath);

        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html);
        var extractor = new DomainStoryExtractor();
        var page = extractor.ExtractPage(document, new Uri("https://www.literotica.com/s/sample"), 1);

        var expectedPrefix = expected.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        Assert.Contains(expectedPrefix, page.ExtractedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sample2_Page1_ContainsExpectedOpeningLine()
    {
        var root = StoryParserTestHelpers.FindRepoRoot();
        var htmlPath = Path.Combine(root, "specs", "StoryParser", "Sample2", "Story2Page1.html");
        var expectedPath = Path.Combine(root, "specs", "StoryParser", "Sample2", "ExpectedPage1.txt");

        var html = await File.ReadAllTextAsync(htmlPath);
        var expected = await File.ReadAllTextAsync(expectedPath);

        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html);
        var extractor = new DomainStoryExtractor();
        var page = extractor.ExtractPage(document, new Uri("https://www.literotica.com/s/sample2"), 1);

        var expectedFirstSentence = expected.Split('.', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        Assert.Contains(expectedFirstSentence, page.ExtractedText, StringComparison.OrdinalIgnoreCase);
    }
}
