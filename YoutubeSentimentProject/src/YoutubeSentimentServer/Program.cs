using Akka.Actor;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using YoutubeSentimentServer.Configuration;
using YoutubeSentimentServer.Endpoints;
using YoutubeSentimentServer.Infrastructure;
using YoutubeSentimentServer.Services;
using YoutubeSentimentServer.Services.Sentiment;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<YoutubeOptions>(
    builder.Configuration.GetSection(YoutubeOptions.SectionName));

builder.Services.AddSingleton<ISentimentAnalysisService, MlNetSentimentAnalysisService>();
builder.Services.AddSingleton<ITextCleaningService, TextCleaningService>();
builder.Services.AddSingleton<ICommentFilteringService, CommentFilteringService>();

builder.Services.AddHttpClient<IYoutubeApiClient, YoutubeApiClient>(
    (serviceProvider, httpClient) =>
    {
        YoutubeOptions options =
            serviceProvider.GetRequiredService<IOptions<YoutubeOptions>>().Value;

        int timeoutSeconds = Math.Max(5, options.RequestTimeoutSeconds);

        httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    });

builder.Services.AddYoutubeSentimentAkka(builder.Environment);

builder.Services.AddHostedService<YoutubeCommentStreamService>();

WebApplication app = builder.Build();

app.Use(async (context, next) =>
{
    Stopwatch stopwatch = Stopwatch.StartNew();

    app.Logger.LogInformation(
        "Primljen HTTP zahtev: {Method} {Path}",
        context.Request.Method,
        context.Request.Path);

    try
    {
        await next();

        stopwatch.Stop();

        app.Logger.LogInformation(
            "Zahtev uspesno obradjen: {Method} {Path} -> {StatusCode} ({ElapsedMilliseconds} ms)",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
    catch (Exception exception)
    {
        stopwatch.Stop();

        app.Logger.LogError(
            exception,
            "Greska pri obradi zahteva: {Method} {Path} ({ElapsedMilliseconds} ms)",
            context.Request.Method,
            context.Request.Path,
            stopwatch.ElapsedMilliseconds);

        throw;
    }
});

app.MapGet("/", () => Results.Ok(new
{
    message = "Youtube Sentiment Server radi.",
    status = "OK",
    phase = "Phase 7 - Retry and resilient YouTube stream"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    time = DateTime.UtcNow
}));

app.MapGet("/health/akka", (ActorSystem actorSystem) => Results.Ok(new
{
    status = "Akka running",
    actorSystem = actorSystem.Name,
    provider = "Akka.Hosting"
}));

app.MapGet("/config/youtube", (
    IOptions<YoutubeOptions> options) =>
{
    YoutubeOptions youtubeOptions = options.Value;

    return Results.Ok(new
    {
        pollingIntervalSeconds = youtubeOptions.PollingIntervalSeconds,
        maxResultsPerRequest = youtubeOptions.MaxResultsPerRequest,
        maxPagesPerVideo = youtubeOptions.MaxPagesPerVideo,
        requestTimeoutSeconds = youtubeOptions.RequestTimeoutSeconds,
        maxConcurrentVideoRequests = youtubeOptions.MaxConcurrentVideoRequests,
        apiKeyConfigured = !string.IsNullOrWhiteSpace(youtubeOptions.ApiKey),
        retryAttempts = youtubeOptions.RetryAttempts,
        retryBaseDelayMilliseconds = youtubeOptions.RetryBaseDelayMilliseconds,
    });
});

app.MapVideoEndpoints(app.Environment);

app.Run();