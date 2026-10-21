using Looppaper.Core.Logging;
using Looppaper.Core.Playback;
using Looppaper.Native;
using System.Runtime.InteropServices;

namespace Looppaper.Desktop;

/// <summary>
/// Turns session and power notifications sent to the control window into <see cref="PauseReason"/> changes:
/// lock / disconnect, sleep, the display turning off, battery saver / Energy Saver and running on battery.
/// Registering a power setting makes Windows send its current value straight away, so the start state is right.
/// </summary>
internal sealed unsafe class PowerWatcher : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly ILog _log;
    private readonly List<IntPtr> _registrations = [];
    private readonly bool _sessionRegistered;
    private bool _batterySaver;
    private bool _energySaver;
    private PauseReason _active;

    /// <summary>The reasons currently in effect, whatever the settings say about them.</summary>
    public PauseReason Active => _active;

    public PowerWatcher(IntPtr hwnd, ILog log)
    {
        _hwnd = hwnd;
        _log = log;
        _sessionRegistered = Power.WTSRegisterSessionNotification(hwnd, Power.NOTIFY_FOR_THIS_SESSION);
        if (!_sessionRegistered)
        {
            log.Warn("Lock / unlock notifications are not available; the wallpaper keeps playing while locked");
        }

        foreach (var setting in new[] { Power.GUID_CONSOLE_DISPLAY_STATE, Power.GUID_POWER_SAVING_STATUS, Power.GUID_ENERGY_SAVER_STATUS, Power.GUID_ACDC_POWER_SOURCE })
        {
            var handle = Power.RegisterPowerSettingNotification(hwnd, setting, Power.DEVICE_NOTIFY_WINDOW_HANDLE);
            if (handle == IntPtr.Zero)
            {
                // Energy Saver only exists on newer Windows 11 builds.
                log.Info($"Power setting {setting} is not available ({Marshal.GetLastPInvokeError()})");
            }
            else
            {
                _registrations.Add(handle);
            }
        }
    }

    /// <summary>Raised when a pause reason starts or ends.</summary>
    public event Action<PauseReason, bool>? Changed;

    /// <summary>Raised after the PC wakes from sleep.</summary>
    public event Action? Resumed;

    /// <summary>Handles session and power messages. Returns true when handled.</summary>
    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Power.WM_WTSSESSION_CHANGE:
                OnSessionChange((int)wParam);
                return true;
            case Power.WM_POWERBROADCAST:
                OnPowerBroadcast((int)wParam, lParam);
                return true;
            default:
                return false;
        }
    }

    private void OnSessionChange(int change)
    {
        switch (change)
        {
            case Power.WTS_SESSION_LOCK:
                Raise("Session locked", PauseReason.Locked, true);
                break;
            case Power.WTS_SESSION_UNLOCK:
                Raise("Session unlocked", PauseReason.Locked, false);
                break;
            case Power.WTS_CONSOLE_DISCONNECT or Power.WTS_REMOTE_DISCONNECT:
                Raise("Session disconnected", PauseReason.Disconnected, true);
                break;
            case Power.WTS_CONSOLE_CONNECT or Power.WTS_REMOTE_CONNECT:
                Raise("Session connected", PauseReason.Disconnected, false);
                break;
        }
    }

    private void OnPowerBroadcast(int kind, IntPtr lParam)
    {
        switch (kind)
        {
            case Power.PBT_APMSUSPEND:
                Raise("Going to sleep", PauseReason.Suspended, true);
                break;
            case Power.PBT_APMRESUMEAUTOMATIC or Power.PBT_APMRESUMESUSPEND:
                // Both arrive after a user-initiated wake; only react once.
                if (Raise("Woke from sleep", PauseReason.Suspended, false))
                {
                    Resumed?.Invoke();
                }

                break;
            case Power.PBT_POWERSETTINGCHANGE when lParam != IntPtr.Zero:
                var setting = (Power.POWERBROADCAST_SETTING*)lParam;
                if (setting->DataLength >= sizeof(uint))
                {
                    OnPowerSetting(setting->PowerSetting, setting->Data);
                }

                break;
        }
    }

    private void OnPowerSetting(Guid setting, uint value)
    {
        if (setting == Power.GUID_CONSOLE_DISPLAY_STATE)
        {
            // Dimmed (2) is still visible.
            Raise(value == 0 ? "Display turned off" : "Display is on", PauseReason.DisplayOff, value == 0);
        }
        else if (setting == Power.GUID_POWER_SAVING_STATUS || setting == Power.GUID_ENERGY_SAVER_STATUS)
        {
            if (setting == Power.GUID_POWER_SAVING_STATUS)
            {
                _batterySaver = value != 0;
            }
            else
            {
                _energySaver = value != 0;
            }

            var on = _batterySaver || _energySaver;
            Raise(on ? "Battery saver is on" : "Battery saver is off", PauseReason.BatterySaver, on);
        }
        else if (setting == Power.GUID_ACDC_POWER_SOURCE)
        {
            Raise(value == 0 ? "Running on AC power" : "Running on battery", PauseReason.OnBattery, value != 0);
        }
    }

    /// <summary>Logs and raises a change; returns false (and stays quiet) when nothing changed.</summary>
    private bool Raise(string what, PauseReason reason, bool active)
    {
        if (_active.HasFlag(reason) == active)
        {
            return false;
        }

        _active = active ? _active | reason : _active & ~reason;
        _log.Info(what);
        Changed?.Invoke(reason, active);
        return true;
    }

    public void Dispose()
    {
        if (_sessionRegistered)
        {
            Power.WTSUnRegisterSessionNotification(_hwnd);
        }

        foreach (var handle in _registrations)
        {
            Power.UnregisterPowerSettingNotification(handle);
        }

        _registrations.Clear();
    }
}
