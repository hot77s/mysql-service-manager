using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class SettingsForm : Form
{
	private readonly AppConfig _workingCopy;

	private TextBox _serviceTextBox;

	private TextBox _pathTextBox;

	private Label _detectionLabel;

	private RoundedButton _detectButton;

	private RoundedButton[] _themeButtons;

	private int _themeIndex;

	private SurfacePanel _surfaceCard;

	private RoundedButton _detectButtonRef;

	private RoundedButton _browseButton;

	private bool _detecting;

	private float _dpiScale = 1f;

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

	[DllImport("user32.dll")]
	private static extern bool GetComboBoxInfo(IntPtr hwnd, ref ComboBoxInfo info);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	private struct ComboBoxInfo
	{
		public int cbSize;
		public Rectangle rcItem;
		public Rectangle rcButton;
		public int stateButton;
		public IntPtr hwndCombo;
		public IntPtr hwndItem;
		public IntPtr hwndList;
	}

	public AppConfig SavedConfig { get; private set; }

	public SettingsForm(AppConfig currentConfig)
	{
		_workingCopy = new AppConfig();
		_workingCopy.ServiceName = ((currentConfig == null) ? string.Empty : (currentConfig.ServiceName ?? string.Empty));
		_workingCopy.MySqlPath = ((currentConfig == null) ? string.Empty : (currentConfig.MySqlPath ?? string.Empty));
		_workingCopy.RefreshSeconds = currentConfig?.RefreshSeconds ?? 2;
		_workingCopy.WorkbenchPath = ((currentConfig == null) ? string.Empty : (currentConfig.WorkbenchPath ?? string.Empty));
		_workingCopy.ThemeMode = ((currentConfig == null || string.IsNullOrWhiteSpace(currentConfig.ThemeMode)) ? "system" : currentConfig.ThemeMode);
		_workingCopy.WorkbenchLatestVersion = ((currentConfig == null) ? string.Empty : (currentConfig.WorkbenchLatestVersion ?? string.Empty));
		_workingCopy.WorkbenchCheckedUtc = ((currentConfig == null) ? string.Empty : (currentConfig.WorkbenchCheckedUtc ?? string.Empty));
		Text = "服务设置";
		base.ClientSize = new Size(620, 500);
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.ShowInTaskbar = false;
		BackColor = Theme.Background;
		Font = Theme.Font(9.5f, FontStyle.Regular);
		base.AutoScaleMode = AutoScaleMode.None;
		try
		{
			base.Icon = AppIcon.Get();
		}
		catch
		{
		}
		BuildInterface();
		base.Shown += SettingsForm_Shown;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		ApplyDpiScale();
		ApplyTitleBarTheme();
	}

	private void ApplyTitleBarTheme()
	{
		int value = (Theme.IsDark ? 1 : 0);
		try { DwmSetWindowAttribute(base.Handle, 20, ref value, 4); } catch { }
		int value2 = ColorTranslator.ToWin32(Theme.IsDark ? Theme.Background : Color.White);
		int value3 = ColorTranslator.ToWin32(Theme.Text);
		try { DwmSetWindowAttribute(base.Handle, 35, ref value2, 4); } catch { }
		try { DwmSetWindowAttribute(base.Handle, 36, ref value3, 4); } catch { }
	}

	private void UpdateThemeButtons()
	{
		if (_themeButtons == null)
		{
			return;
		}
		for (int i = 0; i < _themeButtons.Length; i++)
		{
			RoundedButton roundedButton = _themeButtons[i];
			if (roundedButton == null)
			{
				continue;
			}
			bool flag = i == _themeIndex;
			roundedButton.NormalColor = (flag ? Theme.Accent : Theme.SurfaceAlt);
			roundedButton.HoverColor = (flag ? Theme.AccentDark : Theme.Mix(Theme.SurfaceAlt, Theme.Accent, 0.12));
			roundedButton.PressedColor = (flag ? Theme.AccentDark : Theme.Mix(Theme.SurfaceAlt, Theme.Accent, 0.2));
			roundedButton.ForeColor = (flag ? Color.White : Theme.Muted);
			roundedButton.ButtonBorderColor = Theme.Border;
			roundedButton.SurfaceColor = Theme.Background;
		}
	}

	private void LayoutFields(float scale)
	{
		if (_surfaceCard == null || _serviceTextBox == null || _pathTextBox == null || _detectButtonRef == null || _browseButton == null)
		{
			return;
		}
		int num = (int)Math.Round(22f * scale);
		int num2 = (int)Math.Round(102f * scale);
		int num3 = (int)Math.Round(12f * scale);
		int num4 = (int)Math.Round(24f * scale);
		int num5 = Math.Max((int)Math.Round(160f * scale), _surfaceCard.ClientSize.Width - num - num2 - num3 - num4);
		_serviceTextBox.Width = num5;
		_pathTextBox.Width = num5;
		_detectButtonRef.Left = num + num5 + num3;
		_detectButtonRef.Width = num2;
		_browseButton.Left = num + num5 + num3;
		_browseButton.Width = num2;
		_detectButtonRef.Top = _serviceTextBox.Top;
		_detectButtonRef.Height = _serviceTextBox.Height;
		_browseButton.Top = _pathTextBox.Top;
		_browseButton.Height = _pathTextBox.Height;
	}

	private void ApplyDpiScale()
	{
		float num = 1f;
		try
		{
			uint dpi = GetDpiForWindow(base.Handle);
			if (dpi >= 48 && dpi <= 480)
			{
				num = dpi / 96f;
			}
		}
		catch
		{
		}
		if (num <= 0.1f)
		{
			num = 1f;
		}
		float num2 = num / _dpiScale;
		if (Math.Abs(num2 - 1f) < 0.01f)
		{
			return;
		}
		SuspendLayout();
		try
		{
			base.ClientSize = new Size((int)Math.Round((float)base.ClientSize.Width * num2), (int)Math.Round((float)base.ClientSize.Height * num2));
			ScaleChildControls(this, num2);
			LayoutFields(num);
			_dpiScale = num;
		}
		finally
		{
			ResumeLayout(performLayout: true);
		}
	}

	private static void ScaleChildControls(Control parent, float factor)
	{
		foreach (Control control in parent.Controls)
		{
			if (control.MaximumSize != Size.Empty)
			{
				control.MaximumSize = new Size((int)Math.Round((float)control.MaximumSize.Width * factor), (control.MaximumSize.Height == 0) ? 0 : (int)Math.Round((float)control.MaximumSize.Height * factor));
			}
			control.Location = new Point((int)Math.Round((float)control.Left * factor), (int)Math.Round((float)control.Top * factor));
			if (!control.AutoSize)
			{
				control.Size = new Size((int)Math.Round((float)control.Width * factor), (int)Math.Round((float)control.Height * factor));
			}
			ScaleChildControls(control, factor);
		}
	}

	private void BuildInterface()
	{
		SuspendLayout();
		Label label = new Label();
		label.Text = "选择要控制的 MySQL 服务";
		label.Location = new Point(26, 20);
		label.Size = new Size(420, 32);
		label.Font = Theme.Font(16f, FontStyle.Bold);
		label.ForeColor = Theme.Text;
		label.BackColor = Color.Transparent;
		label.AutoSize = true;
		label.MaximumSize = new Size(500, 0);
		base.Controls.Add(label);
		Label label2 = new Label();
		label2.Text = "指定需要控制的 Windows 服务，并记录 MySQL 程序位置。";
		label2.Location = new Point(28, 53);
		label2.Size = new Size(520, 22);
		label2.Font = Theme.Font(8.8f, FontStyle.Regular);
		label2.ForeColor = Theme.Muted;
		label2.BackColor = Color.Transparent;
		label2.AutoSize = true;
		base.Controls.Add(label2);
		Label themeLabel = new Label();
		themeLabel.Text = "主题";
		themeLabel.Location = new Point(404, 24);
		themeLabel.Size = new Size(40, 22);
		themeLabel.Font = Theme.Font(8.6f, FontStyle.Bold);
		themeLabel.ForeColor = Theme.Muted;
		themeLabel.BackColor = Color.Transparent;
		base.Controls.Add(themeLabel);
		ThemeMode themeMode = Theme.ParseMode(_workingCopy.ThemeMode);
		_themeIndex = ((themeMode == ThemeMode.Light) ? 1 : ((themeMode == ThemeMode.Dark) ? 2 : 0));
		_themeButtons = new RoundedButton[3];
		string[] array = new string[3] { "系统", "浅色", "深色" };
		for (int i = 0; i < 3; i++)
		{
			int themeIndex = i;
			RoundedButton roundedButton5 = new RoundedButton();
			roundedButton5.Text = array[i];
			roundedButton5.Location = new Point(446 + i * 51, 17);
			roundedButton5.Size = new Size(48, 28);
			roundedButton5.Font = Theme.Font(8.6f, FontStyle.Bold);
			roundedButton5.Radius = 8f;
			roundedButton5.ButtonBorderColor = Theme.Border;
			roundedButton5.Click += delegate
			{
				_themeIndex = themeIndex;
				UpdateThemeButtons();
			};
			_themeButtons[i] = roundedButton5;
			base.Controls.Add(roundedButton5);
		}
		UpdateThemeButtons();
		SurfacePanel surfacePanel = new SurfacePanel();
		surfacePanel.Location = new Point(24, 88);
		surfacePanel.Size = new Size(572, 292);
		surfacePanel.FillColor = Theme.Surface;
		surfacePanel.Radius = 14;
		surfacePanel.BorderColor = Color.Transparent;
		_surfaceCard = surfacePanel;
		base.Controls.Add(surfacePanel);
		Label label3 = new Label();
		label3.Text = "Windows 服务名称";
		label3.Location = new Point(52, 18);
		label3.Size = new Size(360, 20);
		label3.Font = Theme.Font(9f, FontStyle.Bold);
		label3.ForeColor = Theme.Text;
		label3.BackColor = Color.Transparent;
		label3.AutoSize = true;
		surfacePanel.Controls.Add(label3);
		BadgeLabel sectionBadge = new BadgeLabel();
		sectionBadge.Text = "1";
		sectionBadge.Location = new Point(22, 16);
		sectionBadge.Size = new Size(22, 22);
		sectionBadge.BadgeColor = Theme.Accent;
		sectionBadge.TextColor = Color.White;
		sectionBadge.SurfaceColor = Theme.Surface;
		surfacePanel.Controls.Add(sectionBadge);
		_serviceTextBox = new TextBox();
		_serviceTextBox.Location = new Point(22, 43);
		_serviceTextBox.Size = new Size(412, 30);
		_serviceTextBox.Font = Theme.Font(10f, FontStyle.Regular);
		_serviceTextBox.BackColor = Theme.SurfaceAlt;
		_serviceTextBox.ForeColor = Theme.Text;
		_serviceTextBox.BorderStyle = BorderStyle.FixedSingle;
		_serviceTextBox.Text = _workingCopy.ServiceName;
		surfacePanel.Controls.Add(_serviceTextBox);
		RoundedButton roundedButton = new RoundedButton();
		roundedButton.Text = "自动检测";
		roundedButton.Location = new Point(446, 42);
		roundedButton.Size = new Size(102, 34);
		roundedButton.NormalColor = Theme.AccentSoft;
		roundedButton.HoverColor = Theme.Mix(Theme.AccentSoft, Theme.Accent, 0.12);
		roundedButton.PressedColor = Theme.Mix(Theme.AccentSoft, Theme.Accent, 0.22);
		roundedButton.ForeColor = Theme.Accent;
		roundedButton.Font = Theme.Font(8.8f, FontStyle.Bold);
		roundedButton.Radius = 8f;
		roundedButton.SurfaceColor = Theme.Surface;
		roundedButton.Click += DetectButton_Click;
		_detectButton = roundedButton;
		_detectButtonRef = roundedButton;
		surfacePanel.Controls.Add(roundedButton);
		Label label4 = new Label();
		label4.Text = "可在“服务”(services.msc) 中查看，例如 MySQL80、MySQL84。";
		label4.Location = new Point(22, 80);
		label4.Size = new Size(520, 20);
		label4.Font = Theme.Font(8.3f, FontStyle.Regular);
		label4.ForeColor = Theme.Subtle;
		label4.BackColor = Color.Transparent;
		label4.AutoSize = true;
		label4.MaximumSize = new Size(520, 0);
		surfacePanel.Controls.Add(label4);
		Label label5 = new Label();
		label5.Text = "MySQL 程序路径（可选）";
		label5.Location = new Point(52, 116);
		label5.Size = new Size(360, 20);
		label5.Font = Theme.Font(9f, FontStyle.Bold);
		label5.ForeColor = Theme.Text;
		label5.BackColor = Color.Transparent;
		label5.AutoSize = true;
		surfacePanel.Controls.Add(label5);
		BadgeLabel sectionBadge2 = new BadgeLabel();
		sectionBadge2.Text = "2";
		sectionBadge2.Location = new Point(22, 114);
		sectionBadge2.Size = new Size(22, 22);
		sectionBadge2.BadgeColor = Theme.Accent;
		sectionBadge2.TextColor = Color.White;
		sectionBadge2.SurfaceColor = Theme.Surface;
		surfacePanel.Controls.Add(sectionBadge2);
		_pathTextBox = new TextBox();
		_pathTextBox.Location = new Point(22, 141);
		_pathTextBox.Size = new Size(412, 30);
		_pathTextBox.Font = Theme.Font(9f, FontStyle.Regular);
		_pathTextBox.Text = _workingCopy.MySqlPath;
		_pathTextBox.BorderStyle = BorderStyle.FixedSingle;
		_pathTextBox.BackColor = Theme.SurfaceAlt;
		_pathTextBox.ForeColor = Theme.Text;
		surfacePanel.Controls.Add(_pathTextBox);
		RoundedButton roundedButton2 = new RoundedButton();
		roundedButton2.Text = "浏览…";
		roundedButton2.Location = new Point(446, 140);
		roundedButton2.Size = new Size(102, 34);
		roundedButton2.NormalColor = Theme.SurfaceAlt;
		roundedButton2.ButtonBorderColor = Theme.Border;
		roundedButton2.HoverColor = Theme.Mix(Theme.NeutralSoft, Theme.Text, 0.08);
		roundedButton2.PressedColor = Theme.Mix(Theme.NeutralSoft, Theme.Text, 0.16);
		roundedButton2.ForeColor = Theme.Text;
		roundedButton2.Font = Theme.Font(8.8f, FontStyle.Bold);
		roundedButton2.Radius = 8f;
		roundedButton2.SurfaceColor = Theme.Surface;
		roundedButton2.Click += BrowseButton_Click;
		surfacePanel.Controls.Add(roundedButton2);
		_browseButton = roundedButton2;
		LinkLabel linkLabel = new LinkLabel();
		linkLabel.Text = "读取服务注册路径";
		linkLabel.Location = new Point(22, 176);
		linkLabel.Size = new Size(140, 22);
		linkLabel.Font = Theme.Font(8.3f, FontStyle.Regular);
		linkLabel.LinkColor = Theme.Accent;
		linkLabel.ActiveLinkColor = Theme.AccentDark;
		linkLabel.VisitedLinkColor = Theme.Accent;
		linkLabel.LinkBehavior = LinkBehavior.HoverUnderline;
		linkLabel.AutoSize = true;
		linkLabel.LinkClicked += ReadPathLink_LinkClicked;
		surfacePanel.Controls.Add(linkLabel);
		_detectionLabel = new Label();
		_detectionLabel.Text = "服务名决定实际控制对象；路径用于记录和辅助识别。";
		_detectionLabel.Location = new Point(170, 176);
		_detectionLabel.Size = new Size(378, 22);
		_detectionLabel.Font = Theme.Font(8.3f, FontStyle.Regular);
		_detectionLabel.ForeColor = Theme.Subtle;
		_detectionLabel.TextAlign = ContentAlignment.MiddleRight;
		_detectionLabel.BackColor = Color.Transparent;
		surfacePanel.Controls.Add(_detectionLabel);
		Panel panel = new Panel();
		panel.Location = new Point(22, 209);
		panel.Size = new Size(526, 1);
		panel.BackColor = Theme.Border;
		surfacePanel.Controls.Add(panel);
		Label label6 = new Label();
		label6.Text = "配置说明";
		label6.Location = new Point(22, 224);
		label6.Size = new Size(120, 20);
		label6.Font = Theme.Font(8.8f, FontStyle.Bold);
		label6.ForeColor = Theme.Text;
		label6.BackColor = Color.Transparent;
		label6.AutoSize = true;
		surfacePanel.Controls.Add(label6);
		Label label7 = new Label();
		label7.Text = "程序通过服务名称调用 Windows 服务控制管理器，启停时会请求管理员授权。\n路径仅用于记录与显示，实际启动命令以服务注册信息为准。";
		label7.Location = new Point(22, 246);
		label7.Size = new Size(526, 48);
		label7.Font = Theme.Font(8.3f, FontStyle.Regular);
		label7.ForeColor = Theme.Muted;
		label7.BackColor = Color.Transparent;
		label7.AutoSize = true;
		label7.MaximumSize = new Size(526, 0);
		surfacePanel.Controls.Add(label7);
		LinkLabel linkLabel2 = new LinkLabel();
		linkLabel2.Text = "打开配置文件所在位置";
		linkLabel2.Location = new Point(26, 400);
		linkLabel2.Size = new Size(400, 24);
		linkLabel2.Font = Theme.Font(8.2f, FontStyle.Regular);
		linkLabel2.LinkColor = Theme.Subtle;
		linkLabel2.ActiveLinkColor = Theme.Accent;
		linkLabel2.VisitedLinkColor = Theme.Subtle;
		linkLabel2.LinkBehavior = LinkBehavior.HoverUnderline;
		linkLabel2.AutoSize = true;
		linkLabel2.LinkClicked += ConfigPathLink_LinkClicked;
		base.Controls.Add(linkLabel2);
		RoundedButton roundedButton3 = new RoundedButton();
		roundedButton3.Text = "取消";
		roundedButton3.Location = new Point(356, 438);
		roundedButton3.Size = new Size(110, 42);
		roundedButton3.NormalColor = Theme.SurfaceAlt;
		roundedButton3.ButtonBorderColor = Theme.Border;
		roundedButton3.HoverColor = Theme.Mix(Theme.NeutralSoft, Theme.Text, 0.08);
		roundedButton3.PressedColor = Theme.Mix(Theme.NeutralSoft, Theme.Text, 0.16);
		roundedButton3.ForeColor = Theme.Text;
		roundedButton3.Font = Theme.Font(9.5f, FontStyle.Bold);
		roundedButton3.Radius = 9f;
		roundedButton3.DialogResult = DialogResult.Cancel;
		base.Controls.Add(roundedButton3);
		RoundedButton roundedButton4 = new RoundedButton();
		roundedButton4.Text = "✓  保存设置";
		roundedButton4.Location = new Point(478, 438);
		roundedButton4.Size = new Size(118, 42);
		roundedButton4.NormalColor = Theme.Accent;
		roundedButton4.HoverColor = Theme.AccentDark;
		roundedButton4.PressedColor = Color.FromArgb(18, 70, 179);
		roundedButton4.ForeColor = Color.White;
		roundedButton4.Font = Theme.Font(9.5f, FontStyle.Bold);
		roundedButton4.Radius = 9f;
		roundedButton4.Click += SaveButton_Click;
		base.Controls.Add(roundedButton4);
		base.AcceptButton = roundedButton4;
		base.CancelButton = roundedButton3;
		ResumeLayout(performLayout: false);
		LayoutFields(_dpiScale);
	}

	private async void SettingsForm_Shown(object sender, EventArgs e)
	{
		ApplyTitleBarTheme();
		if (string.IsNullOrWhiteSpace(_serviceTextBox.Text))
		{
			await DetectServicesAsync(quiet: true);
		}
		if (!base.IsDisposed)
		{
			_serviceTextBox.Focus();
			_serviceTextBox.SelectionStart = _serviceTextBox.Text.Length;
			_serviceTextBox.SelectionLength = 0;
		}
	}

	private async void DetectButton_Click(object sender, EventArgs e)
	{
		await DetectServicesAsync(quiet: false);
	}

	private async Task DetectServicesAsync(bool quiet)
	{
		if (_detecting)
		{
			return;
		}
		_detecting = true;
		_detectButton.Enabled = false;
		_detectionLabel.Text = "正在扫描…";
		_detectionLabel.ForeColor = Theme.Accent;
		try
		{
			List<ServiceOption> list = await Task.Run(() => ServiceManager.FindMySqlServices());
			if (base.IsDisposed)
			{
				return;
			}
			string text = _serviceTextBox.Text.Trim();
			if (list.Count == 0)
			{
				_detectionLabel.Text = "未检测到 MySQL 服务，可手动输入服务名。";
				_detectionLabel.ForeColor = Theme.Warning;
				if (!quiet)
				{
					MessageBox.Show("未检测到服务名或显示名包含 MySQL / MariaDB 的 Windows 服务。\n\n如果 MySQL 已安装，请用 services.msc 查看准确服务名后手动输入。", "未检测到服务", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
				return;
			}
			_detectionLabel.Text = "已检测到 " + list.Count + " 个 MySQL 服务。";
			_detectionLabel.ForeColor = Theme.Success;
			bool flag = false;
			foreach (ServiceOption item in list)
			{
				if (string.Equals(item.Name, text, StringComparison.OrdinalIgnoreCase))
				{
					flag = true;
					break;
				}
			}
			if (!flag && string.IsNullOrWhiteSpace(text))
			{
				_serviceTextBox.Text = list[0].Name;
			}
			if (string.IsNullOrWhiteSpace(_pathTextBox.Text))
			{
				string value = ServiceManager.TryGetImagePath(_serviceTextBox.Text.Trim());
				if (!string.IsNullOrWhiteSpace(value))
				{
					_pathTextBox.Text = value;
				}
			}
		}
		catch (Exception ex)
		{
			AppLog.Error("自动检测 MySQL 服务失败。", ex);
			if (!base.IsDisposed)
			{
				_detectionLabel.Text = "扫描失败，可手动输入服务名。";
				_detectionLabel.ForeColor = Theme.Warning;
			}
		}
		finally
		{
			_detecting = false;
			if (!base.IsDisposed)
			{
				_detectButton.Enabled = true;
			}
		}
	}

	private void BrowseButton_Click(object sender, EventArgs e)
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Title = "选择 mysqld.exe";
		openFileDialog.Filter = "MySQL 程序 (mysqld.exe)|mysqld.exe|可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
		openFileDialog.CheckFileExists = true;
		openFileDialog.Multiselect = false;
		string text = _pathTextBox.Text.Trim().Trim('"');
		if (File.Exists(text))
		{
			openFileDialog.FileName = text;
		}
		else if (Directory.Exists(text))
		{
			openFileDialog.InitialDirectory = text;
		}
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			_pathTextBox.Text = openFileDialog.FileName;
		}
	}

	private void ReadPathLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
	{
		string text = _serviceTextBox.Text.Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			MessageBox.Show("请先填写 Windows 服务名称。", "缺少服务名称", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		string value = ServiceManager.TryGetImagePath(text);
		if (string.IsNullOrWhiteSpace(value))
		{
			MessageBox.Show("无法读取服务“" + text + "”的程序路径。\n\n请确认服务名正确，或手动选择 mysqld.exe。", "未读取到路径", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			_pathTextBox.Text = value;
		}
	}

	private void SaveButton_Click(object sender, EventArgs e)
	{
		string text = _serviceTextBox.Text.Trim();
		string text2 = _pathTextBox.Text.Trim().Trim('"');
		if (!ServiceManager.IsValidServiceName(text))
		{
			MessageBox.Show("服务名称不能为空，也不能包含斜杠或反斜杠。\n\n示例：MySQL80、MySQL84。", "服务名称无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			_serviceTextBox.Focus();
			_serviceTextBox.SelectionStart = _serviceTextBox.Text.Length;
			_serviceTextBox.SelectionLength = 0;
			return;
		}
		if (!ServiceManager.ServiceExists(text))
		{
			DialogResult dialogResult = MessageBox.Show("Windows 中当前没有名为“" + text + "”的服务。\n\n可能是 MySQL 尚未安装，或服务名填写有误。仍要保存吗？", "服务不存在", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2);
			if (dialogResult != DialogResult.Yes)
			{
				_serviceTextBox.Focus();
			_serviceTextBox.SelectionStart = _serviceTextBox.Text.Length;
			_serviceTextBox.SelectionLength = 0;
				_serviceTextBox.SelectAll();
				return;
			}
		}
		if (!string.IsNullOrWhiteSpace(text2) && !File.Exists(text2) && !Directory.Exists(text2))
		{
			DialogResult dialogResult2 = MessageBox.Show("填写的 MySQL 路径当前不存在。\n\n" + text2 + "\n\n仍要保存这个路径吗？", "路径不存在", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2);
			if (dialogResult2 != DialogResult.Yes)
			{
				_pathTextBox.Focus();
				_pathTextBox.SelectAll();
				return;
			}
		}
		_workingCopy.ServiceName = text;
		_workingCopy.MySqlPath = text2;
		_workingCopy.ThemeMode = ((_themeIndex == 1) ? "light" : ((_themeIndex == 2) ? "dark" : "system"));
		try
		{
			ConfigStore.Save(_workingCopy);
			SavedConfig = _workingCopy;
			AppLog.Info("配置已保存。服务：" + _workingCopy.ServiceName + "；路径：" + _workingCopy.MySqlPath);
			base.DialogResult = DialogResult.OK;
			Close();
		}
		catch (Exception ex)
		{
			AppLog.Error("保存配置失败。", ex);
			MessageBox.Show("无法保存设置：\n\n" + ex.Message + "\n\n配置文件位置：" + ConfigStore.ConfigPath, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void ConfigPathLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
	{
		try
		{
			Directory.CreateDirectory(AppLog.AppDataDirectory);
			if (File.Exists(ConfigStore.ConfigPath))
			{
				Process.Start("explorer.exe", "/select,\"" + ConfigStore.ConfigPath + "\"");
			}
			else
			{
				Process.Start("explorer.exe", "\"" + AppLog.AppDataDirectory + "\"");
			}
		}
		catch (Exception ex)
		{
			AppLog.Error("打开配置文件位置失败。", ex);
			MessageBox.Show("无法打开配置文件位置。\n\n" + ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}
}
