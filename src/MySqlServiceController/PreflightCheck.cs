using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MySqlServiceController;

internal enum PreflightSeverity
{
	Ok,
	Info,
	Warning,
	Error
}

internal sealed class PreflightItem
{
	public PreflightSeverity Severity { get; set; }

	public string Title { get; set; } = string.Empty;

	public string Detail { get; set; } = string.Empty;

	public string Advice { get; set; } = string.Empty;
}

internal sealed class PreflightReport
{
	public string ServiceName { get; set; } = string.Empty;

	public DateTime CheckedAt { get; set; }

	public MySqlInstanceInfo Instance { get; set; }

	public List<PreflightItem> Items { get; } = new List<PreflightItem>();

	public int ErrorCount
	{
		get
		{
			int num = 0;
			foreach (PreflightItem item in Items)
			{
				if (item.Severity == PreflightSeverity.Error)
				{
					num++;
				}
			}
			return num;
		}
	}

	public int WarningCount
	{
		get
		{
			int num = 0;
			foreach (PreflightItem item in Items)
			{
				if (item.Severity == PreflightSeverity.Warning)
				{
					num++;
				}
			}
			return num;
		}
	}

	public string Verdict
	{
		get
		{
			if (ErrorCount > 0)
			{
				return "发现 " + ErrorCount + " 个阻塞问题";
			}
			if (WarningCount > 0)
			{
				return WarningCount + " 项提醒，可尝试启动";
			}
			return "未发现阻塞问题";
		}
	}

	public string ToPlainText()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("MySQL管家 · 启动前体检报告");
		stringBuilder.AppendLine("时间：" + CheckedAt.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("服务：" + ServiceName);
		if (Instance != null)
		{
			stringBuilder.AppendLine("配置：" + (string.IsNullOrWhiteSpace(Instance.ConfigPath) ? "未找到 my.ini" : Instance.ConfigPath));
			stringBuilder.AppendLine("数据目录：" + (string.IsNullOrWhiteSpace(Instance.DataDirectory) ? "未确定" : Instance.DataDirectory));
			stringBuilder.AppendLine("端口：" + Instance.Port);
		}
		stringBuilder.AppendLine();
		foreach (PreflightItem item in Items)
		{
			stringBuilder.AppendLine("[" + GetSeverityLabel(item.Severity) + "] " + item.Title);
			if (!string.IsNullOrWhiteSpace(item.Detail))
			{
				foreach (string item2 in item.Detail.Split('\n'))
				{
					stringBuilder.AppendLine("    " + item2);
				}
			}
			if (!string.IsNullOrWhiteSpace(item.Advice))
			{
				stringBuilder.AppendLine("    建议：" + item.Advice);
			}
			stringBuilder.AppendLine();
		}
		stringBuilder.AppendLine("结论：" + Verdict);
		return stringBuilder.ToString();
	}

	public static string GetSeverityLabel(PreflightSeverity severity)
	{
		return severity switch
		{
			PreflightSeverity.Error => "阻塞", 
			PreflightSeverity.Warning => "提醒", 
			PreflightSeverity.Ok => "正常", 
			_ => "提示", 
		};
	}
}

