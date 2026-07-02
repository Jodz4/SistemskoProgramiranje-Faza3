using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.Options;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using YoutubeSentimentServer.Actors;
using YoutubeSentimentServer.Configuration;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;
using System.Diagnostics;

namespace YoutubeSentimentServer.Services;

public sealed class YoutubeCommentStreamService : IHostedService, IDisposable
{
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

        _subscription = Observable
            .Interval(pollingInterval, _scheduler)
            .StartWith(0L)
            .Select(_ => Observable.FromAsync(PollOnceAsync))
            .Concat()
            .Subscribe(
                _ =>
                {
                    //PollOnceAsync sve obavlja kroz side-effect:
                    //API poziv -> filter/mapiranje -> poruka ka aktoru
                },
                error =>
                {
                    _logger.LogError(
                        error,
                        "Rx.NET tok za YouTube komentare je neocekivano zaustavljen.");
                });

        _logger.LogInformation(
            "Rx.NET YouTube comment stream pokrenut. Interval: {IntervalSeconds}s, Scheduler: {Scheduler}",
            pollingInterval.TotalSeconds,
            nameof(EventLoopScheduler));

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

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            YoutubeOptions options = _options.CurrentValue;

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                if (!_apiKeyWarningLogged)
                {
                    _logger.LogWarning(
                        "YouTube API key nije podešen. Rx.NET polling radi, ali preskače YouTube API pozive.");

                    _apiKeyWarningLogged = true;
                }

                return;
            }

            IReadOnlyList<VideoStateDto> videos =
                await _registryActor.ActorRef.Ask<IReadOnlyList<VideoStateDto>>(
                    new GetTrackedVideos(),
                    ActorTimeout);

            List<VideoStateDto> trackedVideos = videos
                .Where(video => video.IsTracked)
                .ToList();

            if (trackedVideos.Count == 0)
            {
                _logger.LogDebug(
                    "Rx.NET polling tick: nema registrovanih video snimaka.");

                return;
            }

            int maxParallelRequests = Math.Clamp(
                options.MaxConcurrentVideoRequests,
                1,
                10);

            using SemaphoreSlim semaphore = new(maxParallelRequests);

            IEnumerable<Task> fetchTasks = trackedVideos.Select(video =>
                FetchMapAndEmitAsync(
                    video.VideoId,
                    semaphore,
                    cancellationToken));

            await Task.WhenAll(fetchTasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Rx.NET polling je otkazan.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Greška tokom jednog Rx.NET polling ciklusa.");
        }
        finally
        {
            stopwatch.Stop();

            _logger.LogInformation(
                "Rx.NET polling ciklus završen za {ElapsedMilliseconds} ms.",
                stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task FetchMapAndEmitAsync(
        string videoId,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            YoutubeCommentFetchResult fetchResult =
                await _youtubeApiClient.GetCommentsForVideoAsync(
                    videoId,
                    cancellationToken);

            if (!fetchResult.Success)
            {
                _logger.LogWarning(
                    "Komentari nisu ucitani za video {VideoId}. Status: {StatusCode}. Greška: {Error}",
                    videoId,
                    fetchResult.StatusCode,
                    fetchResult.ErrorMessage);

                return;
            }

            IReadOnlyList<CommentDto> validComments = _commentFilteringService.FilterValidComments(fetchResult.Comments);

            if (validComments.Count == 0)
            {
                _logger.LogInformation(
                    "YouTube API nije vratio nove validne komentare za video {VideoId}.",
                    videoId);

                return;
            }

            AddComments message = new(videoId, validComments);

            _commentsActor.ActorRef.Tell(message);

            _logger.LogInformation(
                "Rx.NET emitovao AddComments poruku ka VideoCommentsActor-u. Video: {VideoId}, komentara: {Count}, stranica: {Pages}",
                videoId,
                validComments.Count,
                fetchResult.PageCount);
        }
        finally
        {
            semaphore.Release();
        }
    }
}