using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MySqlServiceController.Setup;

internal static class SetupProgram
{
	private struct NativeRect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

	[DllImport("user32.dll")]
	private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

	[STAThread]
	private static void Main(string[] args)
	{
		bool uninstallMode = HasArgument(args, "--uninstall");
		ConfigureNetwork();
		SetupTheme.SetMode(ParseTheme(args));
		if (!string.IsNullOrWhiteSpace(GetArgument(args, "--install-dir")))
		{
			SetupForm.InstallDirectoryOverride = GetArgument(args, "--install-dir");
		}
		string scopeArgument = GetArgument(args, "--scope");
		if (!string.IsNullOrWhiteSpace(scopeArgument))
		{
			SetupForm.ScopeOverride = (string.Equals(scopeArgument, "machine", StringComparison.OrdinalIgnoreCase) ? InstallScope.AllUsers : InstallScope.CurrentUser);
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		string jobPath = GetArgument(args, "--apply-job");
		if (!string.IsNullOrWhiteSpace(jobPath))
		{
			Environment.ExitCode = RunJob(jobPath, GetArgument(args, "--result"), GetArgument(args, "--progress"));
			return;
		}
		string previewPath = GetArgument(args, "--render-preview");
		if (!string.IsNullOrWhiteSpace(previewPath))
		{
			RenderPreview(uninstallMode, previewPath);
			return;
		}
		if (HasArgument(args, "--silent-install"))
		{
			using SetupForm form = new SetupForm(uninstallMode: false);
			Environment.ExitCode = form.RunSilentInstall(!HasArgument(args, "--no-desktop"), !HasArgument(args, "--no-launch"));
			return;
		}
		if (HasArgument(args, "--silent-uninstall"))
		{
			using SetupForm form2 = new SetupForm(uninstallMode: true);
			Environment.ExitCode = form2.RunSilentUninstall();
			return;
		}
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
		{
			MessageBox.Show("安装程序遇到错误：\n\n" + e.Exception.Message, "MySQL管家", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		};
		try
		{
			Application.Run(new SetupForm(uninstallMode));
		}
		catch (Exception ex)
		{
			MessageBox.Show("安装程序遇到错误：\n\n" + ex.Message, "MySQL管家", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	/// <summary>.NET Framework 4.8 默认不启用 TLS 1.2，Oracle 下载站与 CDN 都要求 TLS 1.2。</summary>
	private static void ConfigureNetwork()
	{
		try
		{
			System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11;
		}
		catch
		{
		}
		try
		{
			System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)12288;
		}
		catch
		{
		}
		System.Net.ServicePointManager.Expect100Continue = false;
	}

	/// <summary>提权子进程入口：执行任务文件并回写结果，不显示界面。</summary>
	private static int RunJob(string jobPath, string resultPath, string progressPath)
	{
		SetupResult result;
		try
		{
			SetupJob job = SetupJob.Load(jobPath);
			ISetupProgress progress = (string.IsNullOrWhiteSpace(progressPath) ? new DelegateProgress(delegate { }) : new FileProgress(progressPath));
			result = SetupRunner.Run(job, progress);
		}
		catch (Exception ex)
		{
			SetupLog.Write("子进程执行失败：" + ex);
			result = new SetupResult
			{
				Code = 1,
				Title = "安装失败",
				Message = ex.Message,
				NeedsAttention = true
			};
		}
		if (!string.IsNullOrWhiteSpace(resultPath))
		{
			try
			{
				result.Save(resultPath);
			}
			catch (Exception ex2)
			{
				SetupLog.Write("写入结果文件失败：" + ex2.Message);
			}
		}
		return result.Code;
	}

	private static SetupThemeMode ParseTheme(string[] args)
	{
		string value = GetArgument(args, "--theme");
		if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
		{
			return SetupThemeMode.Dark;
		}
		if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
		{
			return SetupThemeMode.Light;
		}
		return SetupThemeMode.System;
	}

	private static void RenderPreview(bool uninstallMode, string path)
	{
		using SetupForm form = new SetupForm(uninstallMode);
		form.StartPosition = FormStartPosition.Manual;
		form.Location = new Point(-4000, -4000);
		form.ShowInTaskbar = false;
		form.Show();
		Application.DoEvents();
		Thread.Sleep(500);
		Application.DoEvents();
		form.PerformLayout();
		form.Refresh();
		int width = form.Width;
		int height = form.Height;
		if (GetWindowRect(form.Handle, out NativeRect rectangle))
		{
			width = Math.Max(1, rectangle.Right - rectangle.Left);
			height = Math.Max(1, rectangle.Bottom - rectangle.Top);
		}
		using Bitmap bitmap = new Bitmap(width, height);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			IntPtr hdc = graphics.GetHdc();
			try
			{
				PrintWindow(form.Handle, hdc, 2u);
			}
			finally
			{
				graphics.ReleaseHdc(hdc);
			}
		}
		string directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}
		bitmap.Save(path, ImageFormat.Png);
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

	private static bool HasArgument(string[] args, string name)
	{
		foreach (string text in args)
		{
			if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
