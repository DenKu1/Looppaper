using System.Globalization;
using Looppaper.Core.Configuration;

namespace Looppaper.Core.CommandLine;

public static class CommandLineParser
{
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var options = new CommandLineOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string name;
            string? inlineValue = null;

            if (arg is "/?" or "-h" or "-?")
            {
                options.Help = true;
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                options.Errors.Add($"Unexpected argument '{arg}'.");
                continue;
            }

            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                name = arg[2..eq].ToLowerInvariant();
                inlineValue = arg[(eq + 1)..];
            }
            else
            {
                name = arg[2..].ToLowerInvariant();
            }

            string? TakeValue()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return args[++i];
                }

                options.Errors.Add($"Option '--{name}' needs a value.");
                return null;
            }

            void NoValue()
            {
                if (inlineValue is not null)
                {
                    options.Errors.Add($"Option '--{name}' does not take a value.");
                }
            }

            switch (name)
            {
                case "config":
                    options.ConfigPath = NonEmpty(TakeValue(), name, options);
                    break;
                case "folder":
                    options.Folder = NonEmpty(TakeValue(), name, options);
                    break;
                case "picture":
                    options.Picture = NonEmpty(TakeValue(), name, options);
                    break;
                case "interval":
                    var intervalText = TakeValue();
                    if (intervalText is not null)
                    {
                        if (int.TryParse(intervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                        {
                            options.IntervalSeconds = seconds;
                        }
                        else
                        {
                            options.Errors.Add($"'--interval' expects a whole number of seconds, got '{intervalText}'.");
                        }
                    }

                    break;
                case "mode":
                    var modeText = TakeValue();
                    if (modeText is not null)
                    {
                        if (!modeText.Any(char.IsDigit) && Enum.TryParse<WallpaperMode>(modeText, ignoreCase: true, out var mode))
                        {
                            options.Mode = mode;
                        }
                        else
                        {
                            options.Errors.Add($"'--mode' expects slideshow or single, got '{modeText}'.");
                        }
                    }

                    break;
                case "shuffle":
                    NoValue();
                    options.Shuffle = true;
                    break;
                case "no-music":
                    NoValue();
                    options.NoMusic = true;
                    break;
                case "stop":
                    NoValue();
                    options.Stop = true;
                    break;
                case "install-startup":
                    NoValue();
                    options.InstallStartup = true;
                    break;
                case "uninstall-startup":
                    NoValue();
                    options.UninstallStartup = true;
                    break;
                case "help":
                    NoValue();
                    options.Help = true;
                    break;
                default:
                    options.Errors.Add($"Unknown option '{arg}'.");
                    break;
            }
        }

        if (options.InstallStartup && options.UninstallStartup)
        {
            options.Errors.Add("'--install-startup' and '--uninstall-startup' cannot be combined.");
        }

        return options;
    }

    private static string? NonEmpty(string? value, string name, CommandLineOptions options)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            options.Errors.Add($"Option '--{name}' needs a non-empty value.");
            return null;
        }

        return value;
    }
}
