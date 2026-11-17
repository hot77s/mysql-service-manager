using System;
using System.Collections.Generic;
using System.IO;

namespace MySqlServiceController;

internal sealed class MySqlInstanceInfo
{
	public string ServiceName { get; set; } = string.Empty;

	public string RawImagePath { get; set; } = string.Empty;

	public string ExePath { get; set; } = string.Empty;

	public string BaseDirectory { get; set; } = string.Empty;

	public string DataDirectory { get; set; } = string.Empty;

	public string DataDirectorySource { get; set; } = string.Empty;

	public string ConfigPath { get; set; } = string.Empty;

	public string ConfigSource { get; set; } = string.Empty;

	public bool ConfigHasMySqlSection { get; set; }

	public List<string> ConfigCandidates { get; } = new List<string>();

	public List<string> Notes { get; } = new List<string>();

	public int Port { get; set; } = 3306;

	public string PortSource { get; set; } = "默认值";

	public string LogErrorPath { get; set; } = string.Empty;

	public string LogErrorSource { get; set; } = string.Empty;

	public bool SkipNetworking { get; set; }

	public bool NamedPipe { get; set; }

	public string BindAddress { get; set; } = string.Empty;

	public ServiceSnapshot Snapshot { get; set; }

	public bool ExeExists => !string.IsNullOrWhiteSpace(ExePath) && File.Exists(ExePath);

	public bool DataDirectoryExists => !string.IsNullOrWhiteSpace(DataDirectory) && Directory.Exists(DataDirectory);

	public string Version
	{
		get
		{
			try
			{
				if (!ExeExists)
				{
					return string.Empty;
				}
				string directoryName = Path.GetFileName(Path.GetDirectoryName(ExePath) ?? string.Empty);
				if (directoryName.StartsWith("bin", StringComparison.OrdinalIgnoreCase))
				{
					directoryName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(ExePath) ?? string.Empty) ?? string.Empty);
				}
				return directoryName ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}
	}
}

internal static class MySqlInstanceLocator
{
	private static readonly string[] ValueOptions = new string[9] { "defaults-file", "defaults-extra-file", "datadir", "basedir", "port", "log-error", "bind-address", "socket", "pid-file" };

