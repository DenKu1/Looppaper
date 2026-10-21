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
                    options.IntervalSeconds = ParseInt(TakeValue(), "--interval", "a whole number of seconds", options);
                    break;
                case "video":
                    options.Video = NonEmpty(TakeValue(), name, options);
                    break;
                case "no-video":
                    NoValue();
                    options.NoVideo = true;
                    break;
                case "volume":
                    options.Volume = ParseInt(TakeValue(), "--volume", "a number from 0 to 100", options);
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

                case "compress":
                    options.Compress = NonEmpty(TakeValue(), name, options);
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
                case "we-list":
                    NoValue();
                    options.WeList = true;
                    break;
                case "background":
                    NoValue();
                    options.Background = true;
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

        if (options.Video is not null && options.NoVideo)
        {
            options.Errors.Add("'--video' and '--no-video' cannot be combined.");
        }

        return options;
    }

    private static int? ParseInt(string? text, string option, string expected, CommandLineOptions options)
    {
        if (text is null)
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        options.Errors.Add($"'{option}' expects {expected}, got '{text}'.");
        return null;
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
