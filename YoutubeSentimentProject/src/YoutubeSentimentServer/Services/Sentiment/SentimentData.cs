using Microsoft.ML.Data;

namespace YoutubeSentimentServer.Services.Sentiment;

public sealed class SentimentData
{
    [LoadColumn(0)]
    public string Text { get; set; } = string.Empty;

    [LoadColumn(1)]
    public bool Label { get; set; }
}