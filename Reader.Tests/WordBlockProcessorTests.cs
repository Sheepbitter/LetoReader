using Reader.Data.Reading;
using Reader.Modules;
using Reader.Modules.Reading;
using Xunit;

namespace Reader.Tests;

public class WordBlockProcessorTests
{
    // ---- BuildTextPieces (pre-existing behavior) ----

    [Fact]
    public void BuildTextPieces_SplitsSimpleTextIntoWords()
    {
        var pieces = WordBlockProcessor.BuildTextPieces("the quick brown fox", 30);

        Assert.Equal(new List<string> { "the", "quick", "brown", "fox" }, pieces);
    }

    [Fact]
    public void BuildTextPieces_SplitsAcrossLines()
    {
        var pieces = WordBlockProcessor.BuildTextPieces($"line one{Environment.NewLine}line two", 30);

        Assert.Equal(new List<string> { "line", "one", "line", "two" }, pieces);
    }

    [Fact]
    public void BuildTextPieces_LeavesWordAtCharLimitUntouched()
    {
        var pieces = WordBlockProcessor.BuildTextPieces("abcdefghij", 10);

        Assert.Single(pieces);
        Assert.Equal("abcdefghij", pieces[0]);
    }

    [Fact]
    public void BuildTextPieces_WrapsWordLongerThanCharLimit()
    {
        var pieces = WordBlockProcessor.BuildTextPieces("HelloWorld", 5);

        Assert.Equal(new List<string> { "Hello", "World" }, pieces);
    }

    [Fact]
    public void BuildTextPieces_ChunksWordInMultipleSizedFragments()
    {
        var pieces = WordBlockProcessor.BuildTextPieces("abcdefghijklmno", 10);

        Assert.Equal(new List<string> { "abcdefghij", "klmno" }, pieces);
    }

    [Fact]
    public void BuildTextPieces_WrapsLongWordInsideSentenceAndKeepsOrder()
    {
        var pieces = WordBlockProcessor.BuildTextPieces("one s u p e r l o n g w o r d tail", 6);

        Assert.Equal("one", pieces[0]);
        Assert.Equal("s", pieces[1]);
        Assert.Equal("tail", pieces[^1]);
    }

    // ---- GetLookAhead (pre-existing behavior) ----

    [Fact]
    public void GetLookAhead_ReturnsFollowingWordsOnly()
    {
        var words = new List<string> { "aa", "bbb", "cccc", "dd" };
        var lookAhead = WordBlockProcessor.GetLookAhead(words, 0, 6, false);

        // skips current word (index 0); budget 6 -> "bbb " (4) fits, "cccc " (5) -> 9 > 6 stop
        Assert.Equal(new List<string> { "bbb" }, lookAhead);
    }

    [Fact]
    public void GetLookAhead_CountsSpaceTowardsBudget_WhenExactlyConsumingBudget()
    {
        var words = new List<string> { "ab", "cd", "efg" };
        var lookAhead = WordBlockProcessor.GetLookAhead(words, 0, 4, false);

        // "cd " = 3, adding "efg" would be 3 + 4 = 7 > 4, only cd fits.
        Assert.Equal(new List<string> { "cd" }, lookAhead);
    }

    [Fact]
    public void GetLookAhead_ReturnsEmptyAtEnd()
    {
        var words = new List<string> { "one", "two" };
        Assert.Empty(WordBlockProcessor.GetLookAhead(words, 1, 40, false));
    }

    [Fact]
    public void GetLookAhead_StopsEarlyOnPunctuationWhenConfigured()
    {
        var words = new List<string> { "end.", "following", "word" };
        var lookAhead = WordBlockProcessor.GetLookAhead(words, 0, 40, true);

        Assert.Empty(lookAhead);
    }

    [Fact]
    public void GetLookAhead_IgnoresPunctuationCheckWhenConfigOff()
    {
        var words = new List<string> { "end.", "following", "word" };
        var lookAhead = WordBlockProcessor.GetLookAhead(words, 0, 40, false);

        Assert.Equal(new List<string> { "following", "word" }, lookAhead);
    }

    // ---- GetLookBehind (pre-existing behavior) ----

    [Fact]
    public void GetLookBehind_ReturnsPrecedingWordsInReadingOrder()
    {
        var words = new List<string> { "one", "two", "three", "four" };
        var lookBehind = WordBlockProcessor.GetLookBehind(words, 3, 10);

        Assert.Equal(new List<string> { "two", "three" }, lookBehind);
    }

