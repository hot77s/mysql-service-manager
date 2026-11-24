using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MySqlServiceController;

/// <summary>安装或卸载 MySQL Server、MySQL Workbench。</summary>
internal sealed class ComponentInstallForm : Form
{
	private enum StatusKind
	{
		Info,
		Success,
		Warning,
		Error
	}

	private const int ContentWidth = 612;

	private float _scale = 1f;

	private bool _busy;

	private Label _titleLabel;

	private Label _subtitleLabel;

	private SurfacePanel _serverCard;

	private Label _serverTitle;

	private Label _serverVersionCaption;

	private Label _serverHint;

	private SlimCheckBox _serverCheck;

	private RoundedButton _serverVersionButton;

	private PathBox _serverPath;

	private RoundedButton _serverBrowse;

	private SlimCheckBox _serverServiceCheck;

	private SlimCheckBox _serverPasswordCheck;

	private Label _passwordHint;

	private RoundedButton _serverUninstallButton;

	private RoundedButton _workbenchUninstallButton;

	private readonly ToolTip _toolTip = new ToolTip();

	private List<InstalledComponent> _installedServers;

	private List<InstalledComponent> _installedWorkbenches;

	private Label _serverState;

	private readonly List<ComponentOption> _serverOptions = new List<ComponentOption>();

	private int _serverIndex;

	private bool _serverPathCustom;

	private SurfacePanel _workbenchCard;

	private Label _workbenchTitle;

	private Label _workbenchVersionCaption;

	private SlimCheckBox _workbenchCheck;

	private RoundedButton _workbenchVersionButton;

	private PathBox _workbenchPath;

	private RoundedButton _workbenchBrowse;

	private Label _workbenchState;

	private readonly List<ComponentOption> _workbenchOptions = new List<ComponentOption>();

	private int _workbenchIndex;

	private bool _workbenchPathCustom;

	private SurfacePanel _statusCard;

	private Label _statusIcon;

	private ContextMenuStrip _versionMenu;

	private Label _statusText;

	private ProgressLine _progressLine;

	private RoundedButton _closeButton;

	private RoundedButton _installButton;

	private Label _authorLabel;

	private StatusKind _statusKind = StatusKind.Info;

	private DateTime _statusHoldUntil = DateTime.MinValue;

	/// <summary>本次是否成功安装过组件（供主界面刷新使用）。</summary>
	public bool AnyComponentInstalled { get; private set; }

	public bool AnyComponentUninstalled { get; private set; }

	/// <summary>本次安装的 MySQL 服务名称（没有注册服务时为 null）。</summary>
	public string InstalledServiceName { get; private set; }

	/// <summary>本次安装的 Workbench 目录（没有安装时为 null）。</summary>
	public string InstalledWorkbenchDirectory { get; private set; }

	public ComponentInstallForm()
	{
		Text = "组件管理";
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = true;
		base.ShowInTaskbar = false;
		base.BackColor = Theme.Background;
		base.Font = Theme.Font(9.5f, FontStyle.Regular);
		base.AutoScaleMode = AutoScaleMode.None;
		base.KeyPreview = true;
		try
		{
			base.Icon = AppIcon.Get();
		}
		catch
		{
		}
		_scale = 1f;
		_serverOptions.AddRange(ComponentCatalog.GetOptions(ComponentCatalog.KindServer));
		_workbenchOptions.AddRange(ComponentCatalog.GetOptions(ComponentCatalog.KindWorkbench));
		BuildInterface();
		RefreshInstalledComponents();
		ApplyTheme();
		LayoutContent();
		SetStatus(StatusKind.Info, "勾选要安装的组件，或点击对应的“卸载”按钮。");
		base.Shown += ComponentInstallForm_Shown;
	}

	private int Dp(int value)
	{
		return (int)Math.Round((double)(value * _scale));
	}

