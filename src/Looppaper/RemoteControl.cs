using Looppaper.Core.Control;
using Looppaper.Desktop;
using Looppaper.Native;

namespace Looppaper;

/// <summary>
/// Sends a <see cref="ControlCommand"/> to the running wallpaper: a registered window message to its hidden control
/// window (see <see cref="ControlWindow"/>), answered synchronously with a packed <see cref="ControlReply"/>.
/// </summary>
internal static class RemoteControl
{
    private const uint TimeoutMs = 5000;

    public enum SendResult
    {
        Sent,
        NotRunning,
        NoAnswer,

        /// <summary>A Looppaper from before these commands existed is running (its window ignored the message).</summary>
        OlderVersion,
    }

    public static SendResult Send(ControlCommand command, out ControlReply reply)
    {
        reply = default;
        var message = User32.RegisterWindowMessage(ControlReply.MessageName);
        var hwnd = User32.FindWindow(ControlWindow.ClassName, ControlWindow.Title);
        if (message == 0 || hwnd == IntPtr.Zero)
        {
            return SendResult.NotRunning;
        }

        if (User32.SendMessageTimeout(hwnd, message, (int)command, IntPtr.Zero, User32.SMTO_ABORTIFHUNG | User32.SMTO_ERRORONEXIT, TimeoutMs, out var result) == IntPtr.Zero)
        {
            return SendResult.NoAnswer;
        }

        reply = ControlReply.Decode(result);
        return reply.State.HasFlag(ControlState.Running) ? SendResult.Sent : SendResult.OlderVersion;
    }
}
