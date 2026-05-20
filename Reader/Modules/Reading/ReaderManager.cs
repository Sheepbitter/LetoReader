using Reader.Data.Product;
using Microsoft.JSInterop;
using System.Text;
using Newtonsoft.Json;
using Reader.Modules.Logging;
using Reader.Data.Reading;
using HtmlAgilityPack;
using System.Linq;

namespace Reader.Modules.Reading;

public class ReaderManager
{
    public List<string> TextPieces { get; private set; } = new();
    public bool ReadingStatus { get; private set; }
    public StateManager StateManager { get; set; }
    public ReaderConfig Config;
    private SiteInteraction SiteInteraction;

    private double CurrentChunkAvgCharsPerWord = 5.0;
    private double CurrentChunkAvgPunctuationBonus = 0;
    private int CurrentChunkStart = -1;
    private const int ChunkSize = 1000;

    private List<int> PageStartIndices = new();
    public int TotalPages => StateManager.CurrentState.TotalPages;
    public int CurrentPage => GetPageForPosition((int)State.PositionInfo.Position);

    // This is a binding to avoid unnecessarily long names
    private ReaderState State { get => StateManager.CurrentState; set => StateManager.CurrentState = value; }

    private CancellationTokenSource ReadingTaskTokenSource = new();

    public ReaderManager(StateManager stateManager, ReaderConfig config, SiteInteraction siteInteraction)
    {
        StateManager = stateManager;
        Config = config;
        SiteInteraction = siteInteraction;
        SetupTextPieces();
    }

    public void SetupTextPieces()
    {
        var unvalidatedTextPieces = TextHelper.SeparateText(StateManager.CurrentText);

        List<string> newTextPieces = new();
        PageStartIndices = new();
        int currentIndex = 0;

        foreach (var currentTextPiece in unvalidatedTextPieces)
        {
            var textPiece = currentTextPiece;
            while (textPiece.Length > Config.WordCharLimit)
            {
                newTextPieces.Add(textPiece[..Math.Min(Config.WordCharLimit, textPiece.Length - 1)]);
                currentIndex++;
                textPiece = textPiece.Substring(Config.WordCharLimit);
            }
            newTextPieces.Add(textPiece);
            currentIndex++;
        }

        TextPieces = newTextPieces;

        if (StateManager.CurrentState.TotalPages > 0)
        {
            PageStartIndices.Add(0);
            List<string> finalPieces = new();
            for (int i = 0; i < TextPieces.Count; i++)
            {
                string piece = TextPieces[i];
                if (piece.Contains('\f'))
                {
                    int pageBreakIndex = piece.IndexOf('\f');
                    string beforeBreak = piece.Substring(0, pageBreakIndex);
                    string afterBreak = piece.Substring(pageBreakIndex + 1);

                    if (!string.IsNullOrEmpty(beforeBreak))
                    {
                        finalPieces.Add(beforeBreak);
                    }
                    PageStartIndices.Add(finalPieces.Count);
                    if (!string.IsNullOrEmpty(afterBreak))
                    {
                        finalPieces.Add(afterBreak);
                    }
                }
                else
                {
                    finalPieces.Add(piece);
                }
            }
            TextPieces = finalPieces;
            StateManager.CurrentState.TotalPages = PageStartIndices.Count;
        }

        ClampPosition();
        CurrentChunkStart = -1;
    }

    private void CalculateChunkAverages(int position)
    {
        int chunkStart = (position / ChunkSize) * ChunkSize;
        if (chunkStart == CurrentChunkStart) return;

        CurrentChunkStart = chunkStart;
        int end = Math.Min(TextPieces.Count, chunkStart + ChunkSize);
        double totalChars = 0;
        double totalPunctBonus = 0;
        int count = end - chunkStart;

        for (int i = chunkStart; i < end; i++)
        {
            totalChars += TextPieces[i].Length;
            if (Config.AutoPauseOnPunctuation && TextPieces[i].Length > 0)
            {
                char lastChar = TextPieces[i][^1];
                if (lastChar is '.' or '!' or '?' or ',' or ';' or ':' or '-' or '—')
                {
                    totalPunctBonus += Config.PunctuationPauseMultiplier;
                }
            }
        }

        CurrentChunkAvgCharsPerWord = count > 0 ? totalChars / count : 5.0;
        CurrentChunkAvgPunctuationBonus = count > 0 ? totalPunctBonus / count : 0;
    }

    public async Task HandleStartStop()
    {
        if (!ReadingStatus)
        {
            await StartReadingTask();
        }
        else
        {
            await StopReadingTask();
        }
    }

    public async Task StartReadingTask()
    {
        await Log.Information("ReaderContext: StartReadingTask");
        if (ReadingStatus)
            return;

        CalculateChunkAverages((int)State.PositionInfo.Position);
        ReadingStatus = true;
        // start task
        ReadingTaskTokenSource = new CancellationTokenSource();
        var readerTask = new Task(async () =>
        {
            await ReadingTask((double)60 / Config.ReadingSpeed, ReadingTaskTokenSource.Token);
        }, ReadingTaskTokenSource.Token);
        readerTask.Start();

    }

    public async Task StopReadingTask()
    {
        await Log.Information("ReaderContext: StartReadingTask");
        if (!ReadingStatus)
            return;

        ReadingStatus = false;
        // stop task, if the task started
        if (ReadingTaskTokenSource != null)
        {
            ReadingTaskTokenSource.Cancel();
            ReadingTaskTokenSource.Dispose();
        }

    }

