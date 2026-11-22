using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MySqlServiceController;

/// <summary>一个待安装的 MySQL 组件。</summary>
[DataContract]
internal sealed class ComponentJob
{
	[DataMember(Order = 1)]
	public string Kind { get; set; }

	[DataMember(Order = 2)]
	public string Version { get; set; }

	[DataMember(Order = 3)]
	public string Url { get; set; }

	[DataMember(Order = 4)]
	public string InstallDirectory { get; set; }

	[DataMember(Order = 5)]
	public bool RegisterService { get; set; }

	[DataMember(Order = 6)]
	public string ServiceName { get; set; }

	[IgnoreDataMember]
	public bool IsServer => string.Equals(Kind, ComponentCatalog.KindServer, StringComparison.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public string DisplayName => ComponentCatalog.DisplayNameOf(Kind);

	[IgnoreDataMember]
	public string FileName
	{
		get
		{
			try
			{
				return Path.GetFileName(new Uri(Url).AbsolutePath);
			}
			catch
			{
				return string.Empty;
			}
		}
	}
}

/// <summary>组件安装结果。</summary>
[DataContract]
internal sealed class ComponentResult
{
	[DataMember(Order = 1)]
	public bool Success { get; set; }

	[DataMember(Order = 2)]
	public bool Cancelled { get; set; }

	[DataMember(Order = 3)]
	public string Message { get; set; }

	[DataMember(Order = 4)]
	public string Detail { get; set; }

	[DataMember(Order = 5)]
	public string InstalledDirectory { get; set; }

	[DataMember(Order = 6)]
	public string ServiceName { get; set; }
}

/// <summary>下载 MSI、静默安装到指定目录，并可选初始化 MySQL 数据目录与服务。</summary>
internal static class ComponentInstaller
{
	private static readonly object PropertyCacheGate = new object();

	private static readonly Dictionary<string, string> PropertyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public static string WorkRoot
	{
		get
		{
			string custom = Environment.GetEnvironmentVariable("MYSQLSC_COMPONENT_DIR");
			return string.IsNullOrWhiteSpace(custom) ? Path.Combine(Path.GetTempPath(), "MySQLManagerComponents") : custom;
		}
	}

