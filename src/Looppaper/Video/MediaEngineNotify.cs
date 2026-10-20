using System.Runtime.InteropServices;
using Looppaper.Native;

namespace Looppaper.Video;

/// <summary>
/// A minimal native IMFMediaEngineNotify implementation. The Media Engine calls it on its own
/// worker threads; each event is forwarded to the wallpaper window as a posted message so all
/// handling happens on the UI thread. Instances live until <see cref="Free"/> (after the engine
/// has been shut down), so reference counting is not needed.
/// </summary>
internal static unsafe class MediaEngineNotify
{
    public const uint WM_VIDEO_EVENT = User32.WM_APP + 2;

    private static void** _vtable;

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyObject
    {
        public void** Vtable;
        public IntPtr TargetWindow;
        public int PlayerId;
    }

    /// <summary>Creates a callback that posts WM_VIDEO_EVENT(wParam = event, lParam = player id | error) to <paramref name="hwnd"/>.</summary>
    public static IntPtr Create(IntPtr hwnd, int playerId)
    {
        if (_vtable == null)
        {
            _vtable = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
            _vtable[0] = (delegate* unmanaged<NotifyObject*, Guid*, IntPtr*, int>)&QueryInterface;
            _vtable[1] = (delegate* unmanaged<NotifyObject*, uint>)&AddRef;
            _vtable[2] = (delegate* unmanaged<NotifyObject*, uint>)&Release;
            _vtable[3] = (delegate* unmanaged<NotifyObject*, uint, nuint, uint, int>)&EventNotify;
        }

        var obj = (NotifyObject*)NativeMemory.AllocZeroed((nuint)sizeof(NotifyObject));
        obj->Vtable = _vtable;
        obj->TargetWindow = hwnd;
        obj->PlayerId = playerId;
        return (IntPtr)obj;
    }

    public static void Free(IntPtr notify)
    {
        if (notify != IntPtr.Zero)
        {
            NativeMemory.Free((void*)notify);
        }
    }

    /// <summary>Packs the player id and the event's HRESULT (for errors) into lParam.</summary>
    public static (int PlayerId, int HResult) Unpack(IntPtr lParam)
    {
        var value = (long)lParam;
        return ((int)(value >> 32), (int)(value & 0xFFFFFFFF));
    }

    [UnmanagedCallersOnly]
    private static int QueryInterface(NotifyObject* self, Guid* iid, IntPtr* result)
    {
        if (*iid == MediaFoundation.IID_IUnknown || *iid == MediaFoundation.IID_IMFMediaEngineNotify)
        {
            *result = (IntPtr)self;
            return 0;
        }

        *result = IntPtr.Zero;
        return MediaFoundation.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(NotifyObject* self) => 2;

    [UnmanagedCallersOnly]
    private static uint Release(NotifyObject* self) => 1;

    [UnmanagedCallersOnly]
    private static int EventNotify(NotifyObject* self, uint eventCode, nuint param1, uint param2)
    {
        if (eventCode is MediaFoundation.EVENT_ERROR or MediaFoundation.EVENT_LOADEDMETADATA or MediaFoundation.EVENT_PLAYING)
        {
            var lParam = ((long)self->PlayerId << 32) | param2;
            User32.PostMessage(self->TargetWindow, WM_VIDEO_EVENT, (IntPtr)eventCode, (IntPtr)lParam);
        }

        return 0;
    }
}
