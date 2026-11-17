using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MySqlServiceController.Setup;

internal sealed class SetupForm : Form
{
	[DllImport("shell32.dll")]
	private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

	private const string AppProductName = "MySQL管家";

	private const string AppVersion = "1.4.7";

	private const string Publisher = "77's";

	private const string MainExecutableName = "MySQLManager.exe";

	private const string LegacyExecutableName = "MySQLServiceController.exe";

	private const string UninstallKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MySQLServiceController";

	private readonly bool _uninstallMode;

	private readonly List<string> _warnings = new List<string>();

	private CheckBox _desktopShortcutCheck;

	private CheckBox _launchCheck;

	private Label _statusLabel;

	private SetupButton _primaryButton;

	private TextBox _installPathTextBox;

	private string _installDirectory;

	internal static string InstallDirectoryOverride;

	internal static string DefaultInstallDirectory
	{
		get
		{
			string environmentVariable = Environment.GetEnvironmentVariable("MYSQLSC_INSTALL_DIR");
			if (string.Equals(Environment.GetEnvironmentVariable("MYSQLSC_TEST_MODE"), "1", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(environmentVariable))
			{
				return Path.GetFullPath(environmentVariable);
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MySQLManager");
		}
	}

	private string InstalledExecutable => Path.Combine(_installDirectory, MainExecutableName);

	private string UninstallExecutable => Path.Combine(_installDirectory, "Uninstall.exe");

	private static string UserDataDirectory
	{
		get
		{
			string environmentVariable = Environment.GetEnvironmentVariable("MYSQLSC_DATA_DIR");
			if (!string.IsNullOrWhiteSpace(environmentVariable))
			{
				return Path.GetFullPath(environmentVariable);
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MySQLServiceController");
		}
	}

	public SetupForm(bool uninstallMode)
	{
		_uninstallMode = uninstallMode;
		_installDirectory = GetInitialInstallDirectory(uninstallMode);
		Text = (uninstallMode ? "卸载 MySQL管家" : "安装 MySQL管家");
		base.ClientSize = new Size(600, 514);
		base.StartPosition = FormStartPosition.CenterScreen;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		BackColor = SetupTheme.Background;
		Font = SetupTheme.Font(9.3f, FontStyle.Regular);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		try
		{
			base.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch
		{
		}
		BuildInterface();
	}

	private static string GetInitialInstallDirectory(bool uninstallMode)
	{
		if (!uninstallMode && !string.IsNullOrWhiteSpace(InstallDirectoryOverride))
		{
			return Path.GetFullPath(InstallDirectoryOverride);
		}
		if (uninstallMode)
		{
			try
			{
				string directoryName = Path.GetDirectoryName(Path.GetFullPath(Application.ExecutablePath));
				if (!string.IsNullOrWhiteSpace(directoryName) && DirectoryHasInstallation(directoryName))
				{
					return directoryName;
				}
			}
			catch
			{
			}
		}
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MySQLServiceController", writable: false);
			if (registryKey != null)
			{
				string text = registryKey.GetValue("InstallLocation") as string;
				if (!string.IsNullOrWhiteSpace(text) && DirectoryHasInstallation(text))
				{
					return Path.GetFullPath(text);
				}
			}
		}
		catch
		{
		}
		return DefaultInstallDirectory;
	}

	private static bool DirectoryHasInstallation(string directory)
	{
		return File.Exists(Path.Combine(directory, "MySQLManager.exe")) || File.Exists(Path.Combine(directory, "MySQLServiceController.exe"));
	}

	private void BuildInterface()
	{
		SuspendLayout();
		HeaderPanel headerPanel = new HeaderPanel();
		headerPanel.Location = new Point(0, 0);
		headerPanel.Size = new Size(600, 96);
		base.Controls.Add(headerPanel);
		PictureBox pictureBox = new PictureBox();
		pictureBox.Location = new Point(30, 28);
		pictureBox.Size = new Size(64, 64);
		pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
		pictureBox.BackColor = Color.Transparent;
		try
		{
			using Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			pictureBox.Image = icon.ToBitmap();
		}
		catch
		{
		}
		headerPanel.Controls.Add(pictureBox);
		Label label = new Label();
		label.Text = (_uninstallMode ? "卸载 MySQL管家" : "安装 MySQL管家");
		label.Location = new Point(112, 27);
		label.Size = new Size(430, 38);
		label.Font = SetupTheme.Font(18f, FontStyle.Bold);
		label.ForeColor = Color.White;
		label.BackColor = Color.Transparent;
		label.AutoSize = true;
		label.MaximumSize = new Size(450, 0);
		headerPanel.Controls.Add(label);
		Label label2 = new Label();
		label2.Text = (_uninstallMode ? "移除程序、快捷方式和配置，不影响 MySQL 服务与数据" : ("一键启停 MySQL 服务，无需数据库密码  ·  v" + AppVersion));
		label2.Location = new Point(114, 67);
		label2.Size = new Size(430, 24);
		label2.Font = SetupTheme.Font(9f, FontStyle.Regular);
		label2.ForeColor = Color.FromArgb(225, 235, 255);
		label2.BackColor = Color.Transparent;
		label2.AutoSize = true;
		label2.MaximumSize = new Size(450, 0);
		headerPanel.Controls.Add(label2);
		Panel panel = new Panel();
		panel.Location = new Point(28, 96);
		panel.Size = new Size(544, 314);
		panel.BackColor = SetupTheme.Surface;
		panel.Paint += Card_Paint;
		base.Controls.Add(panel);
		panel.BringToFront();
		Label label3 = new Label();
		label3.Text = (_uninstallMode ? "将从当前用户账户中卸载程序，并移除开始菜单和桌面快捷方式。" : "程序将安装到所选目录，不需要管理员权限。");
		label3.Location = new Point(24, 20);
		label3.Size = new Size(490, 24);
		label3.Font = SetupTheme.Font(9.2f, FontStyle.Regular);
		label3.ForeColor = SetupTheme.Text;
		label3.AutoSize = true;
		label3.MaximumSize = new Size(496, 0);
		panel.Controls.Add(label3);
		Label label4 = new Label();
		label4.Text = "安装位置";
		label4.Location = new Point(24, 60);
		label4.Size = new Size(120, 20);
		label4.Font = SetupTheme.Font(8.6f, FontStyle.Bold);
		label4.ForeColor = SetupTheme.Muted;
		label4.AutoSize = true;
		panel.Controls.Add(label4);
		_installPathTextBox = new TextBox();
		_installPathTextBox.Text = _installDirectory;
		_installPathTextBox.Location = new Point(24, 82);
		_installPathTextBox.Size = new Size(390, 30);
		_installPathTextBox.Font = SetupTheme.Font(8.2f, FontStyle.Regular);
		_installPathTextBox.ForeColor = SetupTheme.Text;
		_installPathTextBox.BackColor = (_uninstallMode ? Color.FromArgb(247, 249, 252) : SetupTheme.Surface);
		_installPathTextBox.BorderStyle = BorderStyle.FixedSingle;
		_installPathTextBox.ReadOnly = _uninstallMode;
		_installPathTextBox.SelectionStart = 0;
		_installPathTextBox.SelectionLength = 0;
		panel.Controls.Add(_installPathTextBox);
		if (!_uninstallMode)
		{
			SetupButton setupButton = new SetupButton();
			setupButton.Text = "浏览…";
			setupButton.Location = new Point(430, 115);
			setupButton.Size = new Size(90, 32);
			setupButton.NormalColor = Color.FromArgb(235, 241, 253);
			setupButton.ForeColor = SetupTheme.Accent;
			setupButton.Font = SetupTheme.Font(8.8f, FontStyle.Bold);
			setupButton.SurfaceColor = SetupTheme.Surface;
			setupButton.Click += BrowseButton_Click;
			panel.Controls.Add(setupButton);
			LinkLabel linkLabel = new LinkLabel();
			linkLabel.Text = "恢复默认位置";
			linkLabel.Location = new Point(24, 119);
			linkLabel.Size = new Size(120, 22);
			linkLabel.Font = SetupTheme.Font(8.3f, FontStyle.Regular);
			linkLabel.LinkColor = SetupTheme.Accent;
			linkLabel.ActiveLinkColor = SetupTheme.AccentDark;
			linkLabel.VisitedLinkColor = SetupTheme.Accent;
			linkLabel.LinkBehavior = LinkBehavior.HoverUnderline;
			linkLabel.BackColor = SetupTheme.Surface;
			linkLabel.LinkClicked += RestoreDefaultLink_LinkClicked;
			panel.Controls.Add(linkLabel);
		}
		else
		{
			Label label5 = new Label();
			label5.Text = "卸载程序将从该目录移除已安装文件。";
			label5.Location = new Point(24, 118);
			label5.Size = new Size(496, 20);
			label5.Font = SetupTheme.Font(8.3f, FontStyle.Regular);
			label5.ForeColor = SetupTheme.Subtle;
			label5.BackColor = SetupTheme.Surface;
			label5.AutoSize = true;
			label5.MaximumSize = new Size(496, 0);
			panel.Controls.Add(label5);
		}
		Panel panel2 = new Panel();
		panel2.Location = new Point(24, 150);
		panel2.Size = new Size(496, 1);
		panel2.BackColor = SetupTheme.Border;
		panel.Controls.Add(panel2);
		if (!_uninstallMode)
		{
			_desktopShortcutCheck = MakeCheckBox("创建桌面快捷方式", 24, 164, 460, isChecked: true);
			panel.Controls.Add(_desktopShortcutCheck);
			_launchCheck = MakeCheckBox("安装完成后启动程序", 24, 196, 460, isChecked: true);
			panel.Controls.Add(_launchCheck);
			Label label6 = new Label();
			label6.Text = "权限说明";
			label6.Location = new Point(24, 231);
			label6.Size = new Size(100, 20);
			label6.Font = SetupTheme.Font(8.8f, FontStyle.Bold);
			label6.ForeColor = SetupTheme.Text;
			label6.AutoSize = true;
			panel.Controls.Add(label6);
			Label label7 = new Label();
			label7.Text = "安装本身不需要管理员权限。点击“启动服务”或“停止服务”时，Windows 会弹出 UAC 授权窗口；程序不会保存 MySQL 账号或密码。";
			label7.Location = new Point(24, 254);
			label7.Size = new Size(490, 48);
			label7.Font = SetupTheme.Font(8.4f, FontStyle.Regular);
			label7.ForeColor = SetupTheme.Muted;
			label7.AutoSize = true;
			label7.MaximumSize = new Size(490, 0);
			panel.Controls.Add(label7);
		}
		else
		{
			Label label8 = new Label();
			label8.Text = "程序配置、日志和快捷方式将全部清理，不会删除 MySQL 数据。";
			label8.Location = new Point(24, 166);
			label8.Size = new Size(490, 24);
			label8.Font = SetupTheme.Font(8.8f, FontStyle.Regular);
			label8.ForeColor = SetupTheme.Text;
			label8.BackColor = SetupTheme.Surface;
			label8.AutoSize = true;
			label8.MaximumSize = new Size(490, 0);
			panel.Controls.Add(label8);
			Label label9 = new Label();
			label9.Text = "安全说明";
			label9.Location = new Point(24, 212);
			label9.Size = new Size(100, 20);
			label9.Font = SetupTheme.Font(8.8f, FontStyle.Bold);
			label9.ForeColor = SetupTheme.Text;
			label9.AutoSize = true;
			panel.Controls.Add(label9);
			Label label10 = new Label();
			label10.Text = "卸载不会停止或删除 Windows MySQL 服务，也不会删除数据库文件。程序配置、日志和临时文件会一并清理。";
			label10.Location = new Point(24, 235);
			label10.Size = new Size(490, 42);
			label10.Font = SetupTheme.Font(8.4f, FontStyle.Regular);
			label10.ForeColor = SetupTheme.Muted;
			label10.AutoSize = true;
			label10.MaximumSize = new Size(490, 0);
			panel.Controls.Add(label10);
		}
		_statusLabel = new Label();
		_statusLabel.Text = string.Empty;
		_statusLabel.Location = new Point(118, 414);
		_statusLabel.Size = new Size(272, 24);
		_statusLabel.Font = SetupTheme.Font(8.5f, FontStyle.Regular);
		_statusLabel.ForeColor = SetupTheme.Muted;
		_statusLabel.AutoSize = true;
		base.Controls.Add(_statusLabel);
		Label label11 = new Label();
		label11.Text = "作者  77's";
		label11.Location = new Point(30, 414);
		label11.Size = new Size(82, 24);
		label11.Font = SetupTheme.Font(8.5f, FontStyle.Regular);
		label11.ForeColor = SetupTheme.Muted;
		label11.BackColor = SetupTheme.Background;
		label11.AutoSize = true;
		base.Controls.Add(label11);
		SetupButton setupButton2 = new SetupButton();
		setupButton2.Text = "取消";
		setupButton2.Location = new Point(344, 446);
		setupButton2.Size = new Size(108, 42);
		setupButton2.NormalColor = Color.FromArgb(235, 238, 244);
		setupButton2.ForeColor = SetupTheme.Text;
		setupButton2.Click += delegate
		{
			Close();
		};
		base.Controls.Add(setupButton2);
		_primaryButton = new SetupButton();
		_primaryButton.Text = (_uninstallMode ? "卸载" : "安装");
		_primaryButton.Location = new Point(464, 446);
		_primaryButton.Size = new Size(108, 42);
		_primaryButton.NormalColor = (_uninstallMode ? SetupTheme.Danger : SetupTheme.Accent);
		_primaryButton.Click += PrimaryButton_Click;
		base.Controls.Add(_primaryButton);
		base.AcceptButton = _primaryButton;
		base.CancelButton = setupButton2;
		base.Shown += delegate
		{
			_primaryButton.Focus();
		};
		ResumeLayout(performLayout: false);
	}

	private static CheckBox MakeCheckBox(string text, int x, int y, int width, bool isChecked)
	{
		CheckBox checkBox = new CheckBox();
		checkBox.Text = text;
		checkBox.Location = new Point(x, y);
		checkBox.Size = new Size(width, 24);
		checkBox.Font = SetupTheme.Font(8.8f, FontStyle.Regular);
		checkBox.ForeColor = SetupTheme.Text;
		checkBox.BackColor = SetupTheme.Surface;
		checkBox.Checked = isChecked;
		checkBox.AutoSize = true;
		checkBox.MaximumSize = new Size(width, 0);
		return checkBox;
	}

	private static void Card_Paint(object sender, PaintEventArgs e)
	{
		if (!(sender is Control control))
		{
			return;
		}
		using Pen pen = new Pen(SetupTheme.Border);
		e.Graphics.DrawRectangle(pen, 0, 0, control.ClientSize.Width - 1, control.ClientSize.Height - 1);
	}

	private void BrowseButton_Click(object sender, EventArgs e)
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
		folderBrowserDialog.Description = "选择 MySQL管家的安装文件夹";
		folderBrowserDialog.ShowNewFolderButton = true;
		string text = (_installPathTextBox.Text ?? string.Empty).Trim().Trim('"');
		if (Directory.Exists(text))
		{
			folderBrowserDialog.SelectedPath = text;
		}
		else
		{
			try
			{
				string directoryName = Path.GetDirectoryName(text);
				if (!string.IsNullOrWhiteSpace(directoryName) && Directory.Exists(directoryName))
				{
					folderBrowserDialog.SelectedPath = directoryName;
				}
			}
			catch
			{
			}
		}
		if (folderBrowserDialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(folderBrowserDialog.SelectedPath);
		}
		catch (Exception ex)
		{
			MessageBox.Show("所选路径无效：\n\n" + ex.Message, "安装位置无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		if (!Directory.Exists(fullPath) || IsDirectoryEmpty(fullPath) || IsOurInstallationDirectory(fullPath))
		{
			_installPathTextBox.Text = fullPath;
			return;
		}
		string text2 = Path.Combine(fullPath, "MySQL Service Controller");
		DialogResult dialogResult = MessageBox.Show("所选文件夹不是空文件夹，不能直接安装到其中。\n\n是否创建专用子文件夹？\n" + text2, "选择安装位置", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
		if (dialogResult == DialogResult.Yes)
		{
			_installPathTextBox.Text = text2;
		}
	}

	private void RestoreDefaultLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
	{
		_installPathTextBox.Text = DefaultInstallDirectory;
	}

	private bool PrepareSelectedInstallDirectory(bool interactive)
	{
		string text = (_installPathTextBox.Text ?? string.Empty).Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(text))
		{
			return ReportDirectoryError("请选择安装位置。", interactive);
		}
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(text);
		}
		catch (Exception ex)
		{
			return ReportDirectoryError("安装路径无效：\n\n" + ex.Message, interactive);
		}
		if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetPathRoot(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
		{
			return ReportDirectoryError("不能直接安装到磁盘根目录，请选择一个专用文件夹。", interactive);
		}
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		if (IsSameOrChildPath(fullPath, folderPath))
		{
			return ReportDirectoryError("不能安装到 Windows 系统目录，请选择其他位置。", interactive);
		}
		try
		{
			if (Directory.Exists(fullPath))
			{
				if (!IsDirectoryEmpty(fullPath) && !IsOurInstallationDirectory(fullPath))
				{
					return ReportDirectoryError("所选文件夹不是空文件夹。\n\n请选择专用空文件夹，或先在其中创建“MySQL Service Controller”子文件夹。", interactive);
				}
			}
			else
			{
				Directory.CreateDirectory(fullPath);
			}
			string path = Path.Combine(fullPath, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
			using (FileStream fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.WriteByte(0);
			}
			File.Delete(path);
		}
		catch (Exception ex2)
		{
			return ReportDirectoryError("无法写入所选安装位置：\n\n" + fullPath + "\n\n" + ex2.Message + "\n\n请选择当前用户可写的文件夹。", interactive);
		}
		_installDirectory = fullPath;
		_installPathTextBox.Text = fullPath;
		return true;
	}

	private bool ReportDirectoryError(string message, bool interactive)
	{
		if (interactive)
		{
			MessageBox.Show(message, "安装位置无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			_installPathTextBox.Focus();
			_installPathTextBox.SelectAll();
		}
		return false;
	}

	private static bool IsOurInstallationDirectory(string directory)
	{
		try
		{
			return File.Exists(Path.Combine(directory, "MySQLServiceController.exe")) || File.Exists(Path.Combine(directory, "Uninstall.exe"));
		}
		catch
		{
			return false;
		}
	}

	private static bool IsDirectoryEmpty(string directory)
	{
		try
		{
			return Directory.GetFileSystemEntries(directory).Length == 0;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsSameOrChildPath(string path, string parent)
	{
		if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parent))
		{
			return false;
		}
		string text = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string text2 = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (string.Equals(text, text2, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return text.StartsWith(text2 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private void PrimaryButton_Click(object sender, EventArgs e)
	{
		if (_uninstallMode)
		{
			Uninstall();
		}
		else
		{
			Install();
		}
	}

	private void Install()
	{
		InstallCore(interactive: true);
	}

	private int InstallCore(bool interactive)
	{
		if (IsApplicationRunning())
		{
			if (!interactive)
			{
				return 2;
			}
			DialogResult dialogResult = MessageBox.Show("MySQL管家正在运行。\n\n是否关闭正在运行的程序并继续安装？", "程序正在运行", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
			if (dialogResult != DialogResult.Yes || !TryCloseRunningApplication())
			{
				MessageBox.Show("未能关闭正在运行的程序。\n\n如果正在启动或停止服务，请等待操作完成后再安装。", "无法继续安装", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return 2;
			}
		}
		if (!PrepareSelectedInstallDirectory(interactive))
		{
			return 3;
		}
		if (interactive)
		{
			SetBusy(busy: true, "正在安装…");
		}
		try
		{
			_warnings.Clear();
			Directory.CreateDirectory(_installDirectory);
			ExtractResource("payload.app", InstalledExecutable);
			ExtractResourceIfPresent("payload.config", InstalledExecutable + ".config");
			ExtractResourceIfPresent("payload.guide", Path.Combine(_installDirectory, "使用说明.txt"));
			string executablePath = Application.ExecutablePath;
			if (!string.Equals(Path.GetFullPath(executablePath), Path.GetFullPath(UninstallExecutable), StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(executablePath, UninstallExecutable, overwrite: true);
			}
			DeleteLegacyInstallation();
			try
			{
				WriteUninstallRegistry();
			}
			catch
			{
				_warnings.Add("未能写入“应用和功能”卸载信息，可运行安装目录中的 Uninstall.exe 卸载。");
			}
			try
			{
				CreateOrRemoveShortcuts();
				try
				{
					SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
				}
				catch
				{
				}
			}
			catch
			{
				_warnings.Add("未能创建快捷方式，可直接运行安装目录中的程序。");
			}
			if (_launchCheck.Checked)
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo(InstalledExecutable);
				processStartInfo.UseShellExecute = true;
				processStartInfo.WorkingDirectory = _installDirectory;
				Process.Start(processStartInfo);
			}
			string text = "MySQL管家 " + AppVersion + " 已安装完成。\n\n安装位置：\n" + _installDirectory + "\n\n配置与日志会保留在原来的位置，无需重新配置。";
			if (_warnings.Count > 0)
			{
				text = text + "\n\n注意：\n" + string.Join("\n", _warnings.ToArray());
			}
			if (interactive)
			{
				MessageBox.Show(text, "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				Close();
			}
			return 0;
		}
		catch (Exception ex)
		{
			if (interactive)
			{
				MessageBox.Show("安装失败：\n\n" + ex.Message + "\n\n如文件被占用，请关闭程序后重试。", "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				SetBusy(busy: false, "安装未完成");
			}
			return 1;
		}
	}

	private void Uninstall()
	{
		if (IsApplicationRunning())
		{
			MessageBox.Show("MySQL管家正在运行。\n\n请先关闭主程序，再执行卸载。", "程序正在运行", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		DialogResult dialogResult = MessageBox.Show("确定要卸载 MySQL管家吗？\n\n此操作会移除程序、快捷方式、程序配置和日志，但不会删除 MySQL 服务、数据库或数据文件。", "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
		if (dialogResult == DialogResult.Yes)
		{
			UninstallCore(interactive: true);
		}
	}

	private int UninstallCore(bool interactive)
	{
		if (!ValidateUninstallDirectory(interactive))
		{
			return 3;
		}
		if (IsApplicationRunning())
		{
			if (interactive)
			{
				MessageBox.Show("MySQL管家正在运行。\n\n请先关闭主程序，再执行卸载。", "程序正在运行", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
			return 2;
		}
		if (interactive)
		{
			SetBusy(busy: true, "正在卸载…");
		}
		try
		{
			DeleteShortcut(GetStartMenuShortcutPath());
			DeleteShortcut(GetDesktopShortcutPath());
			using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MySQLServiceController", writable: true))
			{
				if (registryKey != null)
				{
					registryKey.Close();
					Registry.CurrentUser.DeleteSubKeyTree("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MySQLServiceController", throwOnMissingSubKey: false);
				}
			}
			ScheduleDirectoryRemoval();
			if (interactive)
			{
				MessageBox.Show("MySQL管家已卸载。\n\n程序配置、日志和临时文件已清理。\nMySQL 服务和数据库文件未作任何更改。", "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				Close();
			}
			return 0;
		}
		catch (Exception ex)
		{
			if (interactive)
			{
				MessageBox.Show("卸载失败：\n\n" + ex.Message + "\n\n请关闭程序后重试。", "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				SetBusy(busy: false, "卸载未完成");
			}
			return 1;
		}
	}

	private bool ValidateUninstallDirectory(bool interactive)
	{
		string text = null;
		try
		{
			string fullPath = Path.GetFullPath(_installDirectory);
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			DirectoryInfo directoryInfo = new DirectoryInfo(fullPath);
			if (string.Equals(fullPath, Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase) || IsSameOrChildPath(fullPath, folderPath) || !directoryInfo.Exists || (directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0 || !File.Exists(Path.Combine(fullPath, "Uninstall.exe")))
			{
				text = "无法确认有效的安装目录，已取消卸载。\n\n" + _installDirectory + "\n\n请从 Windows“应用和功能”中卸载，或运行原安装目录中的 Uninstall.exe。";
			}
		}
		catch (Exception ex)
		{
			text = "检查安装目录失败：\n\n" + ex.Message;
		}
		if (text == null)
		{
			return true;
		}
		if (interactive)
		{
			MessageBox.Show(text, "无法卸载", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
		return false;
	}

	internal int RunSilentInstall(bool createDesktopShortcut, bool launchApplication)
	{
		_desktopShortcutCheck.Checked = createDesktopShortcut;
		_launchCheck.Checked = launchApplication;
		return InstallCore(interactive: false);
	}

	internal int RunSilentUninstall()
	{
		return UninstallCore(interactive: false);
	}

	private void SetBusy(bool busy, string status)
	{
		_primaryButton.Enabled = !busy;
		_statusLabel.Text = status;
		Application.DoEvents();
	}

	private static void ExtractResource(string resourceName, string destination)
	{
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		using Stream stream = executingAssembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			throw new InvalidOperationException("安装包缺少资源：" + resourceName);
		}
		string text = destination + ".new";
		using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			stream.CopyTo(fileStream);
			fileStream.Flush(flushToDisk: true);
		}
		if (File.Exists(destination))
		{
			File.Replace(text, destination, null, ignoreMetadataErrors: true);
		}
		else
		{
			File.Move(text, destination);
		}
	}

	private static void ExtractResourceIfPresent(string resourceName, string destination)
	{
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		using (Stream stream = executingAssembly.GetManifestResourceStream(resourceName))
		{
			if (stream == null)
			{
				return;
			}
		}
		ExtractResource(resourceName, destination);
	}

	private void CreateOrRemoveShortcuts()
	{
		string startMenuShortcutPath = GetStartMenuShortcutPath();
		if (_desktopShortcutCheck.Checked)
		{
			string desktopShortcutPath = GetDesktopShortcutPath();
			if (!CreateShortcut(desktopShortcutPath))
			{
				_warnings.Add("桌面快捷方式创建失败，可从开始菜单启动程序。");
			}
		}
		else
		{
			DeleteShortcut(GetDesktopShortcutPath());
		}
		if (!CreateShortcut(startMenuShortcutPath))
		{
			_warnings.Add("开始菜单快捷方式创建失败，可直接运行安装目录中的程序。");
		}
	}

	private static string GetStartMenuShortcutPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MySQL管家.lnk");
	}

	private static string GetDesktopShortcutPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MySQL管家.lnk");
	}

	private void DeleteLegacyInstallation()
	{
		string[] array = new string[2] { "MySQLServiceController.exe", "MySQLServiceController.exe.config" };
		foreach (string text in array)
		{
			try
			{
				string path = Path.Combine(_installDirectory, text);
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (Exception ex)
			{
				_warnings.Add("未能删除旧版文件 " + text + "：" + ex.Message);
			}
		}
		try
		{
			DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MySQL 服务管家.lnk"));
			DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MySQL 服务管家.lnk"));
		}
		catch
		{
		}
	}

	private bool CreateShortcut(string shortcutPath)
	{
		try
		{
			if (File.Exists(shortcutPath))
			{
				File.Delete(shortcutPath);
			}
		}
		catch
		{
		}
		object obj = null;
		object obj2 = null;
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
			if (typeFromProgID == null)
			{
				return false;
			}
			obj = Activator.CreateInstance(typeFromProgID);
			obj2 = typeFromProgID.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, obj, new object[1] { shortcutPath });
			Type type = obj2.GetType();
			type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, obj2, new object[1] { InstalledExecutable });
			type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, obj2, new object[1] { _installDirectory });
			type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, obj2, new object[1] { InstalledExecutable + ",0" });
			type.InvokeMember("Description", BindingFlags.SetProperty, null, obj2, new object[1] { "MySQL 服务与 Workbench 管理工具" });
			type.InvokeMember("Save", BindingFlags.InvokeMethod, null, obj2, null);
			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (obj2 != null && Marshal.IsComObject(obj2))
			{
				Marshal.FinalReleaseComObject(obj2);
			}
			if (obj != null && Marshal.IsComObject(obj))
			{
				Marshal.FinalReleaseComObject(obj);
			}
		}
	}

	private static void DeleteShortcut(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private void WriteUninstallRegistry()
	{
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MySQLServiceController");
		if (registryKey == null)
		{
			throw new InvalidOperationException("无法写入卸载信息。");
		}
		FileInfo fileInfo = new FileInfo(InstalledExecutable);
		registryKey.SetValue("DisplayName", "MySQL管家");
		registryKey.SetValue("DisplayVersion", AppVersion);
		registryKey.SetValue("Publisher", "77's");
		registryKey.SetValue("InstallLocation", _installDirectory);
		registryKey.SetValue("DisplayIcon", InstalledExecutable + ",0");
		registryKey.SetValue("UninstallString", "\"" + UninstallExecutable + "\" --uninstall");
		registryKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
		registryKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
		registryKey.SetValue("EstimatedSize", Math.Max(1, (int)(fileInfo.Length / 1024)));
		registryKey.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
	}

	private static Process[] GetRunningInstances()
	{
		List<Process> list = new List<Process>();
		string[] array = new string[2] { "MySQLManager", "MySQLServiceController" };
		foreach (string processName in array)
		{
			try
			{
				list.AddRange(Process.GetProcessesByName(processName));
			}
			catch
			{
			}
		}
		return list.ToArray();
	}

	private static bool IsApplicationRunning()
	{
		Process[] runningInstances = GetRunningInstances();
		foreach (Process process in runningInstances)
		{
			if (process.Id != Process.GetCurrentProcess().Id)
			{
				return true;
			}
		}
		return false;
	}

	private static bool TryCloseRunningApplication()
	{
		Process[] runningInstances = GetRunningInstances();
		foreach (Process process in runningInstances)
		{
			try
			{
				if (process.Id != Process.GetCurrentProcess().Id)
				{
					process.CloseMainWindow();
				}
			}
			catch
			{
			}
		}
		for (int i = 0; i < 50; i++)
		{
			if (!IsApplicationRunning())
			{
				return true;
			}
			Thread.Sleep(100);
		}
		return !IsApplicationRunning();
	}

	private void ScheduleDirectoryRemoval()
	{
		string fullPath = Path.GetFullPath(_installDirectory);
		DirectoryInfo directoryInfo = new DirectoryInfo(fullPath);
		bool flag = string.Equals(Environment.GetEnvironmentVariable("MYSQLSC_TEST_MODE"), "1", StringComparison.Ordinal);
		if (string.Equals(fullPath, Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase) || (directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0 || (!File.Exists(InstalledExecutable) && !File.Exists(UninstallExecutable)))
		{
			throw new InvalidOperationException("安装目录校验失败，已取消删除。");
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("Start-Sleep -Seconds 2; ");
		stringBuilder.Append("$installDir = ");
		stringBuilder.Append(ToPowerShellLiteral(fullPath));
		stringBuilder.Append("; $names = @('MySQLManager.exe',");
		stringBuilder.Append("'MySQLManager.exe.config','使用说明.txt',");
		stringBuilder.Append("'Uninstall.exe','MySQLServiceController.exe',");
		stringBuilder.Append("'MySQLServiceController.exe.config'");
		stringBuilder.Append("); ");
		stringBuilder.Append("foreach ($name in $names) { ");
		stringBuilder.Append("$path = Join-Path $installDir $name; ");
		stringBuilder.Append("if (Test-Path -LiteralPath $path) { ");
		stringBuilder.Append("Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue; ");
		stringBuilder.Append("} }; ");
		stringBuilder.Append("Get-ChildItem -LiteralPath $installDir -Force -Filter '*.new' ");
		stringBuilder.Append("-ErrorAction SilentlyContinue | ");
		stringBuilder.Append("Remove-Item -Force -ErrorAction SilentlyContinue; ");
		stringBuilder.Append("if (-not (Get-ChildItem -LiteralPath $installDir -Force ");
		stringBuilder.Append("-ErrorAction SilentlyContinue)) { ");
		stringBuilder.Append("Remove-Item -LiteralPath $installDir -Force ");
		stringBuilder.Append("-ErrorAction SilentlyContinue; }; ");
		string fullPath2 = Path.GetFullPath(UserDataDirectory);
		string value = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) + Path.DirectorySeparatorChar;
		if (!string.Equals(Path.GetFileName(fullPath2), "MySQLServiceController", StringComparison.OrdinalIgnoreCase) || (!flag && !fullPath2.StartsWith(value, StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidOperationException("配置目录校验失败，已取消删除。");
		}
		stringBuilder.Append("$dataDir = ");
		stringBuilder.Append(ToPowerShellLiteral(fullPath2));
		stringBuilder.Append("; if (Test-Path -LiteralPath $dataDir) { ");
		stringBuilder.Append("Remove-Item -LiteralPath $dataDir -Recurse -Force ");
		stringBuilder.Append("-ErrorAction SilentlyContinue; }; ");
		stringBuilder.Append("$tempDir = [IO.Path]::GetTempPath(); ");
		stringBuilder.Append("Get-ChildItem -LiteralPath $tempDir ");
		stringBuilder.Append("-Filter 'MySQLServiceController-*.result' ");
		stringBuilder.Append("-ErrorAction SilentlyContinue | ");
		stringBuilder.Append("Remove-Item -Force -ErrorAction SilentlyContinue;");
		string text = Convert.ToBase64String(Encoding.Unicode.GetBytes(stringBuilder.ToString()));
		string fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
		ProcessStartInfo processStartInfo = new ProcessStartInfo(fileName);
		processStartInfo.Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + text;
		processStartInfo.UseShellExecute = false;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
		Process.Start(processStartInfo);
	}

	private static string ToPowerShellLiteral(string value)
	{
		return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
	}
}