internal static class PreflightChecker
{
	public static PreflightReport Run(string serviceName)
	{
		PreflightReport report = new PreflightReport
		{
			ServiceName = (serviceName ?? string.Empty).Trim(),
			CheckedAt = DateTime.Now
		};
		try
		{
			MySqlInstanceInfo instance = MySqlInstanceLocator.Resolve(report.ServiceName);
			report.Instance = instance;
			ServiceSnapshot snapshot = instance.Snapshot;
			AddServiceItem(report, instance, snapshot);
			if (snapshot == null || !snapshot.Installed)
			{
				return report;
			}
			AddExecutableItem(report, instance);
			AddConfigItem(report, instance);
			AddDataDirectoryItem(report, instance);
			AddPortItem(report, instance, snapshot);
			AddLogItem(report, instance);
			AddDiskItem(report, instance);
		}
		catch (Exception exception)
		{
			AppLog.Error("启动前体检失败。", exception);
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Error,
				Title = "体检过程出错",
				Detail = exception.Message,
				Advice = "请查看程序日志：" + AppLog.LogPath
			});
		}
		return report;
	}

	private static void AddServiceItem(PreflightReport report, MySqlInstanceInfo instance, ServiceSnapshot snapshot)
	{
		if (snapshot == null || !snapshot.Installed)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Error,
				Title = "未找到 Windows 服务",
				Detail = "服务名：" + report.ServiceName + "。" + ((snapshot == null || string.IsNullOrWhiteSpace(snapshot.ErrorMessage)) ? string.Empty : ("\n" + snapshot.ErrorMessage)),
				Advice = "在设置中重新选择服务名，或先在 Windows 中安装 MySQL 服务。"
			});
			return;
		}
		if (snapshot.IsDisabled)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Error,
				Title = "服务已被禁用",
				Detail = "启动类型为“已禁用”，Windows 会拒绝启动请求。",
				Advice = "打开 services.msc，把启动类型改为“手动”或“自动”后重试。"
			});
			return;
		}
		string text = "启动类型：" + snapshot.StartupType + " · 当前状态：" + snapshot.StatusText;
		if (!string.IsNullOrWhiteSpace(snapshot.Account))
		{
			text = text + "\n运行账户：" + snapshot.Account;
		}
		if (!string.IsNullOrWhiteSpace(snapshot.Account) && snapshot.Account.StartsWith(".\\", StringComparison.Ordinal))
		{
			text += "\n以本地用户账户运行时，若账户密码已更改，服务会报 1069 无法登录。";
		}
		report.Items.Add(new PreflightItem
		{
			Severity = PreflightSeverity.Ok,
			Title = "Windows 服务已就绪",
			Detail = text
		});
	}

	private static void AddExecutableItem(PreflightReport report, MySqlInstanceInfo instance)
	{
		if (string.IsNullOrWhiteSpace(instance.ExePath))
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Warning,
				Title = "未能读取 mysqld.exe 路径",
				Detail = "服务注册信息中没有可解析的 ImagePath。",
				Advice = "在设置中手动指定 MySQL 程序路径，便于体检与日志定位。"
			});
			return;
		}
		if (!instance.ExeExists)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Error,
				Title = "mysqld.exe 不存在",
				Detail = "服务指向：\n" + instance.ExePath + "\n该文件已被移动或删除，服务启动会立即失败。",
				Advice = "修复 MySQL 安装，或用 mysqld --install 重新注册服务。"
			});
			return;
		}
		report.Items.Add(new PreflightItem
		{
			Severity = PreflightSeverity.Ok,
			Title = "mysqld.exe 已找到",
			Detail = instance.ExePath
		});
	}

	private static void AddConfigItem(PreflightReport report, MySqlInstanceInfo instance)
	{
		if (string.IsNullOrWhiteSpace(instance.ConfigPath))
		{
			List<string> list = new List<string>();
			int num = 0;
			foreach (string configCandidate in instance.ConfigCandidates)
			{
				if (num >= 4)
				{
					break;
				}
				list.Add(configCandidate);
				num++;
			}
			string text = ((list.Count > 0) ? ("已检查：\n" + string.Join("\n", list)) : "没有找到可检查的 my.ini 位置。");
			if (instance.ConfigCandidates.Count > list.Count)
			{
				text = text + "\n以及另外 " + (instance.ConfigCandidates.Count - list.Count) + " 个位置";
			}
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Warning,
				Title = "未找到 my.ini",
				Detail = text,
				Advice = "MySQL 会使用内置默认值；若启动失败，请确认服务的 --defaults-file 参数是否正确。"
			});
			return;
		}
		string text2 = "路径：" + instance.ConfigPath + "\n来源：" + instance.ConfigSource;
		if (!instance.ConfigHasMySqlSection)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Warning,
				Title = "配置文件缺少 [mysqld] 段",
				Detail = text2 + "\n文件可读，但没有解析到 [mysqld] 配置段，可能不是 MySQL 的配置文件。",
				Advice = "确认该文件确实是服务的 --defaults-file 指向的配置。"
			});
			return;
		}
		report.Items.Add(new PreflightItem
		{
			Severity = PreflightSeverity.Ok,
			Title = "已加载 MySQL 配置文件",
			Detail = text2
		});
	}

	private static void AddDataDirectoryItem(PreflightReport report, MySqlInstanceInfo instance)
	{
		if (string.IsNullOrWhiteSpace(instance.DataDirectory))
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Warning,
				Title = "无法确定数据目录",
				Detail = "my.ini 中没有 datadir，也没有在常见位置找到 Data 目录。",
				Advice = "在 my.ini 中补充 datadir，或手动确认数据目录位置。"
			});
			return;
		}
		if (!instance.DataDirectoryExists)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Error,
				Title = "数据目录不存在",
				Detail = "路径：" + instance.DataDirectory + "\nMySQL 找不到数据文件，启动会立即退出。",
				Advice = "检查 my.ini 的 datadir 是否拼写正确，或从备份恢复数据目录。"
			});
			return;
		}
		int num = -1;
		try
		{
			num = Directory.GetFileSystemEntries(instance.DataDirectory).Length;
		}
		catch (UnauthorizedAccessException)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Info,
				Title = "数据目录受权限保护",
				Detail = "路径：" + instance.DataDirectory + "\n当前账户没有读取权限，无法校验目录内容；这不代表 MySQL 无法访问。",
				Advice = "只有在启动失败时才需要检查目录 ACL 是否包含服务运行账户。"
			});
			return;
		}
		catch (Exception exception)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Warning,
				Title = "数据目录校验失败",
				Detail = "路径：" + instance.DataDirectory + "\n" + exception.Message
			});
			return;
		}
		if (num == 0)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Warning,
				Title = "数据目录为空",
				Detail = "路径：" + instance.DataDirectory + "\n目录存在但不含任何文件，可能尚未初始化。",
				Advice = "使用 mysqld --initialize 初始化，或把 datadir 指向已有数据目录。"
			});
			return;
		}
		report.Items.Add(new PreflightItem
		{
			Severity = PreflightSeverity.Ok,
			Title = "数据目录可访问",
			Detail = "路径：" + instance.DataDirectory + "\n包含 " + num + " 个条目 · 来源：" + instance.DataDirectorySource
		});
	}

	private static void AddPortItem(PreflightReport report, MySqlInstanceInfo instance, ServiceSnapshot snapshot)
	{
		if (instance.SkipNetworking)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Info,
				Title = "已禁用 TCP 网络",
				Detail = "my.ini 配置了 skip-networking，MySQL 启动时不监听 TCP 端口。",
				Advice = "如需通过 127.0.0.1 或远程连接，请移除 skip-networking。"
			});
			return;
		}
		int port = instance.Port;
		string text = "端口 " + port + "（来源：" + instance.PortSource + "）";
		if (!string.IsNullOrWhiteSpace(instance.BindAddress))
		{
			text = text + " · bind-address " + instance.BindAddress;
		}
		PortListenerInfo portListenerInfo;
		try
		{
			portListenerInfo = PortInspector.FindListener(port, forceRefresh: true);
		}
		catch (Exception exception)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Info,
				Title = "端口占用检测不可用",
				Detail = text + "\n" + exception.Message
			});
			return;
		}
		if (portListenerInfo == null)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Ok,
				Title = "端口 " + port + " 可用",
				Detail = text + "\n当前没有进程监听该端口。"
			});
			return;
		}
		bool flag = IsSameMySql(portListenerInfo, instance);
		if (snapshot.IsRunning || snapshot.IsTransitioning)
		{
			if (flag)
			{
				report.Items.Add(new PreflightItem
				{
					Severity = PreflightSeverity.Ok,
					Title = "端口正在由本服务监听",
					Detail = text + "\n监听进程：" + portListenerInfo.DisplayName
				});
				return;
			}
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Info,
				Title = "服务已运行，端口由其它程序占用",
				Detail = text + "\n监听进程：" + portListenerInfo.DisplayName + "\n服务当前已在运行，说明实际监听端口可能与 my.ini 中的 port 不同。",
				Advice = "如果能正常连接数据库，可以忽略；否则检查 my.ini 与实际启动参数是否一致。"
			});
			return;
		}
		if (flag)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Error,
				Title = "端口 " + port + " 被另一个 MySQL 实例占用",
				Detail = text + "\n监听进程：" + portListenerInfo.DisplayName + (string.IsNullOrWhiteSpace(portListenerInfo.ProcessPath) ? string.Empty : ("\n" + portListenerInfo.ProcessPath)) + "\n当前服务未运行，启动时会绑定端口失败。",
				Advice = "先停止另一个实例（可能是其它 MySQL 版本或手动启动的 mysqld），或修改 my.ini 的 port。"
			});
			return;
		}
		report.Items.Add(new PreflightItem
		{
			Severity = PreflightSeverity.Error,
			Title = "端口 " + port + " 已被其它程序占用",
			Detail = text + "\n监听进程：" + portListenerInfo.DisplayName + (string.IsNullOrWhiteSpace(portListenerInfo.ProcessPath) ? string.Empty : ("\n" + portListenerInfo.ProcessPath)) + "\n当前服务未运行，启动时会因端口冲突失败。",
			Advice = "结束该进程，或在 my.ini 中把 port 改为其它值后重启服务。"
		});
	}

	private static bool IsSameMySql(PortListenerInfo listener, MySqlInstanceInfo instance)
	{
		if (listener.ProcessId <= 0)
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(listener.ProcessPath) && !string.IsNullOrWhiteSpace(instance.ExePath))
		{
			try
			{
				if (string.Equals(Path.GetFullPath(listener.ProcessPath), Path.GetFullPath(instance.ExePath), StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			catch
			{
			}
		}
		return (listener.ProcessName ?? string.Empty).IndexOf("mysqld", StringComparison.OrdinalIgnoreCase) >= 0 || (listener.ProcessName ?? string.Empty).IndexOf("mariadbd", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static void AddLogItem(PreflightReport report, MySqlInstanceInfo instance)
	{
		if (string.IsNullOrWhiteSpace(instance.LogErrorPath))
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Info,
				Title = "未配置独立错误日志",
				Detail = "my.ini 中没有 log-error。MySQL 可能把日志写到数据目录下的 <主机名>.err，或输出到控制台。",
				Advice = "可在“错误日志”窗口中使用自动发现，或为 my.ini 增加 log-error。"
			});
			return;
		}
		if (File.Exists(instance.LogErrorPath))
		{
			string text = "路径：" + instance.LogErrorPath + "\n来源：" + instance.LogErrorSource;
			try
			{
				FileInfo fileInfo = new FileInfo(instance.LogErrorPath);
				text = text + "\n大小：" + FormatSize(fileInfo.Length) + " · 最后写入：" + fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
			}
			catch
			{
			}
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Ok,
				Title = "错误日志可读取",
				Detail = text
			});
			return;
		}
		string directoryName = string.Empty;
		try
		{
			directoryName = Path.GetDirectoryName(instance.LogErrorPath) ?? string.Empty;
		}
		catch
		{
		}
		if (!string.IsNullOrWhiteSpace(directoryName) && Directory.Exists(directoryName))
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Warning,
				Title = "错误日志尚未创建",
				Detail = "路径：" + instance.LogErrorPath + "\n目录存在，文件会在 MySQL 首次写入时创建。",
				Advice = "若启动失败后仍没有此文件，说明 mysqld 没有读到这个日志配置。"
			});
			return;
		}
		report.Items.Add(new PreflightItem
		{
			Severity = PreflightSeverity.Warning,
			Title = "错误日志目录不存在",
			Detail = "路径：" + instance.LogErrorPath,
			Advice = "创建该目录，或修正 my.ini 中的 log-error。"
		});
	}

	private static void AddDiskItem(PreflightReport report, MySqlInstanceInfo instance)
	{
		if (string.IsNullOrWhiteSpace(instance.DataDirectory) || !instance.DataDirectoryExists)
		{
			return;
		}
		try
		{
			string pathRoot = Path.GetPathRoot(instance.DataDirectory);
			if (string.IsNullOrWhiteSpace(pathRoot))
			{
				return;
			}
			DriveInfo driveInfo = new DriveInfo(pathRoot);
			if (!driveInfo.IsReady)
			{
				return;
			}
			long num = driveInfo.AvailableFreeSpace;
			string text = "数据目录所在磁盘 " + driveInfo.Name + " 剩余 " + FormatSize(num) + "。";
			PreflightSeverity severity;
			string advice;
			if (num < 209715200)
			{
				severity = PreflightSeverity.Error;
				advice = "磁盘空间严重不足，MySQL 可能无法写入数据；请先清理磁盘。";
			}
			else if (num < 1073741824)
			{
				severity = PreflightSeverity.Warning;
				advice = "剩余空间偏少，写入大量数据或执行 DDL 时可能失败。";
			}
			else
			{
				severity = PreflightSeverity.Ok;
				advice = null;
			}
			report.Items.Add(new PreflightItem
			{
				Severity = severity,
				Title = ((severity == PreflightSeverity.Ok) ? "磁盘空间充足" : "磁盘空间偏少"),
				Detail = text,
				Advice = advice
			});
		}
		catch (Exception exception)
		{
			report.Items.Add(new PreflightItem
			{
				Severity = PreflightSeverity.Info,
				Title = "无法读取磁盘空间",
				Detail = exception.Message
			});
		}
	}

	private static string FormatSize(long bytes)
	{
		if (bytes >= 1073741824L)
		{
			return (bytes / 1073741824.0).ToString("0.0") + " GB";
		}
		if (bytes >= 1048576)
		{
			return (bytes / 1048576.0).ToString("0.0") + " MB";
		}
		if (bytes >= 1024)
		{
			return (bytes / 1024.0).ToString("0.0") + " KB";
		}
		return bytes + " B";
	}
}
