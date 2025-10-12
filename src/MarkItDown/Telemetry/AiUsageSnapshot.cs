using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarkItDown;

internal readonly struct AiUsageSnapshot
{
    public static AiUsageSnapshot Empty { get; } = new AiUsageSnapshot(0, 0, 0, 0);

    public AiUsageSnapshot(
        int inputTokens,
        int outputTokens,
        int totalTokens,
        int callCount,
        int inputAudioTokens = 0,
        int inputCachedTokens = 0,
        int outputReasoningTokens = 0,
        int outputAudioTokens = 0,
        int outputAcceptedPredictionTokens = 0,
        int outputRejectedPredictionTokens = 0,
        double costUsd = 0d)
    {
        InputTokens = Math.Max(0, inputTokens);
        OutputTokens = Math.Max(0, outputTokens);
        TotalTokens = Math.Max(0, totalTokens);
        CallCount = Math.Max(0, callCount);
        InputAudioTokens = Math.Max(0, inputAudioTokens);
        InputCachedTokens = Math.Max(0, inputCachedTokens);
        OutputReasoningTokens = Math.Max(0, outputReasoningTokens);
        OutputAudioTokens = Math.Max(0, outputAudioTokens);
        OutputAcceptedPredictionTokens = Math.Max(0, outputAcceptedPredictionTokens);
        OutputRejectedPredictionTokens = Math.Max(0, outputRejectedPredictionTokens);
        CostUsd = Math.Max(0, costUsd);
    }

    public int InputTokens { get; }

    public int OutputTokens { get; }

    public int TotalTokens { get; }

    public int CallCount { get; }

    public int InputAudioTokens { get; }

    public int InputCachedTokens { get; }

    public int OutputReasoningTokens { get; }

    public int OutputAudioTokens { get; }

    public int OutputAcceptedPredictionTokens { get; }

    public int OutputRejectedPredictionTokens { get; }

    public double CostUsd { get; }

    public bool IsEmpty =>
        InputTokens == 0 &&
        OutputTokens == 0 &&
        TotalTokens == 0 &&
        CallCount == 0 &&
        InputAudioTokens == 0 &&
        InputCachedTokens == 0 &&
        OutputReasoningTokens == 0 &&
        OutputAudioTokens == 0 &&
        OutputAcceptedPredictionTokens == 0 &&
        OutputRejectedPredictionTokens == 0 &&
        CostUsd <= 0d;

    public static AiUsageSnapshot operator +(AiUsageSnapshot left, AiUsageSnapshot right)
        => new AiUsageSnapshot(
            left.InputTokens + right.InputTokens,
            left.OutputTokens + right.OutputTokens,
            left.TotalTokens + right.TotalTokens,
            left.CallCount + right.CallCount,
            left.InputAudioTokens + right.InputAudioTokens,
            left.InputCachedTokens + right.InputCachedTokens,
            left.OutputReasoningTokens + right.OutputReasoningTokens,
            left.OutputAudioTokens + right.OutputAudioTokens,
            left.OutputAcceptedPredictionTokens + right.OutputAcceptedPredictionTokens,
            left.OutputRejectedPredictionTokens + right.OutputRejectedPredictionTokens,
            left.CostUsd + right.CostUsd);

    public override string ToString()
        => $"Input={InputTokens}, Output={OutputTokens}, Total={TotalTokens}, Calls={CallCount}, Cost={CostUsd.ToString("F4", CultureInfo.InvariantCulture)}";

    public static AiUsageSnapshot FromMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return Empty;
        }

        static int Parse(IReadOnlyDictionary<string, string> source, string key)
        {
            if (source.TryGetValue(key, out var value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return 0;
        }

        var input = Parse(metadata, MetadataKeys.AiInputTokens);
        var output = Parse(metadata, MetadataKeys.AiOutputTokens);
        var total = Parse(metadata, MetadataKeys.AiTotalTokens);
        if (total == 0)
        {
            total = input + output;
        }

        var calls = Parse(metadata, MetadataKeys.AiCallCount);
        var inputAudio = Parse(metadata, MetadataKeys.AiInputAudioTokens);
        var inputCached = Parse(metadata, MetadataKeys.AiInputCachedTokens);
        var outputAudio = Parse(metadata, MetadataKeys.AiOutputAudioTokens);
        var outputReasoning = Parse(metadata, MetadataKeys.AiOutputReasoningTokens);
        var outputAccepted = Parse(metadata, MetadataKeys.AiOutputAcceptedPredictionTokens);
        var outputRejected = Parse(metadata, MetadataKeys.AiOutputRejectedPredictionTokens);

        double cost = 0d;
        if (metadata.TryGetValue(MetadataKeys.AiCostUsd, out var costValue) &&
            double.TryParse(costValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedCost))
        {
            cost = Math.Max(0d, parsedCost);
        }

        return new AiUsageSnapshot(
            input,
            output,
            total,
            calls,
            inputAudio,
            inputCached,
            outputReasoning,
            outputAudio,
            outputAccepted,
            outputRejected,
            cost);
    }
}
