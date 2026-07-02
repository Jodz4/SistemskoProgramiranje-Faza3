using Microsoft.ML;
using YoutubeSentimentServer.Models;

namespace YoutubeSentimentServer.Services.Sentiment;

public sealed class MlNetSentimentAnalysisService : ISentimentAnalysisService
{
    private readonly PredictionEngine<SentimentData, SentimentPrediction> _predictionEngine;
    private readonly ILogger<MlNetSentimentAnalysisService> _logger;

    public MlNetSentimentAnalysisService(
        ILogger<MlNetSentimentAnalysisService> logger)
    {
        _logger = logger;

        MLContext mlContext = new(seed: 1);

        List<SentimentData> trainingData = CreateTrainingData();

        IDataView dataView = mlContext.Data.LoadFromEnumerable(trainingData);

        var pipeline = mlContext.Transforms.Text.FeaturizeText(
                outputColumnName: "Features",
                inputColumnName: nameof(SentimentData.Text))
            .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                labelColumnName: nameof(SentimentData.Label),
                featureColumnName: "Features"));

        ITransformer model = pipeline.Fit(dataView);

        _predictionEngine =
            mlContext.Model.CreatePredictionEngine<SentimentData, SentimentPrediction>(model);

        _logger.LogInformation(
            "ML.NET sentiment model je inicijalizovan. Training samples: {Count}",
            trainingData.Count);
    }

    public SentimentLabel Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SentimentLabel.Neutral;

        SentimentPrediction prediction = _predictionEngine.Predict(new SentimentData
        {
            Text = text
        });

        if (prediction.Probability >= 0.60f)
            return prediction.PredictedLabel
                ? SentimentLabel.Positive
                : SentimentLabel.Negative;

        return SentimentLabel.Neutral;
    }

    private static List<SentimentData> CreateTrainingData()
    {
        return new List<SentimentData>
        {
            new() { Text = "Great video, amazing explanation", Label = true },
            new() { Text = "Excellent content", Label = true },
            new() { Text = "Very useful and helpful", Label = true },
            new() { Text = "I love this video", Label = true },
            new() { Text = "Good job", Label = true },
            new() { Text = "This helped me a lot", Label = true },
            new() { Text = "Amazing work", Label = true },
            new() { Text = "Very clear explanation", Label = true },

            new() { Text = "Odlican video", Label = true },
            new() { Text = "Super objasnjeno", Label = true },
            new() { Text = "Mnogo mi je pomoglo", Label = true },
            new() { Text = "Jako korisno", Label = true },
            new() { Text = "Dobro uradjeno", Label = true },
            new() { Text = "Svidja mi se", Label = true },

            new() { Text = "Bad video", Label = false },
            new() { Text = "Terrible explanation", Label = false },
            new() { Text = "This is useless", Label = false },
            new() { Text = "I hate this", Label = false },
            new() { Text = "Waste of time", Label = false },
            new() { Text = "Awful content", Label = false },
            new() { Text = "Very boring video", Label = false },

            new() { Text = "Lose objasnjeno", Label = false },
            new() { Text = "Uzasno dosadno", Label = false },
            new() { Text = "Nista ne valja", Label = false },
            new() { Text = "Glup video", Label = false },
            new() { Text = "Gubljenje vremena", Label = false },
            new() { Text = "Ne svidja mi se", Label = false }
        };
    }
}