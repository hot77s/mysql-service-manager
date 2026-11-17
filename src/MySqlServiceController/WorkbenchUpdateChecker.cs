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
			return match.Success ? match.Value : text.Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	public static async Task<WorkbenchUpdateInfo> CheckAsync(string executablePath)
	{
		WorkbenchUpdateInfo workbenchUpdateInfo = new WorkbenchUpdateInfo();
		workbenchUpdateInfo.InstalledVersion = GetInstalledVersion(executablePath);
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(VersionApiUrl);
			httpWebRequest.UserAgent = "MySQLManager/1.1";
			httpWebRequest.Accept = "application/vnd.github+json";
			httpWebRequest.Timeout = 8000;
			httpWebRequest.ReadWriteTimeout = 8000;
			httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			using (WebResponse response = await httpWebRequest.GetResponseAsync())
			using (Stream stream = response.GetResponseStream())
			using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8))
			{
				string text = await streamReader.ReadToEndAsync();
				Version version = null;
				string text2 = null;
				foreach (Match item in Regex.Matches(text, "\"name\"\\s*:\\s*\"(\\d+(?:\\.\\d+){1,3})\""))
				{
					Version result;
					if (Version.TryParse(item.Groups[1].Value, out result) && (version == null || result > version))
					{
						version = result;
						text2 = item.Groups[1].Value;
					}
				}
				if (text2 == null)
				{
					workbenchUpdateInfo.ErrorMessage = "未在版本清单中找到 Workbench 版本号。";
					return workbenchUpdateInfo;
				}
				workbenchUpdateInfo.LatestVersion = text2;
				workbenchUpdateInfo.CheckSucceeded = true;
			}
		}
		catch (Exception ex)
		{
			workbenchUpdateInfo.ErrorMessage = ex.Message;
			AppLog.Error("检查 MySQL Workbench 更新失败。", ex);
		}
		return workbenchUpdateInfo;
	}
}
