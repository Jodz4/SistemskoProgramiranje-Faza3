namespace YoutubeSentimentServer.Messages;

public sealed record RegisterVideo(string VideoId);

public sealed record RegisterVideoResult(
    string VideoId,
    bool RegisteredNow,
    string Message);

public sealed record GetTrackedVideos;

public sealed record IsVideoTracked(string VideoId);

public sealed record VideoTrackedResult(
    string VideoId,
    bool IsTracked);