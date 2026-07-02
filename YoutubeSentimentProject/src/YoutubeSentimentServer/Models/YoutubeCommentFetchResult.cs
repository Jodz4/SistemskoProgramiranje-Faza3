using System.Net;

namespace YoutubeSentimentServer.Models;

public sealed class YoutubeCommentFetchResult
{
    public required string VideoId { get; init; }

    public bool Success { get; init; }

    public IReadOnlyList<CommentDto> Comments { get; init; } = Array.Empty<CommentDto>();

    public HttpStatusCode? StatusCode { get; init; }

    public string? ErrorMessage { get; init; }

    public int PageCount { get; init; }

    public DateTime FetchedAt { get; init; } = DateTime.UtcNow;

    public static YoutubeCommentFetchResult Ok(
        string videoId,
        IReadOnlyList<CommentDto> comments,
        int pageCount)
    {
        return new YoutubeCommentFetchResult
        {
            VideoId = videoId,
            Success = true,
            Comments = comments,
            PageCount = pageCount,
            FetchedAt = DateTime.UtcNow
        };
    }

    public static YoutubeCommentFetchResult Fail(
        string videoId,
        HttpStatusCode? statusCode,
        string errorMessage)
    {
        return new YoutubeCommentFetchResult
        {
            VideoId = videoId,
            Success = false,
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            Comments = Array.Empty<CommentDto>(),
            PageCount = 0,
            FetchedAt = DateTime.UtcNow
        };
    }
}