using System.Threading.Tasks;

namespace MarkItDown.Cli;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var interactiveCli = new InteractiveCli();
        await interactiveCli.RunAsync(args);
    }
}
