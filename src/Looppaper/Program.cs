using System.Diagnostics;
using Looppaper.Core;
using Looppaper.Core.CommandLine;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Desktop;
using Looppaper.WallpaperEngine;

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

        if (options.IsWallpaperEngineCommand)
        {
            return new WallpaperEngineCommands(options, config, configPath, log).Run();
        }

        if (options.Compress is not null)
        {
            return OversizedVideoFlow.RunCompressCommand(options.Compress, config, log);
        }

        log.Info($"Looppaper {typeof(Program).Assembly.GetName().Version} starting on {Environment.OSVersion.VersionString}; config {configPath}; overrides {options}");
        log.Info($"Collection {config.ResolveCollectionFolder()}, mode {config.Mode}, interval {config.IntervalSeconds}s, scaling {config.Scaling}");

        // Started from a terminal: run the wallpaper as a background copy so the terminal is free again.
        if (!options.Background && ConsoleOutput.HasConsole && !ConsoleOutput.OwnsConsole)
        {
            return LaunchInBackground(args, log);
        }

        // No terminal to keep (older Windows created a console just for us, or we are the background copy).
        if (ConsoleOutput.HasConsole)
        {
            ConsoleOutput.Detach();
        }

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

    private static int LaunchInBackground(string[] args, ILog log)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            ConsoleOutput.WriteLine("Cannot determine the executable path.");
            return 1;
        }

        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.ArgumentList.Add("--background");
        using var child = Process.Start(start);
        if (child is null)
        {
            ConsoleOutput.WriteLine("Starting Looppaper in the background failed.");
            return 1;
        }

        // Give it a moment so an immediate failure (e.g. already running) is reported here.
        if (child.WaitForExit(1500))
        {
            var message = child.ExitCode == 3
                ? "Looppaper is already running. Use --stop to stop it."
                : $"Looppaper exited with code {child.ExitCode}; see {AppPaths.LogFile}";
            ConsoleOutput.WriteLine(message);
            return child.ExitCode;
        }

        log.Info($"Started background instance (pid {child.Id})");
        ConsoleOutput.WriteLine($"Looppaper is running in the background (pid {child.Id}). Stop it with: Looppaper.exe --stop");
        return 0;
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
