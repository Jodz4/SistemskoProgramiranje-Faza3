using Akka.Actor;
using Akka.Event;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;
using YoutubeSentimentServer.Services;

namespace YoutubeSentimentServer.Actors;

public sealed class CoordinatorActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ISentimentAnalysisService _sentimentService;

    private readonly Dictionary<string, IActorRef> _videoActors = new();
    private readonly Dictionary<string, VideoStateDto> _videoStates = new();

    public CoordinatorActor(ISentimentAnalysisService sentimentService)
    {
        _sentimentService = sentimentService;

        Receive<RegisterVideo>(HandleRegisterVideo);
        Receive<UnregisterVideo>(HandleUnregisterVideo);
        Receive<GetTrackedVideos>(_ => HandleGetTrackedVideos());
        Receive<AddComment>(message => ForwardOrReplyNotRegistered(message.Comment.VideoId, message));
        Receive<AddComments>(message => ForwardOrLogNotRegistered(message.VideoId, message));
        Receive<GetCommentsForVideo>(message => ForwardOrReplyEmptyComments(message.VideoId, message));
        Receive<GetVideoSentiment>(message => ForwardOrReplyEmptySentiment(message.VideoId, message));

    }

    private void HandleRegisterVideo(RegisterVideo message)
    {
        string videoId = message.VideoId.Trim();

        if (string.IsNullOrWhiteSpace(videoId))
        {
            Sender.Tell(new RegisterVideoResult(videoId, false, "Video ID nije validan."));
            return;
        }

        // Provera da li vec postoji child actor za ovaj video PRE kreiranja novog.
        if (_videoActors.ContainsKey(videoId))
        {
            Sender.Tell(new RegisterVideoResult(videoId, false, "Video je vec registrovan."));
            return;
        }

        IActorRef videoActor = Context.ActorOf(
            Props.Create(() => new VideoActor(videoId, _sentimentService))
                .WithDispatcher("video-dispatcher"),
            $"video-{videoId}");

        _videoActors[videoId] = videoActor;

        _videoStates[videoId] = new VideoStateDto
        {
            VideoId = videoId,
            IsTracked = true,
            RegisteredAt = DateTime.UtcNow
        };

        _log.Info("Registrovan novi video: {0}. Kreiran VideoActor.", videoId);

        Sender.Tell(new RegisterVideoResult(videoId, true, "Video je uspesno registrovan."));
    }
    private void HandleUnregisterVideo(UnregisterVideo message)
    {
        string videoId = message.VideoId.Trim();

        if (!_videoActors.TryGetValue(videoId, out IActorRef? videoActor))
        {
            Sender.Tell(new UnregisterVideoResult(
                videoId,
                false,
                "Video nije registrovan, nema sta da se ukloni."));

            return;
        }

        // PoisonPill (ne Context.Stop direktno) - ceka da VideoActor zavrsi obradu
        // svih vec zaprimljenih poruka u svom mailbox-u pre nego sto se ugasi.
        videoActor.Tell(PoisonPill.Instance);

        _videoActors.Remove(videoId);
        _videoStates.Remove(videoId);

        _log.Info("Video {0} je uklonjen iz pracenja. VideoActor zaustavljen.", videoId);

        Sender.Tell(new UnregisterVideoResult(
            videoId,
            true,
            "Video je uklonjen iz pracenja."));
    }

    private void HandleGetTrackedVideos()
    {
        List<VideoStateDto> result = _videoStates
            .Values
            .OrderBy(video => video.RegisteredAt)
            .ToList();

        Sender.Tell(result);
    }

    private void ForwardOrReplyNotRegistered(string videoId, AddComment message)
    {
        if (_videoActors.TryGetValue(videoId, out IActorRef? videoActor))
        {
            videoActor.Forward(message);
            return;
        }

        Sender.Tell(new AddCommentResult(
            videoId,
            false,
            0,
            "Video nije registrovan. Prvo pozovi POST /videos/{videoId}."));
    }

    private void ForwardOrLogNotRegistered(string videoId, AddComments message)
    {
        if (_videoActors.TryGetValue(videoId, out IActorRef? videoActor))
        {
            videoActor.Forward(message);
            return;
        }

        _log.Warning("Rx.NET je poslao komentare za neregistrovan video: {0}", videoId);
    }

    private void ForwardOrReplyEmptyComments(string videoId, GetCommentsForVideo message)
    {
        if (_videoActors.TryGetValue(videoId, out IActorRef? videoActor))
        {
            videoActor.Forward(message);
            return;
        }

        Sender.Tell(new List<CommentDto>());
    }

    private void ForwardOrReplyEmptySentiment(string videoId, GetVideoSentiment message)
    {
        if (_videoActors.TryGetValue(videoId, out IActorRef? videoActor))
        {
            videoActor.Forward(message);
            return;
        }

        Sender.Tell(new SentimentResultDto
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
        });
    }
}