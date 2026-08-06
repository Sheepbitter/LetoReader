using Reader.Modules.Reading;
using Xunit;
using Xunit.Abstractions;

namespace Reader.Tests;

public class BlockReadingFlowTests
{
    private readonly ITestOutputHelper _output;

    public BlockReadingFlowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BlockReading_PrintsWindowSequence()
    {
        const string sample = "The old fortress stood on a windswept hill overlooking the river valley below the town. " +
            "Its walls weathered a thousand storms and three long sieges in the years before the quiet treaty was finally signed at dawn. " +
            "Every single stone held its own story carved into pale gray granite by generations of master masons. " +
            "The moat remained filled with deep frosty water that froze each winter into a shimmering sheet of dark ice. " +
            "Knights of many houses camped upon the eastern meadows near the broad road to the gates. " +
            "The overseer kept the tall doors locked at dusk and patrolled the crenellations with a sharp eyed watch.";

        var pieces = WordBlockProcessor.BuildTextPieces(sample, 30);
        _output.WriteLine($"word count: {pieces.Count}");

        int position = 0;
        int turns = 0;
        var covered = new HashSet<int>();
        while (position < pieces.Count - 1 && turns < 100)
        {
            var block = WordBlockProcessor.GetDisplayBlock(pieces, position, peripheralCharsCount: 10, blockReading: true, autoPauseOnPunctuation: true);
            var text = string.Join(" ", block.LookBehind.Concat(new[] { $"[{block.CurrentWord}]" }).Concat(block.LookAhead));
            _output.WriteLine($"{turns:00}: {text}");

            int blockSize = WordBlockProcessor.GetBlockAdvanceCount(pieces, position, peripheralCharsCount: 10, blockReading: true, autoPauseOnPunctuation: true);
            int blockEnd = Math.Min(position + blockSize, pieces.Count);

            // The displayed window is exactly the consumed block, in order, without repeats.
            Assert.Equal(pieces.Skip(position).Take(blockEnd - position), block.LookBehind.Concat(new[] { block.CurrentWord }).Concat(block.LookAhead));
            // The peripheral budget is measured from the highlighted word, one side at a
            // time: look-behind and look-ahead each fit within the character limit.
            Assert.True(block.LookBehind.Sum(w => w.Length + 1) - 1 <= 10);
            Assert.True(block.LookAhead.Sum(w => w.Length + 1) - 1 <= 10);
            // Blocks are disjoint: every word is read exactly once.
            for (int i = position; i < blockEnd; i++)
                Assert.True(covered.Add(i));

            position += blockSize;
            turns++;
        }

// Blocks cover the whole text exactly once; the final word is read as part of the
        // last block rather than left as an unread stop position.
        Assert.Equal(pieces.Count, covered.Count);
    }

    [Fact]
    public void BlockReading_UserReportedExample()
    {
        const string sample = "they generalize from the nearest example they can remember, even if it is not " +
            "particularly relevant to the new case";

        var pieces = WordBlockProcessor.BuildTextPieces(sample, 30);
        int position = 0;
        while (position < pieces.Count - 1)
        {
            var block = WordBlockProcessor.GetDisplayBlock(pieces, position, peripheralCharsCount: 10, blockReading: true, autoPauseOnPunctuation: true);
            _output.WriteLine(string.Join(" ", block.LookBehind.Concat(new[] { $"[{block.CurrentWord}]" }).Concat(block.LookAhead)));

            position += WordBlockProcessor.GetBlockAdvanceCount(pieces, position, peripheralCharsCount: 10, blockReading: true, autoPauseOnPunctuation: true);
        }
    }
}