	public static bool IsElevated
	{
		get
		{
			try
			{
				using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
				System.Security.Principal.WindowsPrincipal principal = new System.Security.Principal.WindowsPrincipal(identity);
				return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>安装入口：需要管理员时自动以管理员身份重启自身执行。</summary>
	public static ComponentResult Run(ComponentJob job, Action<int, string> report)
	{
		if (IsElevated)
		{
			return Install(job, report);
		}
		return RunElevated(job, report);
	}

	/// <summary>在当前进程（需管理员）执行安装。</summary>
	public static ComponentResult Install(ComponentJob job, Action<int, string> report)
	{
		if (job == null || string.IsNullOrWhiteSpace(job.Url))
		{
			throw new InvalidOperationException("组件信息不完整。");
		}
		if (string.IsNullOrWhiteSpace(job.InstallDirectory))
		{
			throw new InvalidOperationException("请为" + job.DisplayName + "选择安装位置。");
		}
		string msiPath = EnsureDownloaded(job, report);
		report(62, "正在安装 " + job.DisplayName + " " + job.Version + "…");
		Directory.CreateDirectory(job.InstallDirectory);
		string property = ResolveInstallDirectoryProperty(msiPath);
		string logDirectory = Path.Combine(WorkRoot, "logs");
		string logPath = Path.Combine(logDirectory, job.Kind + "-" + job.Version + "-msi.log");
		Directory.CreateDirectory(logDirectory);
		int exitCode = RunMsiInstaller(msiPath, job.InstallDirectory, property, logPath);
		if (exitCode != 0 && exitCode != 3010)
		{
			throw new InvalidOperationException(job.DisplayName + " 安装未完成（msiexec 返回 " + exitCode + "）。\n" + DescribeMsiExitCode(exitCode) + "\n详细日志：" + logPath);
		}
		report(78, job.DisplayName + " 文件已就位，正在校验…");
		string warning = VerifyInstalled(job);
		string serviceName = null;
		if (job.IsServer && job.RegisterService)
		{
			report(84, "正在初始化 MySQL 数据目录…");
			string serviceWarning = InitializeServer(job, report, out serviceName);
			if (!string.IsNullOrWhiteSpace(serviceWarning))
			{
				warning = string.IsNullOrWhiteSpace(warning) ? serviceWarning : (warning + "\n" + serviceWarning);
			}
		}
		try
		{
			CleanupCache(msiPath);
		}
		catch
		{
		}
		report(100, job.DisplayName + " 处理完成");
		return new ComponentResult
		{
			Success = true,
			Message = (string.IsNullOrWhiteSpace(warning) ? (job.DisplayName + " " + job.Version + " 安装完成。") : warning),
			Detail = (string.IsNullOrWhiteSpace(warning) ? null : warning),
			InstalledDirectory = job.InstallDirectory,
			ServiceName = serviceName
		};
	}

	/// <summary>以管理员身份启动自身执行安装任务，并把子进程进度回传给界面。</summary>
	private static ComponentResult RunElevated(ComponentJob job, Action<int, string> report)
	{
		Directory.CreateDirectory(WorkRoot);
		string token = Guid.NewGuid().ToString("N");
		string jobPath = Path.Combine(WorkRoot, "job-" + token + ".json");
		string resultPath = Path.Combine(WorkRoot, "result-" + token + ".json");
		string progressPath = Path.Combine(WorkRoot, "progress-" + token + ".log");
		WriteJson(jobPath, job);
		report(0, "正在请求管理员授权…");
		ProcessStartInfo startInfo = new ProcessStartInfo(Application.ExecutablePath)
		{
			Arguments = "--apply-component \"" + jobPath + "\" --component-result \"" + resultPath + "\" --component-progress \"" + progressPath + "\"",
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory
		};
		Process process;
		try
		{
			process = Process.Start(startInfo);
		}
		catch (Exception ex)
		{
			SafeDelete(jobPath);
			AppLog.Warning("组件安装未获得管理员授权：" + ex.Message);
			return new ComponentResult
			{
				Success = false,
				Cancelled = true,
				Message = "已取消管理员授权，未做任何更改。"
			};
		}
		if (process == null)
		{
			SafeDelete(jobPath);
			return new ComponentResult
			{
				Success = false,
				Cancelled = true,
				Message = "未能启动安装进程。"
			};
		}
		int lastPercent = -1;
		DateTime deadline = DateTime.UtcNow.AddHours(1);
		try
		{
			while (!process.HasExited)
			{
				if (DateTime.UtcNow > deadline)
				{
					try
					{
						process.Kill();
					}
					catch
					{
					}
					return new ComponentResult
					{
						Success = false,
						Message = "安装超时（超过 1 小时），已终止。"
					};
				}
				string line = ReadLastProgress(progressPath);
				if (!string.IsNullOrWhiteSpace(line))
				{
					int tab = line.IndexOf('\t');
					if (tab > 0 && int.TryParse(line.Substring(0, tab), out int percent) && percent != lastPercent)
					{
						lastPercent = percent;
						report(percent, line.Substring(tab + 1));
					}
				}
				Thread.Sleep(250);
			}
			process.WaitForExit();
		}
		finally
		{
			process.Dispose();
		}
		ComponentResult result = ReadResult(resultPath);
		if (result == null)
		{
			result = new ComponentResult
			{
				Success = process.ExitCode == 0,
				Message = ((process.ExitCode == 0) ? "安装完成。" : ("安装进程异常退出（代码 " + process.ExitCode + "）。"))
			};
		}
		SafeDelete(jobPath);
		SafeDelete(resultPath);
		SafeDelete(progressPath);
		return result;
	}

	/// <summary>提权子进程入口：执行任务文件并回写结果，不显示界面。</summary>
	public static bool TryHandleApplyCommand(string[] args)
	{
		string jobPath = GetArgument(args, "--apply-component");
		if (string.IsNullOrWhiteSpace(jobPath))
		{
			return false;
		}
		string resultPath = GetArgument(args, "--component-result");
		string progressPath = GetArgument(args, "--component-progress");
		ComponentResult result;
		try
		{
			ComponentJob job = ReadJson<ComponentJob>(jobPath);
			result = Install(job, delegate(int percent, string message)
			{
				if (!string.IsNullOrWhiteSpace(progressPath))
				{
					try
					{
						File.AppendAllText(progressPath, percent + "\t" + (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ") + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
					}
					catch
					{
					}
				}
			});
		}
		catch (Exception ex)
		{
			AppLog.Error("组件安装失败。", ex);
			result = new ComponentResult
			{
				Success = false,
				Message = ex.Message
			};
		}
		if (!string.IsNullOrWhiteSpace(resultPath))
		{
			try
			{
				WriteJson(resultPath, result);
			}
			catch (Exception ex2)
			{
				AppLog.Error("写入组件安装结果失败。", ex2);
			}
		}
		return true;
	}

	private static string GetArgument(string[] args, string name)
	{
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
			{
				return args[i + 1];
			}
		}
		return null;
	}

	private static string ReadLastProgress(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
			string text = reader.ReadToEnd();
			string[] lines = text.Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			return (lines.Length > 0) ? lines[lines.Length - 1].TrimEnd('\r') : null;
		}
		catch
		{
			return null;
		}
	}

	private static void WriteJson<T>(string path, T value)
	{
		using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
		DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
		serializer.WriteObject(stream, value);
	}

	private static T ReadJson<T>(string path) where T : class
	{
		using FileStream stream = File.OpenRead(path);
		DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
		return serializer.ReadObject(stream) as T;
	}

	private static ComponentResult ReadResult(string path)
	{
		try
		{
			return File.Exists(path) ? ReadJson<ComponentResult>(path) : null;
		}
		catch
		{
			return null;
		}
	}

	private static void SafeDelete(string path)
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

	public static string EnsureDownloaded(ComponentJob job, Action<int, string> report)
	{
		string fileName = job.FileName;
		if (string.IsNullOrWhiteSpace(fileName))
		{
			throw new InvalidOperationException("无法确定安装包文件名。");
		}
		string cacheDirectory = Path.Combine(WorkRoot, "cache");
		Directory.CreateDirectory(cacheDirectory);
		string target = Path.Combine(cacheDirectory, fileName);
		if (File.Exists(target))
		{
			long length = new FileInfo(target).Length;
			if (length > 5 * 1024 * 1024)
			{
				report(40, "已找到本地安装包 " + fileName);
				return target;
			}
			SafeDelete(target);
		}
		string temporary = target + ".part";
		List<string> urls = new List<string>();
		if (!string.IsNullOrWhiteSpace(job.Url))
		{
			urls.Add(job.Url);
		}
		string archiveUrl = ComponentCatalog.BuildArchiveUrl(job.Kind, job.Version);
		if (!urls.Contains(archiveUrl))
		{
			urls.Add(archiveUrl);
		}
		Exception lastError = null;
		foreach (string url in urls)
		{
			report(2, "正在连接下载服务器…");
			try
			{
				DownloadFile(url, temporary, fileName, report);
				lastError = null;
				break;
			}
			catch (WebException ex)
			{
				AppLog.Warning("组件下载地址不可用（" + ex.Status + "），尝试备用地址：" + url);
				lastError = ex;
			}
		}
		if (lastError != null)
		{
			throw new InvalidOperationException("安装包下载失败：" + lastError.Message + "\n\n请确认网络可以访问 cdn.mysql.com 后重试。");
		}
		SafeDelete(target);
		File.Move(temporary, target);
		long size = new FileInfo(target).Length;
		if (size < 5 * 1024 * 1024)
		{
			throw new InvalidOperationException("安装包下载不完整（" + FormatSize(size) + "），请检查网络后重试。");
		}
		report(60, "下载完成（" + FormatSize(size) + "）");
		return target;
	}

	private static void DownloadFile(string url, string temporary, string fileName, Action<int, string> report)
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.UserAgent = "MySQLManager/" + Application.ProductVersion;
		request.Timeout = 30000;
		request.ReadWriteTimeout = 60000;
		request.AllowAutoRedirect = true;
		using WebResponse response = request.GetResponse();
		long total = response.ContentLength;
		using Stream remote = response.GetResponseStream();
		using FileStream local = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
		byte[] buffer = new byte[81920];
		long received = 0L;
		int lastFraction = -1;
		int read;
		while ((read = remote.Read(buffer, 0, buffer.Length)) > 0)
		{
			local.Write(buffer, 0, read);
			received += read;
			if (total <= 0)
			{
				continue;
			}
			int fraction = (int)Math.Min(100L, received * 100L / Math.Max(1L, total));
			if (fraction != lastFraction)
			{
				lastFraction = fraction;
				int percent = (int)Math.Min(58L, received * 58L / Math.Max(1L, total));
				report(percent, "正在下载 " + fileName + "  " + fraction + "%  (" + FormatSize(received) + " / " + FormatSize(total) + ")");
			}
		}
	}

	private static void CleanupCache(string msiPath)
	{
		try
		{
			string cache = Path.GetDirectoryName(msiPath);
			if (!string.IsNullOrWhiteSpace(cache) && Directory.Exists(cache) && string.Equals(Path.GetFileName(cache), "cache", StringComparison.OrdinalIgnoreCase))
			{
				foreach (string file in Directory.GetFiles(cache))
				{
					SafeDelete(file);
				}
			}
		}
		catch
		{
		}
	}

	private static string FormatSize(long bytes)
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

	public static string ResolveInstallDirectoryProperty(string msiPath)
	{
		lock (PropertyCacheGate)
		{
			if (PropertyCache.TryGetValue(msiPath, out string cached))
			{
				return cached;
			}
		}
		string result = "INSTALLDIR";
		try
		{
			Dictionary<string, string> properties = ReadMsiProperties(msiPath);
			string[] candidates = new string[6] { "WIXUI_INSTALLDIR", "INSTALLDIR", "APPLICATIONFOLDER", "INSTALLLOCATION", "INSTALLFOLDER", "TARGETDIR" };
			foreach (string candidate in candidates)
			{
				if (!properties.TryGetValue(candidate, out string value) || string.IsNullOrWhiteSpace(value))
				{
					continue;
				}
				result = (string.Equals(candidate, "WIXUI_INSTALLDIR", StringComparison.OrdinalIgnoreCase) ? value.Trim() : candidate);
				break;
			}
			AppLog.Info("MSI 目录属性：" + result + "（" + Path.GetFileName(msiPath) + "）");
		}
		catch (Exception ex)
		{
			AppLog.Warning("读取 MSI 属性失败，使用 INSTALLDIR：" + ex.Message);
		}
		lock (PropertyCacheGate)
		{
			PropertyCache[msiPath] = result;
		}
		return result;
	}

	[DllImport("msi.dll", CharSet = CharSet.Unicode)]
	private static extern int MsiOpenDatabase(string path, IntPtr persist, out IntPtr database);

	[DllImport("msi.dll", CharSet = CharSet.Unicode)]
	private static extern int MsiDatabaseOpenView(IntPtr database, string query, out IntPtr view);

	[DllImport("msi.dll")]
	private static extern int MsiViewExecute(IntPtr view, IntPtr record);

	[DllImport("msi.dll")]
	private static extern int MsiViewFetch(IntPtr view, out IntPtr record);

	[DllImport("msi.dll", CharSet = CharSet.Unicode)]
	private static extern int MsiRecordGetString(IntPtr record, int field, StringBuilder buffer, ref int length);

	[DllImport("msi.dll")]
	private static extern int MsiCloseHandle(IntPtr handle);

	private static Dictionary<string, string> ReadMsiProperties(string msiPath)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		int code = MsiOpenDatabase(msiPath, IntPtr.Zero, out IntPtr database);
		if (code != 0)
		{
			throw new InvalidOperationException("无法打开安装包（MSI 错误 " + code + "）。");
		}
		try
		{
			code = MsiDatabaseOpenView(database, "SELECT `Property`,`Value` FROM `Property`", out IntPtr view);
			if (code != 0)
			{
				throw new InvalidOperationException("无法读取安装包属性表（MSI 错误 " + code + "）。");
			}
			try
			{
				code = MsiViewExecute(view, IntPtr.Zero);
				if (code != 0)
				{
					throw new InvalidOperationException("无法执行属性查询（MSI 错误 " + code + "）。");
				}
				while (true)
				{
					if (MsiViewFetch(view, out IntPtr record) != 0 || record == IntPtr.Zero)
					{
						break;
					}
					try
					{
						string name = ReadMsiField(record, 1);
						if (!string.IsNullOrWhiteSpace(name))
						{
							result[name] = ReadMsiField(record, 2);
						}
					}
					finally
					{
						MsiCloseHandle(record);
					}
				}
			}
			finally
			{
				MsiCloseHandle(view);
			}
		}
		finally
		{
			MsiCloseHandle(database);
		}
		return result;
	}

	private static string ReadMsiField(IntPtr record, int index)
	{
		StringBuilder buffer = new StringBuilder(512);
		int length = buffer.Capacity;
		return (MsiRecordGetString(record, index, buffer, ref length) == 0) ? buffer.ToString() : string.Empty;
	}

	private static int RunMsiInstaller(string msiPath, string installDirectory, string property, string logPath)
	{
		string arguments = "/i \"" + msiPath + "\" /qn /norestart /l*v \"" + logPath + "\" " + property + "=\"" + installDirectory + "\" REBOOT=ReallySuppress ALLUSERS=1";
		ProcessStartInfo startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), arguments)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = Path.GetTempPath()
		};
		using Process process = Process.Start(startInfo);
		if (process == null)
		{
			throw new InvalidOperationException("无法启动 Windows Installer。");
		}
		if (!process.WaitForExit(45 * 60 * 1000))
		{
			try
			{
				process.Kill();
			}
			catch
			{
			}
			throw new InvalidOperationException("安装超时（超过 45 分钟），已终止。");
		}
		return process.ExitCode;
	}

	private static string DescribeMsiExitCode(int code)
	{
		switch (code)
		{
		case 1602:
			return "已取消安装。";
		case 1603:
			return "安装过程发生严重错误，常见原因是权限不足、已有版本正在安装或磁盘空间不够。";
		case 1618:
			return "另一个安装程序正在运行，请稍后重试。";
		case 1619:
		case 1620:
			return "安装包无法打开，可能下载不完整。";
		case 1625:
			return "安装被系统策略阻止。";
		default:
			return "请在日志中查看详细原因。";
		}
	}

	private static string VerifyInstalled(ComponentJob job)
	{
		try
		{
			if (job.IsServer)
			{
				if (File.Exists(Path.Combine(job.InstallDirectory, "bin", "mysqld.exe")))
				{
					return null;
				}
				string actual = ComponentCatalog.FindInstalledServerDirectory();
				return "未在所选目录找到 mysqld.exe" + (string.IsNullOrWhiteSpace(actual) ? "，MySQL 可能被安装到了其他位置。" : ("，实际安装位置为：" + actual));
			}
			if (Directory.Exists(job.InstallDirectory) && Directory.GetFiles(job.InstallDirectory, "*Workbench*.exe", SearchOption.AllDirectories).Length > 0)
			{
				return null;
			}
			string found = ComponentCatalog.FindInstalledWorkbenchDirectory();
			return "未在所选目录找到 MySQL Workbench" + (string.IsNullOrWhiteSpace(found) ? "，可能被安装到了其他位置。" : ("，实际安装位置为：" + found));
		}
		catch (Exception ex)
		{
			return "校验安装结果时出错：" + ex.Message;
		}
	}

	/// <summary>初始化数据目录、生成 my.ini 并注册 Windows 服务。</summary>
	private static string InitializeServer(ComponentJob job, Action<int, string> report, out string serviceName)
	{
		string basedir = job.InstallDirectory;
		string mysqld = Path.Combine(basedir, "bin", "mysqld.exe");
		serviceName = string.IsNullOrWhiteSpace(job.ServiceName) ? ComponentCatalog.DefaultServiceName(job.Version) : job.ServiceName.Trim();
		if (!File.Exists(mysqld))
		{
			return "未找到 mysqld.exe，已跳过服务初始化。";
		}
		if (ServiceExists(serviceName))
		{
			return "Windows 服务 " + serviceName + " 已存在，已跳过服务初始化，可直接在 MySQL管家中使用该服务。";
		}
		string dataDirectory = Path.Combine(basedir, "data");
		string iniPath = Path.Combine(basedir, "my.ini");
		int port = FindFreePort(3306);
		Directory.CreateDirectory(dataDirectory);
		if (!File.Exists(iniPath))
		{
			File.WriteAllText(iniPath, BuildIni(basedir, dataDirectory, port), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			AppLog.Info("已生成 my.ini：" + iniPath);
		}
		if (Directory.GetFileSystemEntries(dataDirectory).Length == 0)
		{
			report(86, "正在初始化数据目录（可能需要 1-2 分钟）…");
			ProcessResult initResult = RunTool(mysqld, "--defaults-file=\"" + iniPath + "\" --initialize-insecure --console", 600000);
			if (initResult.ExitCode != 0)
			{
				return "数据目录初始化失败（mysqld 返回 " + initResult.ExitCode + "）。\n" + Trim(initResult.Output) + "\n可稍后手动执行 mysqld --initialize-insecure。";
			}
		}
		report(94, "正在注册 Windows 服务 " + serviceName + "…");
		ProcessResult installResult = RunTool(mysqld, "--install " + serviceName + " --defaults-file=\"" + iniPath + "\"", 120000);
		if (installResult.ExitCode != 0)
		{
			return "注册 Windows 服务失败（mysqld --install 返回 " + installResult.ExitCode + "）。\n" + Trim(installResult.Output);
		}
		RunTool(Path.Combine(Environment.SystemDirectory, "sc.exe"), "config " + serviceName + " start= demand", 30000);
		try
		{
			File.WriteAllText(Path.Combine(basedir, "服务信息.txt"), "服务名称：" + serviceName + "\r\n端口：" + port + "\r\n配置文件：" + iniPath + "\r\n数据目录：" + dataDirectory + "\r\n初始 root 密码：空（首次使用后请立即修改）\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
		}
		return null;
	}

	private static string BuildIni(string basedir, string dataDirectory, int port)
	{
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("# 由 MySQL管家生成");
		builder.AppendLine("[mysqld]");
		builder.AppendLine("basedir=\"" + ToUnixPath(basedir) + "\"");
		builder.AppendLine("datadir=\"" + ToUnixPath(dataDirectory) + "\"");
		builder.AppendLine("port=" + port);
		builder.AppendLine("character-set-server=utf8mb4");
		builder.AppendLine();
		builder.AppendLine("[client]");
		builder.AppendLine("port=" + port);
		return builder.ToString();
	}

	private static string ToUnixPath(string path)
	{
		return (path ?? string.Empty).Replace('\\', '/');
	}

	private static bool ServiceExists(string serviceName)
	{
		try
		{
			using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + serviceName);
			return key != null;
		}
		catch
		{
			return false;
		}
	}

	private static int FindFreePort(int preferred)
	{
		for (int port = preferred; port < preferred + 100; port++)
		{
			try
			{
				TcpListener listener = new TcpListener(IPAddress.Loopback, port);
				listener.Start();
				listener.Stop();
				return port;
			}
			catch
			{
			}
		}
		return preferred;
	}

	private sealed class ProcessResult
	{
		public int ExitCode;

		public string Output;
	}

	private static ProcessResult RunTool(string executable, string arguments, int timeout)
	{
		ProcessResult result = new ProcessResult
		{
			ExitCode = -1,
			Output = string.Empty
		};
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo(executable, arguments)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = Path.GetDirectoryName(executable) ?? Path.GetTempPath()
			};
			using Process process = new Process
			{
				StartInfo = startInfo
			};
			StringBuilder output = new StringBuilder();
			process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
			{
				if (e.Data != null)
				{
					output.AppendLine(e.Data);
				}
			};
			process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
			{
				if (e.Data != null)
				{
					output.AppendLine(e.Data);
				}
			};
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			if (!process.WaitForExit(timeout))
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
				result.Output = output.ToString() + "\n（命令超时，已终止）";
				return result;
			}
			process.WaitForExit();
			result.ExitCode = process.ExitCode;
			result.Output = output.ToString();
		}
		catch (Exception ex)
		{
			result.Output = ex.Message;
		}
		return result;
	}

	private static string Trim(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		text = text.Trim();
		return (text.Length > 1200) ? text.Substring(text.Length - 1200) : text;
	}
}
