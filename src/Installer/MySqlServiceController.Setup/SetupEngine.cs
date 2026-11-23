using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MySqlServiceController.Setup;

internal enum InstallScope
{
	CurrentUser,
	AllUsers
}

/// <summary>产品级常量，主程序与安装器共用同一套名称。</summary>
internal static class SetupInfo
{
	public const string ProductName = "MySQL管家";

	public const string FolderName = "MySQL Manager";

	public const string AppVersion = "1.6.2";

	public const string Publisher = "77's";

	public const string MainExecutableName = "MySQLManager.exe";

	public const string UninstallKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MySQLManager";

	public const string LegacyUninstallKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MySQLServiceController";

	/// <summary>1.6.2 起的用户数据目录名。</summary>
	public const string DataFolderName = "MySQLManager";

	/// <summary>1.6.1 及之前的用户数据目录名，卸载时一并清理。</summary>
	public const string LegacyDataFolderName = "MySQLServiceController";

	public const string ShortcutName = "MySQL管家.lnk";

	public const string WorkRootName = "MySQLManagerSetup";

	/// <summary>64 位 Program Files 目录（安装器是 32 位进程，需绕开文件系统重定向）。</summary>
	public static string ProgramFilesDirectory
	{
		get
		{
			string programW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
			if (!string.IsNullOrWhiteSpace(programW6432) && Directory.Exists(programW6432))
			{
				return programW6432;
			}
			return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		}
	}

	public static string DefaultAppDirectory(InstallScope scope)
	{
		string baseDir = ((scope == InstallScope.AllUsers) ? ProgramFilesDirectory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
		return Path.Combine(baseDir, FolderName);
	}

	public static string UserDataDirectory
	{
		get
		{
			string environmentVariable = Environment.GetEnvironmentVariable("MYSQLSC_DATA_DIR");
			if (!string.IsNullOrWhiteSpace(environmentVariable))
			{
				return Path.GetFullPath(environmentVariable);
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
		}
	}

	public static string WorkRoot => Path.Combine(Path.GetTempPath(), WorkRootName);
}

/// <summary>安装任务：既用于进程内直接执行，也用于提权子进程。</summary>
[DataContract]
internal sealed class SetupJob
{
	[DataMember(Order = 1)]
	public bool Uninstall { get; set; }

	[DataMember(Order = 2)]
	public string Scope { get; set; }

	[DataMember(Order = 3)]
	public string AppDirectory { get; set; }

	[DataMember(Order = 4)]
	public bool DesktopShortcut { get; set; }

	[DataMember(Order = 5)]
	public bool Launch { get; set; }

	public SetupJob()
	{
		Scope = "user";
		AppDirectory = string.Empty;
		DesktopShortcut = true;
	}

	public InstallScope InstallScope
	{
		get
		{
			return (string.Equals(Scope, "machine", StringComparison.OrdinalIgnoreCase) ? InstallScope.AllUsers : InstallScope.CurrentUser);
		}
		set
		{
			Scope = ((value == InstallScope.AllUsers) ? "machine" : "user");
		}
	}

	public static SetupJob Load(string path)
	{
		using FileStream stream = File.OpenRead(path);
		DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SetupJob));
		SetupJob job = serializer.ReadObject(stream) as SetupJob;
		if (job == null)
		{
			throw new InvalidOperationException("安装任务文件无效。");
		}
		return job;
	}

	public void Save(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
		DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SetupJob));
		serializer.WriteObject(stream, this);
	}
}

/// <summary>子进程回写的结果。</summary>
[DataContract]
internal sealed class SetupResult
{
	[DataMember(Order = 1)]
	public int Code { get; set; }

	[DataMember(Order = 2)]
	public string Title { get; set; }

	[DataMember(Order = 3)]
	public string Message { get; set; }

	[DataMember(Order = 4)]
	public bool NeedsAttention { get; set; }

	public SetupResult()
	{
		Title = string.Empty;
		Message = string.Empty;
	}

	public static SetupResult Load(string path)
	{
		using FileStream stream = File.OpenRead(path);
		DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SetupResult));
		return serializer.ReadObject(stream) as SetupResult ?? new SetupResult();
	}

	public void Save(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
		DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SetupResult));
		serializer.WriteObject(stream, this);
	}
}

internal interface ISetupProgress
{
	void Report(int percent, string message);
}

