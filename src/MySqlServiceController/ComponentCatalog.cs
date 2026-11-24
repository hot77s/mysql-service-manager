using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MySqlServiceController;

/// <summary>可选的 MySQL 组件版本。</summary>
internal sealed class ComponentOption
{
	public string Version { get; }

	/// <summary>一个词的简称：创新版 / LTS / 旧版 / 正式版。</summary>
	public string Tag { get; }

	/// <summary>悬停提示：这一版适合什么用途。</summary>
	public string Hint { get; }

	public ComponentOption(string version, string tag, string hint = "")
	{
		Version = version;
		Tag = tag ?? string.Empty;
		Hint = hint ?? string.Empty;
	}
}

/// <summary>Windows Installer 登记的一个可卸载组件。</summary>
internal sealed class InstalledComponent
{
	public string Kind { get; set; }
	public string DisplayName { get; set; }
	public string Version { get; set; }
	public string Directory { get; set; }
	public Guid ProductCode { get; set; }
	public bool MachineWide { get; set; }

	public string Label => DisplayName + (string.IsNullOrWhiteSpace(Version) ? string.Empty : "  ·  " + ComponentCatalog.FormatVersion(Version)) + (MachineWide ? string.Empty : "（当前用户）");
}

/// <summary>
/// MySQL 服务与 Workbench 的版本目录。默认选中最新版，联网时可从 winget 官方清单
/// 补充更新的版本；联网失败不影响内置目录。
/// </summary>
internal static class ComponentCatalog
{
	public const string KindServer = "server";

	public const string KindWorkbench = "workbench";

	public const string ServerDisplayName = "MySQL 服务";

	public const string WorkbenchDisplayName = "MySQL Workbench";

	private static readonly ComponentOption[] ServerOptions = new ComponentOption[5]
	{
		new ComponentOption("26.10.0", "创新版", "最新创新版：想尝新功能、做开发与测试可以选它；正式生产建议选 8.4 LTS。"),
		new ComponentOption("26.7.0", "创新版", "创新版：功能较新，适合验证新特性与升级；生产环境建议选 8.4 LTS。"),
		new ComponentOption("9.7.2", "创新版", "9.x 创新版：适合对比旧版本、验证升级兼容性；生产环境建议选 8.4 LTS。"),
		new ComponentOption("8.4.11", "LTS", "LTS 长期支持版：生产环境、需要长期稳定运行的业务优先选它。"),
		new ComponentOption("8.0.45", "旧版", "旧版 8.0 分支：仅在需要兼容老项目或老客户端时选用。")
	};

	private static readonly ComponentOption[] WorkbenchOptions = new ComponentOption[2]
	{
		new ComponentOption("26.10.0", "正式版", "最新版 Workbench，与 MySQL 26.10 同期发布，功能最新。"),
		new ComponentOption("26.7.0", "正式版", "上一版 Workbench，与 MySQL 26.7 同期发布；没有特别原因建议选最新。")
	};

	public static string DisplayNameOf(string kind)
	{
		return (kind == KindWorkbench) ? WorkbenchDisplayName : ServerDisplayName;
	}

	public static List<ComponentOption> GetOptions(string kind)
	{
		return new List<ComponentOption>((kind == KindWorkbench) ? WorkbenchOptions : ServerOptions);
	}

	public static ComponentOption GetLatest(string kind)
	{
		List<ComponentOption> options = GetOptions(kind);
		return (options.Count > 0) ? options[0] : null;
	}

	/// <summary>MySQL 组件默认安装根目录：优先 D:\Program Files，没有 D 盘时退回系统 Program Files。</summary>
	public static string DefaultBaseDirectory()
	{
		try
		{
			foreach (DriveInfo drive in DriveInfo.GetDrives())
			{
				if (drive.DriveType == DriveType.Fixed && string.Equals(drive.Name, "D:\\", StringComparison.OrdinalIgnoreCase) && drive.IsReady)
				{
					return Path.Combine(drive.RootDirectory.FullName, "Program Files");
				}
			}
		}
		catch
		{
		}
		return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
	}

	public static string FolderNameFor(string kind, string version)
	{
		string series = SeriesOf(version);
		return (kind == KindWorkbench) ? ("MySQL Workbench " + series) : ("MySQL Server " + series);
	}

	public static string InstallDirectoryFor(string kind, string version, string baseDirectory)
	{
		return Path.Combine(baseDirectory, "MySQL", FolderNameFor(kind, version));
	}

	public static string SeriesOf(string version)
	{
		string[] parts = (version ?? string.Empty).Split('.');
		return (parts.Length >= 2) ? (parts[0] + "." + parts[1]) : (version ?? string.Empty);
	}

	/// <summary>显示用版本号：MySQL 的 MSI 会把 Workbench 写成 26.10.0.0，末尾多余的 .0 去掉。</summary>
	public static string FormatVersion(string version)
	{
		string[] parts = (version ?? string.Empty).Trim().Split('.');
		int count = parts.Length;
		while (count > 3 && parts[count - 1].Length > 0 && parts[count - 1].Trim('0').Length == 0)
		{
			count--;
		}
		return string.Join(".", parts, 0, count);
	}

