using DreamGenClone.Infrastructure.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public class StoryRankingServiceChunkTests
{
    [Fact]
    public void ShortText_SingleChunk()
    {
        var chunks = StoryRankingService.ChunkStoryText("Short story.", 1000);

        Assert.Single(chunks);
        Assert.Equal("Short story.", chunks[0]);
    }

    [Fact]
    public void EmptyText_SingleChunk()
    {
        var chunks = StoryRankingService.ChunkStoryText("", 1000);

        Assert.Single(chunks);
    }

    [Fact]
    public void ExactSizeText_SingleChunk()
    {
        var text = new string('a', 1000);
        var chunks = StoryRankingService.ChunkStoryText(text, 1000);

        Assert.Single(chunks);
    }

    [Fact]
    public void LongText_SplitsAtParagraphBoundary()
    {
        // Create text with a paragraph break near the middle
        var part1 = new string('a', 450);
        var part2 = new string('b', 450);
        var text = part1 + "\n\n" + part2;

        var chunks = StoryRankingService.ChunkStoryText(text, 500);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(part1, chunks[0]);
        Assert.Equal(part2, chunks[1]);
    }

    [Fact]
    public void LongText_SplitsAtNewlineWhenNoParagraphBreak()
    {
        var part1 = new string('a', 450);
        var part2 = new string('b', 450);
        var text = part1 + "\n" + part2;

        var chunks = StoryRankingService.ChunkStoryText(text, 500);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(part1, chunks[0]);
        Assert.Equal(part2, chunks[1]);
    }

    [Fact]
    public void VeryLongText_ProducesMultipleChunks()
    {
        // 5 paragraphs of ~200 chars each = ~1000 total, chunk at 300
        var paragraphs = Enumerable.Range(1, 5).Select(i => new string((char)('a' + i), 200));
        var text = string.Join("\n\n", paragraphs);

        var chunks = StoryRankingService.ChunkStoryText(text, 300);

        Assert.True(chunks.Count >= 3, $"Expected at least 3 chunks, got {chunks.Count}");
        var reassembled = string.Join("", chunks);
        // All content should be preserved (minus the split newlines)
        Assert.True(reassembled.Length >= text.Length - chunks.Count * 2);
    }

    [Fact]
    public void NoBreaks_CutsAtChunkSize()
    {
        var text = new string('x', 2500); // no newlines at all
        var chunks = StoryRankingService.ChunkStoryText(text, 1000);

        Assert.True(chunks.Count >= 2);
        Assert.True(chunks[0].Length <= 1000);
    }
}
