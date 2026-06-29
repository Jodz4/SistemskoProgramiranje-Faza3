using Akka.Event;
using Akka.Hosting;

namespace YoutubeSentimentServer.Infrastructure;

public static class AkkaConfigurationExtensions
{
    public static IServiceCollection AddYoutubeSentimentAkka(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        services.AddAkka("YoutubeSentimentSystem", (akkaBuilder, serviceProvider) =>
        {
            string akkaConfigPath = Path.Combine(environment.ContentRootPath, "akka.conf");

            if (!File.Exists(akkaConfigPath))
            {
                throw new FileNotFoundException(
                    "Akka konfiguracioni fajl akka.conf nije pronadjen.",
                    akkaConfigPath);
            }

            string hocon = File.ReadAllText(akkaConfigPath);

            akkaBuilder
                .AddHocon(hocon, HoconAddMode.Prepend)
                .ConfigureLoggers(logConfig =>
                {
                    logConfig.ClearLoggers();
                    logConfig.AddLoggerFactory();
                    logConfig.LogLevel = Akka.Event.LogLevel.InfoLevel;
                    logConfig.LogConfigOnStart = false;
                });

            /*
             * Faza 3:
             * Ovde  dodati .WithActors(...)
             * i registrovati:
             * - VideoRegistryActor
             * - VideoCommentsActor
             * - SentimentActor
             */
        });

        return services;
    }
}