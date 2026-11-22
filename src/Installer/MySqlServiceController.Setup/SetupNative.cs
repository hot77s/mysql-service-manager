using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace MySqlServiceController.Setup;

/// <summary>安装器的 Windows 外观辅助：深色标题栏、窗口拖动。</summary>
internal static class DarkMode
{
	[DllImport("uxtheme.dll", EntryPoint = "#135")]
	private static extern int SetPreferredAppMode(int mode);

	[DllImport("uxtheme.dll", EntryPoint = "#136")]
	private static extern int FlushMenuThemes();

	public static void Apply(bool dark)
	{
		try
		{
			SetPreferredAppMode(dark ? 2 : 3);
			FlushMenuThemes();
		}
		catch
		{
		}
	}
}

internal static class SetupNative
{
	public const int WM_NCLBUTTONDOWN = 0xA1;

	public const int HTCAPTION = 0x2;

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

	public static void ApplyTitleBar(IntPtr handle, bool dark, Color background, Color text)
	{
		if (handle == IntPtr.Zero)
		{
			return;
		}
		int value = (dark ? 1 : 0);
		try
		{
			DwmSetWindowAttribute(handle, 20, ref value, 4);
		}
		catch
		{
		}
		int value2 = ColorTranslator.ToWin32(background);
		int value3 = ColorTranslator.ToWin32(text);
		try
		{
			DwmSetWindowAttribute(handle, 35, ref value2, 4);
		}
		catch
		{
		}
		try
		{
			DwmSetWindowAttribute(handle, 36, ref value3, 4);
		}
		catch
		{
		}
	}

	public static void BeginDrag(IntPtr handle)
	{
		try
		{
			ReleaseCapture();
			SendMessage(handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
		}
		catch
		{
		}
	}
}
