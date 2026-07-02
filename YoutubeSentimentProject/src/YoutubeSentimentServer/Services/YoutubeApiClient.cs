using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using YoutubeSentimentServer.Configuration;
using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public sealed class YoutubeApiClient : IYoutubeApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<YoutubeOptions> _options;
    private readonly ITextCleaningService _textCleaningService;
    private readonly ILogger<YoutubeApiClient> _logger;

    public YoutubeApiClient(
        HttpClient httpClient,
        IOptionsMonitor<YoutubeOptions> options,
        ITextCleaningService textCleaningService,
        ILogger<YoutubeApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _textCleaningService = textCleaningService;
        _logger = logger;
    }

    public async Task<YoutubeCommentFetchResult> GetCommentsForVideoAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        YoutubeOptions options = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return YoutubeCommentFetchResult.Fail(
                videoId,
                null,
                "YouTube API key nije podesen.");
        }

        int maxResults = Math.Clamp(options.MaxResultsPerRequest, 1, 100);
        int maxPages = Math.Max(1, options.MaxPagesPerVideo);

        List<CommentDto> comments = new();
        string? nextPageToken = null;
        int pageCount = 0;

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                string requestUri = BuildRequestUri(
                    videoId,
                    options.ApiKey,
                    maxResults,
                    nextPageToken);

                using HttpResponseMessage response =
                    await _httpClient.GetAsync(requestUri, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody =
                        await response.Content.ReadAsStringAsync(cancellationToken);

                    _logger.LogWarning(
                        "YouTube API greska za video {VideoId}. Status: {StatusCode}. Body: {Body}",
                        videoId,
                        response.StatusCode,
                        errorBody);

                    return YoutubeCommentFetchResult.Fail(
                        videoId,
                        response.StatusCode,
                        errorBody);
                }

                await using Stream stream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                using JsonDocument document =
                    await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                pageCount++;

                IReadOnlyList<CommentDto> pageComments =
                    ParseComments(videoId, document.RootElement);

                comments.AddRange(pageComments);

                nextPageToken = TryGetString(document.RootElement, "nextPageToken");

                _logger.LogInformation(
                    "Ucitana stranica {Page} komentara za video {VideoId}. Komentara na strani: {Count}",
                    pageCount,
                    videoId,
                    pageComments.Count);
            }
            while (!string.IsNullOrWhiteSpace(nextPageToken) && pageCount < maxPages);

            return YoutubeCommentFetchResult.Ok(
                videoId,
                comments,
                pageCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Neocekivana greska pri citanju komentara za video {VideoId}",
                videoId);

            return YoutubeCommentFetchResult.Fail(
                videoId,
                HttpStatusCode.InternalServerError,
                exception.Message);
        }
    }

    private static string BuildRequestUri(
        string videoId,
        string apiKey,
        int maxResults,
        string? pageToken)
    {
        List<string> queryParameters = new()
        {
            "part=snippet",
            $"videoId={Uri.EscapeDataString(videoId)}",
            $"maxResults={maxResults}",
            "textFormat=plainText",
            "order=time",
            $"key={Uri.EscapeDataString(apiKey)}"
        };

        if (!string.IsNullOrWhiteSpace(pageToken))
            queryParameters.Add($"pageToken={Uri.EscapeDataString(pageToken)}");

        return "https://www.googleapis.com/youtube/v3/commentThreads?"
            + string.Join("&", queryParameters);
    }

    private IReadOnlyList<CommentDto> ParseComments(
        string videoId,
        JsonElement root)
    {
        List<CommentDto> comments = new();

        if (!root.TryGetProperty("items", out JsonElement items))
            return comments;

        foreach (JsonElement item in items.EnumerateArray())
        {
            try
            {
                JsonElement topLevelComment = item
                    .GetProperty("snippet")
                    .GetProperty("topLevelComment");

                string commentId =
                    TryGetString(topLevelComment, "id")
                    ?? TryGetString(item, "id")
                    ?? Guid.NewGuid().ToString("N");

                JsonElement snippet = topLevelComment.GetProperty("snippet");

                string text = _textCleaningService.Clean(
                    TryGetString(snippet, "textDisplay"));

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                string author =
                    TryGetString(snippet, "authorDisplayName") ?? "Unknown";

                DateTime publishedAt =
                    TryGetDateTime(snippet, "publishedAt") ?? DateTime.UtcNow;

                int likeCount =
                    TryGetInt(snippet, "likeCount") ?? 0;

                comments.Add(new CommentDto
                {
                    VideoId = videoId,
                    CommentId = commentId,
                    Author = author,
                    Text = text,
                    PublishedAt = publishedAt,
                    LikeCount = likeCount
                });
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "1 YouTube komentar nije mogao da se parsira za video {VideoId}",
                    videoId);
            }
        }

        return comments;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return null;

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out int value))
        {
            return value;
        }

        return null;
    }

    private static DateTime? TryGetDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return null;

        if (property.ValueKind == JsonValueKind.String &&
            property.TryGetDateTime(out DateTime value))
        {
            return value;
        }

        return null;
    }
}