internal sealed class DelegateProgress : ISetupProgress
{
	private readonly Action<int, string> _callback;

	public DelegateProgress(Action<int, string> callback)
	{
		_callback = callback;
	}

	public void Report(int percent, string message)
	{
		_callback?.Invoke(percent, message);
	}
}

/// <summary>把进度追加到文件，供父进程轮询显示。</summary>
internal sealed class FileProgress : ISetupProgress
{
	private readonly string _path;

	private readonly object _gate = new object();

	public FileProgress(string path)
	{
		_path = path;
	}

	public void Report(int percent, string message)
	{
		try
		{
			lock (_gate)
			{
				File.AppendAllText(_path, percent + "\t" + (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ") + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
		catch
		{
		}
	}
}

internal static class SetupLog
{
	private static readonly object Gate = new object();

	public static string LogPath
	{
		get
		{
			try
			{
				Directory.CreateDirectory(SetupInfo.WorkRoot);
				return Path.Combine(SetupInfo.WorkRoot, "setup.log");
			}
			catch
			{
				return Path.Combine(Path.GetTempPath(), "mysqlmanager-setup.log");
			}
		}
	}

	public static void Write(string message)
	{
		try
		{
			lock (Gate)
			{
				File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + "\r\n", Encoding.UTF8);
			}
		}
		catch
		{
		}
	}
}

internal static class SetupElevation
{
	public static bool IsAdministrator
	{
		get
		{
			try
			{
				using WindowsIdentity identity = WindowsIdentity.GetCurrent();
				WindowsPrincipal principal = new WindowsPrincipal(identity);
				return principal.IsInRole(WindowsBuiltInRole.Administrator);
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>以管理员身份启动自身执行安装任务，返回子进程（用户取消授权时返回 null）。</summary>
	public static Process StartElevatedJob(string jobPath, string resultPath, string progressPath)
	{
		ProcessStartInfo startInfo = new ProcessStartInfo(Application.ExecutablePath)
		{
			Arguments = "--apply-job \"" + jobPath + "\" --result \"" + resultPath + "\" --progress \"" + progressPath + "\"",
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory
		};
		try
		{
			return Process.Start(startInfo);
		}
		catch
		{
			return null;
		}
	}
}

/// <summary>程序本体的安装、卸载、快捷方式与注册表。</summary>
internal static class SetupInstaller
{
	public static string InstalledExecutable(string appDirectory)
	{
		return Path.Combine(appDirectory, SetupInfo.MainExecutableName);
	}

	public static string UninstallExecutable(string appDirectory)
	{
		return Path.Combine(appDirectory, "Uninstall.exe");
	}

	public static string ShortcutPath(InstallScope scope, bool desktop)
	{
		Environment.SpecialFolder folder = ((scope == InstallScope.AllUsers) ? (desktop ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.CommonPrograms) : (desktop ? Environment.SpecialFolder.DesktopDirectory : Environment.SpecialFolder.Programs));
		return Path.Combine(Environment.GetFolderPath(folder), SetupInfo.ShortcutName);
	}

	public static void Install(SetupJob job, ISetupProgress progress)
	{
		string appDirectory = job.AppDirectory;
		progress.Report(2, "正在创建安装目录…");
		Directory.CreateDirectory(appDirectory);
		progress.Report(8, "正在写入程序文件…");
		ExtractResource("payload.app", InstalledExecutable(appDirectory));
		ExtractResourceIfPresent("payload.config", InstalledExecutable(appDirectory) + ".config");
		ExtractResourceIfPresent("payload.guide", Path.Combine(appDirectory, "使用说明.txt"));
		string executablePath = Application.ExecutablePath;
		string uninstallPath = UninstallExecutable(appDirectory);
		if (!string.Equals(Path.GetFullPath(executablePath), Path.GetFullPath(uninstallPath), StringComparison.OrdinalIgnoreCase))
		{
			progress.Report(14, "正在写入卸载程序…");
			File.Copy(executablePath, uninstallPath, overwrite: true);
		}
		progress.Report(18, "正在清理旧版本文件…");
		RemoveLegacyFiles(appDirectory);
		progress.Report(22, "正在写入卸载信息…");
		WriteUninstallRegistry(job);
		progress.Report(26, "正在创建快捷方式…");
		CreateShortcuts(job);
	}

	public static void Uninstall(SetupJob job, ISetupProgress progress)
	{
		progress.Report(20, "正在移除快捷方式…");
		DeleteShortcut(ShortcutPath(job.InstallScope, desktop: true));
		DeleteShortcut(ShortcutPath(job.InstallScope, desktop: false));
		foreach (InstallScope scope in new InstallScope[2]
		{
			InstallScope.CurrentUser,
			InstallScope.AllUsers
		})
		{
			DeleteLegacyShortcuts(scope);
		}
		progress.Report(40, "正在清理卸载信息…");
		DeleteUninstallRegistry(job.InstallScope);
		DeleteUninstallRegistry(job.InstallScope == InstallScope.AllUsers ? InstallScope.CurrentUser : InstallScope.AllUsers, onlyIfSameDirectory: true, job.AppDirectory);
		progress.Report(60, "正在删除程序文件…");
		ScheduleDirectoryRemoval(job.AppDirectory);
	}

	private static void RemoveLegacyFiles(string appDirectory)
	{
		foreach (string name in new string[2] { "MySQLServiceController.exe", "MySQLServiceController.exe.config" })
		{
			try
			{
				string path = Path.Combine(appDirectory, name);
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (Exception ex)
			{
				SetupLog.Write("删除旧版文件失败 " + name + "：" + ex.Message);
			}
		}
		DeleteLegacyShortcuts(InstallScope.CurrentUser);
		DeleteLegacyShortcuts(InstallScope.AllUsers);
	}

	private static void DeleteLegacyShortcuts(InstallScope scope)
	{
		Environment.SpecialFolder desktop = ((scope == InstallScope.AllUsers) ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory);
		Environment.SpecialFolder programs = ((scope == InstallScope.AllUsers) ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs);
		DeleteShortcut(Path.Combine(Environment.GetFolderPath(desktop), "MySQL 服务管家.lnk"));
		DeleteShortcut(Path.Combine(Environment.GetFolderPath(programs), "MySQL 服务管家.lnk"));
	}

	private static void CreateShortcuts(SetupJob job)
	{
		string target = InstalledExecutable(job.AppDirectory);
		DeleteShortcut(ShortcutPath(job.InstallScope, desktop: false));
		if (CreateShortcut(ShortcutPath(job.InstallScope, desktop: false), target, job.AppDirectory))
		{
			try
			{
				SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
			}
			catch
			{
			}
		}
		if (job.DesktopShortcut)
		{
			CreateShortcut(ShortcutPath(job.InstallScope, desktop: true), target, job.AppDirectory);
		}
		else
		{
			DeleteShortcut(ShortcutPath(job.InstallScope, desktop: true));
		}
	}

	[DllImport("shell32.dll")]
	private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

	public static bool CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
	{
		object shell = null;
		object shortcut = null;
		try
		{
			if (File.Exists(shortcutPath))
			{
				File.Delete(shortcutPath);
			}
		}
		catch
		{
		}
		try
		{
			Type type = Type.GetTypeFromProgID("WScript.Shell");
			if (type == null)
			{
				return false;
			}
			shell = Activator.CreateInstance(type);
			shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[1] { shortcutPath });
			Type shortcutType = shortcut.GetType();
			shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[1] { targetPath });
			shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[1] { workingDirectory });
			shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[1] { targetPath + ",0" });
			shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[1] { "MySQL 服务与 Workbench 管理工具" });
			shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
			return true;
		}
		catch (Exception ex)
		{
			SetupLog.Write("创建快捷方式失败：" + ex.Message);
			return false;
		}
		finally
		{
			if (shortcut != null && Marshal.IsComObject(shortcut))
			{
				Marshal.FinalReleaseComObject(shortcut);
			}
			if (shell != null && Marshal.IsComObject(shell))
			{
				Marshal.FinalReleaseComObject(shell);
			}
		}
	}

	public static void DeleteShortcut(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static RegistryKey OpenUninstallRoot(InstallScope scope, bool writable)
	{
		if (scope == InstallScope.AllUsers)
		{
			return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
		}
		return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
	}

	private static void WriteUninstallRegistry(SetupJob job)
	{
		using RegistryKey root = OpenUninstallRoot(job.InstallScope, writable: true);
		using RegistryKey key = root.CreateSubKey(SetupInfo.UninstallKeyPath);
		if (key == null)
		{
			throw new InvalidOperationException("无法写入“应用和功能”卸载信息。");
		}
		string installed = InstalledExecutable(job.AppDirectory);
		string uninstall = UninstallExecutable(job.AppDirectory);
		FileInfo info = new FileInfo(installed);
		key.SetValue("DisplayName", SetupInfo.ProductName);
		key.SetValue("DisplayVersion", SetupInfo.AppVersion);
		key.SetValue("Publisher", SetupInfo.Publisher);
		key.SetValue("InstallLocation", job.AppDirectory);
		key.SetValue("DisplayIcon", installed + ",0");
		key.SetValue("UninstallString", "\"" + uninstall + "\" --uninstall");
		key.SetValue("QuietUninstallString", "\"" + uninstall + "\" --uninstall --silent-uninstall");
		key.SetValue("InstallScope", (job.InstallScope == InstallScope.AllUsers) ? "machine" : "user");
		key.SetValue("NoModify", 1, RegistryValueKind.DWord);
		key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
		key.SetValue("EstimatedSize", Math.Max(1, (int)(info.Length / 1024)), RegistryValueKind.DWord);
		key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
		try
		{
			using RegistryKey legacy = root.OpenSubKey(SetupInfo.LegacyUninstallKeyPath, writable: true);
			if (legacy != null)
			{
				legacy.Close();
				root.DeleteSubKeyTree(SetupInfo.LegacyUninstallKeyPath, throwOnMissingSubKey: false);
			}
		}
		catch
		{
		}
	}

	private static void DeleteUninstallRegistry(InstallScope scope, bool onlyIfSameDirectory = false, string expectedDirectory = null)
	{
		try
		{
			using RegistryKey root = OpenUninstallRoot(scope, writable: true);
			if (onlyIfSameDirectory)
			{
				using (RegistryKey existing = root.OpenSubKey(SetupInfo.UninstallKeyPath, writable: false))
				{
					string location = existing?.GetValue("InstallLocation") as string;
					if (string.IsNullOrWhiteSpace(location) || !string.Equals(Path.GetFullPath(location).TrimEnd('\\'), Path.GetFullPath(expectedDirectory ?? string.Empty).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
					{
						return;
					}
				}
			}
			root.DeleteSubKeyTree(SetupInfo.UninstallKeyPath, throwOnMissingSubKey: false);
			root.DeleteSubKeyTree(SetupInfo.LegacyUninstallKeyPath, throwOnMissingSubKey: false);
		}
		catch (Exception ex)
		{
			SetupLog.Write("删除卸载信息失败：" + ex.Message);
		}
	}

	/// <summary>读取已安装信息，用于覆盖升级时预填路径。</summary>
	public static bool TryFindInstalled(out string directory, out InstallScope scope)
	{
		directory = null;
		scope = InstallScope.CurrentUser;
		InstallScope[] order = new InstallScope[2]
		{
			InstallScope.CurrentUser,
			InstallScope.AllUsers
		};
		foreach (InstallScope candidate in order)
		{
			foreach (string keyPath in new string[2] { SetupInfo.UninstallKeyPath, SetupInfo.LegacyUninstallKeyPath })
			{
				try
				{
					using RegistryKey root = OpenUninstallRoot(candidate, writable: false);
					using RegistryKey key = root.OpenSubKey(keyPath, writable: false);
					string location = key?.GetValue("InstallLocation") as string;
					if (!string.IsNullOrWhiteSpace(location) && DirectoryHasInstallation(location))
					{
						directory = Path.GetFullPath(location);
						scope = candidate;
						return true;
					}
				}
				catch
				{
				}
			}
		}
		return false;
	}

	public static bool DirectoryHasInstallation(string directory)
	{
		return File.Exists(Path.Combine(directory, SetupInfo.MainExecutableName)) || File.Exists(Path.Combine(directory, "MySQLServiceController.exe"));
	}

	private static void ScheduleDirectoryRemoval(string appDirectory)
	{
		string fullPath = Path.GetFullPath(appDirectory);
		DirectoryInfo info = new DirectoryInfo(fullPath);
		bool testMode = string.Equals(Environment.GetEnvironmentVariable("MYSQLSC_TEST_MODE"), "1", StringComparison.Ordinal);
		if (string.Equals(fullPath.TrimEnd('\\'), Path.GetPathRoot(fullPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || (info.Attributes & FileAttributes.ReparsePoint) != 0 || (!File.Exists(InstalledExecutable(fullPath)) && !File.Exists(UninstallExecutable(fullPath))))
		{
			throw new InvalidOperationException("安装目录校验失败，已取消删除。");
		}
		StringBuilder script = new StringBuilder();
		script.Append("Start-Sleep -Seconds 2; ");
		script.Append("$installDir = ");
		script.Append(ToPowerShellLiteral(fullPath));
		script.Append("; $names = @('MySQLManager.exe','MySQLManager.exe.config','使用说明.txt','Uninstall.exe','MySQLServiceController.exe','MySQLServiceController.exe.config'); ");
		script.Append("foreach ($name in $names) { $path = Join-Path $installDir $name; ");
		script.Append("if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue; } }; ");
		script.Append("Get-ChildItem -LiteralPath $installDir -Force -Filter '*.new' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue; ");
		script.Append("if (-not (Get-ChildItem -LiteralPath $installDir -Force -ErrorAction SilentlyContinue)) { Remove-Item -LiteralPath $installDir -Force -ErrorAction SilentlyContinue; }; ");
		string dataDirectory = Path.GetFullPath(SetupInfo.UserDataDirectory);
		string localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) + Path.DirectorySeparatorChar;
		if (testMode || dataDirectory.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase))
		{
			string[] dataNames = new string[2] { SetupInfo.DataFolderName, SetupInfo.LegacyDataFolderName };
			foreach (string name in dataNames)
			{
				string legacyPath = Path.Combine(Path.GetDirectoryName(dataDirectory) ?? localAppData, name);
				script.Append("$dataDir = ");
				script.Append(ToPowerShellLiteral(legacyPath));
				script.Append("; if (Test-Path -LiteralPath $dataDir) { Remove-Item -LiteralPath $dataDir -Recurse -Force -ErrorAction SilentlyContinue; }; ");
			}
		}
		script.Append("$tempDir = [IO.Path]::GetTempPath(); ");
		script.Append("Get-ChildItem -LiteralPath $tempDir -Filter 'MySQLManager-*.result' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue; Get-ChildItem -LiteralPath $tempDir -Filter 'MySQLServiceController-*.result' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue;");
		string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
		string powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
		ProcessStartInfo startInfo = new ProcessStartInfo(powerShell)
		{
			Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded,
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		};
		Process.Start(startInfo);
	}

	private static string ToPowerShellLiteral(string value)
	{
		return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
	}

	private static void ExtractResource(string resourceName, string destination)
	{
		Assembly assembly = Assembly.GetExecutingAssembly();
		using Stream stream = assembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			throw new InvalidOperationException("安装包缺少资源：" + resourceName);
		}
		string temporary = destination + ".new";
		using (FileStream fileStream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			stream.CopyTo(fileStream);
			fileStream.Flush(flushToDisk: true);
		}
		if (File.Exists(destination))
		{
			File.Replace(temporary, destination, null, ignoreMetadataErrors: true);
		}
		else
		{
			File.Move(temporary, destination);
		}
	}

	private static void ExtractResourceIfPresent(string resourceName, string destination)
	{
		Assembly assembly = Assembly.GetExecutingAssembly();
		using Stream stream = assembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			return;
		}
		ExtractResource(resourceName, destination);
	}
}

/// <summary>串起“程序本体安装 / 卸载”的完整流程。</summary>
internal static class SetupRunner
{
	public static SetupResult Run(SetupJob job, ISetupProgress progress)
	{
		SetupResult result = new SetupResult();
		try
		{
			if (job.Uninstall)
			{
				SetupInstaller.Uninstall(job, progress);
				progress.Report(100, "卸载完成");
				result.Code = 0;
				result.Title = "卸载完成";
				result.Message = "已移除程序文件、快捷方式和程序配置。\nMySQL 服务与数据库文件未作任何更改。";
				return result;
			}
			SetupInstaller.Install(job, progress);
			result.Code = 0;
			result.Title = "安装完成";
			result.Message = "程序位置：" + job.AppDirectory;
			return result;
		}
		catch (Exception ex)
		{
			SetupLog.Write("安装失败：" + ex);
			result.Code = 1;
			result.Title = job.Uninstall ? "卸载失败" : "安装失败";
			result.Message = ex.Message;
			result.NeedsAttention = true;
			return result;
		}
	}
}
