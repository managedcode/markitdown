using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace MarkItDown;

internal static class MarkItDownDiagnostics
{
    public const string ActivitySourceName = "ManagedCode.MarkItDown";
    private const string Version = "1.0.0";

    public const string ActivityNameConvertStream = "markitdown.convert.stream";
    public const string ActivityNameConvertFile = "markitdown.convert.file";
    public const string ActivityNameConvertUrl = "markitdown.convert.url";
    public const string ActivityNameDownload = "markitdown.download";
    public const string ActivityNameMediaTranscription = "markitdown.media.transcription";

    internal static class ActivityTagNames
    {
        public const string GuessCount = "markitdown.guess.count";
        public const string Converter = TelemetryTags.Converter;
        public const string DetectedMime = "markitdown.detected.mime";
        public const string DetectedExtension = "markitdown.detected.extension";
        public const string Mime = TelemetryTags.Mime;
        public const string Extension = TelemetryTags.Extension;
        public const string FileName = TelemetryTags.FileName;
        public const string Url = TelemetryTags.Url;
        public const string Path = TelemetryTags.Path;
    }

    public static ActivitySource DefaultActivitySource { get; } = new(ActivitySourceName, Version);
    public static Meter DefaultMeter { get; } = new(ActivitySourceName, Version);

    public static Counter<long> ConversionsCounter { get; } = DefaultMeter.CreateCounter<long>("markitdown.conversions");
    public static Counter<long> ConversionFailuresCounter { get; } = DefaultMeter.CreateCounter<long>("markitdown.conversion.failures");

    private static readonly ConditionalWeakTable<Meter, MeterCounters> CachedCounters = new();

    public static (Counter<long> Success, Counter<long> Failure) ResolveCounters(Meter? meter)
    {
        if (meter is null)
        {
            return (ConversionsCounter, ConversionFailuresCounter);
        }

        var counters = CachedCounters.GetValue(meter, static m => new MeterCounters(m));
        return (counters.Success, counters.Failure);
    }

    private sealed class MeterCounters
    {
        public MeterCounters(Meter meter)
        {
            Success = meter.CreateCounter<long>("markitdown.conversions");
            Failure = meter.CreateCounter<long>("markitdown.conversion.failures");
        }

        public Counter<long> Success { get; }
        public Counter<long> Failure { get; }
    }
}
