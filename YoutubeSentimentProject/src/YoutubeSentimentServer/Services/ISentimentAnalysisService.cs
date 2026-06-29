using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services;

public interface ISentimentAnalysisService
{
    SentimentLabel Analyze(string text);
}