using Akka.Actor;
using Akka.Event;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Actors;

public sealed class VideoRegistryActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly IActorRef _commentsActor;

    private readonly IActorRef _sentimentActor;

    private readonly Dictionary<string, VideoStateDto> _videos = new();
    public VideoRegistryActor(IActorRef commentsActor, IActorRef sentimentActor)
    {
        _commentsActor = commentsActor;
        _sentimentActor = sentimentActor;

        Receive<RegisterVideo>(HandleRegisterVideo);
        Receive<GetTrackedVideos>(_ => HandleGetTrackedVideos());
        Receive<IsVideoTracked>(HandleIsVideoTracked);
    }

    private void HandleRegisterVideo(RegisterVideo message)
    {
        string videoId = message.VideoId.Trim();

        if (string.IsNullOrWhiteSpace(videoId))
        {
            Sender.Tell(new RegisterVideoResult(
                videoId,
                false,
                "Video ID nije validan."));

            return;
        }

        if (_videos.ContainsKey(videoId))
        {
            Sender.Tell(new RegisterVideoResult(
                videoId,
                false,
                "Video je vec registrovan."));

            return;
        }

        _videos[videoId] = new VideoStateDto
        {
            VideoId = videoId,
            IsTracked = true,
            RegisteredAt = DateTime.UtcNow
        };

        _commentsActor.Tell(new EnsureVideoCommentsState(videoId));
        _sentimentActor.Tell(new EnsureVideoSentimentState(videoId));

        _log.Info("Registrovan novi video: {0}", videoId);

        Sender.Tell(new RegisterVideoResult(
            videoId,
            true,
            "Video je uspesno registrovan."));
    }

    private void HandleGetTrackedVideos()
    {
        List<VideoStateDto> result = _videos
            .Values
            .OrderBy(video => video.RegisteredAt)
            .ToList();

        Sender.Tell(result);
    }

    private void HandleIsVideoTracked(IsVideoTracked message)
    {
        string videoId = message.VideoId.Trim();

        Sender.Tell(new VideoTrackedResult(
            videoId,
            _videos.ContainsKey(videoId)));
    }
}