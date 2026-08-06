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
        var newTextPieces = WordBlockProcessor.BuildTextPieces(StateManager.CurrentText, Config.WordCharLimit);
        PageStartIndices = new();

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

        (CurrentChunkAvgCharsPerWord, CurrentChunkAvgPunctuationBonus) = WordBlockProcessor.CalculateChunkAverages(
            TextPieces, position, ChunkSize, Config.AutoPauseOnPunctuation, Config.PunctuationPauseMultiplier);
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
            int pos = (int)State.PositionInfo.Position;
            if (pos >= TextPieces.Count - 1 || ct.IsCancellationRequested)
            {
                ReadingStatus = false;
                await SiteInteraction.HandleSiteStateChanged();
                break;
            }

            double currentInterval = interval;

            int currentChunk = (pos / ChunkSize) * ChunkSize;
            if (currentChunk != CurrentChunkStart)
            {
                CalculateChunkAverages(pos);
            }

            double avgCharsPerWord = CurrentChunkAvgCharsPerWord;
            double avgPunctuationBonus = CurrentChunkAvgPunctuationBonus;

            int blockSize = WordBlockProcessor.GetBlockAdvanceCount(TextPieces, pos, Config.PeripheralCharsCount, Config.BlockReading, Config.AutoPauseOnPunctuation);

            // Time the block exactly as it is rendered: the centre word with its
            // look-behind and look-ahead. Using the block-start anchor would double-count
            // it (it is already part of the look-behind) and drop the centre word's length.
            var displayBlock = GetDisplayBlock();
            var lookBehindWords = displayBlock.LookBehind;
            var lookAheadWords = displayBlock.LookAhead;
            string currentWord = displayBlock.CurrentWord;

            int capitalizedWordCount = Config.AutoPauseOnCapitalizedWord
                ? WordBlockProcessor.GetCapitalizedWordCount(TextPieces, pos, blockSize)
                : 0;

            currentInterval = WordBlockProcessor.CalculateInterval(
                interval,
                avgCharsPerWord,
                avgPunctuationBonus,
                currentWord,
                lookBehindWords,
                lookAheadWords,
                blockSize,
                Config.AutoPauseOnPunctuation,
                Config.PunctuationPauseMultiplier,
                Config.AutoPauseOnCapitalizedWord,
                capitalizedWordCount);

            State.PositionInfo.Position += blockSize;
            if (State.PositionInfo.Position >= TextPieces.Count - 1)
                State.PositionInfo.Position = TextPieces.Count - 1;
            State.LastRead = DateTime.Now;

            _ = Task.Run(() => StateManager.SaveStates());
            _ = Task.Run(() => SiteInteraction.HandleSiteStateChanged());

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
        var currentWord = GetDisplayBlock().CurrentWord;

        string front = currentWord.Substring(0, (currentWord.Length + 1) / 2 - 1);
        string middle = currentWord.Substring((currentWord.Length + 1) / 2 - 1, 1);
        string back = currentWord.Substring((currentWord.Length + 1) / 2);

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
        return GetDisplayBlock().LookAhead;
    }

    public List<string> GetTextPiecesLookBehindInner()
    {
        return GetDisplayBlock().LookBehind;
    }

    private WordBlockProcessor.DisplayBlock GetDisplayBlock()
    {
        return WordBlockProcessor.GetDisplayBlock(TextPieces, (int)State.PositionInfo.Position, Config.PeripheralCharsCount, Config.BlockReading, Config.AutoPauseOnPunctuation);
    }

    public string TextPiecesToStringInOrder(IEnumerable<string> text)
    {
        if (Config.RightToLeft)
            text.Reverse();
        return String.Join(" ", text);
    }
}
