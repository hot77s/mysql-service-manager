using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MySqlServiceController;

/// <summary>版本号工具：统一 3 段显示，比较时补齐 4 段。</summary>
internal static class UpdateVersion
{
	/// <summary>当前程序版本，例如 1.6.0。</summary>
	public static string CurrentText
	{
		get
		{
			Version version = Current;
			return version.Major + "." + version.Minor + "." + Math.Max(0, version.Build);
		}
	}

	public static Version Current
	{
		get
		{
			Version parsed;
			if (Version.TryParse(Application.ProductVersion ?? string.Empty, out parsed))
			{
				return Normalize(parsed);
			}
			return new Version(1, 0, 0, 0);
		}
	}

	/// <summary>把 "v1.6.1"、"1.6"、"1.6.0.0" 统一成 4 段版本号，解析失败返回 null。</summary>
	public static Version Parse(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		Match match = Regex.Match(text.Trim(), "\\d+(?:\\.\\d+){0,3}");
		if (!match.Success)
		{
			return null;
		}
		Version parsed;
		if (!Version.TryParse(match.Value, out parsed))
		{
			return null;
		}
		return Normalize(parsed);
	}

	public static bool IsNewer(string candidate, string baseline)
	{
		Version left = Parse(candidate);
		Version right = Parse(baseline);
		if (left == null || right == null)
		{
			return false;
		}
		return left > right;
	}

	private static Version Normalize(Version version)
	{
		return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
	}
}

/// <summary>一次自更新检查的结果。</summary>
internal sealed class AppUpdateInfo
{
	public bool CheckSucceeded { get; set; }

	public string CurrentVersion { get; set; } = UpdateVersion.CurrentText;

	public string LatestVersion { get; set; } = string.Empty;

	public string ReleaseNotes { get; set; } = string.Empty;

	public string ReleasePageUrl { get; set; } = AppUpdateChecker.ReleasePageUrl;

	public string DownloadUrl { get; set; } = string.Empty;

	public long DownloadSize { get; set; }

	public string PublishedText { get; set; } = string.Empty;

	public string ErrorMessage { get; set; } = string.Empty;

	/// <summary>是否通过网页兜底渠道取得（此时安装包地址按命名规则拼出）。</summary>
	public bool FromWebFallback { get; set; }

	public bool HasUpdate => CheckSucceeded && UpdateVersion.IsNewer(LatestVersion, CurrentVersion);

	public bool CanInstall => HasUpdate && !string.IsNullOrWhiteSpace(DownloadUrl);
}

[DataContract]
internal sealed class GitHubAsset
{
	[DataMember(Name = "name")]
	public string Name { get; set; }

	[DataMember(Name = "browser_download_url")]
	public string DownloadUrl { get; set; }

	[DataMember(Name = "size")]
	public long Size { get; set; }
}

[DataContract]
internal sealed class GitHubRelease
{
	[DataMember(Name = "tag_name")]
	public string TagName { get; set; }

	[DataMember(Name = "name")]
	public string Name { get; set; }

	[DataMember(Name = "body")]
	public string Body { get; set; }

	[DataMember(Name = "draft")]
	public bool Draft { get; set; }

	[DataMember(Name = "prerelease")]
	public bool Prerelease { get; set; }

	[DataMember(Name = "published_at")]
	public string PublishedAt { get; set; }

	[DataMember(Name = "html_url")]
	public string HtmlUrl { get; set; }

	[DataMember(Name = "assets")]
	public List<GitHubAsset> Assets { get; set; }
}

/// <summary>MySQL管家 自身的更新检查、安装包下载与安装启动。</summary>
internal static class AppUpdateChecker
{
	public const string Owner = "hot77s";

	public const string Repository = "mysql-service-manager";

	public const string ReleasesApiUrl = "https://api.github.com/repos/hot77s/mysql-service-manager/releases?per_page=10";

