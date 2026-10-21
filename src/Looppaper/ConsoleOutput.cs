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

    public static void WriteLine(string text)
    {
        try
        {
            Console.Out.WriteLine(text);
            Console.Out.Flush();
        }
        catch (IOException)
        {
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
            Console.Out.Write(question);
            Console.Out.Flush();
            return Console.In.ReadLine();
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Lets go of the console so the wallpaper keeps running after the terminal closes.</summary>
    public static void Detach() => Kernel32.FreeConsole();
}
