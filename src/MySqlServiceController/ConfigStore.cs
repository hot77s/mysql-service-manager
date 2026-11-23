using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MySqlServiceController;

internal static class ConfigStore
{
	public static string ConfigPath => Path.Combine(AppLog.AppDataDirectory, "config.json");

	public static AppConfig Load(out string warning)
	{
		warning = null;
		AppConfig appConfig = new AppConfig();
		try
		{
			if (!File.Exists(ConfigPath))
			{
				return appConfig;
			}
			DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(AppConfig));
			using FileStream stream = File.OpenRead(ConfigPath);
			if (!(dataContractJsonSerializer.ReadObject((Stream)stream) is AppConfig appConfig2))
			{
				warning = "配置文件内容无效，已使用默认设置。";
				return appConfig;
			}
			appConfig.ServiceName = (appConfig2.ServiceName ?? string.Empty).Trim();
			appConfig.MySqlPath = (appConfig2.MySqlPath ?? string.Empty).Trim();
			appConfig.RefreshSeconds = appConfig2.RefreshSeconds;
			appConfig.WorkbenchPath = (appConfig2.WorkbenchPath ?? string.Empty).Trim();
			appConfig.ThemeMode = (string.IsNullOrWhiteSpace(appConfig2.ThemeMode) ? "system" : appConfig2.ThemeMode.Trim());
			appConfig.WorkbenchLatestVersion = (appConfig2.WorkbenchLatestVersion ?? string.Empty).Trim();
			appConfig.WorkbenchCheckedUtc = (appConfig2.WorkbenchCheckedUtc ?? string.Empty).Trim();
			appConfig.AppLatestVersion = (appConfig2.AppLatestVersion ?? string.Empty).Trim();
			appConfig.AppCheckedUtc = (appConfig2.AppCheckedUtc ?? string.Empty).Trim();
			if (appConfig.RefreshSeconds < 1 || appConfig.RefreshSeconds > 60)
			{
				appConfig.RefreshSeconds = 2;
			}
		}
		catch (Exception exception)
		{
			warning = "无法读取配置文件，已使用默认设置。";
			AppLog.Error("读取配置失败。", exception);
			TryBackupBrokenConfig();
		}
		return appConfig;
	}

	public static void Save(AppConfig config)
	{
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		Directory.CreateDirectory(AppLog.AppDataDirectory);
		DataContractJsonSerializerSettings dataContractJsonSerializerSettings = new DataContractJsonSerializerSettings();
		dataContractJsonSerializerSettings.UseSimpleDictionaryFormat = true;
		DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(AppConfig), dataContractJsonSerializerSettings);
		string text = ConfigPath + ".tmp";
		using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			dataContractJsonSerializer.WriteObject((Stream)fileStream, (object)config);
			fileStream.Flush(flushToDisk: true);
		}
		if (File.Exists(ConfigPath))
		{
			string destinationBackupFileName = ConfigPath + ".bak";
			try
			{
				File.Replace(text, ConfigPath, destinationBackupFileName, ignoreMetadataErrors: true);
				return;
			}
			catch
			{
				File.Copy(text, ConfigPath, overwrite: true);
				File.Delete(text);
				return;
			}
		}
		File.Move(text, ConfigPath);
	}

	public static string GetPrettyJson(AppConfig config)
	{
		DataContractJsonSerializerSettings dataContractJsonSerializerSettings = new DataContractJsonSerializerSettings();
		dataContractJsonSerializerSettings.UseSimpleDictionaryFormat = true;
		DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(AppConfig), dataContractJsonSerializerSettings);
		using MemoryStream memoryStream = new MemoryStream();
		dataContractJsonSerializer.WriteObject((Stream)memoryStream, (object)config);
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private static void TryBackupBrokenConfig()
	{
		try
		{
			if (File.Exists(ConfigPath))
			{
				string destFileName = Path.Combine(AppLog.AppDataDirectory, "config.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
				File.Copy(ConfigPath, destFileName, overwrite: true);
			}
		}
		catch
		{
		}
	}
}