	private void BuildInterface()
	{
		SuspendLayout();
		_titleLabel = MakeLabel("管理 MySQL 组件", 24, 18, 380, 32, Theme.Font(16.5f, FontStyle.Bold), Theme.Text);
		base.Controls.Add(_titleLabel);
		_subtitleLabel = MakeLabel("安装官方 MSI，或卸载已登记的 MySQL Server 和 Workbench", 24, 54, 520, 20, Theme.Font(9f, FontStyle.Regular), Theme.Muted);
		base.Controls.Add(_subtitleLabel);

		// ---- MySQL 服务 ----
		_serverCard = new SurfacePanel
		{
			FillColor = Theme.Surface,
			BorderColor = Color.Transparent
		};
		base.Controls.Add(_serverCard);
		_serverTitle = MakeLabel("MySQL Server", 52, 16, 200, 24, Theme.Font(10.2f, FontStyle.Bold), Theme.Text);
		_serverCard.Controls.Add(_serverTitle);
		_serverVersionCaption = MakeLabel("版本", 0, 0, 38, 20, Theme.Font(8.4f, FontStyle.Regular), Theme.Subtle);
		_serverVersionCaption.TextAlign = ContentAlignment.MiddleRight;
		_serverCard.Controls.Add(_serverVersionCaption);
		_serverHint = MakeLabel(string.Empty, 0, 0, 520, 18, Theme.Font(8.2f, FontStyle.Regular), Theme.Muted);
		_serverHint.AutoEllipsis = true;
		_serverCard.Controls.Add(_serverHint);
		_serverCheck = new SlimCheckBox
		{
			Text = "安装 MySQL Server",
			Checked = false
		};
		_serverCheck.CheckedChanged += delegate
		{
			RefreshStates();
		};
		_serverCard.Controls.Add(_serverCheck);
		_serverVersionButton = new RoundedButton
		{
			Text = "版本",
			Font = Theme.Font(8.8f, FontStyle.Bold),
			Radius = 8f
		};
		_serverVersionButton.Click += delegate
		{
			if (!_busy)
			{
				ShowVersionMenu(_serverVersionButton, _serverOptions, _serverIndex, delegate(int index)
				{
					_serverIndex = index;
					_serverVersionButton.Text = VersionText(_serverOptions, _serverIndex);
					if (!_serverPathCustom)
					{
						_serverPath.SetTextSilently(DefaultDirectory(ComponentCatalog.KindServer, _serverOptions, _serverIndex));
					}
					RefreshStates();
				});
			}
		};
		_serverCard.Controls.Add(_serverVersionButton);
		_serverPath = new PathBox();
		_serverPath.TextEdited += delegate
		{
			_serverPathCustom = true;
		};
		_serverCard.Controls.Add(_serverPath);
		_serverBrowse = new RoundedButton
		{
			Text = "浏览…",
			Font = Theme.Font(8.6f, FontStyle.Bold),
			Radius = 8f
		};
		_serverBrowse.Click += delegate
		{
			Browse(_serverPath, "选择 MySQL 服务的安装根目录", ComponentCatalog.KindServer, _serverOptions, _serverIndex);
		};
		_serverCard.Controls.Add(_serverBrowse);
		_serverServiceCheck = new SlimCheckBox
		{
			Text = "初始化数据目录并注册为 Windows 服务",
			Checked = true
		};
		_serverServiceCheck.CheckedChanged += delegate
		{
			RefreshStates();
		};
		_serverCard.Controls.Add(_serverServiceCheck);
		_serverPasswordCheck = new SlimCheckBox { Text = "设置 root 密码", Checked = true };
		_serverPasswordCheck.CheckedChanged += delegate { RefreshStates(); };
		_serverCard.Controls.Add(_serverPasswordCheck);
		_passwordHint = MakeLabel(string.Empty, 0, 0, 100, 20, Theme.Font(8.4f, FontStyle.Regular), Theme.Muted);
		_passwordHint.TextAlign = ContentAlignment.MiddleLeft;
		_serverCard.Controls.Add(_passwordHint);
		_serverUninstallButton = CreateUninstallButton(ComponentCatalog.KindServer, "卸载 MySQL Server");
		_serverCard.Controls.Add(_serverUninstallButton);
		_serverState = MakeLabel(string.Empty, 40, 0, 320, 20, Theme.Font(8.4f, FontStyle.Regular), Theme.Subtle);
		_serverState.AutoEllipsis = true;
		_serverCard.Controls.Add(_serverState);

		// ---- Workbench ----
		_workbenchCard = new SurfacePanel
		{
			FillColor = Theme.Surface,
			BorderColor = Color.Transparent
		};
		base.Controls.Add(_workbenchCard);
		_workbenchTitle = MakeLabel("MySQL Workbench", 52, 16, 240, 24, Theme.Font(10.2f, FontStyle.Bold), Theme.Text);
		_workbenchCard.Controls.Add(_workbenchTitle);
		_workbenchVersionCaption = MakeLabel("版本", 0, 0, 38, 20, Theme.Font(8.4f, FontStyle.Regular), Theme.Subtle);
		_workbenchVersionCaption.TextAlign = ContentAlignment.MiddleRight;
		_workbenchCard.Controls.Add(_workbenchVersionCaption);
		_workbenchCheck = new SlimCheckBox
		{
			Text = "安装 MySQL Workbench",
			Checked = false
		};
		_workbenchCheck.CheckedChanged += delegate
		{
			RefreshStates();
		};
		_workbenchCard.Controls.Add(_workbenchCheck);
		_workbenchVersionButton = new RoundedButton
		{
			Text = "版本",
			Font = Theme.Font(8.8f, FontStyle.Bold),
			Radius = 8f
		};
		_workbenchVersionButton.Click += delegate
		{
			if (!_busy)
			{
				ShowVersionMenu(_workbenchVersionButton, _workbenchOptions, _workbenchIndex, delegate(int index)
				{
					_workbenchIndex = index;
					_workbenchVersionButton.Text = VersionText(_workbenchOptions, _workbenchIndex);
					if (!_workbenchPathCustom)
					{
						_workbenchPath.SetTextSilently(DefaultDirectory(ComponentCatalog.KindWorkbench, _workbenchOptions, _workbenchIndex));
					}
					RefreshStates();
				});
			}
		};
		_workbenchCard.Controls.Add(_workbenchVersionButton);
		_workbenchPath = new PathBox();
		_workbenchPath.TextEdited += delegate
		{
			_workbenchPathCustom = true;
		};
		_workbenchCard.Controls.Add(_workbenchPath);
		_workbenchBrowse = new RoundedButton
		{
			Text = "浏览…",
			Font = Theme.Font(8.6f, FontStyle.Bold),
			Radius = 8f
		};
		_workbenchBrowse.Click += delegate
		{
			Browse(_workbenchPath, "选择 MySQL Workbench 的安装根目录", ComponentCatalog.KindWorkbench, _workbenchOptions, _workbenchIndex);
		};
		_workbenchCard.Controls.Add(_workbenchBrowse);
		_workbenchUninstallButton = CreateUninstallButton(ComponentCatalog.KindWorkbench, "卸载 MySQL Workbench");
		_workbenchCard.Controls.Add(_workbenchUninstallButton);
		_workbenchState = MakeLabel(string.Empty, 40, 0, 320, 20, Theme.Font(8.4f, FontStyle.Regular), Theme.Subtle);
		_workbenchState.AutoEllipsis = true;
		_workbenchCard.Controls.Add(_workbenchState);

		// ---- 底部状态与按钮 ----
		_statusCard = new SurfacePanel
		{
			FillColor = Theme.AccentSoft,
			AccentColor = Theme.Accent,
			AccentWidth = 3
		};
		base.Controls.Add(_statusCard);
		_statusIcon = MakeLabel("i", 16, 0, 20, 24, Theme.Font(10f, FontStyle.Bold), Theme.Accent);
		_statusIcon.TextAlign = ContentAlignment.MiddleCenter;
		_statusCard.Controls.Add(_statusIcon);
		_statusText = MakeLabel(string.Empty, 42, 0, 520, 24, Theme.Font(8.8f, FontStyle.Regular), Theme.Text);
		_statusText.TextAlign = ContentAlignment.MiddleLeft;
		_statusText.AutoEllipsis = true;
		_statusCard.Controls.Add(_statusText);
		_progressLine = new ProgressLine
		{
			Visible = false
		};
		base.Controls.Add(_progressLine);
		_closeButton = new RoundedButton
		{
			Text = "关闭",
			Font = Theme.Font(9.2f, FontStyle.Bold),
			Radius = 9f
		};
		_closeButton.Click += delegate
		{
			if (!_busy)
			{
				Close();
			}
		};
		base.Controls.Add(_closeButton);
		_installButton = new RoundedButton
		{
			Text = "开始安装",
			Font = Theme.Font(9.4f, FontStyle.Bold),
			Radius = 9f
		};
		_installButton.Click += InstallButton_Click;
		base.Controls.Add(_installButton);
		_authorLabel = MakeLabel("下载源：cdn.mysql.com  ·  需要管理员授权", 24, 0, 360, 20, Theme.Font(8.2f, FontStyle.Regular), Theme.Subtle);
		_authorLabel.AutoEllipsis = true;
		base.Controls.Add(_authorLabel);
		base.AcceptButton = _installButton;
		base.CancelButton = _closeButton;
		ResumeLayout(performLayout: true);
	}

