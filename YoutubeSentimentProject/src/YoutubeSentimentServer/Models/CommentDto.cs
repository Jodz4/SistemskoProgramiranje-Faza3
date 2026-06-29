namespace YoutubeSentimentServer.Models;

public sealed class CommentDto
{
    public required string VideoId { get; init; }

    public required string CommentId { get; init; }

    public required string Author { get; init; }

    public required string Text { get; init; }

    public DateTime PublishedAt { get; init; }

    public int LikeCount { get; init; }
}