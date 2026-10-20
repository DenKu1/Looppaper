using Looppaper.Native;

namespace Looppaper;

/// <summary>
/// Looppaper is a GUI-subsystem exe, so it has no console of its own. For --help and command
/// results it borrows the console of the shell that started it, if there is one.
/// </summary>
internal static class ConsoleOutput
{
    private static bool? _attached;

    public static void WriteLine(string text)
    {
        _attached ??= Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS);
        if (_attached != true)
        {
            return;
        }

        try
        {
            // The shell has already printed its prompt (it does not wait for GUI apps), so start on a fresh line.
            Console.Out.Write(Environment.NewLine + text + Environment.NewLine);
            Console.Out.Flush();
        }
        catch (IOException)
        {
        }
    }

    public static void Detach()
    {
        if (_attached == true)
        {
            Kernel32.FreeConsole();
            _attached = false;
        }
    }
}
