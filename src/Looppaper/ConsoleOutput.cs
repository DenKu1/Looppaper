using Looppaper.Native;

namespace Looppaper;

/// <summary>
/// Looppaper is a console-subsystem exe with the "detached" console allocation policy (see
/// app.manifest): run from a terminal it shares that terminal (so commands can print and prompt,
/// and the shell waits for them), started from Explorer or the Run key it gets no console at all.
/// </summary>
internal static class ConsoleOutput
{
    /// <summary>True when a console is attached (output is visible, or redirected to a file/pipe).</summary>
    public static bool HasConsole => Kernel32.GetConsoleWindow() != IntPtr.Zero || Console.IsOutputRedirected;

    /// <summary>True when a person can answer a prompt.</summary>
    public static bool IsInteractive => Kernel32.GetConsoleWindow() != IntPtr.Zero && !Console.IsInputRedirected;

    /// <summary>
    /// True when the console was created just for this process (older Windows without the detached
    /// policy, started from Explorer or the Run key) rather than shared with a terminal.
    /// </summary>
    public static bool OwnsConsole
    {
        get
        {
            var ids = new uint[4];
            return Kernel32.GetConsoleWindow() != IntPtr.Zero && Kernel32.GetConsoleProcessList(ids, (uint)ids.Length) == 1;
        }
    }

    private static readonly object Gate = new();
    private static int _progressLength;
    private static DateTime _lastRedirectedProgress;

    public static void WriteLine(string text)
    {
        lock (Gate)
        {
            try
            {
                if (_progressLength > 0)
                {
                    // Finish the progress line first.
                    _progressLength = 0;
                    text = Environment.NewLine + text;
                }

                if (!WriteUnicode(text + Environment.NewLine))
                {
                    Console.Out.WriteLine(text);
                    Console.Out.Flush();
                }
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Shows a progress line that the next one overwrites (in a terminal), or writes it as a line at most
    /// every few seconds (when output is redirected). The next <see cref="WriteLine"/> starts a new line.
    /// </summary>
    public static void WriteProgress(string text)
    {
        lock (Gate)
        {
            try
            {
                if (Console.IsOutputRedirected || Kernel32.GetConsoleWindow() == IntPtr.Zero)
                {
                    if (DateTime.UtcNow - _lastRedirectedProgress < TimeSpan.FromSeconds(5))
                    {
                        return;
                    }

                    _lastRedirectedProgress = DateTime.UtcNow;
                    Console.Out.WriteLine(text);
                    Console.Out.Flush();
                    return;
                }

                var line = "\r" + text.PadRight(_progressLength);
                _progressLength = Math.Max(text.Length, 1);
                if (!WriteUnicode(line))
                {
                    Console.Out.Write(line);
                    Console.Out.Flush();
                }
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Prompts and reads one line; returns null when nobody can answer.</summary>
    public static string? Prompt(string question)
    {
        if (!IsInteractive)
        {
            return null;
        }

        try
        {
            if (!WriteUnicode(question))
            {
                Console.Out.Write(question);
                Console.Out.Flush();
            }

            return Console.In.ReadLine();
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes straight to a real console with WriteConsoleW, so any text (e.g. Japanese or Chinese
    /// wallpaper titles) shows correctly whatever the console code page is. False when output is redirected.
    /// </summary>
    private static unsafe bool WriteUnicode(string text)
    {
        if (Console.IsOutputRedirected || Kernel32.GetConsoleWindow() == IntPtr.Zero)
        {
            return false;
        }

        var handle = Kernel32.GetStdHandle(Kernel32.STD_OUTPUT_HANDLE);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return false;
        }

        Console.Out.Flush();
        fixed (char* chars = text)
        {
            return Kernel32.WriteConsole(handle, chars, (uint)text.Length, out _, IntPtr.Zero);
        }
    }

    /// <summary>Lets go of the console so the wallpaper keeps running after the terminal closes.</summary>
    public static void Detach() => Kernel32.FreeConsole();
}
