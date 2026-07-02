using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public interface ICommentFilteringService
{
    IReadOnlyList<CommentDto> FilterValidComments(IReadOnlyList<CommentDto> comments);
}