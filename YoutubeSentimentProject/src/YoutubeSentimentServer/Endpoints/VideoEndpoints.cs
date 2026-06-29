using Akka.Actor;
using Akka.Hosting;
using YoutubeSentimentServer.Actors;
using YoutubeSentimentServer.Messages;
using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Endpoints;

public static class VideoEndpoints
{
    private static readonly TimeSpan ActorTimeout = TimeSpan.FromSeconds(5);

    public static IEndpointRouteBuilder MapVideoEndpoints(
        this IEndpointRouteBuilder app,
        IWebHostEnvironment environment)
    {
        app.MapPost("/videos/{videoId}", async (
            string videoId,
            IRequiredActor<VideoRegistryActor> registryActor) =>
        {
            RegisterVideoResult result = await registryActor.ActorRef.Ask<RegisterVideoResult>(
                new RegisterVideo(videoId),
                ActorTimeout);

            return Results.Ok(ApiResponse<RegisterVideoResult>.Ok(
                result,
                result.Message));
        });

        app.MapGet("/videos", async (
            IRequiredActor<VideoRegistryActor> registryActor) =>
        {
            IReadOnlyList<VideoStateDto> videos = await registryActor.ActorRef.Ask<IReadOnlyList<VideoStateDto>>(
                new GetTrackedVideos(),
                ActorTimeout);

            return Results.Ok(ApiResponse<IReadOnlyList<VideoStateDto>>.Ok(
                videos,
                "Lista registrovanih video snimaka."));
        });

        if (environment.IsDevelopment())
        {
            // Privremen endpoint za manuelno testiranje toka aktora.
            // U finalnoj verziji komentare ce dodavati Rx.NET tok posle poziva YouTube API-ja.
            app.MapPost("/videos/{videoId}/comments/test", async (
                string videoId,
                ManualCommentRequest request,
                IRequiredActor<VideoCommentsActor> commentsActor) =>
            {
                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    return Results.BadRequest(ApiResponse<object>.Fail(
                        "Tekst komentara ne sme biti prazan."));
                }

                CommentDto comment = new()
                {
                    VideoId = videoId.Trim(),
                    CommentId = Guid.NewGuid().ToString("N"),
                    Author = string.IsNullOrWhiteSpace(request.Author)
                        ? "Manual tester"
                        : request.Author.Trim(),
                    Text = request.Text.Trim(),
                    PublishedAt = DateTime.UtcNow,
                    LikeCount = 0
                };

                AddCommentResult result = await commentsActor.ActorRef.Ask<AddCommentResult>(
                    new AddComment(comment),
                    ActorTimeout);

                return Results.Ok(ApiResponse<AddCommentResult>.Ok(
                    result,
                    result.Message));
            });
        }

        app.MapGet("/videos/{videoId}/comments", async (
            string videoId,
            IRequiredActor<VideoCommentsActor> commentsActor) =>
        {
            IReadOnlyList<CommentDto> comments = await commentsActor.ActorRef.Ask<IReadOnlyList<CommentDto>>(
                new GetCommentsForVideo(videoId),
                ActorTimeout);

            return Results.Ok(ApiResponse<IReadOnlyList<CommentDto>>.Ok(
                comments,
                "Komentari za trazeni video."));
        });

        app.MapGet("/videos/{videoId}/sentiment", async (
            string videoId,
            IRequiredActor<SentimentActor> sentimentActor) =>
        {
            SentimentResultDto result = await sentimentActor.ActorRef.Ask<SentimentResultDto>(
                new GetVideoSentiment(videoId),
                ActorTimeout);

            return Results.Ok(ApiResponse<SentimentResultDto>.Ok(
                result,
                "Sentiment rezultat za trazeni video."));
        });

        return app;
    }
}