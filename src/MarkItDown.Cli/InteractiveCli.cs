using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace MarkItDown.Cli;

internal sealed class InteractiveCli
{
    private readonly CliOptionState state = new();
    private readonly ConversionService conversionService = new();

    public async Task RunAsync(string[] args)
    {
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
        };

        RenderHeader();

        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>
            {
                Title = "Select an action",
                HighlightStyle = new Style(Color.Aqua)
            }
            .AddChoices(
                "Convert documents",
                "Configure cloud providers",
                "Configure segmentation",
                "Show current configuration",
                "Exit"));

            switch (choice)
            {
                case "Convert documents":
                    await HandleConversionAsync().ConfigureAwait(false);
                    break;
                case "Configure cloud providers":
                    ConfigureProviders();
                    break;
                case "Configure segmentation":
                    ConfigureSegmentation();
                    break;
                case "Show current configuration":
                    RenderConfiguration();
                    break;
                case "Exit":
                    AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                    return;
            }
        }
    }

    private async Task HandleConversionAsync()
    {
        var action = AnsiConsole.Prompt(new SelectionPrompt<string>
        {
            Title = "Choose a conversion mode",
            HighlightStyle = new Style(Color.Aqua)
        }
        .AddChoices("Single file", "Directory batch", "Web URL", "Preview file info", "Back"));

        switch (action)
        {
            case "Single file":
                await ConvertSingleFileAsync().ConfigureAwait(false);
                break;
            case "Directory batch":
                await ConvertDirectoryAsync().ConfigureAwait(false);
                break;
            case "Web URL":
                await ConvertUrlAsync().ConfigureAwait(false);
                break;
            case "Preview file info":
                PreviewFile();
                break;
            default:
                return;
        }
    }

    private async Task ConvertSingleFileAsync()
    {
        var path = PromptExistingFilePath("Enter the file path to convert");
        if (path is null)
        {
            return;
        }

        var outputDir = PromptOutputDirectory();
        if (outputDir is null)
        {
            return;
        }

        var options = state.BuildOptions();
        var summary = await RunWithProgressAsync(progress => conversionService.ConvertFilesAsync(new[] { path }, outputDir, options, progress), 1);
        RenderSummary(summary);
        PromptToOpenDirectory(outputDir, summary);
    }

    private async Task ConvertDirectoryAsync()
    {
        var directory = PromptExistingDirectory("Enter the directory to process");
        if (directory is null)
        {
            return;
        }

        var recursive = AnsiConsole.Confirm("Process sub-directories as well?", true);
        var pattern = AnsiConsole.Ask<string>("File search pattern", "*.*");
        var outputDir = PromptOutputDirectory();
        if (outputDir is null)
        {
            return;
        }

        var files = Directory.EnumerateFiles(directory, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();
        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No files matched the specified pattern.[/]");
            return;
        }

        RenderRadar(files);

        var options = state.BuildOptions();
        var summary = await RunWithProgressAsync(progress => conversionService.ConvertFilesAsync(files, outputDir, options, progress), files.Count);
        RenderSummary(summary);
        PromptToOpenDirectory(outputDir, summary);
        PromptToOpenDirectory(outputDir, summary);
    }

    private async Task ConvertUrlAsync()
    {
        var url = AnsiConsole.Ask<string>("Enter the URL to fetch");
        if (string.IsNullOrWhiteSpace(url))
        {
            AnsiConsole.MarkupLine("[yellow]URL cannot be empty.[/]");
            return;
        }

        var outputDir = PromptOutputDirectory();
        if (outputDir is null)
        {
            return;
        }

        var options = state.BuildOptions();
        try
        {
            ConversionSummary? summary = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching URL", async _ =>
                {
                    summary = await conversionService.ConvertUrlAsync(url, outputDir, options);
                });

            if (summary is not null)
            {
                RenderSummary(summary);
                PromptToOpenDirectory(outputDir, summary);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to convert URL:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task<ConversionSummary> RunWithProgressAsync(Func<IProgress<ConversionProgress>, Task<ConversionSummary>> operation, int totalItems)
    {
        ConversionSummary? summary = null;
        await AnsiConsole.Progress()
            .AutoClear(true)
            .HideCompleted(true)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn()
            })
            .StartAsync(async context =>
            {
                var task = context.AddTask("Converting", maxValue: totalItems);
                var reporter = new Progress<ConversionProgress>(info =>
                {
                    if (info.Total > 0)
                    {
                        task.MaxValue = info.Total;
                        task.Value = Math.Min(info.Processed, info.Total);
                        var percent = task.MaxValue > 0 ? task.Percentage : 0;
                        if (!string.IsNullOrWhiteSpace(info.Current))
                        {
                            var fileName = Path.GetFileName(info.Current);
                            var label = string.IsNullOrWhiteSpace(fileName) ? info.Current : fileName;
                            task.Description = $"[green]{Markup.Escape(label)}[/] ({percent:0.##}% complete)";
                        }
                        else if (percent > 0)
                        {
                            task.Description = $"[green]{percent:0.##}% complete[/]";
                        }
                    }
                });

                summary = await operation(reporter);
                task.Value = task.MaxValue;
            });

        return summary ?? new ConversionSummary(Array.Empty<ConversionResult>());
    }

    private void PromptToOpenDirectory(string outputDir, ConversionSummary summary)
    {
        if (summary.SuccessCount > 0 && AnsiConsole.Confirm("Open output directory?", false))
        {
            TryOpenPath(outputDir);
        }
    }

    private string? PromptOutputDirectory()
    {
        var path = AnsiConsole.Ask<string>("Output directory", Path.Combine(Environment.CurrentDirectory, "output"));
        if (string.IsNullOrWhiteSpace(path))
        {
            AnsiConsole.MarkupLine("[yellow]Output directory cannot be empty.[/]");
            return null;
        }

        try
        {
            Directory.CreateDirectory(path);
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Unable to create output directory:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    private static string? PromptExistingFilePath(string prompt)
    {
        while (true)
        {
            var input = AnsiConsole.Prompt(new TextPrompt<string>(prompt)
                .AllowEmpty()
                .PromptStyle("green"));
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            if (File.Exists(input))
            {
                return Path.GetFullPath(input);
            }

            AnsiConsole.MarkupLine($"[yellow]File not found:[/] {Markup.Escape(input)}");
        }
    }

    private static string? PromptExistingDirectory(string prompt)
    {
        while (true)
        {
            var input = AnsiConsole.Prompt(new TextPrompt<string>(prompt)
                .DefaultValue(Environment.CurrentDirectory)
                .AllowEmpty()
                .PromptStyle("green"));
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            if (Directory.Exists(input))
            {
                return Path.GetFullPath(input);
            }

            AnsiConsole.MarkupLine($"[yellow]Directory not found:[/] {Markup.Escape(input)}");
        }
    }

    private void ConfigureProviders()
    {
        var providerChoice = AnsiConsole.Prompt(new SelectionPrompt<string>
        {
            Title = "Select provider to configure",
            HighlightStyle = new Style(Color.Aqua)
        }
        .AddChoices("Azure", "Google", "AWS", "Clear all", "Back"));

        switch (providerChoice)
        {
            case "Azure":
                state.ConfigureAzure();
                AnsiConsole.MarkupLine("[green]Azure configuration updated.[/]");
                break;
            case "Google":
                state.ConfigureGoogle();
                AnsiConsole.MarkupLine("[green]Google configuration updated.[/]");
                break;
            case "AWS":
                state.ConfigureAws();
                AnsiConsole.MarkupLine("[green]AWS configuration updated.[/]");
                break;
            case "Clear all":
                state.ClearProviders();
                AnsiConsole.MarkupLine("[yellow]All provider settings cleared.[/]");
                break;
            default:
                return;
        }
    }

    private void ConfigureSegmentation()
    {
        var current = state.SegmentOptions;
        var includeMetadata = AnsiConsole.Confirm("Include segment annotations in Markdown?", current.IncludeSegmentMetadataInMarkdown);
        var durationMinutes = (int)Math.Round(current.Audio.SegmentDuration.TotalMinutes);
        durationMinutes = AnsiConsole.Ask<int>("Audio segment duration (minutes)", Math.Max(1, durationMinutes));
        if (durationMinutes <= 0)
        {
            durationMinutes = 1;
        }

        state.SegmentOptions = current with
        {
            IncludeSegmentMetadataInMarkdown = includeMetadata,
            Audio = current.Audio with { SegmentDuration = TimeSpan.FromMinutes(durationMinutes) }
        };

        AnsiConsole.MarkupLine("[green]Segmentation preferences updated.[/]");
    }

    private void RenderConfiguration()
    {
        var table = new Table().Border(TableBorder.Rounded).Title("Current Configuration");
        table.AddColumn("Setting");
        table.AddColumn("Value");

        table.AddRow("Include segment metadata", state.SegmentOptions.IncludeSegmentMetadataInMarkdown ? "Yes" : "No");
        table.AddRow("Audio segment duration", state.SegmentOptions.Audio.SegmentDuration.ToString());

        var azure = state.AzureSettingsDescription;
        if (!string.IsNullOrWhiteSpace(azure))
        {
            table.AddRow("Azure", azure);
        }

        var google = state.GoogleSettingsDescription;
        if (!string.IsNullOrWhiteSpace(google))
        {
            table.AddRow("Google", google);
        }

        var aws = state.AwsSettingsDescription;
        if (!string.IsNullOrWhiteSpace(aws))
        {
            table.AddRow("AWS", aws);
        }

        AnsiConsole.Write(table);
    }

    private void RenderSummary(ConversionSummary summary)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("Conversion Summary");
        table.AddColumn("Input");
        table.AddColumn("Output");
        table.AddColumn("Segments");
        table.AddColumn("Status");

        foreach (var result in summary.Results)
        {
            var status = result.Success ? "[green]Success[/]" : $"[red]{Markup.Escape(result.Error ?? "Failed")}[/]";
            table.AddRow(
                Markup.Escape(result.Input),
                result.Output is null ? "-" : Markup.Escape(result.Output),
                result.SegmentCount.ToString(),
                status);
        }

        AnsiConsole.Write(table);
        var total = summary.Results.Count;
        var successPercent = total == 0 ? 0 : (double)summary.SuccessCount / total * 100d;
        AnsiConsole.MarkupLine($"[green]Completed[/]: {summary.SuccessCount}/{total} succeeded ({successPercent:0.##}%), [red]{summary.FailureCount} failed[/].");
    }

    private void PreviewFile()
    {
        var path = PromptExistingFilePath("Select file to preview (metadata only)");
        if (path is null)
        {
            return;
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            AnsiConsole.MarkupLine($"[yellow]File not found:[/] {Markup.Escape(path)}");
            return;
        }

        var table = new Table().Border(TableBorder.HeavyHead).Title("File Preview");
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.AddRow("Name", file.Name);
        table.AddRow("Directory", file.DirectoryName ?? "-");
        table.AddRow("Size", FormatSize(file.Length));
        table.AddRow("Created", file.CreationTime.ToString("u"));
        table.AddRow("Modified", file.LastWriteTime.ToString("u"));

        AnsiConsole.Write(table);

        var snippet = ReadFilePreview(file.FullName, 20);
        if (!string.IsNullOrEmpty(snippet))
        {
            var panel = new Panel(new Markup(Markup.Escape(snippet)))
                .Header("Preview")
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(Color.SteelBlue));
            AnsiConsole.Write(panel);
        }
    }

    private static string FormatSize(long byteCount)
    {
        var sizes = new[] { "B", "KB", "MB", "GB", "TB" };
        double len = byteCount;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private static string ReadFilePreview(string path, int maxLines)
    {
        try
        {
            using var reader = new StreamReader(path);
            var builder = new StringBuilder();
            string? line;
            var count = 0;
            while (count < maxLines && (line = reader.ReadLine()) is not null)
            {
                builder.AppendLine(line);
                count++;
            }

            if (reader.ReadLine() is not null)
            {
                builder.AppendLine("...");
            }

            return builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void RenderHeader()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("MarkItDown").Centered().Color(Color.Aqua));
        var rule = new Rule("Interactive Console")
        {
            Style = new Style(Color.SteelBlue)
        }.Centered();
        AnsiConsole.Write(rule);
        AnsiConsole.MarkupLine("[grey]Use the menu to convert files, configure cloud providers, or preview inputs.[/]");
        AnsiConsole.WriteLine();
    }

    private static void TryOpenPath(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", path);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", path);
            }
        }
        catch
        {
            AnsiConsole.MarkupLine($"[yellow]Unable to open:[/] {Markup.Escape(path)}");
        }
    }

    private static void RenderRadar(IReadOnlyList<string> files)
    {
        var groups = files
            .GroupBy(Path.GetExtension)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(g => new { Extension = string.IsNullOrWhiteSpace(g.Key) ? "(none)" : g.Key, Count = g.Count() })
            .ToList();

        if (groups.Count == 0)
        {
            return;
        }

        var chart = new BreakdownChart()
            .Width(60)
            .AddItem(groups[0].Extension, groups[0].Count, Color.Aqua);

        for (var i = 1; i < groups.Count; i++)
        {
            var color = ColorGenerator(i);
            chart.AddItem(groups[i].Extension, groups[i].Count, color);
        }

        AnsiConsole.Write(chart);
        AnsiConsole.MarkupLine("Detected [green]{0}[/] files across [green]{1}[/] extensions.", files.Count, groups.Count);
    }

    private static Color ColorGenerator(int index)
    {
        var palette = new[]
        {
            Color.Teal, Color.LightGoldenrod1, Color.MediumPurple3, Color.SteelBlue1,
            Color.PaleVioletRed1, Color.SpringGreen3, Color.CornflowerBlue, Color.LightSlateGrey
        };
        return palette[index % palette.Length];
    }
}
