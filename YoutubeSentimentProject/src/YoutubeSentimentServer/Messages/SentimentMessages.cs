using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Messages;

public sealed record GetVideoSentiment(string VideoId);

public sealed record AnalyzeSentimentBatch(
    string VideoId,
    IReadOnlyList<CommentDto> Comments);

public sealed record SentimentComputed(
    string VideoId,
    SentimentResultDto Result);