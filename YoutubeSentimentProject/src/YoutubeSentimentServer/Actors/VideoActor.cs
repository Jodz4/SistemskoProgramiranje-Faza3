using Akka.Actor;
using Akka.Event;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Actors;

public sealed class VideoActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _videoId;
    private readonly IActorRef _sentimentAnalysisActor;

    private readonly Dictionary<string, CommentDto> _comments = new();
    private SentimentResultDto _sentimentResult;

    public VideoActor(string videoId, IActorRef sentimentAnalysisActor)
    {
        _videoId = videoId;
        _sentimentAnalysisActor = sentimentAnalysisActor;
        _sentimentResult = CreateEmptyResult(videoId);

        Receive<AddComment>(HandleAddComment);
        Receive<AddComments>(HandleAddComments);
        Receive<GetCommentsForVideo>(_ => HandleGetComments());
        Receive<GetVideoSentiment>(_ => HandleGetSentiment());
        Receive<SentimentComputed>(HandleSentimentComputed);

        _log.Info("VideoActor kreiran za video: {0}", videoId);
    }

    private void HandleAddComment(AddComment message)
    {
        CommentDto comment = message.Comment;

        bool added = TryAddComment(comment);

        if (added)
        {
            RequestSentimentRecalculation();

            _log.Info(
                "Dodat komentar za video {0}. Ukupno komentara: {1}",
                _videoId,
                _comments.Count);
        }

        Sender.Tell(new AddCommentResult(
            _videoId,
            added,
            _comments.Count,
            added ? "Komentar je dodat." : "Komentar vec postoji, nije dupliran."));
    }

    private void HandleAddComments(AddComments message)
    {
        int addedCount = 0;
        int duplicateCount = 0;

        foreach (CommentDto comment in message.Comments)
        {
            if (string.IsNullOrWhiteSpace(comment.CommentId))
                continue;

            bool added = TryAddComment(comment);

            if (added)
                addedCount++;
            else
                duplicateCount++;
        }

        if (addedCount > 0)
        {
            RequestSentimentRecalculation();

            _log.Info(
                "Rx.NET batch dodat za video {0}. Novo: {1}, duplikati: {2}, ukupno: {3}",
                _videoId,
                addedCount,
                duplicateCount,
                _comments.Count);
        }
        else
        {
            _log.Info(
                "Rx.NET batch za video {0} nije dodao nove komentare. Duplikati: {1}",
                _videoId,
                duplicateCount);
        }
    }

    private void HandleGetComments()
    {
        List<CommentDto> result = _comments
            .Values
            .OrderByDescending(comment => comment.PublishedAt)
            .ToList();

        Sender.Tell(result);
    }

    private void HandleGetSentiment()
    {
        Sender.Tell(_sentimentResult);
    }

    private void HandleSentimentComputed(SentimentComputed message)
    {
        if (message.VideoId != _videoId)
        {
            _log.Warning(
                "VideoActor {0} je primio sentiment rezultat za pogresan video {1}.",
                _videoId,
                message.VideoId);

            return;
        }

        _sentimentResult = message.Result;

        _log.Info(
            "VideoActor {0} je sacuvao novi sentiment rezultat. Komentara: {1}",
            _videoId,
            _sentimentResult.TotalComments);
    }

    private bool TryAddComment(CommentDto comment)
    {
        if (comment.VideoId != _videoId)
        {
            _log.Warning(
                "Komentar {0} pripada videu {1}, ali je stigao u VideoActor za video {2}.",
                comment.CommentId,
                comment.VideoId,
                _videoId);

            return false;
        }

        if (_comments.ContainsKey(comment.CommentId))
            return false;

        _comments[comment.CommentId] = comment;
        return true;
    }

    private void RequestSentimentRecalculation()
    {
        IReadOnlyList<CommentDto> snapshot = _comments
            .Values
            .OrderBy(comment => comment.PublishedAt)
            .ToList();

        _sentimentAnalysisActor.Tell(
            new AnalyzeSentimentBatch(_videoId, snapshot),
            Self);
    }

    private static SentimentResultDto CreateEmptyResult(string videoId)
    {
        return new SentimentResultDto
        {
            VideoId = videoId,
            TotalComments = 0,
            PositiveCount = 0,
            NeutralCount = 0,
            NegativeCount = 0,
            PositivePercentage = 0,
            NeutralPercentage = 0,
            NegativePercentage = 0,
            CalculatedAt = DateTime.UtcNow
        };
    }
}