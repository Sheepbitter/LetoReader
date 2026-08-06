using Reader.Modules;

namespace Reader.Modules.Reading;

/// <summary>
/// Pure, dependency-free logic for the speed reader's word pieces,
/// peripheral words and block-based advancement. Extracted from ReaderManager
/// so it can be unit tested without Blazor/state machinery.
/// </summary>
public static class WordBlockProcessor
{
    /// <summary>
    /// One reading window as rendered by the reader: surrounding words, the highlighted
    /// word, and following words.
    /// </summary>
    public sealed record DisplayBlock(List<string> LookBehind, string CurrentWord, List<string> LookAhead);

    /// <summary>
    /// The reading window for <paramref name="position"/>.
    /// <para>
    /// In per-word reading the highlighted word is the word at <paramref name="position"/>,
    /// surrounded by the peripheral look-behind and look-ahead (char limit each side).
    /// </para>
    /// <para>
    /// In block reading the words consumed this tick form one contiguous block (current
    /// word + look-ahead up to the char limit). The highlighted word is the center of that
    /// block, so the block's own words appear to the left and right of it instead of
    /// re-showing previously consumed blocks.
    /// </para>
    /// </summary>
    public static DisplayBlock GetDisplayBlock(List<string> textPieces, int position, int peripheralCharsCount, bool blockReading, bool autoPauseOnPunctuation)
    {
        if (!blockReading)
        {
            return new DisplayBlock(
                GetLookBehind(textPieces, position, peripheralCharsCount),
                textPieces[position],
                GetLookAhead(textPieces, position, peripheralCharsCount, autoPauseOnPunctuation));
        }

        var (center, aheadCount) = ComputeBlock(textPieces, position, peripheralCharsCount);
        int blockEnd = Math.Min(center + 1 + aheadCount, textPieces.Count);

        var lookBehind = new List<string>();
        for (int i = position; i < center; i++)
            lookBehind.Add(textPieces[i]);

        var lookAhead = new List<string>();
        for (int i = center + 1; i < blockEnd; i++)
            lookAhead.Add(textPieces[i]);

        return new DisplayBlock(lookBehind, textPieces[center], lookAhead);
    }
    /// <summary>
    /// Splits raw text into text pieces (words), chopping any piece longer than
    /// <paramref name="wordCharLimit"/> into chunks of that size.
    /// </summary>
    public static List<string> BuildTextPieces(string text, int wordCharLimit)
    {
        var unvalidatedTextPieces = TextHelper.SeparateText(text);

        List<string> newTextPieces = new();
        foreach (var currentTextPiece in unvalidatedTextPieces)
        {
            var textPiece = currentTextPiece;
            while (textPiece.Length > wordCharLimit)
            {
                newTextPieces.Add(textPiece[..Math.Min(wordCharLimit, textPiece.Length - 1)]);
                textPiece = textPiece.Substring(wordCharLimit);
            }
            newTextPieces.Add(textPiece);
        }

        return newTextPieces;
    }

