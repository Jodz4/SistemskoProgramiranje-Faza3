using Akka.Actor;
using Akka.Event;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;
using YoutubeSentimentServer.Services;

namespace YoutubeSentimentServer.Actors;

public sealed class VideoActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _videoId;
    private readonly ISentimentAnalysisService _sentimentService;

    private readonly Dictionary<string, CommentDto> _comments = new();
    private SentimentResultDto _sentimentResult;

    public VideoActor(string videoId, ISentimentAnalysisService sentimentService)
    {
        _videoId = videoId;
        _sentimentService = sentimentService;
        _sentimentResult = CreateEmptyResult(videoId);

        Receive<AddComment>(HandleAddComment);
        Receive<AddComments>(HandleAddComments);
        Receive<GetCommentsForVideo>(_ => HandleGetComments());
        Receive<GetVideoSentiment>(_ => HandleGetSentiment());

        _log.Info("VideoActor kreiran za video: {0}", videoId);
    }

    private void HandleAddComment(AddComment message)
    {
        CommentDto comment = message.Comment;

        bool added = TryAddComment(comment);

        if (added)
        {
            RecalculateSentiment();

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
            RecalculateSentiment();

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

    private bool TryAddComment(CommentDto comment)
    {
        if (_comments.ContainsKey(comment.CommentId))
            return false;

        _comments[comment.CommentId] = comment;
        return true;
    }

    private void RecalculateSentiment()
    {
        int positive = 0;
        int neutral = 0;
        int negative = 0;

        foreach (CommentDto comment in _comments.Values.OrderBy(c => c.PublishedAt))
        {
            SentimentLabel label;

            try
            {
                label = _sentimentService.Analyze(comment.Text);
            }
            catch (Exception exception)
            {
                _log.Warning(
                    exception,
                    "Sentiment analiza nije uspela za komentar {0} (video {1}), tretiram kao Neutral.",
                    comment.CommentId,
                    _videoId);

                label = SentimentLabel.Neutral;
            }

            switch (label)
            {
                case SentimentLabel.Positive:
                    positive++;
                    break;
                case SentimentLabel.Negative:
                    negative++;
                    break;
                default:
                    neutral++;
                    break;
            }
        }

        int total = positive + neutral + negative;

        _sentimentResult = new SentimentResultDto
        {
            VideoId = _videoId,
            TotalComments = total,
            PositiveCount = positive,
            NeutralCount = neutral,
            NegativeCount = negative,
            PositivePercentage = CalculatePercentage(positive, total),
            NeutralPercentage = CalculatePercentage(neutral, total),
            NegativePercentage = CalculatePercentage(negative, total),
            CalculatedAt = DateTime.UtcNow
        };

        _log.Info(
            "Sentiment izracunat za video {0}: +{1}, ~{2}, -{3}",
            _videoId,
            positive,
            neutral,
            negative);
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

    private static float CalculatePercentage(int value, int total)
    {
        if (total == 0)
            return 0;

        return value * 100f / total;
    }
}