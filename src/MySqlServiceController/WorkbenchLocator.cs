using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace MySqlServiceController;

internal static class WorkbenchLocator
{
	private static readonly string[] ExecutableNames = { "MySQLWorkbench.exe", "MySQL Workbench.exe" };

	public static string Find(string configuredPath)
	{
		string text = Resolve(configuredPath);
		if (text != null)
		{
			return text;
		}
		text = FromUninstallRegistry();
		if (text != null)
		{
			return text;
		}
		return FromDefaultLocations();
	}

	public static bool IsWorkbenchExecutable(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string fileName = Path.GetFileName(path.Trim().Trim('"'));
		return ExecutableNames.Any((string name) => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
	}

	private static string Resolve(string path)
	{
		if (!IsWorkbenchExecutable(path))
		{
			return null;
		}
		string text = path.Trim().Trim('"');
		return File.Exists(text) ? text : null;
	}

	private static string FromUninstallRegistry()
	{
		RegistryView[] array = new RegistryView[2]
		{
			RegistryView.Registry64,
			RegistryView.Registry32
		};
		foreach (RegistryView view in array)
		{
			try
			{
				using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
				using RegistryKey uninstall = baseKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
				if (uninstall == null)
				{
					continue;
				}
				foreach (string subKeyName in uninstall.GetSubKeyNames())
				{
					try
					{
						using RegistryKey key = uninstall.OpenSubKey(subKeyName);
						if (key == null)
						{
							continue;
						}
						string text = key.GetValue("DisplayName") as string;
						if (text == null || text.IndexOf("MySQL Workbench", StringComparison.OrdinalIgnoreCase) < 0)
						{
							continue;
						}
						string text2 = FromDirectory(key.GetValue("InstallLocation") as string);
						if (text2 != null)
						{
							return text2;
						}
						text2 = ResolveIconPath(key.GetValue("DisplayIcon") as string);
						if (text2 != null)
						{
							return text2;
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static string ResolveIconPath(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string text = value.Trim();
		if (text.StartsWith("\"", StringComparison.Ordinal))
		{
			int num = text.IndexOf('"', 1);
			if (num > 1)
			{
				text = text.Substring(1, num - 1);
			}
		}
		else
		{
			int num2 = text.LastIndexOf(',');
			if (num2 > 0)
			{
				text = text.Substring(0, num2);
			}
		}
		return Resolve(text);
	}

	private static string FromDirectory(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			return null;
		}
		string text = directory.Trim().Trim('"');
		foreach (string executableName in ExecutableNames)
		{
			try
			{
				string text2 = Path.Combine(text, executableName);
				if (File.Exists(text2))
				{
					return text2;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static string FromDefaultLocations()
	{
		string[] array = new string[2]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
		};
		foreach (string path in array)
		{
			string text = FromMySQLDirectory(Path.Combine(path, "MySQL"));
			if (text != null)
			{
				return text;
			}
		}
		try
		{
			foreach (DriveInfo drive in DriveInfo.GetDrives())
			{
				if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
				{
					continue;
				}
				string[] array2 = new string[2] { "Program Files", "Program Files (x86)" };
				foreach (string path2 in array2)
				{
					string text2 = FromMySQLDirectory(Path.Combine(drive.RootDirectory.FullName, path2, "MySQL"));
					if (text2 != null)
					{
						return text2;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static string FromMySQLDirectory(string mysqlDirectory)
	{
		try
		{
			if (!Directory.Exists(mysqlDirectory))
			{
				return null;
			}
			foreach (string directory in Directory.GetDirectories(mysqlDirectory, "MySQL Workbench*"))
			{
				string text = FromDirectory(directory);
				if (text != null)
				{
					return text;
				}
			}
		}
		catch
		{
		}
		return null;
	}
}
