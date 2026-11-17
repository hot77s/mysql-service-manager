using System;
using System.Threading;
using System.Windows.Forms;

namespace MySqlServiceController;

internal static class Program
{
	private const string MutexName = "Local\\MySQLManager-1F833D0C";

	[STAThread]
	private static void Main(string[] args)
	{
		AppLog.Info("程序入口已启动。版本 " + Application.ProductVersion);
		if (ElevationHelper.TryHandleElevatedAction(args))
		{
			return;
		}
		bool createdNew;
		using (new Mutex(initiallyOwned: true, MutexName, out createdNew))
		{
			if (!createdNew)
			{
				MessageBox.Show("MySQL管家已经在运行。", "MySQL管家", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			Application.EnableVisualStyles();
			DarkMode.Apply(Theme.SystemUsesDarkTheme());
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += OnThreadException;
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			try
			{
				AppLog.Info("正在创建主界面。");
				MainForm mainForm = new MainForm();
				AppLog.Info("主界面已创建，正在进入消息循环。");
				Application.Run(mainForm);
				AppLog.Info("程序消息循环已结束。");
			}
			catch (Exception exception)
			{
				AppLog.Error("程序主循环异常。", exception);
				ShowFatalError(exception);
			}
		}
	}

	private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
	{
		AppLog.Error("界面线程发生未处理异常。", e.Exception);
		ShowFatalError(e.Exception);
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		Exception exception = e.ExceptionObject as Exception;
		AppLog.Error("程序发生未处理异常。", exception);
	}

	private static void ShowFatalError(Exception exception)
	{
		MessageBox.Show("程序遇到意外错误，操作可能没有完成。\n\n" + exception.Message + "\n\n日志位置：" + AppLog.LogPath, "MySQL管家", MessageBoxButtons.OK, MessageBoxIcon.Hand);
	}
}
