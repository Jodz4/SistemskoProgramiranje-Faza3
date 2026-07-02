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
        Receive<AddComments>(HandleAddComments);
        Receive<GetCommentsForVideo>(HandleGetComments);
    }

    private void HandleEnsureState(EnsureVideoCommentsState message)
    {
        if (!_commentsByVideo.ContainsKey(message.VideoId))
        {
            _commentsByVideo[message.VideoId] = new Dictionary<string, CommentDto>();

            _log.Info(
                "Kreirano stanje komentara za video: {0}",
                message.VideoId);
        }
    }

    private void HandleAddComment(AddComment message)
    {
        CommentDto comment = message.Comment;

        if (!_commentsByVideo.TryGetValue(
                comment.VideoId,
                out Dictionary<string, CommentDto>? videoComments))
        {
            Sender.Tell(new AddCommentResult(
                comment.VideoId,
                false,
                0,
                "Video nije registrovan. Prvo pozovi POST /videos/{videoId}."));

            return;
        }

        bool added = TryAddComment(videoComments, comment);

        if (added)
        {
            RecalculateSentiment(comment.VideoId, videoComments);

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

    private void HandleAddComments(AddComments message)
    {
        if (!_commentsByVideo.TryGetValue(
                message.VideoId,
                out Dictionary<string, CommentDto>? videoComments))
        {
            _log.Warning(
                "Rx.NET je poslao komentare za neregistrovan video: {0}",
                message.VideoId);

            return;
        }

        int addedCount = 0;
        int duplicateCount = 0;

        foreach (CommentDto comment in message.Comments)
        {
            if (string.IsNullOrWhiteSpace(comment.CommentId))
                continue;

            bool added = TryAddComment(videoComments, comment);

            if (added)
                addedCount++;
            else
                duplicateCount++;
        }

        if (addedCount > 0)
        {
            RecalculateSentiment(message.VideoId, videoComments);

            _log.Info(
                "Rx.NET batch dodat za video {0}. Novo: {1}, duplikati: {2}, ukupno: {3}",
                message.VideoId,
                addedCount,
                duplicateCount,
                videoComments.Count);
        }
        else
        {
            _log.Info(
                "Rx.NET batch za video {0} nije dodao nove komentare. Duplikati: {1}",
                message.VideoId,
                duplicateCount);
        }
    }

    private void HandleGetComments(GetCommentsForVideo message)
    {
        if (!_commentsByVideo.TryGetValue(
                message.VideoId,
                out Dictionary<string, CommentDto>? videoComments))
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

    private static bool TryAddComment(
        Dictionary<string, CommentDto> videoComments,
        CommentDto comment)
    {
        if (videoComments.ContainsKey(comment.CommentId))
            return false;

        videoComments[comment.CommentId] = comment;
        return true;
    }

    private void RecalculateSentiment(
        string videoId,
        Dictionary<string, CommentDto> videoComments)
    {
        IReadOnlyList<CommentDto> snapshot = videoComments
            .Values
            .OrderBy(comment => comment.PublishedAt)
            .ToList();

        _sentimentActor.Tell(new RecalculateVideoSentiment(
            videoId,
            snapshot));
    }
}