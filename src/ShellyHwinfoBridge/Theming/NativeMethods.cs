using System.Runtime.InteropServices;

namespace ShellyHwinfoBridge.Theming;

/// <summary>P/Invoke into system DLLs only (dwmapi, uxtheme, user32); failures are ignored.</summary>
internal static class NativeMethods
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>Gets the Windows build number.</summary>
    public static int WindowsBuild => Environment.OSVersion.Version.Build;

    /// <summary>
    /// Makes the title bar dark: immersive dark mode on Windows 10 1809+ (attribute 19 before 20H1, 20 from build
    /// 19041), plus caption/border/text colours on Windows 11 (build 22000+).
    /// </summary>
    public static void ApplyDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return;
        }

        var build = WindowsBuild;
        if (build >= 19041)
        {
            SetInt(hwnd, DwmwaUseImmersiveDarkMode, 1);
        }
        else if (build >= 17763)
        {
            SetInt(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, 1);
        }

        if (build >= 22000)
        {
            SetInt(hwnd, DwmwaCaptionColor, ColorRef(Theme.BgTop));
            SetInt(hwnd, DwmwaBorderColor, ColorRef(Theme.Border));
            SetInt(hwnd, DwmwaTextColor, ColorRef(Theme.Ink));
        }
    }

    /// <summary>Gives a window (scrollbars, list views, grids) the dark Explorer theme.</summary>
    public static void UseDarkExplorerTheme(Control control)
    {
        if (control.IsHandleCreated)
        {
            _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
    }

    /// <summary>Destroys an icon handle created by <see cref="Bitmap.GetHicon"/>.</summary>
    public static void DestroyIconHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            _ = DestroyIcon(handle);
        }
    }

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static void SetInt(IntPtr hwnd, int attribute, int value)
    {
        try
        {
            _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not available on this Windows version.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
