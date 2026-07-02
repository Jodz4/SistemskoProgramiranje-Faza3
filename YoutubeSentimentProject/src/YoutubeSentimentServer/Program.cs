using Akka.Actor;
using Microsoft.Extensions.Options;
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

builder.Services.AddSingleton<ISentimentAnalysisService, MlNetSentimentAnalysisService>(); builder.Services.AddSingleton<ITextCleaningService, TextCleaningService>();

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

app.MapGet("/", () => Results.Ok(new
{
    message = "Youtube Sentiment Server radi.",
    status = "OK",
    phase = "Phase 4 - Rx.NET YouTube stream ready"
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
        apiKeyConfigured = !string.IsNullOrWhiteSpace(youtubeOptions.ApiKey)
    });
});

app.MapVideoEndpoints(app.Environment);

app.Run();