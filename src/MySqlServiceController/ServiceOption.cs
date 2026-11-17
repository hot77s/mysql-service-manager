using System;

namespace MySqlServiceController;

internal sealed class ServiceOption
{
	public string Name { get; set; }

	public string DisplayName { get; set; }

	public string StatusText { get; set; }

	public override string ToString()
	{
		if (string.Equals(Name, DisplayName, StringComparison.OrdinalIgnoreCase))
		{
			return Name + "  ·  " + StatusText;
		}
		return Name + "  ·  " + DisplayName + "  ·  " + StatusText;
	}
}
