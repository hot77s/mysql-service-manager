using System;
using System.Drawing;
using System.Windows.Forms;

namespace MySqlServiceController;

internal static class AppIcon
{
	private static Icon _icon;

	public static Icon Get()
	{
		if (_icon == null)
		{
			try
			{
				_icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			}
			catch
			{
			}
		}
		return _icon;
	}
}
