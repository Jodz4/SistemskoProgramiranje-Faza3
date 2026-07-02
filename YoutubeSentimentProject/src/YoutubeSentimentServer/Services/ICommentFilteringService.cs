using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public interface ICommentFilteringService
{
    bool IsValid(CommentDto comment);
}