using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MySqlServiceController;

internal sealed class WorkbenchUpdateInfo
{
	public bool CheckSucceeded { get; set; }

	public string InstalledVersion { get; set; } = string.Empty;

	public string LatestVersion { get; set; } = string.Empty;

	public string ErrorMessage { get; set; } = string.Empty;

	public bool HasUpdate
	{
		get
		{
			if (!CheckSucceeded || string.IsNullOrWhiteSpace(InstalledVersion) || string.IsNullOrWhiteSpace(LatestVersion))
			{
				return false;
			}
			Version installed;
			Version latest;
			if (Version.TryParse(InstalledVersion, out installed) && Version.TryParse(LatestVersion, out latest))
			{
				return latest > installed;
			}
			return false;
		}
	}
}

internal static class WorkbenchUpdateChecker
{
	public const string DownloadUrl = "https://dev.mysql.com/downloads/workbench/";

	/// <summary>官方发布说明索引页：列出所有 Workbench 版本与发行日期，不受 GitHub 限流影响。</summary>
	public const string ReleaseNotesUrl = "https://dev.mysql.com/doc/relnotes/workbench/en/";

	/// <summary>备用来源：winget 清单（GitHub API，未登录时限流很紧，只作兜底）。</summary>
	public const string VersionApiUrl = "https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/o/Oracle/MySQLWorkbench?ref=master";

	public static string GetInstalledVersion(string executablePath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
			{
				return string.Empty;
			}
			FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
			string text = versionInfo.FileVersion ?? versionInfo.ProductVersion;
			if (string.IsNullOrWhiteSpace(text))
			{
				return string.Empty;
			}
			Match match = Regex.Match(text, "\\d+(?:\\.\\d+){1,3}");
			return match.Success ? ComponentCatalog.FormatVersion(match.Value) : text.Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	public static async Task<WorkbenchUpdateInfo> CheckAsync(string executablePath)
	{
		WorkbenchUpdateInfo info = new WorkbenchUpdateInfo
		{
			InstalledVersion = GetInstalledVersion(executablePath)
		};
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		string latest = string.Empty;
		try
		{
			latest = await FetchLatestFromReleaseNotesAsync();
		}
		catch (Exception ex)
		{
			AppLog.Error("从官方发布说明获取 Workbench 版本失败，改用备用来源。", ex);
		}
		if (string.IsNullOrEmpty(latest))
		{
			try
			{
				latest = await FetchLatestFromWingetAsync();
			}
			catch (Exception ex2)
			{
				AppLog.Error("从 winget 清单获取 Workbench 版本失败。", ex2);
			}
		}
		if (string.IsNullOrEmpty(latest))
		{
			info.ErrorMessage = "暂时无法获取最新版本（网络受限或接口限流）。";
			return info;
		}
		info.LatestVersion = latest;
		info.CheckSucceeded = true;
		return info;
	}

	/// <summary>解析官方发布说明索引页，取已发行的最高版本（跳过“Not yet released”）。</summary>
	internal static string ParseLatestFromReleaseNotes(string html)
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			return string.Empty;
		}
		Version best = null;
		string bestText = null;
		foreach (Match match in Regex.Matches(html, "Changes in MySQL Workbench\\s+(\\d+(?:\\.\\d+){1,3})\\s*\\(([^)]*)\\)"))
		{
			if (match.Groups[2].Value.IndexOf("Not yet released", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				continue;
			}
			if (Version.TryParse(match.Groups[1].Value, out Version parsed) && (best == null || parsed > best))
			{
				best = parsed;
				bestText = match.Groups[1].Value;
			}
		}
		return bestText ?? string.Empty;
	}

	private static async Task<string> FetchLatestFromReleaseNotesAsync()
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ReleaseNotesUrl);
		request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MySQLManager/1.6";
		request.Timeout = 10000;
		request.ReadWriteTimeout = 10000;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using WebResponse response = await request.GetResponseAsync();
		using Stream stream = response.GetResponseStream();
		using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
		string html = await reader.ReadToEndAsync();
		return ParseLatestFromReleaseNotes(html);
	}

	private static async Task<string> FetchLatestFromWingetAsync()
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(VersionApiUrl);
		request.UserAgent = "MySQLManager/1.6";
		request.Accept = "application/vnd.github+json";
		request.Timeout = 8000;
		request.ReadWriteTimeout = 8000;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using WebResponse response = await request.GetResponseAsync();
		using Stream stream = response.GetResponseStream();
		using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
		string json = await reader.ReadToEndAsync();
		Version best = null;
		string bestText = null;
		foreach (Match match in Regex.Matches(json, "\"name\"\\s*:\\s*\"(\\d+(?:\\.\\d+){1,3})\""))
		{
			if (Version.TryParse(match.Groups[1].Value, out Version parsed) && (best == null || parsed > best))
			{
				best = parsed;
				bestText = match.Groups[1].Value;
			}
		}
		return bestText ?? string.Empty;
	}
}
