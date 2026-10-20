using Looppaper.Core;
using Looppaper.Core.CommandLine;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;

namespace Looppaper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = CommandLineParser.Parse(args);
        if (options.Errors.Count > 0)
        {
            ConsoleOutput.WriteLine(string.Join(Environment.NewLine, options.Errors) + Environment.NewLine + Environment.NewLine + CommandLineOptions.HelpText);
            return 2;
        }

        if (options.Help)
        {
            ConsoleOutput.WriteLine(CommandLineOptions.HelpText);
            return 0;
        }

        var log = new RollingFileLog(AppPaths.LogFile);
        var configPath = options.ConfigPath is null ? AppPaths.DefaultConfigFile : AppPaths.ExpandPath(options.ConfigPath);
        var config = ConfigLoader.LoadOrCreate(configPath, log);
        options.ApplyTo(config);
        ConfigValidator.Normalize(config, log);

        log.Info($"Looppaper {typeof(Program).Assembly.GetName().Version} starting; config {configPath}; overrides {options}");
        log.Info($"Collection {config.ResolveCollectionFolder()}, mode {config.Mode}, interval {config.IntervalSeconds}s, scaling {config.Scaling}");
        return 0;
    }
}
