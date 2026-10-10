namespace Psarc2Tab.Cli;

/// <summary>Provides the command-line process entry point.</summary>
internal static class Program
{
    /// <summary>Runs the CLI using the process arguments and standard streams.</summary>
    /// <param name="args">Arguments supplied by the operating system.</param>
    /// <returns>The process exit code returned by the CLI application.</returns>
    public static int Main(string[] args) =>
        new CliApplication().Run(args, Console.Out, Console.Error);
}
