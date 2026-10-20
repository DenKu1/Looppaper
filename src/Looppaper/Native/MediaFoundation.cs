using System.Runtime.InteropServices;

namespace Looppaper.Native;

/// <summary>
/// The slice of Media Foundation's Media Engine that Looppaper uses, called through raw COM
/// vtables (no COM interop marshalling). Vtable slots follow mfmediaengine.h / mfobjects.h.
/// </summary>
internal static unsafe partial class MediaFoundation
{
    public const uint MF_VERSION = 0x00020070;
    public const uint MFSTARTUP_FULL = 0;
    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const int E_NOINTERFACE = unchecked((int)0x80004002);

    // MF_MEDIA_ENGINE_EVENT values.
    public const uint EVENT_ERROR = 5;
    public const uint EVENT_LOADEDMETADATA = 10;
    public const uint EVENT_PLAYING = 13;

    public static readonly Guid CLSID_MFMediaEngineClassFactory = new("B44392DA-499B-446b-A4CB-005FEAD0E6D5");
    public static readonly Guid IID_IMFMediaEngineClassFactory = new("4D645ACE-26AA-4688-9BE1-DF3516990B93");
    public static readonly Guid IID_IMFMediaEngineEx = new("83015EAD-B1E6-40D0-A98A-37145FFE1AD1");
    public static readonly Guid IID_IMFMediaEngineNotify = new("FEE7C112-E776-42B5-9BBF-0048524E2BD5");
    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    public static readonly Guid MF_MEDIA_ENGINE_CALLBACK = new("C60381B8-83A4-41F8-A3D0-DE05076849A9");
    public static readonly Guid MF_MEDIA_ENGINE_PLAYBACK_HWND = new("D988879B-67C9-4D92-BAA7-6EADD446039D");

    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    public static partial int MFShutdown();

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateAttributes(out IntPtr attributes, uint initialSize);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint clsContext, in Guid iid, out IntPtr instance);

    public static uint Release(IntPtr unknown) =>
        unknown == IntPtr.Zero ? 0 : ((delegate* unmanaged<IntPtr, uint>)Slot(unknown, 2))(unknown);

    public static int QueryInterface(IntPtr unknown, in Guid iid, out IntPtr result)
    {
        fixed (Guid* g = &iid)
        fixed (IntPtr* r = &result)
        {
            return ((delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)Slot(unknown, 0))(unknown, g, r);
        }
    }

    // IMFAttributes
    public static int SetUINT64(IntPtr attributes, in Guid key, ulong value)
    {
        fixed (Guid* k = &key)
        {
            return ((delegate* unmanaged<IntPtr, Guid*, ulong, int>)Slot(attributes, 22))(attributes, k, value);
        }
    }

    public static int SetUnknown(IntPtr attributes, in Guid key, IntPtr value)
    {
        fixed (Guid* k = &key)
        {
            return ((delegate* unmanaged<IntPtr, Guid*, IntPtr, int>)Slot(attributes, 27))(attributes, k, value);
        }
    }

    // IMFMediaEngineClassFactory
    public static int CreateInstance(IntPtr factory, uint flags, IntPtr attributes, out IntPtr engine)
    {
        fixed (IntPtr* e = &engine)
        {
            return ((delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr*, int>)Slot(factory, 3))(factory, flags, attributes, e);
        }
    }

    // IMFMediaEngine (slot = 3 + position in the interface)
    public static int SetSource(IntPtr engine, string url)
    {
        var bstr = Marshal.StringToBSTR(url);
        try
        {
            return ((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(engine, 6))(engine, bstr);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    public static int SetLoop(IntPtr engine, bool loop) =>
        ((delegate* unmanaged<IntPtr, int, int>)Slot(engine, 31))(engine, loop ? 1 : 0);

    public static int Play(IntPtr engine) => ((delegate* unmanaged<IntPtr, int>)Slot(engine, 32))(engine);

    public static int Pause(IntPtr engine) => ((delegate* unmanaged<IntPtr, int>)Slot(engine, 33))(engine);

    public static int SetMuted(IntPtr engine, bool muted) =>
        ((delegate* unmanaged<IntPtr, int, int>)Slot(engine, 35))(engine, muted ? 1 : 0);

    public static int SetVolume(IntPtr engine, double volume) =>
        ((delegate* unmanaged<IntPtr, double, int>)Slot(engine, 37))(engine, volume);

    public static int GetNativeVideoSize(IntPtr engine, out uint width, out uint height)
    {
        fixed (uint* w = &width)
        fixed (uint* h = &height)
        {
            return ((delegate* unmanaged<IntPtr, uint*, uint*, int>)Slot(engine, 40))(engine, w, h);
        }
    }

    public static int Shutdown(IntPtr engine) => ((delegate* unmanaged<IntPtr, int>)Slot(engine, 42))(engine);

    // IMFMediaEngineEx
    public static int UpdateVideoStream(IntPtr engineEx, in MFVideoNormalizedRect source, in RECT destination, in MFARGB border)
    {
        fixed (MFVideoNormalizedRect* s = &source)
        fixed (RECT* d = &destination)
        fixed (MFARGB* b = &border)
        {
            return ((delegate* unmanaged<IntPtr, MFVideoNormalizedRect*, RECT*, MFARGB*, int>)Slot(engineEx, 47))(engineEx, s, d, b);
        }
    }

    private static void* Slot(IntPtr comObject, int index) => (*(void***)comObject)[index];
}

[StructLayout(LayoutKind.Sequential)]
internal struct MFVideoNormalizedRect
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MFARGB
{
    public byte Blue;
    public byte Green;
    public byte Red;
    public byte Alpha;
}