	public const string LatestReleaseWebUrl = "https://github.com/hot77s/mysql-service-manager/releases/latest";

	public static string ReleasePageUrl => "https://github.com/" + Owner + "/" + Repository + "/releases";

	private const string UninstallKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MySQLManager";

	private const string SetupSuffix = "-Setup.exe";

	/// <summary>检查是否有新版本。优先 GitHub API，失败时退回发布页兜底。</summary>
	public static async Task<AppUpdateInfo> CheckAsync()
	{
		AppUpdateInfo info = new AppUpdateInfo();
		ConfigureNetwork();
		string apiError = null;
		GitHubRelease release = null;
		try
		{
			release = await FetchLatestReleaseAsync();
		}
		catch (Exception ex)
		{
			apiError = Describe(ex);
			AppLog.Warning("从 GitHub API 检查更新失败：" + ex.Message);
		}
		if (release == null)
		{
			try
			{
				release = await FetchLatestFromWebAsync();
			}
			catch (Exception ex2)
			{
				AppLog.Warning("从发布页检查更新失败：" + ex2.Message);
			}
		}
		if (release == null)
		{
			info.ErrorMessage = apiError ?? "暂时无法连接更新服务器，请检查网络后重试。";
			return info;
		}
		string latest = (release.TagName ?? string.Empty).Trim();
		Version parsed = UpdateVersion.Parse(latest);
		if (parsed == null)
		{
			info.ErrorMessage = "更新信息格式异常，无法识别版本号。";
			return info;
		}
		info.LatestVersion = latest.TrimStart('v', 'V');
		info.ReleaseNotes = SimplifyMarkdown(release.Body);
		info.ReleasePageUrl = (string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasePageUrl : release.HtmlUrl);
		info.PublishedText = FormatPublished(release.PublishedAt);
		GitHubAsset asset = FindInstallerAsset(release.Assets, info.LatestVersion);
		if (asset != null)
		{
			info.DownloadUrl = asset.DownloadUrl ?? string.Empty;
			info.DownloadSize = asset.Size;
		}
		else
		{
			// 网页兜底或发布里只有压缩包：按固定命名规则拼出安装包地址。
			info.DownloadUrl = BuildInstallerUrl(info.LatestVersion);
			info.FromWebFallback = true;
		}
		info.CheckSucceeded = true;
		return info;
	}

	/// <summary>下载安装包到临时目录，回调参数依次为百分比、已下载字节、总字节。</summary>
	public static async Task<string> DownloadAsync(AppUpdateInfo info, Action<int, long, long> report)
	{
		if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
		{
			throw new InvalidOperationException("没有可用的下载地址。");
		}
		ConfigureNetwork();
		string directory = UpdateCacheDirectory;
		Directory.CreateDirectory(directory);
		string target = Path.Combine(directory, "MySQL-Manager-" + info.LatestVersion + SetupSuffix);
		if (IsUsableInstaller(target, info.DownloadSize))
		{
			report(100, new FileInfo(target).Length, new FileInfo(target).Length);
			AppLog.Info("复用已下载的更新安装包：" + target);
			return target;
		}
		SafeDelete(target);
		string temporary = target + ".part";
		SafeDelete(temporary);
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(info.DownloadUrl);
		request.UserAgent = "MySQLManager/" + UpdateVersion.CurrentText;
		request.Accept = "application/octet-stream";
		request.Timeout = 30000;
		request.ReadWriteTimeout = 60000;
		request.AllowAutoRedirect = true;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using (WebResponse response = await request.GetResponseAsync())
		{
			long total = response.ContentLength;
			using Stream remote = response.GetResponseStream();
			using FileStream local = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
			byte[] buffer = new byte[81920];
			long received = 0L;
			int lastPercent = -1;
			int read;
			while ((read = await remote.ReadAsync(buffer, 0, buffer.Length)) > 0)
			{
				await local.WriteAsync(buffer, 0, read);
				received += read;
				if (total > 0)
				{
					int percent = (int)Math.Min(100L, received * 100L / total);
					if (percent != lastPercent)
					{
						lastPercent = percent;
						report(percent, received, total);
					}
				}
			}
		}
		if (!HasExecutableHeader(temporary))
		{
			SafeDelete(temporary);
			throw new InvalidOperationException("下载的文件不是有效的安装程序，可能被网络代理拦截。");
		}
		File.Move(temporary, target);
		long size = new FileInfo(target).Length;
		if (info.DownloadSize > 0 && size != info.DownloadSize)
		{
			AppLog.Warning("更新安装包大小与发布信息不一致（" + size + " / " + info.DownloadSize + "）。");
		}
		report(100, size, size);
		AppLog.Info("更新安装包已下载：" + target);
		return target;
	}

