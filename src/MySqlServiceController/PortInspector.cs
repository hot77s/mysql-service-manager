using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace MySqlServiceController;

internal sealed class PortListenerInfo
{
	public int Port { get; set; }

	public int ProcessId { get; set; }

	public string ProcessName { get; set; } = string.Empty;

	public string ProcessPath { get; set; } = string.Empty;

	public string DisplayName
	{
		get
		{
			if (ProcessId <= 0)
			{
				return "未知进程";
			}
			if (string.IsNullOrWhiteSpace(ProcessName))
			{
				return "PID " + ProcessId;
			}
			return ProcessName + "（PID " + ProcessId + "）";
		}
	}
}

internal static class PortInspector
{
	private static readonly object SyncRoot = new object();

	private static DateTime _cacheTime = DateTime.MinValue;

	private static List<PortListenerInfo> _cache = new List<PortListenerInfo>();

	public static PortListenerInfo FindListener(int port, bool forceRefresh = false)
	{
		foreach (PortListenerInfo listener in GetListeners(forceRefresh))
		{
			if (listener.Port == port)
			{
				return listener;
			}
		}
		return null;
	}

	public static List<PortListenerInfo> GetListeners(bool forceRefresh = false)
	{
		lock (SyncRoot)
		{
			if (!forceRefresh && _cacheTime != DateTime.MinValue && DateTime.UtcNow - _cacheTime < TimeSpan.FromSeconds(3.0))
			{
				return new List<PortListenerInfo>(_cache);
			}
			List<PortListenerInfo> list = QueryNetstat();
			if (list.Count == 0)
			{
				list = QueryApi();
			}
			_cache = list;
			_cacheTime = DateTime.UtcNow;
			return new List<PortListenerInfo>(list);
		}
	}

	private static List<PortListenerInfo> QueryNetstat()
	{
		List<PortListenerInfo> list = new List<PortListenerInfo>();
		try
		{
			string text = Path.Combine(Environment.SystemDirectory, "netstat.exe");
			if (!File.Exists(text))
			{
				text = "netstat.exe";
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo(text, "-ano -p TCP")
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.Default,
				StandardErrorEncoding = Encoding.Default
			};
			using Process process = Process.Start(processStartInfo);
			if (process == null)
			{
				return list;
			}
			string text2 = process.StandardOutput.ReadToEnd();
			process.WaitForExit(4000);
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] array = text2.Split('\n');
			foreach (string text3 in array)
			{
				string[] array2 = text3.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (array2.Length < 5)
				{
					continue;
				}
				string text4 = array2[3].Trim();
				if (!text4.Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string text5 = array2[1];
				int num = text5.LastIndexOf(':');
				if (num <= 0)
				{
					continue;
				}
				int result3;
				if (int.TryParse(text5.Substring(num + 1), out int result) && int.TryParse(array2[4], out result3) && result > 0)
				{
					string text6 = result + "|" + result3;
					if (hashSet.Add(text6))
					{
						list.Add(new PortListenerInfo
						{
							Port = result,
							ProcessId = result3
						});
					}
				}
			}
		}
		catch (Exception exception)
		{
			AppLog.Error("调用 netstat 检测端口失败。", exception);
		}
		foreach (PortListenerInfo item in list)
		{
			FillProcessInfo(item);
		}
		return list;
	}

	private static List<PortListenerInfo> QueryApi()
	{
		List<PortListenerInfo> list = new List<PortListenerInfo>();
		try
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (IPEndPoint endPoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
			{
				string text = endPoint.Port + "|0";
				if (hashSet.Add(text))
				{
					list.Add(new PortListenerInfo
					{
						Port = endPoint.Port,
						ProcessId = 0
					});
				}
			}
		}
		catch (Exception exception)
		{
			AppLog.Error("通过系统 API 检测端口失败。", exception);
		}
		return list;
	}

	private static void FillProcessInfo(PortListenerInfo listener)
	{
		if (listener.ProcessId <= 0)
		{
			return;
		}
		try
		{
			using Process process = Process.GetProcessById(listener.ProcessId);
			listener.ProcessName = process.ProcessName;
			try
			{
				listener.ProcessPath = process.MainModule?.FileName ?? string.Empty;
			}
			catch
			{
				listener.ProcessPath = string.Empty;
			}
		}
		catch
		{
			listener.ProcessName = string.Empty;
		}
	}
}
