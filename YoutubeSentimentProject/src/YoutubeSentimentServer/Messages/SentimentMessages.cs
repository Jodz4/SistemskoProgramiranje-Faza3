using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Messages;

public sealed record EnsureVideoSentimentState(string VideoId);

public sealed record RecalculateVideoSentiment(
    string VideoId,
    IReadOnlyList<CommentDto> Comments);

public sealed record GetVideoSentiment(string VideoId);