	public static MySqlInstanceInfo Resolve(string serviceName)
	{
		MySqlInstanceInfo info = new MySqlInstanceInfo
		{
			ServiceName = (serviceName ?? string.Empty).Trim()
		};
		try
		{
			info.Snapshot = ServiceManager.GetSnapshot(info.ServiceName, string.Empty);
			info.RawImagePath = ServiceManager.TryGetRawImagePath(info.ServiceName);
			ParseImagePath(info.RawImagePath, out string exePath, out Dictionary<string, string> options);
			if (string.IsNullOrWhiteSpace(exePath))
			{
				exePath = info.Snapshot?.ImagePath ?? string.Empty;
			}
			info.ExePath = exePath;
			string exeDirectory = string.Empty;
			try
			{
				exeDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
			}
			catch
			{
			}
			string defaultsFile = GetOption(options, "defaults-file");
			string extraFile = GetOption(options, "defaults-extra-file");
			List<KeyValuePair<string, string>> candidates = CollectConfigCandidates(defaultsFile, extraFile, exeDirectory);
			IniDocument ini = null;
			foreach (KeyValuePair<string, string> candidate in candidates)
			{
				info.ConfigCandidates.Add(candidate.Key);
				if (ini == null && File.Exists(candidate.Key))
				{
					info.ConfigPath = candidate.Key;
					info.ConfigSource = candidate.Value;
					ini = LoadIni(candidate.Key, info);
				}
			}
			if (ini == null && !string.IsNullOrWhiteSpace(defaultsFile) && !File.Exists(defaultsFile))
			{
				info.Notes.Add("服务启动参数指定的配置文件不存在：" + defaultsFile);
			}
			if (ini != null)
			{
				info.ConfigHasMySqlSection = ini.HasSection("mysqld") || ini.HasSection("mariadb") || ini.HasSection("server");
			}
			string configDirectory = string.Empty;
			if (!string.IsNullOrWhiteSpace(info.ConfigPath))
			{
				configDirectory = Path.GetDirectoryName(info.ConfigPath) ?? string.Empty;
			}
			string basedir = ini?.GetFirst("basedir");
			if (!string.IsNullOrWhiteSpace(basedir))
			{
				info.BaseDirectory = ResolvePath(basedir, string.IsNullOrWhiteSpace(configDirectory) ? exeDirectory : configDirectory);
			}
			else if (!string.IsNullOrWhiteSpace(exeDirectory))
			{
				string text = Path.GetDirectoryName(exeDirectory);
				info.BaseDirectory = (string.IsNullOrWhiteSpace(text) ? exeDirectory : text);
			}
			string text2 = GetOption(options, "datadir") ?? ini?.GetFirst("datadir");
			if (!string.IsNullOrWhiteSpace(text2))
			{
				info.DataDirectory = ResolvePath(text2, FirstNotEmpty(info.BaseDirectory, configDirectory, exeDirectory));
				info.DataDirectorySource = ((GetOption(options, "datadir") != null) ? "服务启动参数" : "my.ini");
			}
			else
			{
				info.DataDirectory = DetectDataDirectory(info.BaseDirectory);
				info.DataDirectorySource = (string.IsNullOrWhiteSpace(info.DataDirectory) ? string.Empty : "自动发现");
			}
			info.SkipNetworking = IsEnabled(ini?.GetFirst("skip-networking"));
			info.NamedPipe = IsEnabled(ini?.GetFirst("enable-named-pipe")) || IsEnabled(ini?.GetFirst("named-pipe"));
			info.BindAddress = ini?.GetFirst("bind-address") ?? string.Empty;
			string text3 = GetOption(options, "port") ?? ini?.GetFirst("port");
			int result;
			if (!string.IsNullOrWhiteSpace(text3) && int.TryParse(text3, out result) && result > 0 && result <= 65535)
			{
				info.Port = result;
				info.PortSource = ((GetOption(options, "port") != null) ? "服务启动参数" : "my.ini");
			}
			string text4 = GetOption(options, "log-error");
			if (text4 == null)
			{
				text4 = ini?.GetFirst("log-error");
			}
			if (!string.IsNullOrWhiteSpace(text4))
			{
				string text5 = text4.Trim().Trim('"');
				if (text5.Equals("stderr", StringComparison.OrdinalIgnoreCase) || text5.Equals("console", StringComparison.OrdinalIgnoreCase) || text5 == ".")
				{
					info.LogErrorSource = "输出到控制台，不写文件";
				}
				else
				{
					if (string.IsNullOrEmpty(Path.GetExtension(text5)))
					{
						text5 += ".err";
					}
					info.LogErrorPath = ResolvePath(text5, FirstNotEmpty(info.DataDirectory, info.BaseDirectory, configDirectory, exeDirectory));
					info.LogErrorSource = ((GetOption(options, "log-error") != null) ? "服务启动参数" : "my.ini");
				}
			}
			else if (!string.IsNullOrWhiteSpace(info.DataDirectory))
			{
				info.LogErrorPath = Path.Combine(info.DataDirectory, Environment.MachineName + ".err");
				info.LogErrorSource = "数据目录默认命名";
			}
			if (!string.IsNullOrWhiteSpace(info.LogErrorPath) && !File.Exists(info.LogErrorPath))
			{
				string text6 = FindNewestErrorLog(info.DataDirectory);
				if (string.IsNullOrWhiteSpace(text6))
				{
					string text7 = DetectDataDirectory(string.Empty);
					if (!string.Equals(text7, info.DataDirectory, StringComparison.OrdinalIgnoreCase))
					{
						text6 = FindNewestErrorLog(text7);
					}
				}
				if (!string.IsNullOrWhiteSpace(text6))
				{
					info.LogErrorPath = text6;
					info.LogErrorSource += "（已自动发现最新的 .err 文件）";
				}
			}
		}
		catch (Exception exception)
		{
			AppLog.Error("解析 MySQL 实例信息失败：" + info.ServiceName, exception);
			info.Notes.Add("解析实例信息时出错：" + exception.Message);
		}
		return info;
	}

