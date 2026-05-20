using Reader.Modules.Reading;
using System.Web;

namespace Reader.Modules;

public static class TextHelper
{
    public static string Sanitize(string text)
    {

        if (text == null)
            return "";

        text = text.Replace("\r\n", "\n");
        text = text.Replace("\r", "\n");
        text = text.Replace("\n", Environment.NewLine);
        text = RemoveEmptySpaces(text);
        text = text.Trim();
        text = HttpUtility.HtmlDecode(text);

        return text;
    }
    public static string JoinSections(string a, string b)
    {
        return a + Enumerable.Repeat(Environment.NewLine, 3) + b;
    }

    public static IEnumerable<string> SeparateText(string text)
    {
        return text.Split(new string[] { " ", Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static string RemoveEmptySpaces(string text)
    {
        var textPieces = text.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        textPieces = textPieces.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        textPieces = textPieces = textPieces.Select(x => x.Trim()).ToArray();
        textPieces = string.Join(Environment.NewLine, textPieces).Split("" , StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, textPieces);
    }

    public static string GetDisplayableReadingTime(int PieceCount, int ReadingSpeed)
    {
        if (ReadingSpeed <= 0) return "0h 0m 0s";
        float totalMinutes = (float)PieceCount / ReadingSpeed;
        int hours = (int)(totalMinutes / 60);
        int minutes = (int)(totalMinutes % 60);
        int seconds = (int)((totalMinutes - (int)totalMinutes) * 60);

        return $"{hours}h {minutes}m {seconds}s";
    }

    public static string JoinWords (IEnumerable<string> words)
    {
        return String.Join(" ", words);
    }
}
