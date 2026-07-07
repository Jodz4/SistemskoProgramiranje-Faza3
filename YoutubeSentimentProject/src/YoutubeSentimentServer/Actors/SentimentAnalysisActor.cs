using Akka.Actor;
using Akka.Event;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;
using YoutubeSentimentServer.Services;

namespace YoutubeSentimentServer.Actors;

public sealed class SentimentAnalysisActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ISentimentAnalysisService _sentimentService;

    public SentimentAnalysisActor(ISentimentAnalysisService sentimentService)
    {
        _sentimentService = sentimentService;

        Receive<AnalyzeSentimentBatch>(HandleAnalyzeSentimentBatch);

        _log.Info("SentimentAnalysisActor kreiran. ML.NET model se koristi kroz jedan actor.");
    }

    private void HandleAnalyzeSentimentBatch(AnalyzeSentimentBatch message)
    {
        int positive = 0;
        int neutral = 0;
        int negative = 0;

        foreach (CommentDto comment in message.Comments)
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
                    message.VideoId);

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

        SentimentResultDto result = new()
        {
            VideoId = message.VideoId,
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
            message.VideoId,
            positive,
            neutral,
            negative);

        Sender.Tell(new SentimentComputed(message.VideoId, result));
    }

    private static float CalculatePercentage(int value, int total)
    {
        if (total == 0)
            return 0;

        return value * 100f / total;
    }
}