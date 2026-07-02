using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public sealed class CommentFilteringService : ICommentFilteringService
{
    private const int MinimumCommentLength = 2;

    public bool IsValid(CommentDto comment)
    {
        return !string.IsNullOrWhiteSpace(comment.CommentId)
            && !string.IsNullOrWhiteSpace(comment.VideoId)
            && !string.IsNullOrWhiteSpace(comment.Text)
            && comment.Text.Trim().Length >= MinimumCommentLength;
    }
}