using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for RTF documents with a minimal text extractor.
/// </summary>
public sealed class RtfConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".rtf",
    };

    private static readonly string RtfMime = MimeHelper.GetMimeType(".rtf")
        ?? MimeTypeUtilities.Compose("application", "rtf");

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        RtfMime,
    };

    public int Priority => 190;

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && Extensions.Contains(extension))
        {
            return true;
        }

        return MimeTypeUtilities.MatchesAny(normalizedMime, MimeTypes)
            || MimeTypeUtilities.MatchesAny(streamInfo.MimeType, MimeTypes);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return AcceptsInput(streamInfo);
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var markdown = ExtractText(content);
        return new DocumentConverterResult(markdown, streamInfo.FileName);
    }

    private static string ExtractText(string rtf)
    {
        var builder = new StringBuilder();
        var stack = new Stack<int>();
        var i = 0;
        while (i < rtf.Length)
        {
            var ch = rtf[i];
            switch (ch)
            {
                case '{':
                    stack.Push(0);
                    i++;
                    break;
                case '}':
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    i++;
                    break;
                case '\\':
                    i++;
                    if (i >= rtf.Length)
                    {
                        break;
                    }

                    var next = rtf[i];
                    if (next == '\\' || next == '{' || next == '}')
                    {
                        builder.Append(next);
                        i++;
                    }
                    else if (next == '\'')
                    {
                        i++;
                        if (i + 1 < rtf.Length)
                        {
                            var hex = rtf.Substring(i, 2);
                            if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                            {
                                builder.Append(Encoding.Default.GetString(new[] { value }));
                            }

                            i += 2;
                        }
                    }
                    else
                    {
                        var control = ReadControlWord(rtf, ref i);
                        if (string.Equals(control.Word, "par", StringComparison.OrdinalIgnoreCase) || string.Equals(control.Word, "line", StringComparison.OrdinalIgnoreCase))
                        {
                            builder.AppendLine();
                        }
                        else if (string.Equals(control.Word, "tab", StringComparison.OrdinalIgnoreCase))
                        {
                            builder.Append('\t');
                        }
                        else if (control.Word is not null && control.Word.StartsWith("u", StringComparison.OrdinalIgnoreCase) && control.Argument is not null)
                        {
                            builder.Append(char.ConvertFromUtf32(control.Argument.Value));
                            if (control.Skip > 0)
                            {
                                i += control.Skip;
                            }
                        }
                    }

                    break;
                default:
                    if (!char.IsControl(ch))
                    {
                        builder.Append(ch);
                    }

                    i++;
                    break;
            }
        }

        return builder.ToString().Replace("\r\n\r\n", "\n\n").Trim();
    }

    private static (string? Word, int? Argument, int Skip) ReadControlWord(string rtf, ref int index)
    {
        var start = index;
        while (index < rtf.Length && char.IsLetter(rtf[index]))
        {
            index++;
        }

        var word = index > start ? rtf[start..index] : null;

        var negative = false;
        if (index < rtf.Length && rtf[index] == '-')
        {
            negative = true;
            index++;
        }

        var argStart = index;
        while (index < rtf.Length && char.IsDigit(rtf[index]))
        {
            index++;
        }

        int? argument = null;
        if (index > argStart)
        {
            var span = rtf[argStart..index];
            if (int.TryParse(span, out var value))
            {
                argument = negative ? -value : value;
            }
        }

        if (index < rtf.Length && rtf[index] == ' ')
        {
            index++;
        }

        return (word, argument, 0);
    }
}