	private static IniDocument LoadIni(string path, MySqlInstanceInfo info)
	{
		if (!TextDecoder.TryReadFile(path, out string text, out byte[] _, out string error))
		{
			info.Notes.Add("无法读取配置文件：" + error);
			return null;
		}
		return IniDocument.Parse(text);
	}

	private static List<KeyValuePair<string, string>> CollectConfigCandidates(string defaultsFile, string extraFile, string exeDirectory)
	{
		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddCandidate(list, hashSet, defaultsFile, "服务启动参数 --defaults-file");
		AddCandidate(list, hashSet, extraFile, "服务启动参数 --defaults-extra-file");
		if (!string.IsNullOrWhiteSpace(exeDirectory))
		{
			AddCandidate(list, hashSet, Path.Combine(exeDirectory, "my.ini"), "安装目录 bin\\my.ini");
			try
			{
				string text = Path.GetDirectoryName(exeDirectory);
				if (!string.IsNullOrWhiteSpace(text))
				{
					AddCandidate(list, hashSet, Path.Combine(text, "my.ini"), "安装目录 my.ini");
					AddCandidate(list, hashSet, Path.Combine(text, "my.cnf"), "安装目录 my.cnf");
				}
			}
			catch
			{
			}
		}
		try
		{
			string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MySQL");
			if (Directory.Exists(text2))
			{
				string[] directories = Directory.GetDirectories(text2);
				Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
				Array.Reverse(directories);
				foreach (string text3 in directories)
				{
					AddCandidate(list, hashSet, Path.Combine(text3, "my.ini"), "ProgramData\\MySQL\\" + Path.GetFileName(text3));
				}
			}
		}
		catch
		{
		}
		try
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				AddCandidate(list, hashSet, Path.Combine(folderPath, "my.ini"), "Windows 目录");
				AddCandidate(list, hashSet, Path.Combine(folderPath, "my.cnf"), "Windows 目录");
			}
		}
		catch
		{
		}
		return list;
	}

	private static void AddCandidate(List<KeyValuePair<string, string>> list, HashSet<string> seen, string path, string source)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		try
		{
			string text = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
			if (seen.Add(text))
			{
				list.Add(new KeyValuePair<string, string>(text, source));
			}
		}
		catch
		{
		}
	}

	private static string DetectDataDirectory(string basedir)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(basedir))
			{
				string text = Path.Combine(basedir, "data");
				if (Directory.Exists(text))
				{
					return text;
				}
			}
		}
		catch
		{
		}
		try
		{
			string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MySQL");
			if (!Directory.Exists(text2))
			{
				return string.Empty;
			}
			string[] directories = Directory.GetDirectories(text2);
			Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
			Array.Reverse(directories);
			foreach (string text3 in directories)
			{
				string text4 = Path.Combine(text3, "Data");
				if (Directory.Exists(text4))
				{
					return text4;
				}
			}
		}
		catch
		{
		}
		return string.Empty;
	}

	private static string FindNewestErrorLog(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			return string.Empty;
		}
		try
		{
			if (!Directory.Exists(directory))
			{
				return string.Empty;
			}
			string[] files = Directory.GetFiles(directory, "*.err");
			if (files.Length == 0)
			{
				return string.Empty;
			}
			string text = string.Empty;
			DateTime dateTime = DateTime.MinValue;
			foreach (string text2 in files)
			{
				DateTime lastWriteTime = File.GetLastWriteTime(text2);
				if (lastWriteTime > dateTime)
				{
					dateTime = lastWriteTime;
					text = text2;
				}
			}
			return text;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string ResolvePath(string value, string baseDirectory)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string text = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
		try
		{
			if (Path.IsPathRooted(text))
			{
				return Path.GetFullPath(text);
			}
			if (!string.IsNullOrWhiteSpace(baseDirectory))
			{
				return Path.GetFullPath(Path.Combine(baseDirectory, text));
			}
			return Path.GetFullPath(text);
		}
		catch
		{
			return text;
		}
	}

	private static string FirstNotEmpty(params string[] values)
	{
		foreach (string text in values)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		return string.Empty;
	}

	private static bool IsEnabled(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string text = value.Trim();
		if (text.Equals("0", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase) || text.Equals("false", StringComparison.OrdinalIgnoreCase) || text.Equals("no", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return true;
	}

	private static string GetOption(Dictionary<string, string> options, string key)
	{
		if (options != null && options.TryGetValue(key, out string value))
		{
			return value;
		}
		return null;
	}

	private static void ParseImagePath(string raw, out string exePath, out Dictionary<string, string> options)
	{
		exePath = string.Empty;
		options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return;
		}
		string text2;
		if (text[0] == '"')
		{
			int num = text.IndexOf('"', 1);
			if (num > 1)
			{
				exePath = text.Substring(1, num - 1);
				text2 = text.Substring(num + 1);
			}
			else
			{
				exePath = text.Trim('"');
				text2 = string.Empty;
			}
		}
		else
		{
			int num2 = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
			if (num2 > 0)
			{
				exePath = text.Substring(0, num2 + 4);
				text2 = text.Substring(num2 + 4);
			}
			else
			{
				int num3 = text.IndexOf(' ');
				exePath = ((num3 > 0) ? text.Substring(0, num3) : text);
				text2 = ((num3 > 0) ? text.Substring(num3) : string.Empty);
			}
		}
		exePath = Environment.ExpandEnvironmentVariables(exePath.Trim().Trim('"'));
		int i = 0;
		while (i < text2.Length)
		{
			while (i < text2.Length && char.IsWhiteSpace(text2[i]))
			{
				i++;
			}
			if (i >= text2.Length)
			{
				break;
			}
			if (text2[i] != '-')
			{
				while (i < text2.Length && !char.IsWhiteSpace(text2[i]))
				{
					i++;
				}
				continue;
			}
			int num4 = i;
			while (i < text2.Length && !char.IsWhiteSpace(text2[i]) && text2[i] != '=')
			{
				i++;
			}
			string text3 = text2.Substring(num4, i - num4);
			text3 = (text3.StartsWith("--", StringComparison.Ordinal) ? text3.Substring(2) : ((text3.Length > 0 && text3[0] == '-') ? text3.Substring(1) : text3));
			text3 = IniDocument.NormalizeKey(text3);
			string value = null;
			if (i < text2.Length && text2[i] == '=')
			{
				i++;
				value = ReadValue(text2, ref i);
			}
			else
			{
				int num5 = i;
				while (i < text2.Length && char.IsWhiteSpace(text2[i]))
				{
					i++;
				}
				if (i < text2.Length && text2[i] == '=')
				{
					i++;
					value = ReadValue(text2, ref i);
				}
				else if (i < text2.Length && text2[i] != '-' && IsValueOption(text3))
				{
					value = ReadValue(text2, ref i);
				}
				else
				{
					i = num5;
				}
			}
			if (text3.Length > 0 && !options.ContainsKey(text3))
			{
				options[text3] = value ?? string.Empty;
			}
		}
	}

	private static string ReadValue(string text, ref int index)
	{
		while (index < text.Length && char.IsWhiteSpace(text[index]))
		{
			index++;
		}
		if (index >= text.Length)
		{
			return string.Empty;
		}
		if (text[index] == '"' || text[index] == '\'')
		{
			char c = text[index];
			int num = text.IndexOf(c, index + 1);
			if (num < 0)
			{
				string result = text.Substring(index + 1);
				index = text.Length;
				return result;
			}
			string result2 = text.Substring(index + 1, num - index - 1);
			index = num + 1;
			return result2;
		}
		int num2 = index;
		while (index < text.Length && !char.IsWhiteSpace(text[index]))
		{
			index++;
		}
		return text.Substring(num2, index - num2);
	}

	private static bool IsValueOption(string key)
	{
		foreach (string text in ValueOptions)
		{
			if (string.Equals(text, key, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
