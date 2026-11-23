using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MySqlServiceController;

internal static class AppLog
{
	private static readonly object SyncRoot = new object();

	public static string AppDataDirectory
	{
		get
		{
#if DEBUG
			string environmentVariable = Environment.GetEnvironmentVariable("MYSQLSC_DATA_DIR");
			if (!string.IsNullOrWhiteSpace(environmentVariable))
			{
				return Path.GetFullPath(environmentVariable);
			}
#endif
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
		}
	}

	/// <summary>配置与日志的存放目录名（1.6.2 起）。旧名 MySQLServiceController 会在启动时自动迁移。</summary>
	public const string DataFolderName = "MySQLManager";

	/// <summary>1.6.1 及之前使用的目录名，仅用于一次性迁移。</summary>
	public const string LegacyDataFolderName = "MySQLServiceController";

	/// <summary>
	/// 旧版本的数据目录迁移：把 config.json 与日志复制到新目录。
	/// 新目录已有配置时不动；迁移失败不影响启动（后续照常重建默认配置）。
	/// </summary>
	public static void MigrateLegacyDataDirectory()
	{
		try
		{
			string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyDataFolderName);
			if (!Directory.Exists(legacy))
			{
				return;
			}
			string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
			if (File.Exists(Path.Combine(target, "config.json")))
			{
				return;
			}
			Directory.CreateDirectory(target);
			int copied = 0;
			foreach (string file in Directory.GetFiles(legacy, "*.json"))
			{
				File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
				copied++;
			}
			string legacyLogs = Path.Combine(legacy, "logs");
			if (Directory.Exists(legacyLogs))
			{
				Directory.CreateDirectory(LogDirectory);
				foreach (string file in Directory.GetFiles(legacyLogs))
				{
					File.Copy(file, Path.Combine(LogDirectory, Path.GetFileName(file)), overwrite: true);
					copied++;
				}
			}
			Info("已把旧目录 " + LegacyDataFolderName + " 的配置与日志迁移到 " + DataFolderName + "（" + copied + " 个文件）。");
		}
		catch (Exception exception)
		{
			Error("迁移旧数据目录失败，将使用新目录继续。", exception);
		}
	}

	public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");

	public static string LogPath => Path.Combine(LogDirectory, "app.log");

	public static void Info(string message)
	{
		Write("INFO", message, null);
	}

	public static void Warning(string message)
	{
		Write("WARN", message, null);
	}

	public static void Error(string message, Exception exception)
	{
		Write("ERROR", message, exception);
	}

	private static void Write(string level, string message, Exception exception)
	{
		try
		{
			lock (SyncRoot)
			{
				Directory.CreateDirectory(LogDirectory);
				RotateIfNeeded();
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
				stringBuilder.Append(" [");
				stringBuilder.Append(level);
				stringBuilder.Append("] ");
				stringBuilder.Append(message);
				if (exception != null)
				{
					stringBuilder.AppendLine();
					stringBuilder.Append(exception);
				}
				stringBuilder.AppendLine();
				File.AppendAllText(LogPath, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			}
		}
		catch
		{
		}
	}

	private static void RotateIfNeeded()
	{
		FileInfo fileInfo = new FileInfo(LogPath);
		if (fileInfo.Exists && fileInfo.Length >= 524288)
		{
			string text = Path.Combine(LogDirectory, "app.old.log");
			if (File.Exists(text))
			{
				File.Delete(text);
			}
			File.Move(LogPath, text);
		}
	}
}
