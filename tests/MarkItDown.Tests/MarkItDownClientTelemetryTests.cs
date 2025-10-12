using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Converters;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests;

public sealed class MarkItDownClientTelemetryTests
{
    [Fact]
    public async Task ConvertAsync_EmitsActivityAndMetrics()
    {
        using var activitySource = new ActivitySource("Test.MarkItDown", "1.0");
        using var meter = new Meter("Test.MarkItDown", "1.0");
        var logger = new TestLogger();
        var options = new MarkItDownOptions
        {
            EnableBuiltins = false,
            ActivitySource = activitySource,
            Meter = meter
        };

        var client = new MarkItDownClient(options, logger);
        client.RegisterConverter(new FakeConverter());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var info = new StreamInfo("text/plain", ".txt", fileName: "sample.txt");

        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == activitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var measurements = new List<long>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == meter && (instrument.Name == "markitdown.conversions" || instrument.Name == "markitdown.conversion.failures"))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "markitdown.conversions")
            {
                measurements.Add(measurement);
            }
        });
        meterListener.Start();

        var result = await client.ConvertAsync(stream, info);
        result.Markdown.ShouldBe("ok");

        activities.ShouldNotBeEmpty();
        activities.Any(a => a.OperationName == MarkItDownDiagnostics.ActivityNameConvertStream).ShouldBeTrue();
        measurements.ShouldContain(1);
    }

    [Fact]
    public async Task ConvertAsync_EmitsStructuredLogs()
    {
        var logger = new TestLogger();
        var options = new MarkItDownOptions
        {
            EnableBuiltins = false,
            LoggerFactory = new TestLoggerFactory(logger)
        };

        var client = new MarkItDownClient(options);
        client.RegisterConverter(new FakeConverter());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var info = new StreamInfo("text/plain", ".txt", fileName: "structured.txt");

        await client.ConvertAsync(stream, info);

        logger.Entries.Any(entry => entry.Level == LogLevel.Information && entry.Message.Contains("Converted") && entry.Properties?.Any(p => p.Key == "Source" && (string?)p.Value == "structured.txt") == true)
            .ShouldBeTrue();
    }

    private sealed class FakeConverter : DocumentConverterBase
    {
        public FakeConverter()
            : base(priority: 0)
        {
        }

        public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default) => true;

        public override bool AcceptsInput(StreamInfo streamInfo) => true;

        public override Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DocumentConverterResult("ok", streamInfo.FileName));
        }
    }

    private sealed class TestLoggerFactory : ILoggerFactory
    {
        private readonly ILogger logger;

        public TestLoggerFactory(ILogger logger)
        {
            this.logger = logger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger : ILogger
    {
        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();
            public void Dispose()
            {
            }
        }

        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            IReadOnlyList<KeyValuePair<string, object?>>? properties = null;
            if (state is IReadOnlyList<KeyValuePair<string, object?>> structured)
            {
                properties = structured;
            }

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
        }

        public sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>>? Properties);
    }
}
