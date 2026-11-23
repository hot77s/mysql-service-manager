using System;
using System.Diagnostics;
using System.Threading;

namespace MySqlServiceController.Setup;

/// <summary>覆盖安装前等待旧进程退出，避免安装包与正在运行的程序抢占文件。</summary>
internal static class SetupWait
{
	private const int TimeoutSeconds = 90;

	public static void WaitForExit(string pidText)
	{
		if (string.IsNullOrWhiteSpace(pidText) || !int.TryParse(pidText.Trim(), out int pid) || pid <= 0)
		{
			return;
		}
		if (pid == Process.GetCurrentProcess().Id)
		{
			return;
		}
		DateTime deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);
		bool exited = false;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using Process process = Process.GetProcessById(pid);
				if (process.HasExited)
				{
					exited = true;
					break;
				}
			}
			catch (ArgumentException)
			{
				exited = true;
				break;
			}
			catch
			{
				exited = true;
				break;
			}
			Thread.Sleep(200);
		}
		if (!exited)
		{
			SetupLog.Write("等待旧进程退出超时（PID " + pid + "），继续安装。");
			return;
		}
		// 再等一小会儿，让旧进程完成退出清理、释放文件句柄。
		Thread.Sleep(600);
	}
}
