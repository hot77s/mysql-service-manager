using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MySqlServiceController;

internal static class ElevationHelper
{
	public static bool TryHandleElevatedAction(string[] args)
	{
		string argument = GetArgument(args, "--elevated-action");
		if (string.IsNullOrWhiteSpace(argument))
		{
			return false;
		}
		string argument2 = GetArgument(args, "--service");
		string argument3 = GetArgument(args, "--result");
		ServiceActionResult serviceActionResult;
		if (string.Equals(argument, "start", StringComparison.OrdinalIgnoreCase))
		{
			serviceActionResult = ServiceManager.StartService(argument2);
		}
		else if (string.Equals(argument, "stop", StringComparison.OrdinalIgnoreCase))
		{
			serviceActionResult = ServiceManager.StopService(argument2);
		}
		else
		{
			ServiceActionResult serviceActionResult2 = new ServiceActionResult();
			serviceActionResult2.Success = false;
			serviceActionResult2.Message = "未知操作：" + argument;
			serviceActionResult = serviceActionResult2;
		}
		if (!string.IsNullOrWhiteSpace(argument3))
		{
			WriteResult(argument3, serviceActionResult);
		}
		Environment.ExitCode = ((!serviceActionResult.Success) ? 1 : 0);
		return true;
	}

	public static ServiceActionResult RunElevatedAction(string action, string serviceName)
	{
		string text = Path.Combine(Path.GetTempPath(), "MySQLManager-" + Guid.NewGuid().ToString("N") + ".result");
		try
		{
			string arguments = "--elevated-action " + QuoteArgument(action) + " --service " + QuoteArgument(serviceName) + " --result " + QuoteArgument(text);
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = Process.GetCurrentProcess().MainModule.FileName;
			processStartInfo.Arguments = arguments;
			processStartInfo.UseShellExecute = true;
			processStartInfo.Verb = "runas";
			processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			processStartInfo.WorkingDirectory = Path.GetDirectoryName(processStartInfo.FileName);
			using Process process = Process.Start(processStartInfo);
			if (process == null)
			{
				ServiceActionResult serviceActionResult = new ServiceActionResult();
				serviceActionResult.Success = false;
				serviceActionResult.Message = "无法启动管理员授权进程。";
				serviceActionResult.Detail = "请关闭程序后，右键选择“以管理员身份运行”。";
				return serviceActionResult;
			}
			if (!process.WaitForExit(60000))
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
				ServiceActionResult serviceActionResult2 = new ServiceActionResult();
				serviceActionResult2.Success = false;
				serviceActionResult2.Message = "管理员操作在 60 秒内没有完成。";
				serviceActionResult2.Detail = "请检查服务是否卡在启动/停止状态，然后重试。";
				return serviceActionResult2;
			}
			ServiceActionResult serviceActionResult3 = ReadResult(text);
			if (serviceActionResult3 != null)
			{
				return serviceActionResult3;
			}
			ServiceActionResult serviceActionResult4 = new ServiceActionResult();
			serviceActionResult4.Success = process.ExitCode == 0;
			serviceActionResult4.Message = ((process.ExitCode == 0) ? "操作已完成。" : ("管理员进程执行失败（退出代码 " + process.ExitCode + "）。"));
			serviceActionResult4.Detail = "请查看程序日志：" + AppLog.LogPath;
			return serviceActionResult4;
		}
		catch (Win32Exception ex)
		{
			if (ex.NativeErrorCode == 1223)
			{
				AppLog.Warning("用户取消了 UAC 授权。");
				ServiceActionResult serviceActionResult5 = new ServiceActionResult();
				serviceActionResult5.Success = false;
				serviceActionResult5.WasCancelled = true;
				serviceActionResult5.Message = "已取消管理员授权，服务没有发生改变。";
				serviceActionResult5.Detail = "再次点击启动或停止按钮，并在 UAC 窗口中选择“是”。";
				return serviceActionResult5;
			}
			AppLog.Error("请求管理员授权失败。", ex);
			ServiceActionResult serviceActionResult6 = new ServiceActionResult();
			serviceActionResult6.Success = false;
			serviceActionResult6.Message = "无法请求管理员授权（Windows 错误 " + ex.NativeErrorCode + "）。";
			serviceActionResult6.Detail = "请右键程序并选择“以管理员身份运行”，或联系系统管理员。";
			return serviceActionResult6;
		}
		catch (Exception ex2)
		{
			AppLog.Error("管理员操作失败。", ex2);
			ServiceActionResult serviceActionResult7 = new ServiceActionResult();
			serviceActionResult7.Success = false;
			serviceActionResult7.Message = "管理员操作失败：" + ex2.Message;
			serviceActionResult7.Detail = "请查看程序日志：" + AppLog.LogPath;
			return serviceActionResult7;
		}
		finally
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
		}
	}

	private static void WriteResult(string path, ServiceActionResult result)
	{
		try
		{
			string text = (result.Success ? "OK" : "ERROR");
			string text2 = Convert.ToBase64String(Encoding.UTF8.GetBytes(result.Message ?? string.Empty));
			string text3 = Convert.ToBase64String(Encoding.UTF8.GetBytes(result.Detail ?? string.Empty));
			File.WriteAllText(path, text + "\n" + text2 + "\n" + text3, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch (Exception exception)
		{
			AppLog.Error("写入管理员操作结果失败。", exception);
		}
	}

	private static ServiceActionResult ReadResult(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			string[] array = File.ReadAllLines(path, Encoding.UTF8);
			if (array.Length < 2)
			{
				return null;
			}
			ServiceActionResult serviceActionResult = new ServiceActionResult();
			serviceActionResult.Success = string.Equals(array[0], "OK", StringComparison.Ordinal);
			serviceActionResult.Message = ((array.Length > 1) ? Encoding.UTF8.GetString(Convert.FromBase64String(array[1])) : string.Empty);
			serviceActionResult.Detail = ((array.Length > 2) ? Encoding.UTF8.GetString(Convert.FromBase64String(array[2])) : string.Empty);
			return serviceActionResult;
		}
		catch (Exception exception)
		{
			AppLog.Error("读取管理员操作结果失败。", exception);
			return null;
		}
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

	private static string QuoteArgument(string value)
	{
		return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
	}
}
