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
		bool uninstallMode = false;
		foreach (string a in args)
		{
			if (string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase))
			{
				uninstallMode = true;
			}
		}
		string installDirArgument = GetArgument(args, "--install-dir");
		if (!string.IsNullOrWhiteSpace(installDirArgument))
		{
			SetupForm.InstallDirectoryOverride = installDirArgument;
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		string argument = GetArgument(args, "--render-preview");
		if (!string.IsNullOrWhiteSpace(argument))
		{
			RenderPreview(uninstallMode, argument);
			return;
		}
		if (HasArgument(args, "--silent-install"))
		{
			using (SetupForm setupForm = new SetupForm(uninstallMode: false))
			{
				Environment.ExitCode = setupForm.RunSilentInstall(!HasArgument(args, "--no-desktop"), !HasArgument(args, "--no-launch"));
				return;
			}
		}
		if (HasArgument(args, "--silent-uninstall"))
		{
			using (SetupForm setupForm2 = new SetupForm(uninstallMode: true))
			{
				Environment.ExitCode = setupForm2.RunSilentUninstall();
				return;
			}
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

	private static void RenderPreview(bool uninstallMode, string path)
	{
		using SetupForm setupForm = new SetupForm(uninstallMode);
		setupForm.StartPosition = FormStartPosition.Manual;
		setupForm.Location = new Point(-4000, -4000);
		setupForm.ShowInTaskbar = false;
		setupForm.Show();
		Application.DoEvents();
		Thread.Sleep(400);
		Application.DoEvents();
		setupForm.PerformLayout();
		setupForm.Refresh();
		int width = setupForm.Width;
		int height = setupForm.Height;
		if (GetWindowRect(setupForm.Handle, out var rectangle))
		{
			width = Math.Max(1, rectangle.Right - rectangle.Left);
			height = Math.Max(1, rectangle.Bottom - rectangle.Top);
		}
		using Bitmap bitmap = new Bitmap(width, height);
		bool flag = false;
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			IntPtr hdc = graphics.GetHdc();
			try
			{
				flag = PrintWindow(setupForm.Handle, hdc, 2u);
			}
			finally
			{
				graphics.ReleaseHdc(hdc);
			}
		}
		if (!flag)
		{
			setupForm.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
		}
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
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
		foreach (string a in args)
		{
			if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
