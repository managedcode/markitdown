using MarkItDown.Core;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Text;

namespace MarkItDown.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var inputOption = new Option<string?>(
            aliases: ["--input", "-i"],
            description: "Input file path. If not specified, reads from stdin."
        );

        var outputOption = new Option<string?>(
            aliases: ["--output", "-o"],
            description: "Output file path. If not specified, writes to stdout."
        );

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose logging."
        );

        var rootCommand = new RootCommand("MarkItDown - Convert various file formats to Markdown")
        {
            inputOption,
            outputOption,
            verboseOption
        };

        // Handle positional argument for input file
        var inputArgument = new Argument<string?>(
            "inputFile",
            description: "Input file path"
        );
        inputArgument.SetDefaultValue(null);
        rootCommand.AddArgument(inputArgument);

        rootCommand.SetHandler(async (string? inputFile, string? input, string? output, bool verbose) =>
        {
            try
            {
                // Set up logging
                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    if (verbose)
                    {
                        builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
                    }
                    else
                    {
                        builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
                    }
                });
                var logger = loggerFactory.CreateLogger<Program>();

                // Create MarkItDown instance
                using var httpClient = new HttpClient();
                var markItDown = new Core.MarkItDown(logger, httpClient);

                DocumentConverterResult result;

                // Determine input source
                if (!string.IsNullOrEmpty(inputFile))
                {
                    // Use positional argument
                    result = await markItDown.ConvertAsync(inputFile);
                }
                else if (!string.IsNullOrEmpty(input))
                {
                    // Use --input option
                    result = await markItDown.ConvertAsync(input);
                }
                else if (Console.IsInputRedirected)
                {
                    // Read from stdin
                    using var stdin = Console.OpenStandardInput();
                    using var memoryStream = new MemoryStream();
                    await stdin.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;
                    
                    var streamInfo = new StreamInfo(extension: ".txt"); // Default to text
                    result = await markItDown.ConvertAsync(memoryStream, streamInfo);
                }
                else
                {
                    Console.Error.WriteLine("Error: No input specified. Provide a file path or pipe content to stdin.");
                    Environment.Exit(1);
                    return;
                }

                // Write output
                if (!string.IsNullOrEmpty(output))
                {
                    await File.WriteAllTextAsync(output, result.Markdown);
                    if (verbose)
                    {
                        Console.Error.WriteLine($"Output written to: {output}");
                    }
                }
                else
                {
                    Console.Write(result.Markdown);
                }
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: File not found - {ex.Message}");
                Environment.Exit(1);
            }
            catch (UnsupportedFormatException ex)
            {
                Console.Error.WriteLine($"Error: Unsupported format - {ex.Message}");
                Environment.Exit(2);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace);
                }
                Environment.Exit(3);
            }
        }, inputArgument, inputOption, outputOption, verboseOption);

        return await rootCommand.InvokeAsync(args);
    }
}
