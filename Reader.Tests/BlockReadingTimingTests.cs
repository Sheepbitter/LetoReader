using Reader.Modules.Reading;
using Xunit;
using Xunit.Abstractions;

namespace Reader.Tests;

public class BlockReadingTimingTests
{
    private readonly ITestOutputHelper _output;

    public BlockReadingTimingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Timing_BlocksScaleWithWordsAndChars()
    {
        const double readingSpeed = 250;
        const int peripheralCharsCount = 10;
        const double punctuationPauseMultiplier = 0.35;
        const int chunkSize = 1000;

        const string passage = "The old fortress stood on a windswept hill overlooking the river valley below the town. " +
            "Its walls weathered a thousand storms and three long sieges in the years before the quiet treaty was finally signed at dawn. " +
            "Every single stone held its own story carved into pale gray granite by generations of master masons. " +
            "The moat remained filled with deep frosty water that froze each winter into a shimmering sheet of dark ice. " +
            "Knights of many houses camped upon the eastern meadows near the broad road to the gates. " +
            "The overseer kept the tall doors locked at dusk and patrolled the crenellations with a sharp eyed watch.";

        var pieces = WordBlockProcessor.BuildTextPieces(string.Join(" ", Enumerable.Repeat(passage, 10)), 30);
        _output.WriteLine($"word count: {pieces.Count}");

        double baseInterval = 60.0 / readingSpeed;
        double totalTime = 0;
        int totalWords = 0;
        var msPerChars = new List<double>();
        int position = 0;
        int turns = 0;

        while (position < pieces.Count - 1 && turns < 300)
        {
            var (avgChars, avgPunct) = WordBlockProcessor.CalculateChunkAverages(pieces, position, chunkSize, true, punctuationPauseMultiplier);
            var block = WordBlockProcessor.GetDisplayBlock(pieces, position, peripheralCharsCount, true, true);
            int blockSize = WordBlockProcessor.GetBlockAdvanceCount(pieces, position, peripheralCharsCount, true, true);

            double interval = WordBlockProcessor.CalculateInterval(
                baseInterval, avgChars, avgPunct, block.CurrentWord,
                block.LookBehind, block.LookAhead, blockSize, true, punctuationPauseMultiplier);

            int chars = block.LookBehind.Sum(w => w.Length) + block.CurrentWord.Length + block.LookAhead.Sum(w => w.Length);
            double msPerChar = chars > 0 ? interval * 1000 / chars : 0;
            totalTime += interval;
            totalWords += blockSize;
            msPerChars.Add(msPerChar);

            if (turns % 10 == 0 || turns < 5)
            {
                _output.WriteLine($"{turns:000} | words {blockSize:00} | chars {chars:000} | {interval,6:0.000}s | {interval / blockSize * 1000,5:0}ms/word | {msPerChar,5:0}ms/char | {string.Join(" ", block.LookBehind.Concat(new[] { $"[{block.CurrentWord}]" }).Concat(block.LookAhead))}");
            }

            position += blockSize;
            turns++;
        }

        double expectedSeconds = (double)totalWords / (readingSpeed / 60.0);
        _output.WriteLine($"blocks: {turns}, words covered: {totalWords}");
        _output.WriteLine($"total: {totalTime:0.0}s vs expected per-word total {expectedSeconds:0.0}s");
        _output.WriteLine($"ms/char range: {msPerChars.Min():0.0} - {msPerChars.Max():0.0} (max/min ratio {msPerChars.Max() / msPerChars.Min():0.00})");

        // Total reading time must stay close to words-per-minute, not drift with block sizes.
        Assert.True(totalTime < expectedSeconds * 1.25, $"total {totalTime:0.0}s exceeds expected {expectedSeconds:0.0}s by 25%");
        Assert.True(totalTime > expectedSeconds * 0.8, $"total {totalTime:0.0}s is under expected {expectedSeconds:0.0}s by 20%");

        // Every block must be paced by its visible characters: no long block may clock in
        // faster per-char than the shortest block, and vice versa. A blatant inversion
        // means timing no longer scales with the block that is actually shown.
        Assert.True(msPerChars.Max() / msPerChars.Min() < 2.0,
            $"per-char pacing varies too much across blocks: {msPerChars.Min():0.0}-{msPerChars.Max():0.0} ms/char");
    }
}