	/// <summary>启动安装程序：静默覆盖安装到原位置，装完自动重新打开；需要时请求管理员授权。</summary>
	public static bool TryLaunchInstaller(string installerPath, out string error)
	{
		error = null;
		if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
		{
			error = "安装包不存在，请重新下载。";
			return false;
		}
		bool machine;
		string directory = ResolveInstallDirectory(out machine);
		string arguments = "--silent-install --install-dir \"" + directory + "\" --scope " + (machine ? "machine" : "user") + " --wait-for-pid " + Process.GetCurrentProcess().Id;
		ProcessStartInfo startInfo = new ProcessStartInfo(installerPath)
		{
			Arguments = arguments,
			UseShellExecute = true,
			WindowStyle = ProcessWindowStyle.Normal,
			WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory
		};
		if (machine)
		{
			startInfo.Verb = "runas";
		}
		try
		{
			Process.Start(startInfo);
			AppLog.Info("已启动更新安装程序：" + installerPath + " " + arguments);
			return true;
		}
		catch (Win32Exception ex)
		{
			if (ex.NativeErrorCode == 1223)
			{
				error = "已取消管理员授权，更新没有开始。";
				AppLog.Warning("用户取消了更新安装的 UAC 授权。");
				return false;
			}
			AppLog.Error("启动更新安装程序失败。", ex);
			error = "无法启动安装程序：" + ex.Message;
			return false;
		}
		catch (Exception ex2)
		{
			AppLog.Error("启动更新安装程序失败。", ex2);
			error = "无法启动安装程序：" + ex2.Message;
			return false;
		}
	}

	/// <summary>更新会安装到的实际目录，用于在界面上提前告知。</summary>
	public static string ResolveInstallDirectory(out bool machine)
	{
		machine = false;
		RegistryKey[] roots = new RegistryKey[2]
		{
			RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
			RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
		};
		try
		{
			RegistryKey[] array = roots;
			foreach (RegistryKey root in array)
			{
				try
				{
					using RegistryKey key = root.OpenSubKey(UninstallKeyPath, writable: false);
					string location = key?.GetValue("InstallLocation") as string;
					if (!string.IsNullOrWhiteSpace(location) && File.Exists(Path.Combine(location, "MySQLManager.exe")))
					{
						machine = string.Equals(key.GetValue("InstallScope") as string, "machine", StringComparison.OrdinalIgnoreCase);
						return Path.GetFullPath(location);
					}
				}
				catch
				{
				}
			}
		}
		finally
		{
			RegistryKey[] array2 = roots;
			foreach (RegistryKey root2 in array2)
			{
				root2?.Dispose();
			}
		}
		// 便携运行（没有安装记录）：装到当前程序所在目录；目录不可写时按“所有用户”处理。
		string current = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
		machine = !CanWriteDirectory(current);
		return current;
	}

	public static void SafeDelete(string path)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static string UpdateCacheDirectory => Path.Combine(Path.GetTempPath(), "MySQLManagerUpdate");

