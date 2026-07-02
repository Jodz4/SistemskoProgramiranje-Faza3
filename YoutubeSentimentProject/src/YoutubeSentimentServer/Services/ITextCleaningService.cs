namespace YoutubeSentimentServer.Services;

public interface ITextCleaningService
{
    string Clean(string? text);
}