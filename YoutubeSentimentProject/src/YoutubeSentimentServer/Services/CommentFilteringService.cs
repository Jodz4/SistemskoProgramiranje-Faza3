using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public sealed class CommentFilteringService : ICommentFilteringService
{
    private const int MinimumCommentLength = 2;

    public IReadOnlyList<CommentDto> FilterValidComments(IReadOnlyList<CommentDto> comments)
    {
        return comments
            .Where(comment => !string.IsNullOrWhiteSpace(comment.CommentId))
            .Where(comment => !string.IsNullOrWhiteSpace(comment.VideoId))
            .Where(comment => !string.IsNullOrWhiteSpace(comment.Text))
            .Where(comment => comment.Text.Trim().Length >= MinimumCommentLength)
            .DistinctBy(comment => comment.CommentId)
            .ToList();
    }
}