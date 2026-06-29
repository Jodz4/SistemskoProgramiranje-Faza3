namespace YoutubeSentimentServer.Configuration;

public sealed class YoutubeOptions
{
    public const string SectionName = "Youtube";

    public string ApiKey { get; set; } = string.Empty;

    public int PollingIntervalSeconds { get; set; } = 60;

    public int MaxResultsPerRequest { get; set; } = 100;

    public int MaxPagesPerVideo { get; set; } = 3;
}