    [Fact]
    public void GetLookBehind_ReturnsEmptyAtStart()
    {
        var words = new List<string> { "one", "two" };
        Assert.Empty(WordBlockProcessor.GetLookBehind(words, 0, 40));
    }

    [Fact]
    public void GetLookBehind_RespectsPeripheralCharLimit()
    {
        var words = new List<string> { "one", "two", "three", "four" };
        var lookBehind = WordBlockProcessor.GetLookBehind(words, 4, 4);

        Assert.Single(lookBehind);
        Assert.Equal("four", lookBehind[0]);
    }

    // ---- IsPunctuationEnding (pre-existing behavior) ----

    [Theory]
    [InlineData("word.", true)]
    [InlineData("word!", true)]
    [InlineData("word?", true)]
    [InlineData("word,", true)]
    [InlineData("word;", true)]
    [InlineData("word:", true)]
    [InlineData("word-", true)]
    [InlineData("word—", true)]
    [InlineData("word", false)]
    [InlineData("", false)]
    public void IsPunctuationEnding_DetectsTrailingPunctuation(string word, bool expected)
    {
        Assert.Equal(expected, WordBlockProcessor.IsPunctuationEnding(word));
    }

    // ---- GetBlockAdvanceCount (new block reading behavior) ----

    [Fact]
    public void GetBlockAdvanceCount_ReturnsOneWhenBlockReadingDisabled()
    {
        var words = new List<string> { "a", "b", "c" };
        Assert.Equal(1, WordBlockProcessor.GetBlockAdvanceCount(words, 0, 12, false, false));
    }

    [Fact]
    public void GetBlockAdvanceCount_ReturnsOneWhenPeripheralLimitIsZero()
    {
        var words = new List<string> { "a", "b", "c" };
        Assert.Equal(1, WordBlockProcessor.GetBlockAdvanceCount(words, 0, 0, true, false));
    }

    [Fact]
    public void GetBlockAdvanceCount_JumpsPastWholeLookAheadBlock()
    {
        var words = new List<string> { "one", "two", "three", "four", "five" };
        // center = "two"; ahead from center fits "three", "four" (each under limit 10) -> advance 4
        Assert.Equal(4, WordBlockProcessor.GetBlockAdvanceCount(words, 0, 10, true, false));
    }

    [Fact]
    public void GetBlockAdvanceCount_DoesNotStopBlockAtPunctuation()
    {
        var words = new List<string> { "end.", "next", "word" };
        // Punctuation doesn't cut the block; the pause is handled via CalculateInterval.
        Assert.Equal(3, WordBlockProcessor.GetBlockAdvanceCount(words, 0, 20, true, true));
    }

    [Fact]
    public void GetBlockAdvanceCount_LowLimitMeansSmallBlock()
    {
        var words = new List<string> { "one", "two", "three" };
        // limit 4 -> only "two" fits -> 1 + 1 = 2
        Assert.Equal(2, WordBlockProcessor.GetBlockAdvanceCount(words, 0, 4, true, false));
    }

    [Fact]
    public void GetDisplayBlock_ShowsNextWordAfterPunctuationInBlockMode()
    {
        var words = new List<string> { "town.", "Its", "walls" };
        var block = WordBlockProcessor.GetDisplayBlock(words, 0, 10, true, true);

        // block = "town. Its walls" fits the limit; center is "Its".
        Assert.Equal(new List<string> { "town." }, block.LookBehind);
        Assert.Equal("Its", block.CurrentWord);
        Assert.Equal(new List<string> { "walls" }, block.LookAhead);
    }

    [Fact]
    public void GetDisplayBlock_PeripheralBudgetStartsAtMainWord()
    {
        var words = new List<string> { "nearest", "example", "they", "said" };
        var block = WordBlockProcessor.GetDisplayBlock(words, 0, 10, true, false);

        // "they" is 4 chars and fits the 10-char budget measured from "example":
        // "they said" is 9 chars. The block-start budget would have excluded both.
        Assert.Equal("example", block.CurrentWord);
        Assert.Equal(new List<string> { "nearest" }, block.LookBehind);
        Assert.Equal(new List<string> { "they", "said" }, block.LookAhead);
    }