	public static string DefaultServiceName(string version)
	{
		string[] parts = (version ?? string.Empty).Split('.');
		return (parts.Length >= 2) ? ("MySQL" + parts[0] + parts[1]) : "MySQL";
	}

	public static string BuildDownloadUrl(string kind, string version)
	{
		string fileName = ((kind == KindWorkbench) ? ("mysql-workbench-" + version + "-winx64.msi") : ("mysql-" + version + "-winx64.msi"));
		if (kind == KindWorkbench)
		{
			return "https://cdn.mysql.com/Downloads/MySQLGUITools/" + fileName;
		}
		return "https://cdn.mysql.com/Downloads/MySQL-" + SeriesOf(version) + "/" + fileName;
	}

	public static string BuildArchiveUrl(string kind, string version)
	{
		string fileName = ((kind == KindWorkbench) ? ("mysql-workbench-" + version + "-winx64.msi") : ("mysql-" + version + "-winx64.msi"));
		string product = ((kind == KindWorkbench) ? "8" : "23");
		return "https://downloads.mysql.com/archives/get/p/" + product + "/file/" + fileName;
	}

	/// <summary>
	/// 从 winget 官方清单（GitHub）取最新版本；只在内置目录不是最新时返回，失败返回 null。
	/// 该方法是同步网络请求，请放在后台线程调用。
	/// </summary>
	public static ComponentOption TryRefreshLatest(string kind)
	{
		try
		{
			string api = ((kind == KindWorkbench) ? "https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/o/Oracle/MySQLWorkbench?ref=master" : "https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/o/Oracle/MySQL?ref=master");
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(api);
			request.UserAgent = "MySQLManager/" + AppVersion;
			request.Accept = "application/vnd.github+json";
			request.Timeout = 10000;
			request.ReadWriteTimeout = 10000;
			request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			string json;
			using (WebResponse response = request.GetResponse())
			using (Stream stream = response.GetResponseStream())
			using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
			{
				json = reader.ReadToEnd();
			}
			Version maxVersion = null;
			string maxText = null;
			foreach (Match match in Regex.Matches(json, "\"name\"\\s*:\\s*\"(\\d+(?:\\.\\d+){1,3})\""))
			{
				if (Version.TryParse(match.Groups[1].Value, out Version parsed) && (maxVersion == null || parsed > maxVersion))
				{
					maxVersion = parsed;
					maxText = match.Groups[1].Value;
				}
			}
			if (maxText == null)
			{
				return null;
			}
			string current = GetLatest(kind)?.Version;
			if (!string.IsNullOrWhiteSpace(current) && Version.TryParse(current, out Version currentVersion) && maxVersion <= currentVersion)
			{
				return null;
			}
			return new ComponentOption(maxText, (kind == KindWorkbench) ? "正式版" : "创新版", "联网发现的新版本，想尝新可以选它；生产环境仍建议选 LTS。");
		}
		catch
		{
			return null;
		}
	}

	/// <summary>在常见位置查找已安装的 MySQL 服务端目录。</summary>
	public static string FindInstalledServerDirectory()
	{
		return FindNewest(Path.Combine("MySQL", string.Empty), "MySQL Server*", delegate(string dir)
		{
			return File.Exists(Path.Combine(dir, "bin", "mysqld.exe"));
		});
	}

	/// <summary>在常见位置查找已安装的 Workbench 目录。</summary>
	public static string FindInstalledWorkbenchDirectory()
	{
		string exe = WorkbenchLocator.Find(null);
		return string.IsNullOrWhiteSpace(exe) ? null : (Path.GetDirectoryName(exe) ?? string.Empty);
	}

	private static string FindNewest(string relativeRoot, string pattern, Func<string, bool> validate)
	{
		string best = null;
		Version bestVersion = null;
		foreach (string baseDir in CandidateBases())
		{
			try
			{
				string root = Path.Combine(baseDir, relativeRoot.TrimEnd(Path.DirectorySeparatorChar));
				if (!Directory.Exists(root))
				{
					continue;
				}
				foreach (string directory in Directory.GetDirectories(root, pattern))
				{
					if (!validate(directory))
					{
						continue;
					}
					string series = Path.GetFileName(directory).Replace("MySQL Server ", string.Empty).Replace("MySQL Workbench ", string.Empty);
					if (!Version.TryParse(series, out Version parsed))
					{
						best ??= directory;
						continue;
					}
					if (bestVersion == null || parsed > bestVersion)
					{
						bestVersion = parsed;
						best = directory;
					}
				}
			}
			catch
			{
			}
		}
		return best;
	}

