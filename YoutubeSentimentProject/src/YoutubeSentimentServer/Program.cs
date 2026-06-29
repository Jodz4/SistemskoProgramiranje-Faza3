using Akka.Actor;
using YoutubeSentimentServer.Configuration;
using YoutubeSentimentServer.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<YoutubeOptions>(
    builder.Configuration.GetSection(YoutubeOptions.SectionName));

builder.Services.AddYoutubeSentimentAkka(builder.Environment);

WebApplication app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    message = "Youtube Sentiment Server radi.",
    status = "OK",
    phase = "Phase 2 - Configuration and Akka Hosting ready"
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
    Microsoft.Extensions.Options.IOptions<YoutubeOptions> options) =>
{
    YoutubeOptions youtubeOptions = options.Value;

    return Results.Ok(new
    {
        pollingIntervalSeconds = youtubeOptions.PollingIntervalSeconds,
        maxResultsPerRequest = youtubeOptions.MaxResultsPerRequest,
        maxPagesPerVideo = youtubeOptions.MaxPagesPerVideo,
        apiKeyConfigured = !string.IsNullOrWhiteSpace(youtubeOptions.ApiKey)
    });
});

app.Run();