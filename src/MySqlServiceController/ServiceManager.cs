using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace MySqlServiceController;

internal static class ServiceManager
{
	private const string ServicesRegistryPath = "SYSTEM\\CurrentControlSet\\Services";

	private static readonly Lazy<bool> Administrator = new Lazy<bool>(() =>
	{
		try
		{
			using (WindowsIdentity current = WindowsIdentity.GetCurrent())
			{
				return new WindowsPrincipal(current).IsInRole(WindowsBuiltInRole.Administrator);
			}
		}
		catch
		{
			return false;
		}
	});

	public static bool IsAdministrator()
	{
		return Administrator.Value;
	}

	public static bool IsMySqlService(string serviceName, string imagePath)
	{
		string text = (serviceName ?? string.Empty).Trim();
		if (text.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || text.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(imagePath))
		{
			string fileName = Path.GetFileName(imagePath.Trim().Trim('"'));
			return string.Equals(fileName, "mysqld.exe", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static bool IsValidServiceName(string serviceName)
	{
		if (string.IsNullOrWhiteSpace(serviceName))
		{
			return false;
		}
		string text = serviceName.Trim();
		if (text.Length <= 256 && text.IndexOf('\\') < 0 && text.IndexOf('/') < 0 && text.IndexOf('"') < 0)
		{
			return text.IndexOf('\0') < 0;
		}
		return false;
	}

	public static bool ServiceExists(string serviceName)
	{
		if (!IsValidServiceName(serviceName))
		{
			return false;
		}
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + serviceName.Trim(), writable: false);
			return registryKey != null;
		}
		catch
		{
			return false;
		}
	}

	public static ServiceSnapshot GetSnapshot(string serviceName, string configuredPath)
	{
		ServiceSnapshot serviceSnapshot = new ServiceSnapshot();
		serviceSnapshot.ServiceName = (serviceName ?? string.Empty).Trim();
		serviceSnapshot.ImagePath = (configuredPath ?? string.Empty).Trim();
		serviceSnapshot.DisplayName = serviceSnapshot.ServiceName;
		serviceSnapshot.StatusText = "未知";
		serviceSnapshot.StartupType = "未知";
		serviceSnapshot.Account = "未知";
		if (!IsValidServiceName(serviceSnapshot.ServiceName))
		{
			serviceSnapshot.Installed = false;
			serviceSnapshot.StatusText = "未配置";
			serviceSnapshot.ErrorMessage = "尚未配置 Windows 服务名称。";
			return serviceSnapshot;
		}
		try
		{
			using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + serviceSnapshot.ServiceName, writable: false))
			{
				if (registryKey == null)
				{
					serviceSnapshot.Installed = false;
					serviceSnapshot.StatusText = "未安装";
					serviceSnapshot.ErrorMessage = "Windows 中未找到名为“" + serviceSnapshot.ServiceName + "”的服务。";
					return serviceSnapshot;
				}
				serviceSnapshot.Installed = true;
				object value = registryKey.GetValue("DisplayName");
				if (value != null)
				{
					string text = value.ToString();
					if (text.StartsWith("@", StringComparison.Ordinal))
					{
						text = serviceSnapshot.ServiceName;
					}
					serviceSnapshot.DisplayName = text;
				}
				object value2 = registryKey.GetValue("ImagePath");
				if (value2 != null)
				{
					string text2 = NormalizeImagePath(value2.ToString());
					if (!string.IsNullOrWhiteSpace(text2))
					{
						serviceSnapshot.ImagePath = text2;
					}
				}
				object value3 = registryKey.GetValue("Start");
				int num = ((value3 == null) ? 3 : Convert.ToInt32(value3));
				serviceSnapshot.StartupType = GetStartupType(num);
				serviceSnapshot.IsDisabled = num == 4;
				object value4 = registryKey.GetValue("ObjectName");
				if (value4 != null)
				{
					serviceSnapshot.Account = value4.ToString();
				}
			}
			using ServiceController serviceController = new ServiceController(serviceSnapshot.ServiceName);
			serviceController.Refresh();
			serviceSnapshot.Status = serviceController.Status;
			serviceSnapshot.StatusText = GetStatusText(serviceController.Status, serviceSnapshot.IsDisabled);
			try
			{
				serviceSnapshot.CanStop = serviceController.CanStop;
			}
			catch
			{
				serviceSnapshot.CanStop = serviceSnapshot.IsRunning;
			}
		}
		catch (InvalidOperationException exception)
		{
			serviceSnapshot.ErrorMessage = FriendlyExceptionMessage(exception, "读取服务状态", serviceSnapshot.ServiceName);
			serviceSnapshot.StatusText = "读取失败";
			AppLog.Error("读取服务快照失败：" + serviceSnapshot.ServiceName, exception);
		}
		catch (Win32Exception exception2)
		{
			serviceSnapshot.ErrorMessage = FriendlyExceptionMessage(exception2, "读取服务状态", serviceSnapshot.ServiceName);
			serviceSnapshot.StatusText = "读取失败";
			AppLog.Error("读取服务快照失败：" + serviceSnapshot.ServiceName, exception2);
		}
		catch (UnauthorizedAccessException exception3)
		{
			serviceSnapshot.ErrorMessage = "没有读取该服务状态的权限。可尝试以管理员身份运行程序。";
			serviceSnapshot.StatusText = "读取失败";
			AppLog.Error("读取服务快照失败：" + serviceSnapshot.ServiceName, exception3);
		}
		catch (Exception ex)
		{
			serviceSnapshot.ErrorMessage = "读取服务状态时发生异常：" + ex.Message;
			serviceSnapshot.StatusText = "读取失败";
			AppLog.Error("读取服务快照失败：" + serviceSnapshot.ServiceName, ex);
		}
		return serviceSnapshot;
	}

	public static List<ServiceOption> FindMySqlServices()
	{
		List<ServiceOption> list = new List<ServiceOption>();
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services", writable: false);
			if (registryKey == null)
			{
				return list;
			}
			string[] subKeyNames = registryKey.GetSubKeyNames();
			foreach (string text in subKeyNames)
			{
				try
				{
					using RegistryKey registryKey2 = registryKey.OpenSubKey(text, writable: false);
					if (registryKey2 == null)
					{
						continue;
					}
					object value = registryKey2.GetValue("DisplayName");
					string text2 = ((value == null) ? text : value.ToString());
					if (text2.StartsWith("@", StringComparison.Ordinal))
					{
						text2 = text;
					}
					if (text.IndexOf("mysql", StringComparison.OrdinalIgnoreCase) < 0 && text2.IndexOf("mysql", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("maria", StringComparison.OrdinalIgnoreCase) < 0 && text2.IndexOf("maria", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					string statusText = "未知";
					try
					{
						using ServiceController serviceController = new ServiceController(text);
						serviceController.Refresh();
						statusText = GetStatusText(serviceController.Status, isDisabled: false);
					}
					catch
					{
					}
					list.Add(new ServiceOption
					{
						Name = text,
						DisplayName = text2,
						StatusText = statusText
					});
				}
				catch
				{
				}
			}
		}
		catch (Exception exception)
		{
			AppLog.Error("自动检测 MySQL 服务失败。", exception);
		}
		return list.OrderByDescending((ServiceOption item) => item.StatusText == "运行中").ThenBy((ServiceOption item) => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public static ServiceActionResult StartService(string serviceName)
	{
		ServiceSnapshot snapshot = GetSnapshot(serviceName, string.Empty);
		if (!snapshot.Installed)
		{
			ServiceActionResult serviceActionResult = new ServiceActionResult();
			serviceActionResult.Success = false;
			serviceActionResult.Message = snapshot.ErrorMessage;
			serviceActionResult.Detail = "请点击“设置”选择正确的 Windows 服务名称。";
			return serviceActionResult;
		}
		if (snapshot.IsDisabled)
		{
			ServiceActionResult serviceActionResult2 = new ServiceActionResult();
			serviceActionResult2.Success = false;
			serviceActionResult2.Message = "服务“" + serviceName + "”的启动类型为“已禁用”，无法启动。";
			serviceActionResult2.Detail = "请打开 services.msc，将该服务的启动类型改为“手动”或“自动”，然后再试。";
			return serviceActionResult2;
		}
		if (!IsMySqlService(snapshot.ServiceName, snapshot.ImagePath))
		{
			return RejectNonMySql(snapshot, "启动");
		}
		try
		{
			using (ServiceController serviceController = new ServiceController(serviceName))
			{
				serviceController.Refresh();
				if (serviceController.Status == ServiceControllerStatus.Running)
				{
					ServiceActionResult serviceActionResult3 = new ServiceActionResult();
					serviceActionResult3.Success = true;
					serviceActionResult3.Message = "MySQL 服务已经在运行。";
					return serviceActionResult3;
				}
				if (serviceController.Status == ServiceControllerStatus.StartPending)
				{
					serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45.0));
				}
				else
				{
					serviceController.Start();
					serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45.0));
				}
			}
			AppLog.Info("服务已启动：" + serviceName);
			ServiceActionResult serviceActionResult4 = new ServiceActionResult();
			serviceActionResult4.Success = true;
			serviceActionResult4.Message = "MySQL 服务已启动，可以连接数据库了。";
			return serviceActionResult4;
		}
		catch (System.TimeoutException exception)
		{
			AppLog.Error("启动服务超时：" + serviceName, exception);
			ServiceActionResult serviceActionResult5 = new ServiceActionResult();
			serviceActionResult5.Success = false;
			serviceActionResult5.Message = "启动命令已发送，但服务在 45 秒内没有进入“运行中”状态。";
			serviceActionResult5.Detail = "请稍后刷新状态并查看 MySQL 错误日志，常见原因是数据目录、配置文件或端口冲突。";
			return serviceActionResult5;
		}
		catch (Exception exception2)
		{
			AppLog.Error("启动服务失败：" + serviceName, exception2);
			ServiceActionResult serviceActionResult6 = new ServiceActionResult();
			serviceActionResult6.Success = false;
			serviceActionResult6.Message = FriendlyExceptionMessage(exception2, "启动", serviceName);
			serviceActionResult6.Detail = BuildFailureDetail(exception2, "启动");
			return serviceActionResult6;
		}
	}

	public static ServiceActionResult StopService(string serviceName)
	{
		ServiceSnapshot snapshot = GetSnapshot(serviceName, string.Empty);
		if (!snapshot.Installed)
		{
			ServiceActionResult serviceActionResult = new ServiceActionResult();
			serviceActionResult.Success = false;
			serviceActionResult.Message = snapshot.ErrorMessage;
			serviceActionResult.Detail = "请点击“设置”选择正确的 Windows 服务名称。";
			return serviceActionResult;
		}
		if (!IsMySqlService(snapshot.ServiceName, snapshot.ImagePath))
		{
			return RejectNonMySql(snapshot, "停止");
		}
		try
		{
			using (ServiceController serviceController = new ServiceController(serviceName))
			{
				serviceController.Refresh();
				if (serviceController.Status == ServiceControllerStatus.Stopped)
				{
					ServiceActionResult serviceActionResult2 = new ServiceActionResult();
					serviceActionResult2.Success = true;
					serviceActionResult2.Message = "MySQL 服务已经停止。";
					return serviceActionResult2;
				}
				if (serviceController.Status == ServiceControllerStatus.StopPending)
				{
					serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45.0));
				}
				else
				{
					serviceController.Stop();
					serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45.0));
				}
			}
			AppLog.Info("服务已停止：" + serviceName);
			ServiceActionResult serviceActionResult3 = new ServiceActionResult();
			serviceActionResult3.Success = true;
			serviceActionResult3.Message = "MySQL 服务已安全停止，数据库文件不会被删除。";
			return serviceActionResult3;
		}
		catch (System.TimeoutException exception)
		{
			AppLog.Error("停止服务超时：" + serviceName, exception);
			ServiceActionResult serviceActionResult4 = new ServiceActionResult();
			serviceActionResult4.Success = false;
			serviceActionResult4.Message = "停止命令已发送，但服务在 45 秒内没有完全停止。";
			serviceActionResult4.Detail = "MySQL 可能正在完成收尾写入。请稍等后刷新；若长时间无响应，可在任务管理器中检查 mysqld.exe。";
			return serviceActionResult4;
		}
		catch (Exception exception2)
		{
			AppLog.Error("停止服务失败：" + serviceName, exception2);
			ServiceActionResult serviceActionResult5 = new ServiceActionResult();
			serviceActionResult5.Success = false;
			serviceActionResult5.Message = FriendlyExceptionMessage(exception2, "停止", serviceName);
			serviceActionResult5.Detail = BuildFailureDetail(exception2, "停止");
			return serviceActionResult5;
		}
	}

	public static string TryGetImagePath(string serviceName)
	{
		ServiceSnapshot snapshot = GetSnapshot(serviceName, string.Empty);
		return snapshot.ImagePath;
	}

	public static string TryGetRawImagePath(string serviceName)
	{
		if (!IsValidServiceName(serviceName))
		{
			return string.Empty;
		}
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + serviceName.Trim(), writable: false);
			object value = registryKey?.GetValue("ImagePath");
			return value?.ToString() ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string GetStartupType(int value)
	{
		return value switch
		{
			0 => "引导启动", 
			1 => "系统启动", 
			2 => "自动", 
			3 => "手动", 
			4 => "已禁用", 
			_ => "未知", 
		};
	}

	private static string GetStatusText(ServiceControllerStatus status, bool isDisabled)
	{
		if (isDisabled)
		{
			return "已禁用";
		}
		return status switch
		{
			ServiceControllerStatus.Running => "运行中", 
			ServiceControllerStatus.Stopped => "已停止", 
			ServiceControllerStatus.StartPending => "正在启动", 
			ServiceControllerStatus.StopPending => "正在停止", 
			ServiceControllerStatus.Paused => "已暂停", 
			ServiceControllerStatus.PausePending => "正在暂停", 
			ServiceControllerStatus.ContinuePending => "正在继续", 
			_ => "未知", 
		};
	}

	private static string NormalizeImagePath(string rawPath)
	{
		if (string.IsNullOrWhiteSpace(rawPath))
		{
			return string.Empty;
		}
		string text = Environment.ExpandEnvironmentVariables(rawPath.Trim());
		if (text.StartsWith("\"", StringComparison.Ordinal))
		{
			int num = text.IndexOf('"', 1);
			if (num > 1)
			{
				return text.Substring(1, num - 1);
			}
		}
		int num2 = text.IndexOf(" --", StringComparison.Ordinal);
		if (num2 > 0)
		{
			text = text.Substring(0, num2);
		}
		int num3 = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
		if (num3 > 0)
		{
			text = text.Substring(0, num3 + 4);
		}
		return text.Trim().Trim('"');
	}

	private static ServiceActionResult RejectNonMySql(ServiceSnapshot snapshot, string action)
	{
		return new ServiceActionResult
		{
			Success = false,
			Message = "服务“" + snapshot.ServiceName + "”不是 MySQL/MariaDB 服务，已拒绝" + action + "。",
			Detail = "本程序只允许控制 ImagePath 指向 mysqld.exe，或名称以 MySQL / MariaDB 开头的 Windows 服务。"
		};
	}

	private static string FriendlyExceptionMessage(Exception exception, string action, string serviceName)
	{
		Exception ex = exception;
		while (ex.InnerException != null)
		{
			ex = ex.InnerException;
		}
		if (ex is Win32Exception { NativeErrorCode: var nativeErrorCode } ex2)
		{
			return nativeErrorCode switch
			{
				5 => "权限不足，无法" + action + "服务“" + serviceName + "”。", 
				1058 => "服务“" + serviceName + "”已被禁用，无法" + action + "。", 
				1060 => "Windows 中不存在服务“" + serviceName + "”。", 
				1067 => "MySQL 进程启动后意外退出，服务未能" + action + "。", 
				1069 => "服务无法登录到配置的账户，可能是账户密码已更改。", 
				1075 => "服务依赖项不存在或已被禁用，因此无法" + action + "。", 
				1053 => "服务没有及时响应" + action + "请求。", 
				_ => "无法" + action + "服务“" + serviceName + "”（Windows 错误 " + ex2.NativeErrorCode + "：" + ex2.Message + "）。", 
			};
		}
		if (exception is UnauthorizedAccessException)
		{
			return "权限不足，无法" + action + "服务“" + serviceName + "”。";
		}
		if (exception is InvalidOperationException)
		{
			return "服务当前状态不允许" + action + "（" + exception.Message + "）。";
		}
		return "无法" + action + "服务“" + serviceName + "：" + exception.Message;
	}

	private static string BuildFailureDetail(Exception exception, string action)
	{
		Exception ex = exception;
		while (ex.InnerException != null)
		{
			ex = ex.InnerException;
		}
		Win32Exception ex2 = ex as Win32Exception;
		if (ex2 != null && ex2.NativeErrorCode == 5)
		{
			return "请通过界面重新操作，并在 UAC 提示中选择“是”；也可以右键程序，选择“以管理员身份运行”。";
		}
		if (ex2 != null && ex2.NativeErrorCode == 1067)
		{
			return "请查看 MySQL 数据目录中的 .err 错误日志，常见原因是 my.ini 配置错误、数据目录权限不足或端口被占用。";
		}
		if (ex2 != null && ex2.NativeErrorCode == 1069)
		{
			return "请打开 services.msc，在服务属性的“登录”页重新设置服务账户密码。";
		}
		if (action == "停止")
		{
			return "请稍后重试并查看程序日志；如果 MySQL 正在写入大量数据，停止可能需要更长时间。";
		}
		return "请确认 MySQL 安装路径、my.ini 和数据目录有效，然后查看程序日志与 MySQL 错误日志。";
	}
}