    private async Task ReadingTask(double interval, CancellationToken ct)
    {
        while (true)
        {
            if (State.PositionInfo.Position >= TextPieces.Count - 1 || ct.IsCancellationRequested)
            {
                ReadingStatus = false;
                await SiteInteraction.HandleSiteStateChanged();
                break;
            }

            State.PositionInfo.Position++;
            State.LastRead = DateTime.Now;

            _ = Task.Run(() => StateManager.SaveStates());
            _ = Task.Run(() => SiteInteraction.HandleSiteStateChanged());

            double currentInterval = interval;

            int pos = (int)State.PositionInfo.Position;
            int currentChunk = (pos / ChunkSize) * ChunkSize;
            if (currentChunk != CurrentChunkStart)
            {
                CalculateChunkAverages(pos);
            }

            double avgCharsPerWord = CurrentChunkAvgCharsPerWord;
            double avgPunctuationBonus = CurrentChunkAvgPunctuationBonus;

            double adjustedBaseInterval = interval / (1 + avgPunctuationBonus);

            var lookBehindWords = GetTextPiecesLookBehindInner();
            var lookAheadWords = GetTextPiecesLookAheadInner();
            string currentWord = TextPieces[pos];

            int displayedChars = currentWord.Length
                + lookBehindWords.Sum(w => w.Length)
                + lookAheadWords.Sum(w => w.Length);

            int displayedWordCount = 1 + lookBehindWords.Count + lookAheadWords.Count;
            double expectedChars = avgCharsPerWord * displayedWordCount;

            if (expectedChars > 0)
            {
                adjustedBaseInterval *= (displayedChars / expectedChars);
            }

            currentInterval = adjustedBaseInterval;

            if (Config.AutoPauseOnPunctuation)
            {
                char lastChar = currentWord.Length > 0 ? currentWord[^1] : '\0';
                bool isPunctuation = lastChar is '.' or '!' or '?' or ',' or ';' or ':' or '-' or '—';
                if (isPunctuation)
                {
                    currentInterval += adjustedBaseInterval * Config.PunctuationPauseMultiplier;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(currentInterval));
        }
    }

    public void HandleNavBefore()
    {
        State.PositionInfo.Position -= Config.WordNavCount;
        State.PositionInfo.Position = Math.Max(0, State.PositionInfo.Position);
    }

    public void HandleNavNext()
    {
        State.PositionInfo.Position += Config.WordNavCount;
        State.PositionInfo.Position = Math.Min(TextPieces.Count - 1, State.PositionInfo.Position);
    }

    public int GetPageForPosition(int position)
    {
        if (PageStartIndices.Count == 0) return 0;
        int page = 0;
        for (int i = PageStartIndices.Count - 1; i >= 0; i--)
        {
            if (position >= PageStartIndices[i])
            {
                page = i + 1;
                break;
            }
        }
        return page;
    }

    public void JumpToPage(int page)
    {
        if (PageStartIndices.Count == 0) return;
        int targetPage = Math.Max(1, Math.Min(page, PageStartIndices.Count));
        State.PositionInfo.Position = PageStartIndices[targetPage - 1];
    }

    public void ClampPosition()
    {
        State.PositionInfo.Position = Math.Min(TextPieces.Count - 1, Math.Max(0, State.PositionInfo.Position));
    }

    public Tuple<string, string, string> GetCurrentTextPiece()
    {
        string word = TextPieces[State.PositionInfo.Position];

        string front = word.Substring(0, (word.Length + 1) / 2 - 1);
        string middle = word.Substring((word.Length + 1) / 2 - 1, 1);
        string back = word.Substring((word.Length + 1) / 2);

        return Tuple.Create(front, middle, back);
    }

    public string GetTextPiecesLookAhead()
    {
        var result = Config.RightToLeft ? GetTextPiecesLookBehindInner() : GetTextPiecesLookAheadInner();
        return TextPiecesToStringInOrder(result);
    }
    public string GetTextPiecesLookBehind()
    {
        var result = Config.RightToLeft ? GetTextPiecesLookAheadInner() : GetTextPiecesLookBehindInner();
        return TextPiecesToStringInOrder(result);
    }

    public List<string> GetTextPiecesLookAheadInner()
    {
        if (State.PositionInfo.Position < TextPieces.Count && Config.AutoPauseOnPunctuation)
        {
            string currentWord = TextPieces[(int)State.PositionInfo.Position];
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
        foreach (string word in TextPieces.Skip(State.PositionInfo.Position + 1))
        {
            if (totalChars + word.Length <= Config.PeripheralCharsCount)
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

    public List<string> GetTextPiecesLookBehindInner()
    {
        List<string> result = new();

        if (State.PositionInfo.Position == 0)
            return result;

        int charCount = 0;

        int i = (int)State.PositionInfo.Position - 1;

        while (i >= 0 && charCount + TextPieces[i].Length <= Config.PeripheralCharsCount)
        {
            result.Add(TextPieces[i]);
            charCount += TextPieces[i].Length + 1;
            i--;
        }

        result.Reverse();
        return result;
    }

    public string TextPiecesToStringInOrder(IEnumerable<string> text)
    {
        if (Config.RightToLeft)
            text.Reverse();
        return String.Join(" ", text);
    }
}
