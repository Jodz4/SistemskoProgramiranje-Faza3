using Akka.Actor;
using Akka.Hosting;
using YoutubeSentimentServer.Actors;
using YoutubeSentimentServer.Services;

namespace YoutubeSentimentServer.Infrastructure;

public static class AkkaConfigurationExtensions
{
    public static IServiceCollection AddYoutubeSentimentAkka(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        services.AddAkka("YoutubeSentimentSystem", (akkaBuilder, serviceProvider) =>
        {
            string configPath = Path.Combine(environment.ContentRootPath, "akka.conf");

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException(
                    $"akka.conf nije pronadjen na putanji: {configPath}",
                    configPath);
            }

            string hocon = File.ReadAllText(configPath);

            akkaBuilder
                .AddHocon(hocon, HoconAddMode.Prepend)
                .ConfigureLoggers(logConfig =>
                {
                    logConfig.ClearLoggers();
                    logConfig.AddLoggerFactory();
                    logConfig.LogLevel = Akka.Event.LogLevel.InfoLevel;
                    logConfig.LogConfigOnStart = false;
                })
                .WithActors((actorSystem, actorRegistry) =>
                {
                    ISentimentAnalysisService sentimentService =
                        serviceProvider.GetRequiredService<ISentimentAnalysisService>();

                    IActorRef sentimentActor = actorSystem.ActorOf(
                        Props.Create(() => new SentimentActor(sentimentService))
                            .WithDispatcher("sentiment-dispatcher"),
                        "sentiment-actor");

                    IActorRef commentsActor = actorSystem.ActorOf(
                        Props.Create(() => new VideoCommentsActor(sentimentActor))
                            .WithDispatcher("comments-dispatcher"),
                        "video-comments-actor");

                    IActorRef registryActor = actorSystem.ActorOf(
                        Props.Create(() => new VideoRegistryActor(commentsActor, sentimentActor))
                            .WithDispatcher("registry-dispatcher"),
                        "video-registry-actor");

                    actorRegistry.Register<SentimentActor>(sentimentActor);
                    actorRegistry.Register<VideoCommentsActor>(commentsActor);
                    actorRegistry.Register<VideoRegistryActor>(registryActor);
                });
        });

        return services;
    }
}