	public static string FormatSize(long bytes)
	{
		if (bytes >= 1073741824L)
		{
			return (bytes / 1073741824.0).ToString("0.0") + " GB";
		}
		if (bytes >= 1048576L)
		{
			return (bytes / 1048576.0).ToString("0.0") + " MB";
		}
		if (bytes >= 1024L)
		{
			return (bytes / 1024.0).ToString("0.0") + " KB";
		}
		return bytes + " B";
	}

	private static bool IsUsableInstaller(string path, long expectedSize)
	{
		try
		{
			if (!File.Exists(path))
			{
				return false;
			}
			long length = new FileInfo(path).Length;
			if (length < 65536L)
			{
				return false;
			}
			if (expectedSize > 0 && length != expectedSize)
			{
				return false;
			}
			return HasExecutableHeader(path);
		}
		catch
		{
			return false;
		}
	}

	private static bool HasExecutableHeader(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			int first = stream.ReadByte();
			int second = stream.ReadByte();
			return first == 'M' && second == 'Z';
		}
		catch
		{
			return false;
		}
	}

	private static bool CanWriteDirectory(string directory)
	{
		try
		{
			string probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
			using (File.Create(probe, 1, FileOptions.DeleteOnClose))
			{
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void ConfigureNetwork()
	{
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		catch
		{
		}
		ServicePointManager.Expect100Continue = false;
	}

	private static async Task<GitHubRelease> FetchLatestReleaseAsync()
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ReleasesApiUrl);
		request.UserAgent = "MySQLManager/" + UpdateVersion.CurrentText;
		request.Accept = "application/vnd.github+json";
		request.Timeout = 12000;
		request.ReadWriteTimeout = 12000;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		string json;
		using (WebResponse response = await request.GetResponseAsync())
		{
			using Stream stream = response.GetResponseStream();
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
			json = await reader.ReadToEndAsync();
		}
		GitHubRelease best = null;
		foreach (GitHubRelease release in DeserializeReleases(json))
		{
			if (release.Draft || release.Prerelease)
			{
				continue;
			}
			Version version = UpdateVersion.Parse(release.TagName);
			if (version == null)
			{
				continue;
			}
			if (best == null || version > UpdateVersion.Parse(best.TagName))
			{
				best = release;
			}
		}
		return best;
	}

	private static List<GitHubRelease> DeserializeReleases(string json)
	{
		try
		{
			DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<GitHubRelease>));
			using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
			if (serializer.ReadObject(stream) is List<GitHubRelease> releases)
			{
				return releases;
			}
		}
		catch (Exception ex)
		{
			AppLog.Warning("解析更新信息失败：" + ex.Message);
		}
		return new List<GitHubRelease>();
	}

	/// <summary>备用渠道：访问 releases/latest，让它 302 到具体 tag，避免 API 限流。</summary>
	private static async Task<GitHubRelease> FetchLatestFromWebAsync()
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LatestReleaseWebUrl);
		request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MySQLManager/" + UpdateVersion.CurrentText;
		request.Timeout = 12000;
		request.ReadWriteTimeout = 12000;
		request.AllowAutoRedirect = true;
		using WebResponse response = await request.GetResponseAsync();
		string finalUrl = response.ResponseUri?.AbsoluteUri ?? string.Empty;
		string tag = ExtractTag(finalUrl);
		if (string.IsNullOrWhiteSpace(tag))
		{
			using Stream stream = response.GetResponseStream();
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
			string html = await reader.ReadToEndAsync();
			Match match = Regex.Match(html, "/releases/tag/(v?\\d+(?:\\.\\d+){1,3})");
			if (match.Success)
			{
				tag = match.Groups[1].Value;
			}
		}
		if (string.IsNullOrWhiteSpace(tag))
		{
			throw new InvalidOperationException("未能从发布页识别最新版本。");
		}
		string version = tag.TrimStart('v', 'V');
		return new GitHubRelease
		{
			TagName = tag,
			Name = "MySQL管家 v" + version,
			HtmlUrl = "https://github.com/" + Owner + "/" + Repository + "/releases/tag/" + tag,
			Assets = new List<GitHubAsset>(),
			Body = string.Empty
		};
	}

	private static string ExtractTag(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return string.Empty;
		}
		Match match = Regex.Match(url, "/releases/tag/([^/?#]+)");
		return match.Success ? match.Groups[1].Value : string.Empty;
	}

	private static GitHubAsset FindInstallerAsset(List<GitHubAsset> assets, string version)
	{
		if (assets == null || assets.Count == 0)
		{
			return null;
		}
		GitHubAsset fallback = null;
		foreach (GitHubAsset asset in assets)
		{
			string name = asset?.Name ?? string.Empty;
			if (string.IsNullOrWhiteSpace(asset?.DownloadUrl))
			{
				continue;
			}
			if (name.EndsWith(SetupSuffix, StringComparison.OrdinalIgnoreCase))
			{
				if (name.IndexOf(version, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return asset;
				}
				if (fallback == null)
				{
					fallback = asset;
				}
			}
		}
		return fallback;
	}

	private static string BuildInstallerUrl(string version)
	{
		return "https://github.com/" + Owner + "/" + Repository + "/releases/download/v" + version + "/MySQL-Manager-" + version + SetupSuffix;
	}

	private static string FormatPublished(string publishedAt)
	{
		DateTime parsed;
		if (DateTime.TryParse(publishedAt, out parsed))
		{
			return parsed.ToLocalTime().ToString("yyyy-MM-dd");
		}
		return string.Empty;
	}

	private static string Describe(Exception ex)
	{
		if (ex is WebException web)
		{
			HttpWebResponse response = web.Response as HttpWebResponse;
			if (response != null)
			{
				int code = (int)response.StatusCode;
				if (code == 403 || code == 429)
				{
					return "更新接口访问过于频繁（GitHub 每小时只允许少量匿名请求），请稍后重试。";
				}
				if (code == 404)
				{
					return "没有找到发布信息，仓库或版本可能已变更。";
				}
				return "更新服务器返回错误（HTTP " + code + "）。";
			}
			if (web.Status == WebExceptionStatus.Timeout)
			{
				return "连接更新服务器超时，请检查网络后重试。";
			}
			if (web.Status == WebExceptionStatus.NameResolutionFailure || web.Status == WebExceptionStatus.ConnectFailure)
			{
				return "无法连接更新服务器，请检查网络或代理设置。";
			}
			return "网络请求失败：" + web.Message;
		}
		return "检查更新失败：" + ex.Message;
	}

	/// <summary>去掉 Markdown 记号，让发布说明在纯文本框中好读。</summary>
	private static string SimplifyMarkdown(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return string.Empty;
		}
		string text = body.Replace("\r\n", "\n").Replace("**", string.Empty).Replace("`", string.Empty);
		string[] lines = text.Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].TrimEnd();
			string trimmed = line.TrimStart();
			if (trimmed.StartsWith("#", StringComparison.Ordinal))
			{
				line = trimmed.TrimStart('#').Trim();
			}
			else if (trimmed.StartsWith(">", StringComparison.Ordinal))
			{
				line = trimmed.TrimStart('>').Trim();
			}
			if (Regex.IsMatch(line, "^[-*]\\s+"))
			{
				line = "· " + line.Substring(2).Trim();
			}
			lines[i] = line;
		}
		// WinForms 文本框只认 CRLF，统一转换后换行才会正常显示。
		string result = string.Join("\r\n", lines).Trim();
		result = Regex.Replace(result, "(\r\n){3,}", "\r\n\r\n");
		if (result.Length > 2200)
		{
			result = result.Substring(0, 2200).TrimEnd() + "\r\n…";
		}
		return result;
	}
}
