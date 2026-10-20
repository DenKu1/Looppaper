using Looppaper.Core;
using Looppaper.Core.CommandLine;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Desktop;

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
        if (options.Stop)
        {
            return Stop(log);
        }

        if (options.InstallStartup || options.UninstallStartup)
        {
            return ConfigureStartup(options.InstallStartup, log);
        }

        var configPath = options.ConfigPath is null ? AppPaths.DefaultConfigFile : AppPaths.ExpandPath(options.ConfigPath);
        var config = ConfigLoader.LoadOrCreate(configPath, log);
        options.ApplyTo(config);
        ConfigValidator.Normalize(config, log);

        log.Info($"Looppaper {typeof(Program).Assembly.GetName().Version} starting on {Environment.OSVersion.VersionString}; config {configPath}; overrides {options}");
        log.Info($"Collection {config.ResolveCollectionFolder()}, mode {config.Mode}, interval {config.IntervalSeconds}s, scaling {config.Scaling}");

        using var instance = SingleInstance.TryClaim();
        if (instance is null)
        {
            log.Info("Another Looppaper is already running; exiting");
            ConsoleOutput.WriteLine("Looppaper is already running. Use --stop to stop it.");
            return 3;
        }

        ResourceSaver.Apply(log);
        var app = new WallpaperApp(config, log);
        instance.ListenForStop(app.RequestStop);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Error("Unhandled exception; restoring wallpaper", e.ExceptionObject as Exception);
            WallpaperRestorer.Restore(log);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WallpaperRestorer.Restore(log);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            app.RequestStop();
        };

        return app.Run();
    }

    private static int ConfigureStartup(bool install, ILog log)
    {
        try
        {
            string message;
            if (install)
            {
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.");
                StartupRegistration.Install(exe);
                message = $"Looppaper will start when you sign in ({StartupRegistration.Command(exe)}).";
            }
            else
            {
                message = StartupRegistration.Uninstall()
                    ? "Looppaper will no longer start when you sign in."
                    : "Looppaper was not set to start when you sign in.";
            }

            log.Info(message);
            ConsoleOutput.WriteLine(message);
            return 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            log.Error("Changing the startup entry failed", ex);
            ConsoleOutput.WriteLine($"Changing the startup entry failed: {ex.Message}");
            return 1;
        }
    }

    private static int Stop(ILog log)
    {
        var result = SingleInstance.SignalStop(TimeSpan.FromSeconds(10));
        var message = result switch
        {
            SingleInstance.StopResult.Stopped => "Looppaper stopped.",
            SingleInstance.StopResult.NotRunning => "Looppaper is not running.",
            _ => "Looppaper did not stop within 10 seconds.",
        };
        log.Info($"--stop: {message}");
        ConsoleOutput.WriteLine(message);
        return result == SingleInstance.StopResult.TimedOut ? 1 : 0;
    }
}
