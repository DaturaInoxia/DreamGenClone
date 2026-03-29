using AngleSharp.Html.Parser;
using DreamGenClone.Infrastructure.StoryParser;

namespace DreamGenClone.Tests.StoryParser;

public sealed class DeterminismTests
{
    [Fact]
    public async Task Extractor_IsDeterministic_ForSameHtmlInput()
    {
        var root = StoryParserTestHelpers.FindRepoRoot();
        var htmlPath = Path.Combine(root, "specs", "StoryParser", "Sample2", "Story2Page1.html");
        var html = await File.ReadAllTextAsync(htmlPath);

        var parser = new HtmlParser();
        var doc1 = await parser.ParseDocumentAsync(html);
        var doc2 = await parser.ParseDocumentAsync(html);

        var extractor = new DomainStoryExtractor();
        var page1 = extractor.ExtractPage(doc1, new Uri("https://www.literotica.com/s/sample?page=1"), 1);
        var page2 = extractor.ExtractPage(doc2, new Uri("https://www.literotica.com/s/sample?page=1"), 1);

        Assert.Equal(page1.ExtractedText, page2.ExtractedText);
    }
}
