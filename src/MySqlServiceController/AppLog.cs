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
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MySQLServiceController");
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