    [Fact]
    public void GetDisplayBlock_LargerLimitAddsFollowingWords()
    {
        var words = new List<string> { "generalize", "from", "the", "nearest", "example", "they" };
        var block = WordBlockProcessor.GetDisplayBlock(words, 0, 16, true, false);

        // main = "the"; "nearest example" (15 chars) fits a fresh 16-char budget.
        Assert.Equal("the", block.CurrentWord);
        Assert.Equal(new List<string> { "generalize", "from" }, block.LookBehind);
        Assert.Equal(new List<string> { "nearest", "example" }, block.LookAhead);
    }

    // ---- CalculateInterval (block reading timing) ----

    [Fact]
    public void CalculateInterval_ReturnsBaseIntervalForSingleWord()
    {
        double interval = WordBlockProcessor.CalculateInterval(1.0, 5.0, 0, "hello", new(), new(), 1, false, 0.5);

        Assert.Equal(1.0, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_SingleWordWithoutDensityDataKeepsBaseInterval()
    {
        double interval = WordBlockProcessor.CalculateInterval(1.0, 0.0, 0, "hello", new(), new(), 1, false, 0.5);

        Assert.Equal(1.0, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_ScalesByCharacterDensity()
    {
        // 10 chars vs expected 5 per word -> 2x
        double interval = WordBlockProcessor.CalculateInterval(1.0, 5.0, 0, "helloworld", new List<string>(), new List<string>(), 1, false, 0.5);

        Assert.Equal(2.0, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_ScalesBlockIntervalByBlockSize()
    {
        double interval = WordBlockProcessor.CalculateInterval(1.0, 0.0, 0, "a", new List<string>(), new List<string>(), 5, false, 0.5);

        Assert.Equal(5.0, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_AddsPunctuationPauseForCurrentWord()
    {
        double interval = WordBlockProcessor.CalculateInterval(1.0, 5.0, 0, "hello.", new List<string>(), new List<string>(), 1, true, 0.5);

        // adjusted base (scaled by density 6/5 = 1.2) + pause 1.2 * 0.5 = 1.8
        Assert.Equal(1.8, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_CapsPunctuationPauseAtOnePerBlock()
    {
        var lookAhead = new List<string> { "no.", "yes!" };
        double interval = WordBlockProcessor.CalculateInterval(1.0, 0.0, 0, "plain,", new List<string>(), lookAhead, 3, true, 0.5);

        // base 1 * blockSize 3 = 3; the current word and two look-ahead words all end in
        // punctuation, but only ONE pause kicks in: 3 + 1 * 0.5 = 3.5
        Assert.Equal(3.5, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_AddsPunctuationPauseForBlockWordBeforeCenter()
    {
        var lookBehind = new List<string> { "end." };
        double interval = WordBlockProcessor.CalculateInterval(1.0, 0.0, 0, "next", lookBehind, new List<string>(), 2, true, 0.5);

        // base 1 * blockSize 2 = 2, plus 1 punctuation word in look-behind * 1 * 0.5 = 2.5
        Assert.Equal(2.5, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_CapsPunctuationPauseAcrossWholeBlock()
    {
        // A comma-dense block is consumed whole in one tick; every word ends in
        // punctuation, but only one sentence-boundary pause may apply.
        var words = new List<string> { "Hell,", "yeah,", "sure,", "ok." };
        var block = WordBlockProcessor.GetDisplayBlock(words, 0, 10, true, true);
        int blockSize = WordBlockProcessor.GetBlockAdvanceCount(words, 0, 10, true, true);

        Assert.Equal(4, blockSize);
        Assert.Equal(new List<string> { "Hell," }, block.LookBehind);
        Assert.Equal("yeah,", block.CurrentWord);
        Assert.Equal(new List<string> { "sure,", "ok." }, block.LookAhead);

        double interval = WordBlockProcessor.CalculateInterval(
            1.0, 0.0, 0, block.CurrentWord, block.LookBehind, block.LookAhead, blockSize, true, 0.5);

        // base 1 * blockSize 4 = 4, plus exactly ONE pause = 4.5 (not 4 + 4 * 0.5 = 6)
        Assert.Equal(4.5, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_DoesNotRescaleWhenPunctuationBonusPresent()
    {
        // avgPunctuationBonus 1.0 halves the base before density scaling
        double interval = WordBlockProcessor.CalculateInterval(2.0, 10.0, 1.0, "hello", new List<string>(), new List<string>(), 1, false, 0.5);

        // adjusted base = 2 / (1+1) = 1, density 5/10 -> 1 * 0.5 = 0.5
        Assert.Equal(0.5, interval, precision: 4);
    }

    [Fact]
    public void GetDisplayBlock_ShiftsCenterLeftForLongAnchorWord()
    {
        var words = new List<string> { "remember,", "even", "if", "it", "is", "not" };
        var block = WordBlockProcessor.GetDisplayBlock(words, 0, 10, true, false);

        // "remember, even" would exceed the limit, so the center shifts to "even":
        // look-behind = "remember," (9), look-ahead = "if it is" (8).
        Assert.Equal(new List<string> { "remember," }, block.LookBehind);
        Assert.Equal("even", block.CurrentWord);
        Assert.Equal(new List<string> { "if", "it", "is" }, block.LookAhead);
    }

[Fact]
    public void GetDisplayBlock_LongAnchorWordBecomesHighlightedWord()
    {
        var words = new List<string> { "particularly", "relevant", "to", "the", "new" };
        var block = WordBlockProcessor.GetDisplayBlock(words, 0, 10, true, false);

        // 12-char anchor can't fit behind the highlight, so it is the highlighted word.
        Assert.Empty(block.LookBehind);
        Assert.Equal("particularly", block.CurrentWord);
        Assert.Equal(new List<string> { "relevant" }, block.LookAhead);
    }

    [Fact]
    public void GetDisplayBlock_DoesNotStrandBlockStartWordAlone()
    {
        var words = new List<string> { "not", "particularly", "relevant", "to", "the", "new" };
        var block = WordBlockProcessor.GetDisplayBlock(words, 0, 10, true, false);

        // "not" has no room ahead (particularly is 12 chars), so it joins as look-behind
        // of the long word instead of appearing alone.
        Assert.Equal(new List<string> { "not" }, block.LookBehind);
        Assert.Equal("particularly", block.CurrentWord);
        Assert.Equal(new List<string> { "relevant" }, block.LookAhead);
    }

    // ---- Capitalized word pause ----

    [Fact]
    public void GetCapitalizedWordCount_CountsMidSentenceProperNouns()
    {
        var words = new List<string> { "The", "neighbors,", "John", "and", "Mary", "moved", "away.", "Next", "week", "Sam", "did." };

        // "John", "Mary" and "Sam" sit mid-sentence; "The" starts the text and "Next"
        // follows a period, so neither counts.
        Assert.Equal(3, WordBlockProcessor.GetCapitalizedWordCount(words, 0, words.Count));
    }

    [Fact]
    public void GetCapitalizedWordCount_RespectsBlockRange()
    {
        var words = new List<string> { "the", "girl", "said,", "Peter", "came", "home", "early." };

        // The block only covers [.. "said,"]; "Peter" (in the next block) must not count.
        Assert.Equal(0, WordBlockProcessor.GetCapitalizedWordCount(words, 0, 3));
        Assert.Equal(1, WordBlockProcessor.GetCapitalizedWordCount(words, 3, 2));
    }

    [Fact]
    public void CalculateInterval_CapitalizedPauseAddedOncePerTick()
    {
        // base 1 * blockSize 3 = 3; three capitalized words in the block still pause once.
        double interval = WordBlockProcessor.CalculateInterval(
            1.0, 0.0, 0, "plain", new List<string>(), new List<string>(), 3, false, 0.5,
            autoPauseOnCapitalizedWord: true, capitalizedWordCount: 3);

        Assert.Equal(3.5, interval, precision: 4);
    }

    [Fact]
    public void CalculateInterval_NoCapitalizedPauseWhenDisabled()
    {
        double interval = WordBlockProcessor.CalculateInterval(
            1.0, 0.0, 0, "plain", new List<string>(), new List<string>(), 1, false, 0.5,
            autoPauseOnCapitalizedWord: false, capitalizedWordCount: 1);

        Assert.Equal(1.0, interval, precision: 4);
    }

    // ---- ReaderConfig defaults (regression guard) ----

    [Fact]
    public void ReaderConfig_BlockReadingDefaultsToTrue()
    {
        var config = new ReaderConfig();

        Assert.True(config.BlockReading);
    }
}