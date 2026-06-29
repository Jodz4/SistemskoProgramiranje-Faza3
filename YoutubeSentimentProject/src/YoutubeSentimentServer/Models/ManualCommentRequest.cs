namespace YoutubeSentimentServer.Models;

public sealed class ManualCommentRequest
{
    public string Author { get; init; } = "Manual tester";

    public required string Text { get; init; }
}