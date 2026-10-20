using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DropSort.UI.Services;

/// <summary>Enforces a client-area minimum during native resize, at the window's current DPI.</summary>
internal sealed class MinimumWindowSize : IDisposable
{
    // 64 navigation rail + 64 library padding + four 192-wide poster cells + scrollbar reserve.
    internal const int ClientWidth = 960;
    internal const int ClientHeight = 640;
    private const uint GetMinMaxInfo = 0x0024;
    private const uint NcDestroy = 0x0082;
    private readonly nint _window;
    private readonly SubclassProc _callback;
    private bool _attached;

    internal MinimumWindowSize(nint window)
    {
        _window = window;
        _callback = WindowProc;
        _attached = SetWindowSubclass(window, _callback, 1, 0);
        if (!_attached)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot install window size constraints.");
    }

    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nuint referenceData)
    {
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == GetMinMaxInfo)
        {
            var size = MinimumOuterSize;
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            limits.MinTrackSize.X = Math.Max(limits.MinTrackSize.X, size.Width);
            limits.MinTrackSize.Y = Math.Max(limits.MinTrackSize.Y, size.Height);
            Marshal.StructureToPtr(limits, lParam, false);
        }
        else if (message == NcDestroy)
        {
            Dispose();
        }
        return result;
    }

    internal (int Width, int Height) MinimumOuterSize
    {
        get
        {
            var dpi = GetDpiForWindow(_window);
            var scale = dpi / 96.0;
            var rect = new Rect { Right = (int)Math.Ceiling(ClientWidth * scale),
                Bottom = (int)Math.Ceiling(ClientHeight * scale) };
            AdjustWindowRectExForDpi(ref rect, unchecked((uint)GetWindowLong(_window, -16)),
                false, unchecked((uint)GetWindowLong(_window, -20)), dpi);
            return (rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    public void Dispose()
    {
        if (!_attached) return;
        RemoveWindowSubclass(_window, _callback, 1);
        _attached = false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nuint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style,
        [MarshalAs(UnmanagedType.Bool)] bool menu, uint extendedStyle, uint dpi);
}
