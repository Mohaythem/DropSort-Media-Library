using System.Runtime.InteropServices;
using TBDMovies.UI.Services;
using Xunit;

namespace TBDMovies.Tests;

public sealed class MinimumWindowSizeTests
{
    [Fact]
    public void GetMinMaxInfo_uses_dpi_adjusted_outer_minimum_and_disposes_on_destroy()
    {
        using var window = HiddenWindow.Create();
        using var minimum = new MinimumWindowSize(window.Handle);

        var expected = minimum.MinimumOuterSize;
        var info = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(new MinMaxInfo(), info, false);
            var result = SendMessage(window.Handle, WmGetMinMaxInfo, 0, info);
            Assert.Equal(nint.Zero, result);
            var limits = Marshal.PtrToStructure<MinMaxInfo>(info);
            Assert.Equal(expected.Width, limits.MinTrackSize.X);
            Assert.Equal(expected.Height, limits.MinTrackSize.Y);
            Assert.True(expected.Width > 960);
            Assert.True(expected.Height > 640);
        }
        finally
        {
            Marshal.FreeHGlobal(info);
        }

        window.Destroy();
        Assert.False(window.IsValid);
        minimum.Dispose();
        minimum.Dispose();
    }

    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint CwUseDefault = 0x80000000;
    private const uint CsOwnDc = 0x0020;
    private static readonly nint HwndMessage = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style;
        public WndProcDelegate WndProc;
        public int ClsExtra;
        public int WndExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string MenuName;
        public string ClassName;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProcDelegate(nint window, uint message, nuint wParam, nint lParam);

    private sealed class HiddenWindow : IDisposable
    {
        private static readonly WndProcDelegate Procedure = WindowProc;
        private static readonly string ClassName = "TBDMovies.MinimumWindowSizeTests";
        public nint Handle { get; private set; }
        public bool IsValid => Handle != nint.Zero && IsWindow(Handle);

        private HiddenWindow(nint handle) => Handle = handle;

        public static HiddenWindow Create()
        {
            var module = GetModuleHandle(null);
            var cls = new WndClass
            {
                Style = CsOwnDc,
                WndProc = Procedure,
                Instance = module,
                ClassName = ClassName,
                MenuName = string.Empty,
            };
            _ = RegisterClass(ref cls);
            var handle = CreateWindowEx(0, ClassName, ClassName, WsOverlappedWindow,
                unchecked((int)CwUseDefault), unchecked((int)CwUseDefault), 1000, 700,
                HwndMessage, nint.Zero, module, nint.Zero);
            Assert.NotEqual(nint.Zero, handle);
            return new HiddenWindow(handle);
        }

        public void Destroy()
        {
            if (Handle != nint.Zero)
            {
                DestroyWindow(Handle);
                Handle = nint.Zero;
            }
        }

        public void Dispose() => Destroy();

        private static nint WindowProc(nint window, uint message, nuint wParam, nint lParam) =>
            DefWindowProc(window, message, wParam, lParam);
    }

    private static nint SendMessage(nint window, uint message, nuint wParam, nint lParam) =>
        SendMessageNative(window, message, wParam, lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessageNative(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WndClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
