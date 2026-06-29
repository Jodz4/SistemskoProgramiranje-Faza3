using Akka.Actor;
using Akka.Event;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;
using YoutubeSentimentServer.Services;

namespace YoutubeSentimentServer.Actors;

public sealed class SentimentActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly ISentimentAnalysisService _sentimentService;

    private readonly Dictionary<string, SentimentResultDto> _sentimentByVideo = new();

    public SentimentActor(ISentimentAnalysisService sentimentService)
    {
        _sentimentService = sentimentService;

        Receive<EnsureVideoSentimentState>(HandleEnsureState);
        Receive<RecalculateVideoSentiment>(HandleRecalculate);
        Receive<GetVideoSentiment>(HandleGetSentiment);
    }

    private void HandleEnsureState(EnsureVideoSentimentState message)
    {
        if (!_sentimentByVideo.ContainsKey(message.VideoId))
        {
            _sentimentByVideo[message.VideoId] = CreateEmptyResult(message.VideoId);
            _log.Info("Kreirano sentiment stanje za video: {0}", message.VideoId);
        }
    }

    private void HandleRecalculate(RecalculateVideoSentiment message)
    {
        int positive = 0;
        int neutral = 0;
        int negative = 0;

        foreach (CommentDto comment in message.Comments)
        {
            SentimentLabel label = _sentimentService.Analyze(comment.Text);

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

        _sentimentByVideo[message.VideoId] = result;

        _log.Info(
            "Sentiment izracunat za video {0}: +{1}, ~{2}, -{3}",
            message.VideoId,
            positive,
            neutral,
            negative);
    }

    private void HandleGetSentiment(GetVideoSentiment message)
    {
        if (_sentimentByVideo.TryGetValue(message.VideoId, out SentimentResultDto? result))
        {
            Sender.Tell(result);
            return;
        }

        Sender.Tell(CreateEmptyResult(message.VideoId));
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