    /// <summary>
    /// Words following <paramref name="position"/> that fit within the peripheral
    /// character limit. The limit is a running budget that includes one space per word.
    /// When <paramref name="autoPauseOnPunctuation"/> is set and the current word ends
    /// with strong punctuation, no look-ahead is returned.
    /// </summary>
    public static List<string> GetLookAhead(List<string> textPieces, int position, int peripheralCharsCount, bool autoPauseOnPunctuation)
    {
        if (position < textPieces.Count && autoPauseOnPunctuation)
        {
            string currentWord = textPieces[position];
            if (currentWord.Length > 0)
            {
                char lastChar = currentWord[^1];
                if (lastChar is '.' or '!' or '?' or ',' or ';' or ':')
                {
                    return new List<string>();
                }
            }
        }

        List<string> result = new();
        int totalChars = 0;
        for (int i = position + 1; i < textPieces.Count; i++)
        {
            string word = textPieces[i];
            if (totalChars + word.Length <= peripheralCharsCount)
            {
                result.Add(word);
                totalChars += word.Length + 1;
            }
            else
            {
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// Words preceding <paramref name="position"/> that fit within the peripheral
    /// character limit, returned in reading order.
    /// </summary>
    public static List<string> GetLookBehind(List<string> textPieces, int position, int peripheralCharsCount)
    {
        List<string> result = new();

        if (position <= 0)
            return result;

        int charCount = 0;

        int i = position - 1;

        while (i >= 0 && charCount + textPieces[i].Length <= peripheralCharsCount)
        {
            result.Add(textPieces[i]);
            charCount += textPieces[i].Length + 1;
            i--;
        }

        result.Reverse();
        return result;
    }

    public static bool IsPunctuationEnding(string word)
    {
        return word.Length > 0 && word[^1] is '.' or '!' or '?' or ',' or ';' or ':' or '-' or '—';
    }

    /// <summary>
    /// Words in <c>[position, position + blockSize)</c> that begin with an uppercase letter
    /// and do not follow a sentence boundary. Such words are usually names or formal
    /// nouns introduced mid-sentence, which benefit from a short pause when first shown.
    /// A word at the very start of the text is never counted.
    /// </summary>
    public static int GetCapitalizedWordCount(List<string> textPieces, int position, int blockSize)
    {
        int count = 0;
        int end = Math.Min(position + blockSize, textPieces.Count);
        for (int i = position; i < end; i++)
        {
            if (StartsWithUppercaseLetter(textPieces[i]) && !FollowsSentenceBoundary(textPieces, i))
                count++;
        }
        return count;
    }

    private static bool StartsWithUppercaseLetter(string word)
    {
        for (int i = 0; i < word.Length; i++)
        {
            if (char.IsLetter(word[i]))
                return char.IsUpper(word[i]);
            if (!(word[i] == '"' || word[i] == '\'' || word[i] == '(' || word[i] == '[' || word[i] == '“' || word[i] == '‘' || word[i] == '«'))
                return false;
        }
        return false;
    }

    private static bool FollowsSentenceBoundary(List<string> textPieces, int index)
    {
        if (index <= 0)
            return true;
        string previous = textPieces[index - 1];
        return previous.Length > 0 && previous[^1] is '.' or '!' or '?';
    }

    /// <summary>
    /// How many words the reader should advance per tick.
    /// When block reading is enabled and a peripheral character limit is set, the
    /// current word and its look-ahead peripherals are treated as one block, so the
    /// position jumps past the whole block instead of one word at a time.
    /// Punctuation-ending words do not cut the block short: following words still fit
    /// the peripheral budget and the sentence-end pause is applied by
    /// <see cref="CalculateInterval"/> instead.
    /// The peripheral budget for the words after the highlighted word starts at the
    /// highlighted word, not at the block start.
    /// </summary>
    public static int GetBlockAdvanceCount(List<string> textPieces, int position, int peripheralCharsCount, bool blockReading, bool autoPauseOnPunctuation)
    {
        if (!blockReading || peripheralCharsCount <= 0)
            return 1;

        var (center, aheadCount) = ComputeBlock(textPieces, position, peripheralCharsCount);
        return (center - position) + 1 + aheadCount;
    }

    /// <summary>
    /// The consumed block: words from <paramref name="position"/> up to the last word
    /// that fits the peripheral budget measured from the highlighted word
    /// (<paramref name="center"/>). The center starts around the middle of the block and
    /// shifts toward the block-start word when that word is long enough to push the
    /// look-behind over the budget (the look-ahead budget never counts the block-start
    /// word itself). When the block-start word has no room ahead (the next word is longer
    /// than the limit), the center moves to that next word so the block-start word is not
    /// left stranded on its own.
    /// </summary>
    private static (int Center, int AheadCount) ComputeBlock(List<string> textPieces, int position, int peripheralCharsCount)
    {
        int oldBlockSize = 1 + GetLookAhead(textPieces, position, peripheralCharsCount, false).Count;
        int center = position + oldBlockSize / 2;

        while (center > position)
        {
            int behindChars = 0;
            for (int i = position; i < center; i++)
                behindChars += textPieces[i].Length + 1;
            if (behindChars - 1 <= peripheralCharsCount)
                break;
            center--;
        }

        // If the block-start word alone was the only option (next word too long to fit
        // ahead of it), step the center to that next word instead, as long as the
        // block-start word fits the look-behind budget on its own.
        if (center == position
            && center + 1 < textPieces.Count
            && textPieces[position].Length <= peripheralCharsCount)
        {
            center = position + 1;
        }

        int aheadCount = GetLookAhead(textPieces, center, peripheralCharsCount, false).Count;
        return (center, aheadCount);
    }

    /// <summary>
    /// Average word length and average punctuation pause per word for the 1000-word
    /// chunk containing <paramref name="position"/>. Used to keep the per-block display
    /// interval proportional to the number of words read.
    /// </summary>
    public static (double AvgCharsPerWord, double AvgPunctuationBonus) CalculateChunkAverages(
        List<string> textPieces, int position, int chunkSize, bool autoPauseOnPunctuation, double punctuationPauseMultiplier)
    {
        int chunkStart = (position / chunkSize) * chunkSize;
        int end = Math.Min(textPieces.Count, chunkStart + chunkSize);
        int count = end - chunkStart;

        double totalChars = 0;
        double totalPunctBonus = 0;
        for (int i = chunkStart; i < end; i++)
        {
            totalChars += textPieces[i].Length;
            if (autoPauseOnPunctuation && IsPunctuationEnding(textPieces[i]))
                totalPunctBonus += punctuationPauseMultiplier;
        }

        return (count > 0 ? totalChars / count : 5.0, count > 0 ? totalPunctBonus / count : 0);
    }

    /// <summary>
    /// Display interval for one tick. The base per-word interval is scaled by the
    /// character density of the displayed block (current word + peripherals) and by
    /// the number of words consumed in this tick (<paramref name="blockSize"/>),
    /// so total reading time is preserved in both block and per-word modes.
    /// Punctuation pauses are applied for each punctuation-ending word consumed in
    /// the tick.
    /// </summary>
    public static double CalculateInterval(
        double baseInterval,
        double avgCharsPerWord,
        double avgPunctuationBonus,
        string currentWord,
        List<string> lookBehindWords,
        List<string> lookAheadWords,
        int blockSize,
        bool autoPauseOnPunctuation,
        double punctuationPauseMultiplier,
        bool autoPauseOnCapitalizedWord = false,
        int capitalizedWordCount = 0)
    {
        double adjustedBaseInterval = baseInterval / (1 + avgPunctuationBonus);

        int displayedChars = currentWord.Length
            + lookBehindWords.Sum(w => w.Length)
            + lookAheadWords.Sum(w => w.Length);

        int displayedWordCount = 1 + lookBehindWords.Count + lookAheadWords.Count;
        double expectedChars = avgCharsPerWord * displayedWordCount;

        if (expectedChars > 0)
        {
            adjustedBaseInterval *= (displayedChars / expectedChars);
        }

        double currentInterval = adjustedBaseInterval * blockSize;

        if (autoPauseOnPunctuation)
        {
            int punctuationWordCount = 0;
            if (blockSize > 1)
            {
                // The whole block is consumed this tick; the punctuation-ending word can sit
                // before or after the highlighted center, so count the full block.
                punctuationWordCount += lookBehindWords.Count(IsPunctuationEnding)
                    + (IsPunctuationEnding(currentWord) ? 1 : 0)
                    + lookAheadWords.Count(IsPunctuationEnding);
            }
            else
            {
                punctuationWordCount = IsPunctuationEnding(currentWord) ? 1 : 0;
            }

            // At most one punctuation pause per tick. Blocks can contain several
            // punctuation-ending words, but only one sentence boundary pauses between
            // consecutive windows; stacking a pause per word would over-inflate one tick.
            if (punctuationWordCount > 1)
                punctuationWordCount = 1;

            if (punctuationWordCount > 0)
            {
                currentInterval += adjustedBaseInterval * punctuationPauseMultiplier * punctuationWordCount;
            }
        }

        // A capitalized word mid-sentence (a name or formal noun) pauses once per tick,
        // using the same pause amount as the punctuation pause.
        if (autoPauseOnCapitalizedWord && capitalizedWordCount > 0)
        {
            currentInterval += adjustedBaseInterval * punctuationPauseMultiplier;
        }

        return currentInterval;
    }
}
