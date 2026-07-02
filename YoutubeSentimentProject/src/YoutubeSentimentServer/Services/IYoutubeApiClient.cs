using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public interface IYoutubeApiClient
{
    Task<YoutubeCommentFetchResult> GetCommentsForVideoAsync(
        string videoId,
        CancellationToken cancellationToken);
}