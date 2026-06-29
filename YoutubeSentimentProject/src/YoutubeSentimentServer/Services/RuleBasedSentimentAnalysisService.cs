using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public sealed class RuleBasedSentimentAnalysisService : ISentimentAnalysisService
{
    private static readonly string[] PositiveWords =
    {
        "good", "great", "excellent", "amazing", "useful", "helpful", "love", "like",
        "odlican", "odlicno", "super", "dobro", "korisno", "pomoglo", "volim", "svidja"
    };

    private static readonly string[] NegativeWords =
    {
        "bad", "terrible", "useless", "hate", "awful", "waste", "boring",
        "lose", "uzasno", "glupo", "mrzim", "odvratno", "ne valja", "dosadno"
    };

    public SentimentLabel Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SentimentLabel.Neutral;

        string normalizedText = text.Trim().ToLowerInvariant();

        int positiveScore = CountMatches(normalizedText, PositiveWords);
        int negativeScore = CountMatches(normalizedText, NegativeWords);

        if (positiveScore > negativeScore)
            return SentimentLabel.Positive;

        if (negativeScore > positiveScore)
            return SentimentLabel.Negative;

        return SentimentLabel.Neutral;
    }

    private static int CountMatches(string text, string[] words)
    {
        int count = 0;

        foreach (string word in words)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }
}