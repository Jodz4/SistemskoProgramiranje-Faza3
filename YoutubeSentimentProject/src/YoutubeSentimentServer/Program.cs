WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

WebApplication app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    message = "Youtube Sentiment Server radi.",
    status = "OK"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    time = DateTime.UtcNow
}));

app.Run();