	private static IEnumerable<string> CandidateBases()
	{
		List<string> bases = new List<string>();
		void Add(string value)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				string normalized = value.TrimEnd(Path.DirectorySeparatorChar);
				if (!bases.Contains(normalized, StringComparer.OrdinalIgnoreCase))
				{
					bases.Add(normalized);
				}
			}
		}
		Add(DefaultBaseDirectory());
		Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
		Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
		Add("C:\\Program Files");
		Add("D:\\Program Files");
		return bases;
	}

	/// <summary>读取已安装组件的简短描述；未安装时返回 null。</summary>
	public static string DescribeInstalled(string kind, out string directory)
	{
		directory = (kind == KindWorkbench) ? FindInstalledWorkbenchDirectory() : FindInstalledServerDirectory();
		if (string.IsNullOrWhiteSpace(directory))
		{
			return null;
		}
		string label = DisplayNameOf(kind);
		string folder = (Path.GetFileName(directory) ?? string.Empty).Trim();
		string version = folder.StartsWith("MySQL Server", StringComparison.OrdinalIgnoreCase) ? folder.Substring("MySQL Server".Length).Trim() : (folder.StartsWith("MySQL Workbench", StringComparison.OrdinalIgnoreCase) ? folder.Substring("MySQL Workbench".Length).Trim() : folder);
		return string.IsNullOrWhiteSpace(version) ? ("已安装 " + label) : ("已安装 " + label + " " + version);
	}

	private const string UninstallRegistryPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall";

	internal static string IdentifyInstalledKind(string displayName)
	{
		string name = displayName?.Trim() ?? string.Empty;
		// 只识别 Server / Workbench 本身，不把 MySQL Installer、样例、Shell 等算进去。
		if (Regex.IsMatch(name, @"^MySQL (?:Community |Enterprise )?Server(?: \d[\d.]*| \d[\d.]* (?:Community|Enterprise) Edition)?(?: \((?:x64|x86|64-bit|32-bit)\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			return KindServer;
		}
		if (Regex.IsMatch(name, @"^MySQL Workbench(?: \d[\d.]*)?(?: (?:CE|SE|OSS|Community Edition|Commercial Edition))?(?: \((?:x64|x86|64-bit|32-bit)\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			return KindWorkbench;
		}
		return null;
	}

	public static List<InstalledComponent> FindUninstallableComponents(string kind)
	{
		List<InstalledComponent> matches = new List<InstalledComponent>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		RegistryView[] views = Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 };
		foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
		{
			foreach (RegistryView view in views)
			{
				try
				{
					using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
					using RegistryKey uninstall = baseKey.OpenSubKey(UninstallRegistryPath);
					if (uninstall == null)
					{
						continue;
					}
					foreach (string keyName in uninstall.GetSubKeyNames())
					{
						if (!Guid.TryParseExact(keyName, "B", out Guid productCode))
						{
							continue;
						}
						using RegistryKey entry = uninstall.OpenSubKey(keyName);
						if (entry == null || !Equals(entry.GetValue("WindowsInstaller"), 1) || Equals(entry.GetValue("NoRemove"), 1))
						{
							continue;
						}
						string name = entry.GetValue("DisplayName") as string;
						if (IdentifyInstalledKind(name) == kind && seen.Add(hive + ":" + productCode.ToString("B")))
						{
							matches.Add(new InstalledComponent
							{
								Kind = kind,
								DisplayName = name.Trim(),
								Version = (entry.GetValue("DisplayVersion") as string)?.Trim(),
								Directory = (entry.GetValue("InstallLocation") as string)?.Trim().Trim('"'),
								ProductCode = productCode,
								MachineWide = hive == RegistryHive.LocalMachine
							});
						}
					}
				}
				catch (Exception ex)
				{
					AppLog.Warning("读取组件卸载项失败（" + hive + "/" + view + "）：" + ex.Message);
				}
			}
		}
		return matches.OrderByDescending(item => Version.TryParse(item.Version, out Version version) ? version : new Version()).ToList();
	}

	/// <summary>从注册表卸载项里取 MySQL 相关产品的安装版本（用于展示，失败返回空）。</summary>
	public static string ReadInstalledVersionFromRegistry(string kind)
	{
		string match = (kind == KindWorkbench) ? "MySQL Workbench" : "MySQL Server";
		RegistryView[] views = new RegistryView[2] { RegistryView.Registry64, RegistryView.Registry32 };
		RegistryHive[] hives = new RegistryHive[2] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
		foreach (RegistryHive hive in hives)
		{
			foreach (RegistryView view in views)
			{
				try
				{
					using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
					using RegistryKey uninstall = baseKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
					if (uninstall == null)
					{
						continue;
					}
					foreach (string name in uninstall.GetSubKeyNames())
					{
						using RegistryKey entry = uninstall.OpenSubKey(name);
						string displayName = entry?.GetValue("DisplayName") as string;
						if (string.IsNullOrWhiteSpace(displayName) || displayName.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0)
						{
							continue;
						}
						string version = entry.GetValue("DisplayVersion") as string;
						if (!string.IsNullOrWhiteSpace(version))
						{
							return version.Trim();
						}
					}
				}
				catch
				{
				}
			}
		}
		return string.Empty;
	}

	private static string AppVersion
	{
		get
		{
			try
			{
				string text = System.Windows.Forms.Application.ProductVersion;
				int index = text.LastIndexOf('.');
				return (index > 0) ? text.Substring(0, index) : text;
			}
			catch
			{
				return "1.7";
			}
		}
	}
}
