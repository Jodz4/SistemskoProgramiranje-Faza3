namespace YoutubeSentimentServer.Messages;

public sealed record RegisterVideo(string VideoId);

public sealed record RegisterVideoResult(
    string VideoId,
    bool RegisteredNow,
    string Message);

public sealed record GetTrackedVideos;

public sealed record UnregisterVideo(string VideoId);

public sealed record UnregisterVideoResult(
    string VideoId,
    bool Removed,
    string Message);