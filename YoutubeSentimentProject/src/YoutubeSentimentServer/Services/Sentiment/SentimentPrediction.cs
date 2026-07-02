using Microsoft.ML.Data;

namespace YoutubeSentimentServer.Services.Sentiment;

public sealed class SentimentPrediction
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    public float Probability { get; set; }

    public float Score { get; set; }
}