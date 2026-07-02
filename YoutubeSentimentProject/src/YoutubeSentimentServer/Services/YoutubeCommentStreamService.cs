using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using YoutubeSentimentServer.Actors;
using YoutubeSentimentServer.Configuration;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public sealed class YoutubeCommentStreamService : IHostedService, IDisposable
{
    private const int MaxCommentTextLength = 2000;

    private static readonly TimeSpan ActorTimeout = TimeSpan.FromSeconds(5);

    private readonly IYoutubeApiClient _youtubeApiClient;
    private readonly IRequiredActor<VideoRegistryActor> _registryActor;
    private readonly IRequiredActor<VideoCommentsActor> _commentsActor;
    private readonly IOptionsMonitor<YoutubeOptions> _options;
    private readonly ILogger<YoutubeCommentStreamService> _logger;
    private readonly ICommentFilteringService _commentFilteringService;

    private readonly EventLoopScheduler _scheduler;
    private IDisposable? _subscription;
    private bool _apiKeyWarningLogged;

    public YoutubeCommentStreamService(
        IYoutubeApiClient youtubeApiClient,
        IRequiredActor<VideoRegistryActor> registryActor,
        IRequiredActor<VideoCommentsActor> commentsActor,
        IOptionsMonitor<YoutubeOptions> options,
        ICommentFilteringService commentFilteringService,
        ILogger<YoutubeCommentStreamService> logger)
    {
        _youtubeApiClient = youtubeApiClient;
        _registryActor = registryActor;
        _commentsActor = commentsActor;
        _options = options;
        _commentFilteringService = commentFilteringService;
        _logger = logger;

        _scheduler = new EventLoopScheduler(threadStart =>
        {
            Thread thread = new(threadStart)
            {
                IsBackground = true,
                Name = "rx-youtube-comment-stream"
            };

            return thread;
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        YoutubeOptions options = _options.CurrentValue;

        TimeSpan pollingInterval = TimeSpan.FromSeconds(
            Math.Max(10, options.PollingIntervalSeconds));

        int maxConcurrentVideoRequests = Math.Clamp(
            options.MaxConcurrentVideoRequests,
            1,
            10);

        _subscription = Observable
            .Interval(pollingInterval, _scheduler)
            .StartWith(0L)
            .Select(_ => RunPollingCycle(maxConcurrentVideoRequests))
            .Concat()
            .Subscribe(
                batch =>
                {
                    _commentsActor.ActorRef.Tell(new AddComments(batch.VideoId, batch.Comments));

                    _logger.LogInformation(
                        "Rx.NET emitovao AddComments poruku ka VideoCommentsActor-u. Video: {VideoId}, komentara: {Count}",
                        batch.VideoId,
                        batch.Comments.Count);
                },
                error =>
                {
                    _logger.LogError(
                        error,
                        "Rx.NET tok za YouTube komentare je neocekivano zaustavljen.");
                });

        _logger.LogInformation(
            "Rx.NET YouTube comment stream pokrenut. Interval: {IntervalSeconds}s, Scheduler: {Scheduler}, MaxConcurrentVideoRequests: {MaxConcurrent}",
            pollingInterval.TotalSeconds,
            nameof(EventLoopScheduler),
            maxConcurrentVideoRequests);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();

        _logger.LogInformation(
            "Rx.NET YouTube comment stream zaustavljen.");

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _scheduler.Dispose();
    }

    private IObservable<(string VideoId, IReadOnlyList<CommentDto> Comments)> RunPollingCycle(
        int maxConcurrentVideoRequests)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        return Observable
            .FromAsync(GetTrackedVideoIdsAsync)
            .SelectMany(videoIds => videoIds.ToObservable())
            .Select(videoId => FetchAndMapComments(videoId))
            .Merge(maxConcurrentVideoRequests)
            .Finally(() =>
            {
                stopwatch.Stop();

                _logger.LogInformation(
                    "Rx.NET polling ciklus zavrsen za {ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);
            });
    }

    private IObservable<(string VideoId, IReadOnlyList<CommentDto> Comments)> FetchAndMapComments(string videoId)
    {
        return Observable
            .FromAsync(ct => _youtubeApiClient.GetCommentsForVideoAsync(videoId, ct))
            .Do(result => LogFetchOutcome(videoId, result))
            .Where(result => result.Success)
            .SelectMany(result => result.Comments)
            .Where(comment => _commentFilteringService.IsValid(comment))
            .Select(NormalizeComment)
            .ToList()
            .Select(comments => (videoId, (IReadOnlyList<CommentDto>)comments))
            .Where(batch => batch.Item2.Count > 0)
            .Catch<(string, IReadOnlyList<CommentDto>), Exception>(exception =>
            {
                _logger.LogError(
                    exception,
                    "Greska u Rx pipeline-u za video {VideoId}.",
                    videoId);

                return Observable.Empty<(string, IReadOnlyList<CommentDto>)>();
            });
    }

    private async Task<IReadOnlyList<string>> GetTrackedVideoIdsAsync(CancellationToken cancellationToken)
    {
        try
        {
            YoutubeOptions options = _options.CurrentValue;

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                if (!_apiKeyWarningLogged)
                {
                    _logger.LogWarning(
                        "YouTube API key nije podesen. Rx.NET polling radi, ali preskace YouTube API pozive.");

                    _apiKeyWarningLogged = true;
                }

                return Array.Empty<string>();
            }

            IReadOnlyList<VideoStateDto> videos =
                await _registryActor.ActorRef.Ask<IReadOnlyList<VideoStateDto>>(
                    new GetTrackedVideos(),
                    ActorTimeout);

            List<string> trackedVideoIds = videos
                .Where(video => video.IsTracked)
                .Select(video => video.VideoId)
                .ToList();

            if (trackedVideoIds.Count == 0)
            {
                _logger.LogDebug(
                    "Rx.NET polling tick: nema registrovanih video snimaka.");
            }

            return trackedVideoIds;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Greska pri dobavljanju registrovanih video snimaka za Rx.NET polling ciklus.");

            return Array.Empty<string>();
        }
    }

    private void LogFetchOutcome(string videoId, YoutubeCommentFetchResult result)
    {
        if (result.Success)
        {
            _logger.LogInformation(
                "Rx.NET je preuzeo {Count} komentara za video {VideoId} (stranica: {Pages}).",
                result.Comments.Count,
                videoId,
                result.PageCount);

            return;
        }

        _logger.LogWarning(
            "Komentari nisu ucitani za video {VideoId}. Status: {StatusCode}. Greska: {Error}",
            videoId,
            result.StatusCode,
            result.ErrorMessage);
    }

    private static CommentDto NormalizeComment(CommentDto comment)
    {
        if (comment.Text.Length <= MaxCommentTextLength)
            return comment;

        return new CommentDto
        {
            VideoId = comment.VideoId,
            CommentId = comment.CommentId,
            Author = comment.Author,
            Text = comment.Text[..MaxCommentTextLength],
            PublishedAt = comment.PublishedAt,
            LikeCount = comment.LikeCount
        };
    }
}