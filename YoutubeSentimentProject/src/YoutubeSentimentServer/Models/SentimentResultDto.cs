namespace YoutubeSentimentServer.Models;

public sealed class SentimentResultDto
{
    public required string VideoId { get; init; }

    public int TotalComments { get; init; }

    public int PositiveCount { get; init; }

    public int NeutralCount { get; init; }

    public int NegativeCount { get; init; }

    public float PositivePercentage { get; init; }

    public float NeutralPercentage { get; init; }

    public float NegativePercentage { get; init; }

    public DateTime CalculatedAt { get; init; }
}