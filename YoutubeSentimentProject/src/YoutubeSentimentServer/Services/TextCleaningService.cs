using System.Net;
using System.Text.RegularExpressions;

namespace YoutubeSentimentServer.Services;

public sealed class TextCleaningService : ITextCleaningService
{
    public string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string decoded = WebUtility.HtmlDecode(text);
        string normalized = Regex.Replace(decoded, @"\s+", " ");

        return normalized.Trim();
    }
}