using Akka.Actor;
using Akka.Event;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Actors;

public sealed class VideoCommentsActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly IActorRef _sentimentActor;

    private readonly Dictionary<string, Dictionary<string, CommentDto>> _commentsByVideo = new();

    public VideoCommentsActor(IActorRef sentimentActor)
    {
        _sentimentActor = sentimentActor;

        Receive<EnsureVideoCommentsState>(HandleEnsureState);
        Receive<AddComment>(HandleAddComment);
        Receive<GetCommentsForVideo>(HandleGetComments);
    }

    private void HandleEnsureState(EnsureVideoCommentsState message)
    {
        if (!_commentsByVideo.ContainsKey(message.VideoId))
        {
            _commentsByVideo[message.VideoId] = new Dictionary<string, CommentDto>();
            _log.Info("Kreirano stanje komentara za video: {0}", message.VideoId);
        }
    }

    private void HandleAddComment(AddComment message)
    {
        CommentDto comment = message.Comment;

        if (!_commentsByVideo.TryGetValue(comment.VideoId, out Dictionary<string, CommentDto>? videoComments))
        {
            Sender.Tell(new AddCommentResult(
                comment.VideoId,
                false,
                0,
                "Video nije registrovan. Prvo pozovi POST /videos/{videoId}."));

            return;
        }

        bool added = !videoComments.ContainsKey(comment.CommentId);

        if (added)
        {
            videoComments[comment.CommentId] = comment;

            IReadOnlyList<CommentDto> snapshot = videoComments
                .Values
                .OrderBy(commentItem => commentItem.PublishedAt)
                .ToList();

            _sentimentActor.Tell(new RecalculateVideoSentiment(
                comment.VideoId,
                snapshot));

            _log.Info(
                "Dodat komentar za video {0}. Ukupno komentara: {1}",
                comment.VideoId,
                videoComments.Count);
        }

        Sender.Tell(new AddCommentResult(
            comment.VideoId,
            added,
            videoComments.Count,
            added ? "Komentar je dodat." : "Komentar vec postoji, nije dupliran."));
    }

    private void HandleGetComments(GetCommentsForVideo message)
    {
        if (!_commentsByVideo.TryGetValue(message.VideoId, out Dictionary<string, CommentDto>? videoComments))
        {
            Sender.Tell(new List<CommentDto>());
            return;
        }

        List<CommentDto> result = videoComments
            .Values
            .OrderByDescending(comment => comment.PublishedAt)
            .ToList();

        Sender.Tell(result);
    }
}