	private RoundedButton CreateUninstallButton(string kind, string name)
	{
		RoundedButton button = new RoundedButton
		{
			Text = "卸载…",
			AccessibleName = name,
			Font = Theme.Font(8.6f, FontStyle.Bold),
			Radius = 8f
		};
		button.Click += delegate { ShowUninstallMenu(button, kind); };
		return button;
	}

	private static Label MakeLabel(string text, int x, int y, int width, int height, Font font, Color color)
	{
		Label label = new Label
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(width, height),
			Font = font,
			ForeColor = color,
			BackColor = Color.Transparent,
			AutoSize = false
		};
		return label;
	}

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	/// <summary>窗口真实 DPI：DeviceDpi 在高分屏（如 150%）下可能仍返回 96，只能问系统。</summary>
	private float CurrentScale()
	{
		try
		{
			if (base.IsHandleCreated)
			{
				uint dpi = GetDpiForWindow(base.Handle);
				if (dpi >= 48 && dpi <= 480)
				{
					return dpi / 96f;
				}
			}
		}
		catch
		{
		}
		return _scale;
	}

	/// <summary>标题栏跟随主题，与主界面一致：深色模式下标题栏也是黑的。</summary>
	private void ApplyTitleBarTheme()
	{
		if (!base.IsHandleCreated)
		{
			return;
		}
		int dark = (Theme.IsDark ? 1 : 0);
		try
		{
			DwmSetWindowAttribute(base.Handle, 20, ref dark, 4);
		}
		catch
		{
		}
		int caption = ColorTranslator.ToWin32(Theme.IsDark ? Theme.Background : Color.White);
		int text = ColorTranslator.ToWin32(Theme.Text);
		try
		{
			DwmSetWindowAttribute(base.Handle, 35, ref caption, 4);
		}
		catch
		{
		}
		try
		{
			DwmSetWindowAttribute(base.Handle, 36, ref text, 4);
		}
		catch
		{
		}
	}

	/// <summary>按窗口真实 DPI 重算一次比例并重排，避免文字比控件大导致重叠。</summary>
	/// <summary>模态窗口自己最小化后主窗口仍被禁用，会把用户卡住；这里改成连同主窗口一起最小化。</summary>
	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 0x0112 && (m.WParam.ToInt32() & 0xFFF0) == 0xF020 && base.Owner != null)
		{
			base.Owner.WindowState = FormWindowState.Minimized;
			return;
		}
		base.WndProc(ref m);
	}

	private void ApplyScaleFromWindow()
	{
		float scale = CurrentScale();
		if (Math.Abs(scale - _scale) < 0.01f)
		{
			return;
		}
		_scale = scale;
		LayoutContent();
		Invalidate(true);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		ApplyScaleFromWindow();
		ApplyTitleBarTheme();
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		ApplyScaleFromWindow();
		ApplyTitleBarTheme();
	}

	private void LayoutContent()
	{
		if (_serverCard == null)
		{
			return;
		}
		SuspendLayout();
		try
		{
			int margin = Dp(24);
			int contentW = Dp(ContentWidth);
			int right = contentW;
			int pickerW = Dp(118);
			int browseW = Dp(78);
			int buttonH = Dp(28);

			_titleLabel.SetBounds(margin, Dp(18), contentW - margin * 2, Dp(32));
			_subtitleLabel.SetBounds(margin, Dp(54), contentW - margin * 2, Dp(20));

			// MySQL 服务卡片
			int top = Dp(92);
			int serverH = Dp(230);
			_serverCard.SetBounds(margin, top, contentW, serverH);
			_serverCard.Radius = Dp(16);
			_serverTitle.SetBounds(Dp(22), Dp(16), Dp(220), Dp(24));
			_serverCheck.SetBounds(Dp(22), Dp(48), Dp(200), Dp(24));
			_serverVersionButton.SetBounds(right - Dp(20) - pickerW, Dp(46), pickerW, buttonH);
			_serverVersionCaption.SetBounds(_serverVersionButton.Left - Dp(44), Dp(46) + (buttonH - Dp(20)) / 2, Dp(38), Dp(20));
			int pathLeft = Dp(22);
			int pathW = contentW - pathLeft - browseW - Dp(30);
			_serverHint.SetBounds(Dp(22), Dp(78), contentW - Dp(44), Dp(18));
			_serverPath.SetBounds(pathLeft, Dp(102), pathW, Dp(30));
			_serverBrowse.SetBounds(right - Dp(20) - browseW, Dp(102), browseW, Dp(30));
			_serverServiceCheck.SetBounds(Dp(22), Dp(142), contentW - Dp(44), Dp(22));
			_serverPasswordCheck.SetBounds(Dp(22), Dp(167), Dp(138), Dp(22));
			_passwordHint.SetBounds(Dp(165), Dp(167), contentW - Dp(187), Dp(22));
			_serverUninstallButton.SetBounds(right - Dp(20) - browseW, Dp(194), browseW, buttonH);
			_serverState.SetBounds(Dp(22), Dp(198), _serverUninstallButton.Left - Dp(34), Dp(20));

			// Workbench 卡片
			int wbTop = _serverCard.Bottom + Dp(14);
			int wbH = Dp(154);
			_workbenchCard.SetBounds(margin, wbTop, contentW, wbH);
			_workbenchCard.Radius = Dp(16);
			_workbenchTitle.SetBounds(Dp(22), Dp(16), Dp(260), Dp(24));
			_workbenchCheck.SetBounds(Dp(22), Dp(48), Dp(240), Dp(24));
			_workbenchVersionButton.SetBounds(right - Dp(20) - pickerW, Dp(46), pickerW, buttonH);
			_workbenchVersionCaption.SetBounds(_workbenchVersionButton.Left - Dp(44), Dp(46) + (buttonH - Dp(20)) / 2, Dp(38), Dp(20));
			_workbenchPath.SetBounds(pathLeft, Dp(86), pathW, Dp(30));
			_workbenchBrowse.SetBounds(right - Dp(20) - browseW, Dp(86), browseW, Dp(30));
			_workbenchUninstallButton.SetBounds(right - Dp(20) - browseW, Dp(120), browseW, buttonH);
			_workbenchState.SetBounds(Dp(22), Dp(124), _workbenchUninstallButton.Left - Dp(34), Dp(20));

			// 状态与按钮
			int statusTop = _workbenchCard.Bottom + Dp(16);
			int statusH = Dp(54);
			_statusCard.SetBounds(margin, statusTop, contentW, statusH);
			_statusCard.Radius = Dp(12);
			_statusCard.AccentWidth = Dp(3);
			_statusIcon.SetBounds(Dp(16), (statusH - Dp(24)) / 2, Dp(20), Dp(24));
			_statusText.SetBounds(Dp(42), (statusH - Dp(24)) / 2, contentW - Dp(60), Dp(24));
			_progressLine.SetBounds(margin + Dp(42), _statusCard.Bottom + Dp(8), contentW - Dp(42), Dp(4));

			int buttonTop = _statusCard.Bottom + Dp(22);
			int primaryW = Dp(118);
			int closeW = Dp(84);
			_installButton.SetBounds(right + margin - primaryW, buttonTop, primaryW, Dp(36));
			_closeButton.SetBounds(_installButton.Left - Dp(10) - closeW, buttonTop, closeW, Dp(36));
			_authorLabel.SetBounds(margin, buttonTop + Dp(8), Dp(380), Dp(20));

			int height = buttonTop + Dp(36) + Dp(22);
			base.ClientSize = new Size(contentW + margin * 2, height);
		}
		finally
		{
			ResumeLayout(performLayout: true);
		}
	}

	private void ApplyTheme()
	{
		SuspendLayout();
		try
		{
			BackColor = Theme.Background;
			_titleLabel.ForeColor = Theme.Text;
			_subtitleLabel.ForeColor = Theme.Muted;
			_serverCard.FillColor = Theme.Surface;
			_serverCard.BorderColor = Color.Transparent;
			_serverTitle.ForeColor = Theme.Text;
			_serverVersionCaption.ForeColor = Theme.Subtle;
			_serverHint.ForeColor = Theme.Muted;
			_serverCheck.ForeColor = Theme.Text;
			_serverServiceCheck.ForeColor = Theme.Muted;
			_workbenchCard.FillColor = Theme.Surface;
			_workbenchCard.BorderColor = Color.Transparent;
			_workbenchTitle.ForeColor = Theme.Text;
			_workbenchVersionCaption.ForeColor = Theme.Subtle;
			_workbenchCheck.ForeColor = Theme.Text;
			_authorLabel.ForeColor = Theme.Subtle;
			_serverPath.ApplyTheme();
			_workbenchPath.ApplyTheme();
			StyleNeutral(_serverVersionButton, Theme.Surface);
			StyleNeutral(_serverBrowse, Theme.Surface);
			StyleNeutral(_workbenchVersionButton, Theme.Surface);
			StyleNeutral(_workbenchBrowse, Theme.Surface);
			StyleUninstall(_serverUninstallButton);
			StyleUninstall(_workbenchUninstallButton);
			StyleNeutral(_closeButton, Theme.Background);
			_installButton.NormalColor = Theme.Accent;
			_installButton.HoverColor = Theme.Mix(Theme.Accent, Color.White, 0.12);
			_installButton.PressedColor = Theme.Mix(Theme.Accent, Color.Black, 0.1);
			_installButton.DisabledColor = Theme.NeutralSoft;
			_installButton.DisabledTextColor = Theme.Subtle;
			_installButton.ForeColor = Color.White;
			_installButton.SurfaceColor = Theme.Background;
			_installButton.ButtonBorderColor = Theme.Border;
			ApplyStatusColors();
			RefreshStates();
		}
		finally
		{
			ResumeLayout(performLayout: true);
		}
		Invalidate(true);
	}

	private static void StyleNeutral(RoundedButton button, Color surface)
	{
		button.NormalColor = Theme.SurfaceAlt;
		button.HoverColor = Theme.Mix(Theme.SurfaceAlt, Theme.Text, 0.06);
		button.PressedColor = Theme.Mix(Theme.SurfaceAlt, Theme.Text, 0.12);
		button.DisabledColor = Theme.NeutralSoft;
		button.DisabledTextColor = Theme.Subtle;
		button.ForeColor = Theme.Text;
		button.ButtonBorderColor = Theme.Border;
		button.SurfaceColor = surface;
	}

	private static void StyleUninstall(RoundedButton button)
	{
		StyleNeutral(button, Theme.Surface);
		button.NormalColor = Theme.DangerSoft;
		button.HoverColor = Theme.Mix(Theme.DangerSoft, Theme.Danger, 0.12);
		button.PressedColor = Theme.Mix(Theme.DangerSoft, Theme.Danger, 0.22);
		button.ForeColor = Theme.Danger;
	}

	private void RefreshInstalledComponents()
	{
		_installedServers = ComponentCatalog.FindUninstallableComponents(ComponentCatalog.KindServer);
		_installedWorkbenches = ComponentCatalog.FindUninstallableComponents(ComponentCatalog.KindWorkbench);
	}

	private void ApplyStatusColors()
	{
		Color fill;
		Color accent;
		string icon;
		switch (_statusKind)
		{
		case StatusKind.Success:
			fill = Theme.SuccessSoft;
			accent = Theme.Success;
			icon = "✓";
			break;
		case StatusKind.Warning:
			fill = Theme.WarningSoft;
			accent = Theme.Warning;
			icon = "!";
			break;
		case StatusKind.Error:
			fill = Theme.DangerSoft;
			accent = Theme.Danger;
			icon = "!";
			break;
		default:
			fill = Theme.AccentSoft;
			accent = Theme.Accent;
			icon = "i";
			break;
		}
		_statusCard.FillColor = fill;
		_statusCard.AccentColor = accent;
		_statusIcon.ForeColor = accent;
		_statusIcon.Text = icon;
		_statusText.ForeColor = Theme.Text;
	}

	private void SetStatus(StatusKind kind, string message, int percent = -1, int holdSeconds = 0)
	{
		_statusKind = kind;
		ApplyStatusColors();
		_statusText.Text = message;
		if (percent >= 0)
		{
			_progressLine.Value = percent;
		}
		_statusHoldUntil = (holdSeconds > 0) ? DateTime.Now.AddSeconds(holdSeconds) : DateTime.MinValue;
	}

	private void RefreshStates()
	{
		if (_serverCheck == null)
		{
			return;
		}
		bool serverOn = _serverCheck.Checked;
		bool workbenchOn = _workbenchCheck.Checked;
		_serverVersionButton.Text = VersionText(_serverOptions, _serverIndex);
		_workbenchVersionButton.Text = VersionText(_workbenchOptions, _workbenchIndex);
		if (string.IsNullOrWhiteSpace(_serverPath.Text))
		{
			_serverPath.SetTextSilently(DefaultDirectory(ComponentCatalog.KindServer, _serverOptions, _serverIndex));
		}
		if (string.IsNullOrWhiteSpace(_workbenchPath.Text))
		{
			_workbenchPath.SetTextSilently(DefaultDirectory(ComponentCatalog.KindWorkbench, _workbenchOptions, _workbenchIndex));
		}
		_serverHint.Text = _serverOptions[_serverIndex].Hint;
		_serverVersionButton.Enabled = !_busy;
		_serverPath.Enabled = serverOn && !_busy;
		_serverBrowse.Enabled = serverOn && !_busy;
		_serverServiceCheck.Enabled = serverOn && !_busy;
		_serverPasswordCheck.Enabled = serverOn && _serverServiceCheck.Checked && !_busy;
		_passwordHint.Text = !_serverServiceCheck.Checked ? "仅安装程序文件，不设置密码" :
			(_serverPasswordCheck.Checked ? "推荐：点击开始安装后输入并确认密码" : "不设置：新实例的 root 密码将为空");
		_passwordHint.ForeColor = _serverPasswordCheck.Enabled && !_serverPasswordCheck.Checked ? Theme.Warning : Theme.Muted;
		_serverCheck.Enabled = !_busy;
		_workbenchVersionButton.Enabled = !_busy;
		_workbenchPath.Enabled = workbenchOn && !_busy;
		_workbenchBrowse.Enabled = workbenchOn && !_busy;
		_workbenchCheck.Enabled = !_busy;
		_serverUninstallButton.Enabled = !_busy && _installedServers.Count > 0;
		_workbenchUninstallButton.Enabled = !_busy && _installedWorkbenches.Count > 0;
		_toolTip.SetToolTip(_serverUninstallButton, "选择要卸载的 MySQL Server 版本");
		_toolTip.SetToolTip(_workbenchUninstallButton, "选择要卸载的 MySQL Workbench 版本");
		if (serverOn)
		{
			string serviceName = ComponentCatalog.DefaultServiceName(_serverOptions[_serverIndex].Version);
			_serverState.Text = (_serverServiceCheck.Checked ? ("安装后初始化数据目录，并注册为 Windows 服务 " + serviceName) : "仅安装程序文件，不初始化数据目录、不注册服务");
			_serverState.ForeColor = Theme.Muted;
		}
		else
		{
			DescribeComponentState(_serverState, ComponentCatalog.KindServer, _installedServers);
		}
		if (workbenchOn)
		{
			_workbenchState.Text = "安装后可在主界面直接打开 Workbench";
			_workbenchState.ForeColor = Theme.Muted;
		}
		else
		{
			DescribeComponentState(_workbenchState, ComponentCatalog.KindWorkbench, _installedWorkbenches);
		}
		_installButton.Enabled = !_busy;
		_closeButton.Enabled = !_busy;
	}

	private void DescribeComponentState(Label label, string kind, List<InstalledComponent> installed)
	{
		if (installed.Count > 0)
		{
			label.Text = installed.Count == 1 ? "已安装：" + installed[0].Label : "检测到 " + installed.Count + " 个已安装版本，可选择卸载";
			label.ForeColor = Theme.Success;
			_toolTip.SetToolTip(label, installed.Count == 1 ? installed[0].Directory : label.Text);
			return;
		}
		string found = ComponentCatalog.DescribeInstalled(kind, out string directory);
		label.Text = found == null ? "未检测到可卸载的 MSI 安装" : "检测到程序文件，但未找到可卸载的 MSI 记录";
		label.ForeColor = Theme.Subtle;
		_toolTip.SetToolTip(label, directory);
	}

	private static string VersionText(List<ComponentOption> options, int index)
	{
		if (index < 0 || index >= options.Count)
		{
			return "版本";
		}
		return options[index].Version + "  ▾";
	}

	/// <summary>版本列表里的简短标注：最新的一版写“最新”，其余用一个词的简称。</summary>
	private static string OptionLabel(List<ComponentOption> options, int index)
	{
		if (index == 0)
		{
			return "最新";
		}
		string tag = options[index].Tag;
		return string.IsNullOrWhiteSpace(tag) ? "旧版" : tag;
	}

	private string DefaultDirectory(string kind, List<ComponentOption> options, int index)
	{
		string version = (index >= 0 && index < options.Count) ? options[index].Version : string.Empty;
		return ComponentCatalog.InstallDirectoryFor(kind, version, ComponentCatalog.DefaultBaseDirectory());
	}

	private void Browse(PathBox target, string description, string kind, List<ComponentOption> options, int index)
	{
		using FolderBrowserDialog dialog = new FolderBrowserDialog();
		dialog.Description = description;
		dialog.ShowNewFolderButton = true;
		string current = target.Text;
		if (!string.IsNullOrWhiteSpace(current))
		{
			try
			{
				string parent = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
				if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
				{
					dialog.SelectedPath = parent;
				}
			}
			catch
			{
			}
		}
		if (dialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		string version = (index >= 0 && index < options.Count) ? options[index].Version : string.Empty;
		target.Text = Path.Combine(dialog.SelectedPath, "MySQL", ComponentCatalog.FolderNameFor(kind, version));
		if (target == _serverPath)
		{
			_serverPathCustom = true;
		}
		else
		{
			_workbenchPathCustom = true;
		}
	}

	private ContextMenuStrip PrepareMenu()
	{
		// 复用同一个菜单实例：如果每次都在 Closed 里 Dispose，ToolStripManager
		// 仍把它当作活动下拉菜单，下一次鼠标按下会访问已释放对象并崩溃。
		ContextMenuStrip menu = _versionMenu;
		if (menu == null)
		{
			menu = new ContextMenuStrip
			{
				ShowImageMargin = false,
				ShowItemToolTips = true,
				Renderer = new ToolStripProfessionalRenderer(new ComponentMenuColors())
			};
			_versionMenu = menu;
		}
		if (menu.Visible)
		{
			menu.Close();
		}
		menu.Font = Theme.Font(9f, FontStyle.Regular);
		menu.BackColor = Theme.Surface;
		menu.ForeColor = Theme.Text;
		while (menu.Items.Count > 0)
		{
			ToolStripItem stale = menu.Items[0];
			menu.Items.RemoveAt(0);
			stale.Dispose();
		}
		return menu;
	}

	private void ShowVersionMenu(RoundedButton owner, List<ComponentOption> options, int selected, Action<int> onPicked)
	{
		ContextMenuStrip menu = PrepareMenu();
		for (int i = 0; i < options.Count; i++)
		{
			int index = i;
			ToolStripMenuItem item = new ToolStripMenuItem(((i == selected) ? "✓  " : "     ") + options[index].Version + "（" + OptionLabel(options, i) + "）")
			{
				ForeColor = (i == selected) ? Theme.Accent : Theme.Text,
				Font = Theme.Font(9f, (i == selected) ? FontStyle.Bold : FontStyle.Regular),
				ToolTipText = options[index].Hint
			};
			item.Click += delegate
			{
				onPicked(index);
			};
			menu.Items.Add(item);
		}
		menu.Show(owner, new Point(0, owner.Height + Dp(4)));
	}

	private void ShowUninstallMenu(RoundedButton owner, string kind)
	{
		if (_busy)
		{
			return;
		}
		RefreshInstalledComponents();
		RefreshStates();
		List<InstalledComponent> items = kind == ComponentCatalog.KindServer ? _installedServers : _installedWorkbenches;
		if (items.Count == 0)
		{
			SetStatus(StatusKind.Warning, "未找到可卸载的 Windows Installer 记录。");
			return;
		}
		ContextMenuStrip menu = PrepareMenu();
		foreach (InstalledComponent target in items)
		{
			ToolStripMenuItem item = new ToolStripMenuItem("卸载 " + target.Label)
			{
				ForeColor = Theme.Text,
				ToolTipText = target.Directory ?? string.Empty
			};
			item.Click += async delegate { await ConfirmAndUninstallAsync(target); };
			menu.Items.Add(item);
		}
		menu.Show(owner, new Point(0, owner.Height + Dp(4)));
	}

	private async Task ConfirmAndUninstallAsync(InstalledComponent target)
	{
		if (_busy)
		{
			return;
		}
		bool server = target.Kind == ComponentCatalog.KindServer;
		string impact = server
			? "卸载会停止匹配的 MySQL 服务，现有数据库连接将中断。MySQL管家不会主动删除数据目录或配置文件；请先备份，尤其是数据位于安装目录内时。"
			: "请先关闭 Workbench。MySQL管家不会主动删除 Workbench 的连接配置，也不会卸载 MySQL Server。";
		string directory = string.IsNullOrWhiteSpace(target.Directory) ? "Windows Installer 未记录安装位置" : target.Directory;
		if (MessageBox.Show(this, "确认卸载以下组件？\n\n" + target.Label + "\n" + directory + "\n\n" + impact,
			"确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
		{
			return;
		}
		_busy = true;
		RefreshStates();
		_progressLine.Visible = true;
		_progressLine.Reset();
		SetStatus(StatusKind.Info, "准备卸载 " + target.DisplayName + "…", 0);
		try
		{
			ComponentJob job = new ComponentJob { Kind = target.Kind, UninstallProductCode = target.ProductCode, MachineWide = target.MachineWide };
			ComponentResult result = await Task.Run(() => ComponentInstaller.Run(job, ReportProgress));
			if (IsDisposed)
			{
				return;
			}
			if (result.Success)
			{
				AnyComponentUninstalled = true;
				SetStatus(result.Detail == null ? StatusKind.Success : StatusKind.Warning, result.Message, 100);
				if (result.Detail != null)
				{
					MessageBox.Show(this, result.Message + "\n\n" + result.Detail, "卸载结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
			else if (result.Cancelled)
			{
				SetStatus(StatusKind.Warning, result.Message, 0);
			}
			else
			{
				ShowUninstallError(result.Message);
			}
		}
		catch (Exception ex)
		{
			AppLog.Error("卸载 " + target.DisplayName + " 失败。", ex);
			if (!IsDisposed)
			{
				ShowUninstallError(ex.Message);
			}
		}
		finally
		{
			if (!IsDisposed)
			{
				_busy = false;
				_progressLine.Visible = false;
				RefreshInstalledComponents();
				RefreshStates();
			}
		}
	}

	private void ShowUninstallError(string message)
	{
		SetStatus(StatusKind.Error, "卸载失败：" + FirstLine(message), 0);
		MessageBox.Show(this, message + "\n\n日志：" + AppLog.LogPath, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_versionMenu?.Dispose();
			_toolTip.Dispose();
		}
		base.Dispose(disposing);
	}

	private async void ComponentInstallForm_Shown(object sender, EventArgs e)
	{
		LayoutContent();
		await RefreshLatestVersionsAsync();
	}

	private async Task RefreshLatestVersionsAsync()
	{
		try
		{
			string selectedServer = _serverOptions[_serverIndex].Version;
			ComponentOption serverLatest = await Task.Run(() => ComponentCatalog.TryRefreshLatest(ComponentCatalog.KindServer));
			if (serverLatest != null && !base.IsDisposed)
			{
				ApplyLatest(_serverOptions, serverLatest);
				_serverIndex = IndexOf(_serverOptions, selectedServer);
				_serverVersionButton.Text = VersionText(_serverOptions, _serverIndex);
				RefreshStates();
			}
			string selectedWorkbench = _workbenchOptions[_workbenchIndex].Version;
			ComponentOption workbenchLatest = await Task.Run(() => ComponentCatalog.TryRefreshLatest(ComponentCatalog.KindWorkbench));
			if (workbenchLatest != null && !base.IsDisposed)
			{
				ApplyLatest(_workbenchOptions, workbenchLatest);
				_workbenchIndex = IndexOf(_workbenchOptions, selectedWorkbench);
				_workbenchVersionButton.Text = VersionText(_workbenchOptions, _workbenchIndex);
				RefreshStates();
			}
		}
		catch
		{
		}
	}

	private static void ApplyLatest(List<ComponentOption> options, ComponentOption latest)
	{
		if (options.Count > 0 && string.Equals(options[0].Version, latest.Version, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		foreach (ComponentOption option in options)
		{
			if (string.Equals(option.Version, latest.Version, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		options.Insert(0, latest);
	}

	private static int IndexOf(List<ComponentOption> options, string version)
	{
		for (int i = 0; i < options.Count; i++)
		{
			if (string.Equals(options[i].Version, version, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return 0;
	}

	private bool TryReadRootPassword(out string password)
	{
		password = null;
		using Form dialog = new Form
		{
			Text = "设置 MySQL root 密码",
			StartPosition = FormStartPosition.CenterParent,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MaximizeBox = false,
			MinimizeBox = false,
			ShowInTaskbar = false,
			AutoScaleMode = AutoScaleMode.None,
			ClientSize = new Size(Dp(424), Dp(224)),
			BackColor = Theme.Background,
			ForeColor = Theme.Text,
			Font = Theme.Font(9.5f, FontStyle.Regular)
		};
		Label note = MakeLabel("密码仅用于本次新建的实例，不会由 MySQL管家保存。", Dp(20), Dp(18), Dp(384), Dp(24), dialog.Font, Theme.Text);
		Label firstLabel = MakeLabel("root 密码", Dp(20), Dp(55), Dp(94), Dp(26), dialog.Font, Theme.Text);
		Label secondLabel = MakeLabel("确认密码", Dp(20), Dp(95), Dp(94), Dp(26), dialog.Font, Theme.Text);
		TextBox first = CreatePasswordInput("root 密码", 55);
		TextBox second = CreatePasswordInput("确认密码", 95);
		Label status = MakeLabel(string.Empty, Dp(20), Dp(135), Dp(384), Dp(24), Theme.Font(8.7f, FontStyle.Regular), Theme.Danger);
		RoundedButton cancel = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(Dp(229), Dp(172), Dp(78), Dp(32)), Font = dialog.Font };
		RoundedButton confirm = new RoundedButton { Text = "确认", Bounds = new Rectangle(Dp(319), Dp(172), Dp(85), Dp(32)), Font = dialog.Font };
		StyleNeutral(cancel, Theme.Background);
		StyleNeutral(confirm, Theme.Background);
		confirm.ForeColor = Theme.Accent;
		confirm.ButtonBorderColor = Theme.Accent;
		confirm.Click += delegate
		{
			if (!ComponentInstaller.IsValidRootPassword(first.Text))
			{
				status.Text = "密码不能全为空白、有控制字符或超过 256 字节。";
				first.Focus();
			}
			else if (!string.Equals(first.Text, second.Text, StringComparison.Ordinal))
			{
				status.Text = "两次输入的密码不一致。";
				second.Focus();
			}
			else
			{
				dialog.DialogResult = DialogResult.OK;
			}
		};
		dialog.Controls.AddRange(new Control[] { note, firstLabel, first, secondLabel, second, status, cancel, confirm });
		dialog.AcceptButton = confirm;
		dialog.CancelButton = cancel;
		dialog.ActiveControl = first;
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			password = first.Text;
		}
		first.Clear();
		second.Clear();
		return password != null;
	}

	private TextBox CreatePasswordInput(string name, int y) => new TextBox
	{
		AccessibleName = name,
		UseSystemPasswordChar = true,
		MaxLength = 256,
		Bounds = new Rectangle(Dp(122), Dp(y), Dp(282), Dp(28)),
		BackColor = Theme.Surface,
		ForeColor = Theme.Text,
		BorderStyle = BorderStyle.FixedSingle
	};

	private async void InstallButton_Click(object sender, EventArgs e)
	{
		if (_busy)
		{
			return;
		}
		List<ComponentJob> jobs = new List<ComponentJob>();
		string rootPassword = null;
		if (_serverCheck.Checked)
		{
			string directory = _serverPath.Text?.Trim();
			if (!ValidateDirectory(directory, "MySQL 服务安装位置"))
			{
				return;
			}
			jobs.Add(new ComponentJob
			{
				Kind = ComponentCatalog.KindServer,
				Version = _serverOptions[_serverIndex].Version,
				Url = ComponentCatalog.BuildDownloadUrl(ComponentCatalog.KindServer, _serverOptions[_serverIndex].Version),
				InstallDirectory = directory,
				RegisterService = _serverServiceCheck.Checked,
				SetRootPassword = _serverServiceCheck.Checked && _serverPasswordCheck.Checked,
				ServiceName = ComponentCatalog.DefaultServiceName(_serverOptions[_serverIndex].Version)
			});
		}
		if (_workbenchCheck.Checked)
		{
			string directory2 = _workbenchPath.Text?.Trim();
			if (!ValidateDirectory(directory2, "MySQL Workbench 安装位置"))
			{
				return;
			}
			jobs.Add(new ComponentJob
			{
				Kind = ComponentCatalog.KindWorkbench,
				Version = _workbenchOptions[_workbenchIndex].Version,
				Url = ComponentCatalog.BuildDownloadUrl(ComponentCatalog.KindWorkbench, _workbenchOptions[_workbenchIndex].Version),
				InstallDirectory = directory2
			});
		}
		if (jobs.Count == 0)
		{
			SetStatus(StatusKind.Warning, "请先勾选要安装的组件。", 0, 6);
			return;
		}
		ComponentJob serverJob = jobs.Find(job => job.IsServer && job.RegisterService);
		if (serverJob != null)
		{
			if (serverJob.SetRootPassword)
			{
				if (!TryReadRootPassword(out rootPassword))
				{
					return;
				}
			}
			else if (MessageBox.Show(this, "新实例的 root 账户将没有密码。仅建议在受控的本地开发环境使用，并在首次使用后尽快设置密码。\n\n仍要继续安装吗？",
				"确认不设置 root 密码", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
			{
				return;
			}
		}
		_busy = true;
		RefreshStates();
		_installButton.Text = "正在安装…";
		_progressLine.Visible = true;
		_progressLine.Reset();
		SetStatus(StatusKind.Info, "准备安装 " + jobs.Count + " 个组件…", 0);
		bool allOk = true;
		foreach (ComponentJob job in jobs)
		{
			SetStatus(StatusKind.Info, "即将安装 " + job.DisplayName + " " + job.Version + "…", 0);
			ComponentResult result;
			try
			{
				result = await Task.Run(() => ComponentInstaller.Run(job, ReportProgress, job.SetRootPassword ? rootPassword : null));
			}
			catch (Exception ex)
			{
				AppLog.Error("安装 " + job.DisplayName + " 失败。", ex);
				result = new ComponentResult
				{
					Success = false,
					Message = ex.Message
				};
			}
			finally
			{
				if (job.SetRootPassword)
				{
					rootPassword = null;
				}
			}
			if (base.IsDisposed)
			{
				return;
			}
			if (!result.Success)
			{
				allOk = false;
				_progressLine.Visible = false;
				if (result.Cancelled)
				{
					SetStatus(StatusKind.Warning, result.Message ?? "已取消安装。", 0);
				}
				else
				{
					SetStatus(StatusKind.Error, job.DisplayName + " 安装失败：" + FirstLine(result.Message), 0);
					MessageBox.Show(this, job.DisplayName + " 安装失败。\n\n" + result.Message + "\n\n日志：" + AppLog.LogPath, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				}
				break;
			}
			AnyComponentInstalled = true;
			if (job.IsServer)
			{
				InstalledServiceName = string.IsNullOrWhiteSpace(result.ServiceName) ? null : result.ServiceName;
			}
			else
			{
				InstalledWorkbenchDirectory = result.InstalledDirectory;
			}
			if (!string.IsNullOrWhiteSpace(result.Detail))
			{
				SetStatus(StatusKind.Warning, FirstLine(result.Detail), 100, 20);
			}
			else
			{
				SetStatus(StatusKind.Success, job.DisplayName + " " + job.Version + " 安装完成。", 100, 12);
			}
			RefreshStates();
		}
		_busy = false;
		_installButton.Text = "开始安装";
		_progressLine.Visible = false;
		RefreshInstalledComponents();
		RefreshStates();
		if (!allOk)
		{
			_installButton.Text = "重试安装";
		}
	}

	private static string FirstLine(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "未知错误";
		}
		int index = text.IndexOfAny(new char[2] { '\r', '\n' });
		string line = (index > 0) ? text.Substring(0, index) : text;
		return (line.Length > 120) ? (line.Substring(0, 120) + "…") : line;
	}

	private bool ValidateDirectory(string directory, string caption)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			SetStatus(StatusKind.Warning, "请填写" + caption + "。", 0, 6);
			return false;
		}
		try
		{
			string full = Path.GetFullPath(directory);
			string root = Path.GetPathRoot(full) ?? string.Empty;
			if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
			{
				SetStatus(StatusKind.Warning, caption + "不能直接使用磁盘根目录。", 0, 6);
				return false;
			}
			if (full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
			{
				SetStatus(StatusKind.Warning, caption + "不能放在 Windows 系统目录。", 0, 6);
				return false;
			}
		}
		catch (Exception ex)
		{
			SetStatus(StatusKind.Warning, "路径无效：" + ex.Message, 0, 6);
			return false;
		}
		return true;
	}

	private void ReportProgress(int percent, string message)
	{
		if (base.IsDisposed || base.Disposing)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			try
			{
				base.BeginInvoke(new Action<int, string>(ReportProgress), percent, message);
			}
			catch
			{
			}
			return;
		}
		if (!_progressLine.Visible)
		{
			_progressLine.Visible = true;
		}
		_progressLine.Value = percent;
		SetStatus(StatusKind.Info, message, percent);
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		ApplyScaleFromWindow();
		ApplyTitleBarTheme();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (_versionMenu != null && _versionMenu.Visible)
		{
			_versionMenu.Close();
		}
		if (_busy && e.CloseReason == CloseReason.UserClosing)
		{
			e.Cancel = true;
			SetStatus(StatusKind.Info, "组件操作进行中，请等待完成后再关闭。", -1, 5);
		}
		base.OnFormClosing(e);
	}
}
