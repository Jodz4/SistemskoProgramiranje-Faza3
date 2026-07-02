using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Messages;

public sealed record EnsureVideoCommentsState(string VideoId);

public sealed record AddComment(CommentDto Comment);

public sealed record AddComments(
    string VideoId,
    IReadOnlyList<CommentDto> Comments);

public sealed record AddCommentResult(
    string VideoId,
    bool Added,
    int TotalComments,
    string Message);

public sealed record GetCommentsForVideo(string VideoId);