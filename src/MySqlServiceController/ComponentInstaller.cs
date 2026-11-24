using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

	[DataMember(Order = 7, EmitDefaultValue = false)]
	public bool SetRootPassword { get; set; }

	[DataMember(Order = 8, EmitDefaultValue = false)]
	public Guid UninstallProductCode { get; set; }

	[DataMember(Order = 9, EmitDefaultValue = false)]
	public bool MachineWide { get; set; }

	[IgnoreDataMember]
	public bool IsUninstall => UninstallProductCode != Guid.Empty;

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

	/// <summary>执行组件任务；当前用户的 MSI 卸载保留原用户上下文。</summary>
	public static ComponentResult Run(ComponentJob job, Action<int, string> report, string rootPassword = null)
	{
		if (IsElevated || (job.IsUninstall && !job.MachineWide))
		{
			return Execute(job, report, rootPassword);
		}
		return RunElevated(job, report, rootPassword);
	}

	private static ComponentResult Execute(ComponentJob job, Action<int, string> report, string rootPassword)
	{
		if (job == null || (job.Kind != ComponentCatalog.KindServer && job.Kind != ComponentCatalog.KindWorkbench))
		{
			throw new InvalidOperationException("组件信息不完整。");
		}
		return job.IsUninstall ? Uninstall(job, report) : Install(job, report, rootPassword);
	}

	private static ComponentResult Install(ComponentJob job, Action<int, string> report, string rootPassword)
	{
		if (string.IsNullOrWhiteSpace(job.Url))
		{
			throw new InvalidOperationException("组件信息不完整。");
		}
		if (string.IsNullOrWhiteSpace(job.InstallDirectory))
		{
			throw new InvalidOperationException("请为" + job.DisplayName + "选择安装位置。");
		}
		if (job.SetRootPassword && (!job.IsServer || !job.RegisterService || !IsValidRootPassword(rootPassword)))
		{
			throw new InvalidOperationException("没有收到有效的 root 密码，未开始安装。");
		}
		if (job.IsServer && job.RegisterService)
		{
			ValidateNewServerInstance(job.InstallDirectory, GetServiceName(job));
		}
		string msiPath = EnsureDownloaded(job, report);
		try
		{
			return ApplyMsiAndConfigure(job, report, rootPassword, msiPath);
		}
		finally
		{
			SafeDelete(msiPath);
		}
	}

	private static ComponentResult ApplyMsiAndConfigure(ComponentJob job, Action<int, string> report, string rootPassword, string msiPath)
	{
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
			string serviceWarning = InitializeServer(job, report, rootPassword, out serviceName);
			if (!string.IsNullOrWhiteSpace(serviceWarning))
			{
				return new ComponentResult
				{
					Message = "MySQL Server 程序文件已安装，但实例配置未完成。\n" + serviceWarning,
					InstalledDirectory = job.InstallDirectory
				};
			}
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

	private static ComponentResult Uninstall(ComponentJob job, Action<int, string> report)
	{
		InstalledComponent target = ComponentCatalog.FindUninstallableComponents(job.Kind).FirstOrDefault(item =>
			item.ProductCode == job.UninstallProductCode && item.MachineWide == job.MachineWide);
		if (target == null)
		{
			throw new InvalidOperationException("未找到所选组件的 Windows Installer 记录，请刷新后重试。");
		}
		string serverDirectory = job.IsServer ? ResolveMsiServerDirectory(target) : null;
		if (job.IsServer && string.IsNullOrWhiteSpace(serverDirectory))
		{
			throw new InvalidOperationException("无法确认所选 Server 的安装目录和关联服务。为免影响其他实例，请备份数据，停止对应服务后使用 Windows 的“已安装的应用”卸载。");
		}
		Dictionary<string, string> services = job.IsServer ? FindServicesForServer(serverDirectory) : new Dictionary<string, string>();
		List<string> stopped = new List<string>();
		bool removed = false;
		try
		{
			foreach (string name in services.Keys)
			{
				using ServiceController service = new ServiceController(name);
				if (service.Status == ServiceControllerStatus.Stopped)
				{
					continue;
				}
				report(15, "正在停止 Windows 服务 " + name + "…");
				bool runningDependent = false;
				foreach (ServiceController dependent in service.DependentServices)
				{
					using (dependent)
					{
						runningDependent |= dependent.Status != ServiceControllerStatus.Stopped;
					}
				}
				if (runningDependent)
				{
					throw new InvalidOperationException("有其他服务依赖 " + name + " 且仍在运行，请先手动停止后重试。");
				}
				if (service.Status != ServiceControllerStatus.StopPending)
				{
					service.Stop();
					stopped.Add(name);
				}
				service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
			}
			string logDirectory = Path.Combine(WorkRoot, "logs");
			Directory.CreateDirectory(logDirectory);
			string logPath = Path.Combine(logDirectory, target.Kind + "-uninstall-" + Guid.NewGuid().ToString("N") + ".log");
			report(35, "正在通过 Windows Installer 卸载 " + target.DisplayName + "…");
			int exitCode = RunMsi("/x " + target.ProductCode.ToString("B") + " /qn /norestart /l*v \"" + logPath + "\"", "卸载");
			if (exitCode == 1602)
			{
				return new ComponentResult { Cancelled = true, Message = "已取消卸载。" };
			}
			if (exitCode != 0 && exitCode != 3010)
			{
				throw new InvalidOperationException("Windows Installer 未完成卸载（代码 " + exitCode + "）。\n" + DescribeMsiExitCode(exitCode) + "\n详细日志：" + logPath);
			}
			removed = true;
			List<string> warnings = new List<string>();
			if (exitCode == 3010)
			{
				warnings.Add("需要重新启动 Windows 以完成卸载。");
			}
			else if (job.IsServer)
			{
				foreach (KeyValuePair<string, string> service in services)
				{
					if (!string.Equals(ServiceManager.TryGetRawImagePath(service.Key), service.Value, StringComparison.Ordinal))
					{
						continue;
					}
					if (File.Exists(Path.Combine(serverDirectory, "bin", "mysqld.exe")) || File.Exists(ServiceManager.NormalizeImagePath(service.Value)) || !target.MachineWide)
					{
						warnings.Add("保留了 Windows 服务 " + service.Key + "，请核对其安装路径及状态。");
						continue;
					}
					ProcessResult deletion = RunTool(Path.Combine(Environment.SystemDirectory, "sc.exe"), "delete \"" + service.Key + "\"", 30000);
					if (deletion.ExitCode != 0 && ServiceManager.ServiceExists(service.Key))
					{
						warnings.Add("未能移除残留服务 " + service.Key + "，请在 Windows 中检查。");
					}
				}
			}
			report(100, target.DisplayName + " 卸载流程完成");
			return new ComponentResult
			{
				Success = true,
				Message = target.DisplayName + (exitCode == 3010 ? " 已卸载，重启后生效。" : " 已卸载。"),
				Detail = warnings.Count == 0 ? null : string.Join("\n", warnings)
			};
		}
		finally
		{
			if (!removed)
			{
				foreach (string name in stopped)
				{
					ServiceActionResult restored = ServiceManager.StartService(name);
					if (!restored.Success)
					{
						AppLog.Warning("卸载未完成，无法恢复 MySQL 服务 " + name + "：" + restored.Message);
					}
				}
			}
		}
	}

	[DllImport("msi.dll", CharSet = CharSet.Unicode)]
	private static extern int MsiGetProductInfo(string product, string property, StringBuilder buffer, ref int chars);

	[DllImport("msi.dll", CharSet = CharSet.Unicode)]
	private static extern int MsiGetComponentPath(string product, string component, StringBuilder buffer, ref int chars);

	private static string ResolveMsiServerDirectory(InstalledComponent target)
	{
		string product = target.ProductCode.ToString("B");
		string location = ReadProductProperty(product, "InstallLocation");
		foreach (string candidate in new[] { location, target.Directory })
		{
			if (!string.IsNullOrEmpty(candidate) && Path.IsPathRooted(candidate) && File.Exists(Path.Combine(candidate, "bin", "mysqld.exe")))
			{
				return candidate;
			}
		}
		// 一些 MySQL MSI 不提供 InstallLocation；从该产品的缓存 MSI 找 mysqld 的组件键路径。
		string package = ReadProductProperty(product, "LocalPackage");
		if (string.IsNullOrWhiteSpace(package) || MsiOpenDatabase(package, IntPtr.Zero, out IntPtr database) != 0)
		{
			return null;
		}
		try
		{
			const string sql = "SELECT `File`.`FileName`, `Component`.`ComponentId` FROM `File`, `Component` WHERE `File`.`Component_` = `Component`.`Component` AND `File`.`File` = `Component`.`KeyPath`";
			if (MsiDatabaseOpenView(database, sql, out IntPtr view) != 0)
			{
				return null;
			}
			try
			{
				if (MsiViewExecute(view, IntPtr.Zero) != 0)
				{
					return null;
				}
				while (MsiViewFetch(view, out IntPtr record) == 0)
				{
					try
					{
						if (!ReadMsiField(record, 1).Split('|').Contains("mysqld.exe", StringComparer.OrdinalIgnoreCase) ||
							!Guid.TryParseExact(ReadMsiField(record, 2), "B", out Guid component))
						{
							continue;
						}
						StringBuilder path = new StringBuilder(1024);
						int chars = path.Capacity;
						const int InstallStateLocal = 3;
						if (MsiGetComponentPath(product, component.ToString("B"), path, ref chars) == InstallStateLocal && File.Exists(path.ToString()))
						{
							string bin = Path.GetDirectoryName(path.ToString());
							if (string.Equals(Path.GetFileName(bin), "bin", StringComparison.OrdinalIgnoreCase))
							{
								return Path.GetDirectoryName(bin);
							}
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
		return null;
	}

	private static string ReadProductProperty(string product, string property)
	{
		StringBuilder buffer = new StringBuilder(1024);
		int chars = buffer.Capacity;
		return MsiGetProductInfo(product, property, buffer, ref chars) == 0 ? buffer.ToString() : null;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, StringBuilder path, uint chars, uint flags);

	private static string CanonicalFilePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
		{
			return string.Empty;
		}
		try
		{
			using FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			StringBuilder buffer = new StringBuilder(1024);
			uint size = GetFinalPathNameByHandle(file.SafeFileHandle, buffer, (uint)buffer.Capacity, 0);
			if (size > 0 && size < buffer.Capacity)
			{
				return buffer.ToString();
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		return Path.GetFullPath(path);
	}

	private static Dictionary<string, string> FindServicesForServer(string directory)
	{
		Dictionary<string, string> matches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string expected = CanonicalFilePath(Path.Combine(directory, "bin", "mysqld.exe"));
		foreach (ServiceController service in ServiceController.GetServices())
		{
			using (service)
			{
				string raw = ServiceManager.TryGetRawImagePath(service.ServiceName);
				string actual = ServiceManager.NormalizeImagePath(raw);
				if (string.Equals(Path.GetFileName(actual), "mysqld.exe", StringComparison.OrdinalIgnoreCase) &&
					string.Equals(expected, CanonicalFilePath(actual), StringComparison.OrdinalIgnoreCase))
				{
					matches.Add(service.ServiceName, raw);
				}
			}
		}
		return matches;
	}

	/// <summary>以管理员身份启动自身执行任务，并把子进程进度回传给界面。</summary>
	private static ComponentResult RunElevated(ComponentJob job, Action<int, string> report, string rootPassword)
	{
		string token = Guid.NewGuid().ToString("N");
		using WindowsIdentity identity = WindowsIdentity.GetCurrent();
		using Process parent = Process.GetCurrentProcess();
		PipeSecurity security = new PipeSecurity();
		security.SetAccessRuleProtection(true, false);
		security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
		security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
		using NamedPipeServerStream pipe = new NamedPipeServerStream("MySQLManager.Component." + token, PipeDirection.InOut, 1,
			PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
		report(0, "正在请求管理员授权…");
		ProcessStartInfo startInfo = new ProcessStartInfo(Application.ExecutablePath)
		{
			Arguments = "--apply-component " + token + " --component-parent " + parent.Id,
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory
		};
		try
		{
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return new ComponentResult { Message = "未能启动管理员进程。" };
			}
			Task connection = pipe.WaitForConnectionAsync();
			for (int attempt = 0; !connection.Wait(250); attempt++)
			{
				if (attempt >= 80 || process.HasExited)
				{
					return new ComponentResult { Message = "未能与管理员进程建立连接，未发送组件任务。" };
				}
			}
			if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) || pid != process.Id)
			{
				return new ComponentResult { Message = "管理员进程身份校验失败，未发送组件任务。" };
			}
			using BinaryWriter writer = new BinaryWriter(pipe, Encoding.UTF8, true);
			using BinaryReader reader = new BinaryReader(pipe, Encoding.UTF8, true);
			WriteMessage(writer, job);
			if (job.SetRootPassword)
			{
				writer.Write(rootPassword ?? string.Empty);
				writer.Flush();
			}
			while (true)
			{
				int percent = reader.ReadInt32();
				if (percent == -1)
				{
					return ReadMessage<ComponentResult>(reader);
				}
				report(percent, reader.ReadString());
			}
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return new ComponentResult { Cancelled = true, Message = "已取消管理员授权，未做任何更改。" };
		}
		catch (IOException)
		{
			return new ComponentResult { Message = "与管理员进程的连接意外断开，请确认组件状态后再试。" };
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);

	/// <summary>提权子进程入口。任务、密码、进度和结果都只通过本机管道传递。</summary>
	public static bool TryHandleApplyCommand(string[] args)
	{
		string token = GetArgument(args, "--apply-component");
		if (token == null)
		{
			return false;
		}
		if (!IsElevated || !Guid.TryParseExact(token, "N", out _) || !uint.TryParse(GetArgument(args, "--component-parent"), out uint parentPid))
		{
			return true;
		}
		try
		{
			using NamedPipeClientStream pipe = new NamedPipeClientStream(".", "MySQLManager.Component." + token,
				PipeAccessRights.ReadWrite, PipeOptions.None, TokenImpersonationLevel.Identification, HandleInheritability.None);
			pipe.Connect(15000);
			if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid) || pid != parentPid)
			{
				return true;
			}
			using BinaryReader reader = new BinaryReader(pipe, Encoding.UTF8, true);
			using BinaryWriter writer = new BinaryWriter(pipe, Encoding.UTF8, true);
			ComponentResult result;
			try
			{
				ComponentJob job = ReadMessage<ComponentJob>(reader);
				string rootPassword = job.SetRootPassword ? reader.ReadString() : null;
				result = Execute(job, (percent, message) =>
				{
					try
					{
						writer.Write(percent);
						writer.Write(message ?? string.Empty);
						writer.Flush();
					}
					catch (IOException)
					{
						// 界面断开时仍让已开始的 Windows Installer 操作正常结束。
					}
				}, rootPassword);
			}
			catch (Exception ex)
			{
				AppLog.Error("组件操作失败。", ex);
				result = new ComponentResult { Message = ex.Message };
			}
			writer.Write(-1);
			WriteMessage(writer, result);
		}
		catch (Exception ex)
		{
			AppLog.Warning("管理员组件进程通信失败：" + ex.Message);
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

	private static void WriteMessage<T>(BinaryWriter writer, T value)
	{
		using MemoryStream stream = new MemoryStream();
		new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
		writer.Write((int)stream.Length);
		writer.Write(stream.GetBuffer(), 0, (int)stream.Length);
		writer.Flush();
	}

	private static T ReadMessage<T>(BinaryReader reader) where T : class
	{
		int length = reader.ReadInt32();
		if (length <= 0 || length > 1024 * 1024)
		{
			throw new InvalidDataException("组件任务消息长度无效。");
		}
		byte[] bytes = reader.ReadBytes(length);
		if (bytes.Length != length)
		{
			throw new EndOfStreamException();
		}
		using MemoryStream stream = new MemoryStream(bytes);
		return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
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
		// 顺序：自定义地址 → 官方 CDN（当前版本，实测约快 10 倍）→ 官方归档（旧版本兜底）。
		List<string> urls = new List<string>();
		if (!string.IsNullOrWhiteSpace(job.Url))
		{
			urls.Add(job.Url);
		}
		string cdnUrl = ComponentCatalog.BuildDownloadUrl(job.Kind, job.Version);
		if (!urls.Contains(cdnUrl))
		{
			urls.Add(cdnUrl);
		}
		string archiveUrl = ComponentCatalog.BuildArchiveUrl(job.Kind, job.Version);
		if (!urls.Contains(archiveUrl))
		{
			urls.Add(archiveUrl);
		}
		Exception lastError = null;
		foreach (string url in urls)
		{
			report(2, "正在连接下载服务器（官方 CDN）…");
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
			SafeDelete(temporary);
			throw new InvalidOperationException("安装包下载失败：" + lastError.Message + "\n\n请确认网络可以访问 cdn.mysql.com 或 downloads.mysql.com 后重试。");
		}
		SafeDelete(target);
		File.Move(temporary, target);
		long size = new FileInfo(target).Length;
		if (size < 5 * 1024 * 1024)
		{
			SafeDelete(target);
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
		byte[] buffer = new byte[262144];
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
		return RunMsi("/i \"" + msiPath + "\" /qn /norestart /l*v \"" + logPath + "\" " + property + "=\"" + installDirectory + "\" REBOOT=ReallySuppress ALLUSERS=1", "安装");
	}

	private static int RunMsi(string arguments, string operation)
	{
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
			throw new InvalidOperationException(operation + "超时（超过 45 分钟）。请在 Windows 中确认组件状态后再重试。");
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

	internal static bool IsValidRootPassword(string password)
	{
		if (string.IsNullOrWhiteSpace(password) || password.Length > 256 || password.Any(char.IsControl))
		{
			return false;
		}
		try
		{
			return new UTF8Encoding(false, true).GetByteCount(password) <= 256;
		}
		catch (EncoderFallbackException)
		{
			return false;
		}
	}

	private static string GetServiceName(ComponentJob job) => string.IsNullOrWhiteSpace(job.ServiceName) ? ComponentCatalog.DefaultServiceName(job.Version) : job.ServiceName.Trim();

	private static void ValidateNewServerInstance(string basedir, string serviceName)
	{
		if (!Regex.IsMatch(serviceName, @"^MySQL[0-9]*$", RegexOptions.CultureInvariant))
		{
			throw new InvalidOperationException("MySQL 服务名无效。");
		}
		if (ServiceManager.ServiceExists(serviceName))
		{
			throw new InvalidOperationException("Windows 服务 " + serviceName + " 已存在。可取消“初始化数据目录并注册为 Windows 服务”，仅安装程序文件。");
		}
		string dataDirectory = Path.Combine(basedir, "data");
		if (File.Exists(Path.Combine(basedir, "my.ini")) || (Directory.Exists(dataDirectory) && Directory.EnumerateFileSystemEntries(dataDirectory).Any()))
		{
			throw new InvalidOperationException("所选目录已有 MySQL 数据或配置，不会覆盖或修改已有实例。请换用新目录，或取消初始化选项。");
		}
	}

	/// <summary>仅初始化全新实例；密码配置成功前不注册 Windows 服务。</summary>
	private static string InitializeServer(ComponentJob job, Action<int, string> report, string rootPassword, out string serviceName)
	{
		serviceName = null;
		string basedir = Path.GetFullPath(job.InstallDirectory);
		string mysqld = Path.Combine(basedir, "bin", "mysqld.exe");
		string mysql = Path.Combine(basedir, "bin", "mysql.exe");
		if (!File.Exists(mysqld) || (job.SetRootPassword && !File.Exists(mysql)))
		{
			return "未在所选目录找到所需的 MySQL 程序，已跳过实例初始化。";
		}
		string name = GetServiceName(job);
		ValidateNewServerInstance(basedir, name);
		string dataDirectory = Path.Combine(basedir, "data");
		string iniPath = Path.Combine(basedir, "my.ini");
		int port = FindFreePort(3306);
		Directory.CreateDirectory(dataDirectory);
		using (FileStream newIni = new FileStream(iniPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
		using (StreamWriter writer = new StreamWriter(newIni, new UTF8Encoding(false)))
		{
			writer.Write(BuildIni(basedir, dataDirectory, port));
		}
		string defaults = "--defaults-file=\"" + iniPath + "\"";
		report(86, "正在初始化数据目录（可能需要 1-2 分钟）…");
		ProcessResult initResult = RunTool(mysqld, defaults + (job.SetRootPassword ? " --initialize --console" : " --initialize-insecure --console"), 600000);
		if (initResult.ExitCode != 0)
		{
			return "数据目录初始化失败（mysqld 返回 " + initResult.ExitCode + "）。" + (job.SetRootPassword ? string.Empty : "\n" + Trim(initResult.Output));
		}
		if (job.SetRootPassword)
		{
			report(90, "正在为新实例设置 root 密码…");
			Match temporary = Regex.Match(initResult.Output, @"temporary password is generated for root@localhost:\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			if (!temporary.Success || !ConfigureRootPassword(mysqld, mysql, defaults, temporary.Groups[1].Value, rootPassword))
			{
				return "未能确认 root 密码已设置，未注册 Windows 服务。已保留新建的数据和配置，请检查后再试，或换用一个空目录。";
			}
		}
		report(94, "正在注册 Windows 服务 " + name + "…");
		ProcessResult installResult = RunTool(mysqld, "--install-manual " + name + " " + defaults, 120000);
		if (installResult.ExitCode != 0 || !ServiceManager.ServiceExists(name))
		{
			return "注册 Windows 服务失败（mysqld 返回 " + installResult.ExitCode + "）。\n" + Trim(installResult.Output);
		}
		serviceName = name;
		try
		{
			File.WriteAllText(Path.Combine(basedir, "服务信息.txt"), "服务名称：" + name + "\r\n端口：" + port + "\r\n配置文件：" + iniPath + "\r\n数据目录：" + dataDirectory + "\r\n" + (job.SetRootPassword ? "初始 root 密码：已设置（未保存明文）" : "初始 root 密码：空（建议首次使用后立即设置）") + "\r\n", new UTF8Encoding(false));
		}
		catch
		{
		}
		return null;
	}

	private static bool ConfigureRootPassword(string mysqld, string mysql, string defaults, string temporaryPassword, string password)
	{
		string id = Guid.NewGuid().ToString("N");
		string sharedMemory = "MySQLManager" + id;
		string clientOptions = Path.Combine(Path.GetTempPath(), "MySQLManager-" + id + ".cnf");
		bool optionsCreated = false;
		try
		{
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			FileSecurity security = new FileSecurity();
			security.SetAccessRuleProtection(true, false);
			security.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl, AccessControlType.Allow));
			// 仅 Windows ACL 保护的临时文件保存随机过渡密码；用户输入的密码只经客户端标准输入发送。
			using (FileStream file = new FileStream(clientOptions, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security))
			{
				optionsCreated = true;
				using StreamWriter writer = new StreamWriter(file, new UTF8Encoding(false));
				writer.WriteLine("[client]");
				writer.WriteLine("password=\"" + temporaryPassword.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
			}
			using Process server = new Process
			{
				StartInfo = HiddenProcess(mysqld, defaults + " --console --skip-networking --mysqlx=OFF --shared-memory --shared-memory-base-name=" + sharedMemory + " --skip-log-bin --general-log=OFF --slow-query-log=OFF --sql-mode=NO_ENGINE_SUBSTITUTION")
			};
			if (!server.Start())
			{
				return false;
			}
			try
			{
				server.BeginOutputReadLine();
				server.BeginErrorReadLine();
				string args = "--defaults-file=\"" + clientOptions + "\" --no-login-paths --protocol=MEMORY --shared-memory-base-name=" + sharedMemory + " --user=root --connect-timeout=3 --connect-expired-password --default-character-set=utf8mb4 --batch --binary-mode";
				string sql = BuildRootPasswordSql(password);
				Stopwatch timer = Stopwatch.StartNew();
				while (timer.Elapsed < TimeSpan.FromMinutes(2) && !server.HasExited)
				{
					ProcessResult changed = RunTool(mysql, args, 15000, sql);
					if (changed.ExitCode == 0)
					{
						return server.WaitForExit(60000) && server.ExitCode == 0;
					}
					// 只重试启动尚未就绪的本地连接；不会输出含 SQL 的客户端错误。
					if (!Regex.IsMatch(changed.Output, @"\bERROR 20(?:3[7-9]|4[0-6])\b", RegexOptions.CultureInvariant))
					{
						return false;
					}
					Thread.Sleep(350);
				}
				return false;
			}
			finally
			{
				if (!server.HasExited)
				{
					server.Kill();
					server.WaitForExit(10000);
				}
			}
		}
		catch (Exception)
		{
			return false;
		}
		finally
		{
			if (optionsCreated)
			{
				SafeDelete(clientOptions);
			}
		}
	}

	internal static string BuildRootPasswordSql(string password) =>
		"ALTER USER 'root'@'localhost' IDENTIFIED BY '" + password.Replace("\\", "\\\\").Replace("'", "''") + "';\nSHUTDOWN;\n";

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

	private static ProcessStartInfo HiddenProcess(string executable, string arguments) => new ProcessStartInfo(executable, arguments)
	{
		UseShellExecute = false,
		CreateNoWindow = true,
		RedirectStandardOutput = true,
		RedirectStandardError = true,
		WindowStyle = ProcessWindowStyle.Hidden,
		WorkingDirectory = Path.GetDirectoryName(executable) ?? Path.GetTempPath()
	};

	private static ProcessResult RunTool(string executable, string arguments, int timeout, string input = null)
	{
		ProcessResult result = new ProcessResult
		{
			ExitCode = -1,
			Output = string.Empty
		};
		try
		{
			ProcessStartInfo startInfo = HiddenProcess(executable, arguments);
			startInfo.RedirectStandardInput = input != null;
			using Process process = new Process
			{
				StartInfo = startInfo
			};
			StringBuilder output = new StringBuilder();
			process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
			{
				if (e.Data != null)
				{
					lock (output)
					{
						output.AppendLine(e.Data);
					}
				}
			};
			process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
			{
				if (e.Data != null)
				{
					lock (output)
					{
						output.AppendLine(e.Data);
					}
				}
			};
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			if (input != null)
			{
				try
				{
					byte[] bytes = new UTF8Encoding(false, true).GetBytes(input);
					try
					{
						process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
					}
					finally
					{
						Array.Clear(bytes, 0, bytes.Length);
					}
				}
				catch (IOException)
				{
					// 进程可能在读取输入前就退出，仍读取其退出代码和诊断。
				}
				finally
				{
					process.StandardInput.Close();
				}
			}
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
