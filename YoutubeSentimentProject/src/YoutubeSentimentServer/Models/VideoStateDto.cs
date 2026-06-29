namespace YoutubeSentimentServer.Models;

public sealed class VideoStateDto
{
    public required string VideoId { get; init; }

    public bool IsTracked { get; init; }

    public DateTime RegisteredAt { get; init; }
}