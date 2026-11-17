using System.Runtime.InteropServices;

namespace MySqlServiceController;

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
