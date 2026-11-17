using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class MainForm : Form
{
	private enum BannerKind
	{
		Info,
		Success,
		Warning,
		Error
	}

	private AppConfig _config;

	private ServiceSnapshot _snapshot;

	private readonly Timer _refreshTimer;

	private readonly ToolTip _toolTip;

	private bool _refreshing;

	private bool _busy;

	private bool _initialized;

	private DateTime _bannerHoldUntil = DateTime.MinValue;

	private Label _statusLabel;

	private Label _statusDescription;

	private Label _serviceCaption;

	private Label _serviceValue;

	private Label _startupCaption;

	private Label _startupValue;

	private Label _accountCaption;

	private Label _accountValue;

	private Label _pathCaption;

	private Label _pathValue;

	private Label _lastRefreshLabel;

	private Label _authorLabel;

	private Label _footerHintLabel;

	private StatusDot _statusDot;

	private StatusDot _smallDot;

	private BadgeLabel _adminBadge;

	private Panel _statusDivider;

	private RoundedButton _startButton;

	private RoundedButton _stopButton;

	private RoundedButton _settingsButton;

	private RoundedButton _workbenchButton;

	private SurfacePanel _statusCard;

	private SurfacePanel _bannerPanel;

	private Label _bannerIcon;

	private Label _bannerText;

	private LinkLabel _bannerAction;

	private LinkLabel _configLink;

	private string _bannerActionKind;

	private string _startupWarning;

	private DatabaseLogo _logo;

	private Label _titleLabel;

	private Label _subtitleLabel;

	private Label _statusTitleLabel;

	private bool _layingOut;

	private bool _userResizing;

	private bool _initialLayoutDone;

	private bool _headerDragging;

	private Point _headerDragStart;

	private Point _headerFormStart;

	private bool _captionDragging;



	private bool _wasMinimized;

	private bool _restoringSize;

	private Size _normalClientSize = Size.Empty;

	private Size _restoreTargetSize = Size.Empty;

	private BannerKind _lastBannerKind = BannerKind.Info;

	private TrafficLights _trafficLights;

	private SurfacePanel _detailsCard;

	private Panel _detailSeparator1;

	private Panel _detailSeparator2;

	private Panel _detailSeparator3;

	private LinkLabel _workbenchUpdateLink;

	private string _workbenchInstalledVersion = string.Empty;

	private string _workbenchLatestVersion = string.Empty;

	private bool _workbenchUpdateAvailable;

	private bool _checkingWorkbenchUpdate;

	public MainForm()
	{
		AppLog.Info("主界面构造开始。");
		Text = "MySQL管家";
		base.ClientSize = new Size(720, 660);
		base.StartPosition = FormStartPosition.CenterScreen;
		base.FormBorderStyle = FormBorderStyle.Sizable;
		base.MaximizeBox = true;
		base.MinimizeBox = true;
		base.ShowIcon = true;
		base.MinimumSize = new Size(640, 600);
		BackColor = Theme.Background;
		Font = Theme.Font(9.5f, FontStyle.Regular);
		base.AutoScaleMode = AutoScaleMode.None;
		DoubleBuffered = true;
		try
		{
			base.Icon = AppIcon.Get();
		}
		catch
		{
		}
		_toolTip = new ToolTip();
		_toolTip.AutoPopDelay = 12000;
		_toolTip.InitialDelay = 450;
		_toolTip.ReshowDelay = 100;
		_toolTip.ShowAlways = true;
		_config = ConfigStore.Load(out _startupWarning);
		Theme.SetMode(Theme.ParseMode(_config.ThemeMode));
		AppLog.Info("主界面配置已读取。");
		BuildInterface();
		AppLog.Info("主界面控件已创建。");
		Theme.Changed += Theme_Changed;
		ApplyTheme();
		_refreshTimer = new Timer();
		_refreshTimer.Interval = GetRefreshInterval();
		_refreshTimer.Tick += RefreshTimer_Tick;
		_refreshTimer.Start();
		base.Shown += MainForm_Shown;
		base.FormClosing += MainForm_FormClosing;
		base.KeyPreview = true;
		base.KeyDown += MainForm_KeyDown;
		AppLog.Info("主界面构造完成。");
	}

	private void Theme_Changed(object sender, EventArgs e)
	{
		if (base.IsDisposed || !base.IsHandleCreated)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			base.BeginInvoke(new EventHandler(Theme_Changed), sender, e);
			return;
		}
		ApplyTheme();
	}

	private void ApplyTheme()
	{
		if (_statusCard == null)
		{
			return;
		}
		SuspendLayout();
		try
		{
			BackColor = Theme.Background;
			_logo.SurfaceColor = Theme.Background;
			_titleLabel.ForeColor = Theme.Text;
			_subtitleLabel.ForeColor = Theme.Muted;
			_statusCard.FillColor = Theme.Surface;
			_adminBadge.SurfaceColor = Theme.Surface;
			_statusTitleLabel.ForeColor = Theme.Muted;
			_statusDescription.ForeColor = Theme.Muted;
			_serviceCaption.ForeColor = Theme.Subtle;
			_serviceValue.ForeColor = Theme.Text;
			_startupCaption.ForeColor = Theme.Subtle;
			_startupValue.ForeColor = Theme.Muted;
			_accountCaption.ForeColor = Theme.Subtle;
			_accountValue.ForeColor = Theme.Muted;
			_pathCaption.ForeColor = Theme.Subtle;
			_pathValue.ForeColor = Theme.Subtle;
			_statusCard.BorderColor = Color.Transparent;
			_detailsCard.FillColor = Theme.Surface;
			_detailsCard.BorderColor = Color.Transparent;
			_detailSeparator1.BackColor = Theme.Border;
			_detailSeparator2.BackColor = Theme.Border;
			_detailSeparator3.BackColor = Theme.Border;
			_statusDivider.BackColor = Theme.Border;
			_statusDot.SurfaceColor = Theme.Surface;
			if (_snapshot != null)
			{
				Color statusColor = ((!_snapshot.Installed) ? Theme.Danger : GetStatusColor(_snapshot));
				_statusLabel.ForeColor = statusColor;
				_statusDot.DotColor = statusColor;
				_statusCard.AccentColor = statusColor;
			}
			else
			{
				_statusLabel.ForeColor = Theme.Muted;
				_statusDot.DotColor = Theme.Subtle;
				_statusCard.AccentColor = Theme.Subtle;
			}
			_footerHintLabel.ForeColor = Theme.Subtle;
			_lastRefreshLabel.ForeColor = Theme.Muted;
			_authorLabel.ForeColor = Theme.Muted;
			_configLink.LinkColor = Theme.Muted;
			_configLink.ActiveLinkColor = Theme.Accent;
			_configLink.VisitedLinkColor = Theme.Muted;
			_bannerText.ForeColor = Theme.Text;
			_bannerAction.LinkColor = Theme.Accent;
			_bannerAction.ActiveLinkColor = Theme.AccentDark;
			_bannerAction.VisitedLinkColor = Theme.Accent;
			_workbenchUpdateLink.LinkColor = (_workbenchUpdateAvailable ? Theme.Accent : Theme.Muted);
			_workbenchUpdateLink.ActiveLinkColor = Theme.Accent;
			_workbenchUpdateLink.VisitedLinkColor = _workbenchUpdateLink.LinkColor;
			_workbenchUpdateLink.ForeColor = _workbenchUpdateLink.LinkColor;
			_smallDot.SurfaceColor = Theme.Background;
			_smallDot.DotColor = Theme.Success;
			ApplyBannerColors();
			ApplyButtonTheme();
			UpdateAdminBadge();
			ApplyTitleBarTheme();
		}
		finally
		{
			ResumeLayout(performLayout: true);
		}
		Invalidate(true);
	}

	private void ApplyTitleBarTheme()
	{
		if (!base.IsHandleCreated)
		{
			return;
		}
		int value = (Theme.IsDark ? 1 : 0);
		try
		{
			DwmSetWindowAttribute(base.Handle, 20, ref value, 4);
		}
		catch
		{
		}
		int value2 = ColorTranslator.ToWin32(Theme.IsDark ? Theme.Background : Color.White);
		int value3 = ColorTranslator.ToWin32(Theme.Text);
		try
		{
			DwmSetWindowAttribute(base.Handle, 35, ref value2, 4);
		}
		catch
		{
		}
		try
		{
			DwmSetWindowAttribute(base.Handle, 36, ref value3, 4);
		}
		catch
		{
		}
	}

	private void ApplyButtonTheme()
	{
		_startButton.NormalColor = Theme.Accent;
		_startButton.HoverColor = Theme.Mix(Theme.Accent, Color.White, 0.12);
		_startButton.PressedColor = Theme.Mix(Theme.Accent, Color.Black, 0.1);
		_startButton.DisabledColor = Theme.NeutralSoft;
		_startButton.DisabledTextColor = Theme.Subtle;
		_startButton.ForeColor = Color.White;
		_startButton.ButtonBorderColor = Theme.Border;
		_startButton.SurfaceColor = Theme.Background;
		_stopButton.NormalColor = Theme.NeutralSoft;
		_stopButton.HoverColor = Theme.Mix(Theme.NeutralSoft, Theme.Danger, 0.08);
		_stopButton.PressedColor = Theme.Mix(Theme.NeutralSoft, Theme.Danger, 0.16);
		_stopButton.DisabledColor = Theme.NeutralSoft;
		_stopButton.DisabledTextColor = Theme.Subtle;
		_stopButton.ForeColor = Theme.Danger;
		_stopButton.ButtonBorderColor = Theme.Border;
		_stopButton.SurfaceColor = Theme.Background;
		_settingsButton.NormalColor = Theme.AccentSoft;
		_settingsButton.HoverColor = Theme.Mix(Theme.AccentSoft, Theme.Accent, 0.1);
		_settingsButton.PressedColor = Theme.Mix(Theme.AccentSoft, Theme.Accent, 0.18);
		_settingsButton.ForeColor = Theme.Accent;
		_settingsButton.SurfaceColor = Theme.Background;
		_workbenchButton.NormalColor = Theme.Surface;
		_workbenchButton.HoverColor = Theme.SurfaceAlt;
		_workbenchButton.PressedColor = Theme.Mix(Theme.SurfaceAlt, Theme.Text, 0.06);
		_workbenchButton.ButtonBorderColor = Theme.Border;
		_workbenchButton.SurfaceColor = Theme.Background;
		_workbenchButton.ForeColor = Theme.Text;
		_workbenchButton.DisabledColor = Theme.NeutralSoft;
		_workbenchButton.DisabledTextColor = Theme.Subtle;
	}

	private void ApplyBannerColors()
	{
		Color fillColor;
		Color color;
		string text;
		switch (_lastBannerKind)
		{
		case BannerKind.Success:
			fillColor = Theme.SuccessSoft;
			color = Theme.Success;
			text = "✓";
			break;
		case BannerKind.Warning:
			fillColor = Theme.WarningSoft;
			color = Theme.Warning;
			text = "!";
			break;
		case BannerKind.Error:
			fillColor = Theme.DangerSoft;
			color = Theme.Danger;
			text = "!";
			break;
		default:
			fillColor = Theme.AccentSoft;
			color = Theme.Accent;
			text = "i";
			break;
		}
		_bannerPanel.FillColor = fillColor;
		_bannerPanel.AccentColor = color;
		_bannerIcon.ForeColor = color;
		_bannerIcon.Text = text;
	}

	private void BuildInterface()
	{
		SuspendLayout();
		BackColor = Theme.Background;
		_trafficLights = new TrafficLights();
		_trafficLights.Visible = false;
		_trafficLights.Location = new Point(Dp(20), Dp(14));
		_trafficLights.CloseClicked += delegate { Close(); };
		_trafficLights.MinimizeClicked += delegate { base.WindowState = FormWindowState.Minimized; };
		_trafficLights.ZoomClicked += delegate
		{
			base.WindowState = ((base.WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized);
		};
		base.Controls.Add(_trafficLights);
		_titleLabel = MakeLabel("MySQL管家", Dp(82), Dp(10), Dp(320), Dp(28), Theme.Font(12.5f, FontStyle.Bold), Theme.Text);
		_titleLabel.Visible = true;
		_titleLabel.AutoSize = false;
		_titleLabel.TextAlign = ContentAlignment.MiddleLeft;
		_titleLabel.Cursor = Cursors.SizeAll;
		_titleLabel.MouseDown += HeaderDragStart;
		_titleLabel.MouseMove += HeaderDragMove;
		_titleLabel.MouseUp += HeaderDragEnd;
		_titleLabel.DoubleClick += delegate
		{
			base.WindowState = ((base.WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized);
		};
		base.Controls.Add(_titleLabel);
		_subtitleLabel = MakeLabel(string.Empty, 0, 0, 1, 1, Theme.Font(9f, FontStyle.Regular), Theme.Muted);
		_subtitleLabel.Visible = false;
		base.Controls.Add(_subtitleLabel);
		_logo = new DatabaseLogo();
		_logo.Visible = false;
		base.Controls.Add(_logo);
		_settingsButton = new RoundedButton();
		_settingsButton.Text = "设置";
		_settingsButton.Location = new Point(Dp(636), Dp(10));
		_settingsButton.Size = new Size(Dp(60), Dp(28));
		_settingsButton.NormalColor = Theme.AccentSoft;
		_settingsButton.HoverColor = Theme.Mix(Theme.AccentSoft, Theme.Accent, 0.1);
		_settingsButton.PressedColor = Theme.Mix(Theme.AccentSoft, Theme.Accent, 0.18);
		_settingsButton.ForeColor = Theme.Accent;
		_settingsButton.Font = Theme.Font(9f, FontStyle.Bold);
		_settingsButton.Radius = 9f;
		_settingsButton.Click += SettingsButton_Click;
		_settingsButton.AccessibleName = "打开服务设置";
		base.Controls.Add(_settingsButton);

		_statusCard = new SurfacePanel();
		_statusCard.Location = new Point(Dp(24), Dp(58));
		_statusCard.Size = new Size(Dp(672), Dp(148));
		_statusCard.FillColor = Theme.Surface;
		_statusCard.Radius = Dp(16);
		_statusCard.BorderColor = Color.Transparent;
		base.Controls.Add(_statusCard);
		_statusTitleLabel = MakeLabel("MySQL 服务", Dp(22), Dp(16), Dp(200), Dp(20), Theme.Font(9f, FontStyle.Regular), Theme.Muted);
		_statusTitleLabel.AutoSize = true;
		_statusCard.Controls.Add(_statusTitleLabel);
		_adminBadge = new BadgeLabel();
		_adminBadge.Location = new Point(Dp(490), Dp(14));
		_adminBadge.Size = new Size(Dp(160), Dp(24));
		_adminBadge.BadgeColor = Theme.WarningSoft;
		_adminBadge.TextColor = Theme.Warning;
		_adminBadge.Text = "普通权限 · 操作时授权";
		_statusCard.Controls.Add(_adminBadge);
		_statusLabel = MakeLabel("正在读取…", Dp(22), Dp(38), Dp(600), Dp(48), Theme.Font(28f, FontStyle.Bold), Theme.Muted);
		_statusLabel.BackColor = Color.Transparent;
		_statusCard.Controls.Add(_statusLabel);
		_statusDot = new StatusDot();
		_statusDot.Location = new Point(Dp(22), Dp(112));
		_statusDot.Size = new Size(Dp(10), Dp(10));
		_statusDot.DotColor = Theme.Subtle;
		_statusCard.Controls.Add(_statusDot);
		_statusDescription = MakeLabel("正在连接 Windows 服务管理器", Dp(40), Dp(104), Dp(580), Dp(22), Theme.Font(9.5f, FontStyle.Regular), Theme.Muted);
		_statusDescription.BackColor = Color.Transparent;
		_statusCard.Controls.Add(_statusDescription);
		_statusDivider = new Panel();
		_statusDivider.Visible = false;
		_statusCard.Controls.Add(_statusDivider);

		_startButton = new RoundedButton();
		_startButton.Text = "启动服务";
		_startButton.Location = new Point(Dp(24), Dp(222));
		_startButton.Size = new Size(Dp(216), Dp(40));
		_startButton.NormalColor = Theme.Accent;
		_startButton.HoverColor = Theme.Mix(Theme.Accent, Color.White, 0.12);
		_startButton.PressedColor = Theme.Mix(Theme.Accent, Color.Black, 0.12);
		_startButton.DisabledColor = Theme.NeutralSoft;
		_startButton.DisabledTextColor = Theme.Subtle;
		_startButton.ForeColor = Color.White;
		_startButton.ButtonBorderColor = Theme.Border;
		_startButton.Font = Theme.Font(10.5f, FontStyle.Bold);
		_startButton.Radius = 20f;
		_startButton.Click += StartButton_Click;
		_startButton.AccessibleName = "启动 MySQL 服务";
		base.Controls.Add(_startButton);
		_stopButton = new RoundedButton();
		_stopButton.Text = "停止服务";
		_stopButton.Location = new Point(Dp(252), Dp(222));
		_stopButton.Size = new Size(Dp(216), Dp(40));
		_stopButton.NormalColor = Theme.NeutralSoft;
		_stopButton.HoverColor = Theme.Mix(Theme.NeutralSoft, Theme.Danger, 0.08);
		_stopButton.PressedColor = Theme.Mix(Theme.NeutralSoft, Theme.Danger, 0.16);
		_stopButton.DisabledColor = Theme.NeutralSoft;
		_stopButton.DisabledTextColor = Theme.Subtle;
		_stopButton.ForeColor = Theme.Danger;
		_stopButton.ButtonBorderColor = Theme.Border;
		_stopButton.Font = Theme.Font(10.5f, FontStyle.Bold);
		_stopButton.Radius = 20f;
		_stopButton.Click += StopButton_Click;
		_stopButton.AccessibleName = "停止 MySQL 服务";
		base.Controls.Add(_stopButton);
		_workbenchButton = new RoundedButton();
		_workbenchButton.Text = "Workbench";
		_workbenchButton.Location = new Point(Dp(480), Dp(222));
		_workbenchButton.Size = new Size(Dp(216), Dp(40));
		_workbenchButton.NormalColor = Theme.Surface;
		_workbenchButton.HoverColor = Theme.SurfaceAlt;
		_workbenchButton.PressedColor = Theme.Mix(Theme.SurfaceAlt, Theme.Text, 0.06);
		_workbenchButton.ButtonBorderColor = Theme.Border;
		_workbenchButton.SurfaceColor = Theme.Background;
		_workbenchButton.DisabledColor = Theme.NeutralSoft;
		_workbenchButton.DisabledTextColor = Theme.Subtle;
		_workbenchButton.ForeColor = Theme.Text;
		_workbenchButton.Font = Theme.Font(9.5f, FontStyle.Bold);
		_workbenchButton.Radius = 20f;
		_workbenchButton.Click += WorkbenchButton_Click;
		_workbenchButton.AccessibleName = "打开 MySQL Workbench";
		base.Controls.Add(_workbenchButton);
		_toolTip.SetToolTip(_workbenchButton, "打开 MySQL Workbench（不会启动数据库服务）");
		_toolTip.SetToolTip(_startButton, "启动当前配置的 Windows MySQL 服务");
		_toolTip.SetToolTip(_stopButton, "正常停止 MySQL 服务，不会删除数据库文件");
		_toolTip.SetToolTip(_settingsButton, "修改服务名称、MySQL 程序路径与界面主题");

		_detailsCard = new SurfacePanel();
		_detailsCard.FillColor = Theme.Surface;
		_detailsCard.Radius = Dp(16);
		_detailsCard.BorderColor = Color.Transparent;
		base.Controls.Add(_detailsCard);
		int num = Dp(20);
		_serviceCaption = MakeLabel("服务名称", num, Dp(12), Dp(84), Dp(20), Theme.Font(8.8f, FontStyle.Regular), Theme.Subtle);
		_serviceValue = MakeLabel("正在检测", num + Dp(96), Dp(10), Dp(500), Dp(24), Theme.Font(9.8f, FontStyle.Bold), Theme.Text);
		_startupCaption = MakeLabel("启动类型", num, Dp(54), Dp(84), Dp(20), Theme.Font(8.8f, FontStyle.Regular), Theme.Subtle);
		_startupValue = MakeLabel("未知", num + Dp(96), Dp(52), Dp(500), Dp(22), Theme.Font(9.5f, FontStyle.Regular), Theme.Text);
		_accountCaption = MakeLabel("运行账户", num, Dp(96), Dp(84), Dp(20), Theme.Font(8.8f, FontStyle.Regular), Theme.Subtle);
		_accountValue = MakeLabel("—", num + Dp(96), Dp(94), Dp(500), Dp(22), Theme.Font(9.3f, FontStyle.Regular), Theme.Text);
		_pathCaption = MakeLabel("程序路径", num, Dp(138), Dp(84), Dp(20), Theme.Font(8.8f, FontStyle.Regular), Theme.Subtle);
		_pathValue = MakeLabel("正在读取", num + Dp(96), Dp(136), Dp(500), Dp(22), Theme.Font(9f, FontStyle.Regular), Theme.Muted);
		_detailsCard.Controls.Add(_serviceCaption);
		_detailsCard.Controls.Add(_serviceValue);
		_detailsCard.Controls.Add(_startupCaption);
		_detailsCard.Controls.Add(_startupValue);
		_detailsCard.Controls.Add(_accountCaption);
		_detailsCard.Controls.Add(_accountValue);
		_detailsCard.Controls.Add(_pathCaption);
		_detailsCard.Controls.Add(_pathValue);
		_detailSeparator1 = new Panel();
		_detailSeparator2 = new Panel();
		_detailSeparator3 = new Panel();
		Panel[] array = new Panel[3] { _detailSeparator1, _detailSeparator2, _detailSeparator3 };
		foreach (Panel value in array)
		{
			value.Height = 1;
			value.BackColor = Theme.Border;
			_detailsCard.Controls.Add(value);
		}

		_footerHintLabel = MakeLabel("启停操作不会删除数据库文件", Dp(24), Dp(500), Dp(420), Dp(18), Theme.Font(8.5f, FontStyle.Regular), Theme.Subtle);
		_footerHintLabel.AutoSize = true;
		base.Controls.Add(_footerHintLabel);
		_workbenchUpdateLink = new LinkLabel();
		_workbenchUpdateLink.Visible = false;
		_workbenchUpdateLink.AutoSize = false;
		_workbenchUpdateLink.TextAlign = ContentAlignment.MiddleLeft;
		_workbenchUpdateLink.Font = Theme.Font(8.8f, FontStyle.Regular);
		_workbenchUpdateLink.LinkColor = Theme.Accent;
		_workbenchUpdateLink.ActiveLinkColor = Theme.AccentDark;
		_workbenchUpdateLink.VisitedLinkColor = Theme.Accent;
		_workbenchUpdateLink.LinkBehavior = LinkBehavior.HoverUnderline;
		_workbenchUpdateLink.BackColor = Color.Transparent;
		_workbenchUpdateLink.LinkClicked += WorkbenchUpdateLink_LinkClicked;
		base.Controls.Add(_workbenchUpdateLink);

		_bannerPanel = new SurfacePanel();
		_bannerPanel.Location = new Point(Dp(24), Dp(530));
		_bannerPanel.Size = new Size(Dp(672), Dp(54));
		_bannerPanel.FillColor = Theme.AccentSoft;
		_bannerPanel.AccentColor = Theme.Accent;
		_bannerPanel.AccentWidth = 3;
		_bannerPanel.Radius = Dp(12);
		base.Controls.Add(_bannerPanel);
		_bannerIcon = MakeLabel("i", Dp(16), Dp(14), Dp(20), Dp(24), Theme.Font(10.5f, FontStyle.Bold), Theme.Accent);
		_bannerIcon.TextAlign = ContentAlignment.MiddleCenter;
		_bannerIcon.BackColor = Color.Transparent;
		_bannerPanel.Controls.Add(_bannerIcon);
		_bannerText = MakeLabel("正在准备服务状态…", Dp(42), Dp(8), Dp(480), Dp(38), Theme.Font(9f, FontStyle.Regular), Theme.Text);
		_bannerText.TextAlign = ContentAlignment.MiddleLeft;
		_bannerText.AutoSize = false;
		_bannerText.BackColor = Color.Transparent;
		_bannerPanel.Controls.Add(_bannerText);
		_bannerAction = new LinkLabel();
		_bannerAction.Location = new Point(Dp(580), Dp(12));
		_bannerAction.Size = new Size(Dp(80), Dp(28));
		_bannerAction.TextAlign = ContentAlignment.MiddleRight;
		_bannerAction.Font = Theme.Font(8.8f, FontStyle.Bold);
		_bannerAction.LinkColor = Theme.Accent;
		_bannerAction.ActiveLinkColor = Theme.AccentDark;
		_bannerAction.VisitedLinkColor = Theme.Accent;
		_bannerAction.LinkBehavior = LinkBehavior.HoverUnderline;
		_bannerAction.BackColor = Color.Transparent;
		_bannerAction.Visible = false;
		_bannerAction.LinkClicked += BannerAction_LinkClicked;
		_bannerPanel.Controls.Add(_bannerAction);

		_smallDot = new StatusDot();
		_smallDot.Name = "AutoRefreshDot";
		_smallDot.Location = new Point(Dp(24), Dp(610));
		_smallDot.Size = new Size(Dp(8), Dp(8));
		_smallDot.DotColor = Theme.Success;
		_smallDot.SurfaceColor = Theme.Background;
		base.Controls.Add(_smallDot);
		_lastRefreshLabel = MakeLabel("每 2 秒自动刷新", Dp(38), Dp(604), Dp(260), Dp(20), Theme.Font(8.5f, FontStyle.Regular), Theme.Muted);
		_lastRefreshLabel.AutoSize = true;
		base.Controls.Add(_lastRefreshLabel);
		string productVersion = Application.ProductVersion;
		int versionSeparator = productVersion.LastIndexOf('.');
		if (versionSeparator > 0)
		{
			productVersion = productVersion.Substring(0, versionSeparator);
		}
		_authorLabel = MakeLabel("作者  77's  ·  v" + productVersion, Dp(400), Dp(604), Dp(160), Dp(20), Theme.Font(8.5f, FontStyle.Regular), Theme.Muted);
		_authorLabel.AutoSize = true;
		base.Controls.Add(_authorLabel);
		_configLink = new LinkLabel();
		_configLink.Text = "打开配置文件夹";
		_configLink.Location = new Point(Dp(560), Dp(602));
		_configLink.Size = new Size(Dp(140), Dp(24));
		_configLink.TextAlign = ContentAlignment.MiddleRight;
		_configLink.Font = Theme.Font(8.5f, FontStyle.Regular);
		_configLink.LinkColor = Theme.Muted;
		_configLink.ActiveLinkColor = Theme.Accent;
		_configLink.VisitedLinkColor = Theme.Muted;
		_configLink.LinkBehavior = LinkBehavior.HoverUnderline;
		_configLink.BackColor = Color.Transparent;
		_configLink.LinkClicked += ConfigLink_LinkClicked;
		base.Controls.Add(_configLink);
		ResumeLayout(performLayout: false);
		LayoutMainContent();
	}

	private static Label MakeLabel(string text, int x, int y, int width, int height, Font font, Color color)
	{
		Label label = new Label();
		label.Text = text;
		label.Location = new Point(x, y);
		label.Size = new Size(width, height);
		label.Font = font;
		label.ForeColor = color;
		label.BackColor = Color.Transparent;
		label.AutoSize = false;
		return label;
	}

	private void LayoutMainContent()
	{
		if (_layingOut || base.IsDisposed || !base.IsHandleCreated || base.WindowState == FormWindowState.Minimized)
		{
			return;
		}
		if (_statusCard == null || _statusDescription == null || _bannerPanel == null || _detailsCard == null)
		{
			return;
		}
		_layingOut = true;
		SuspendLayout();
		try
		{
			int margin = Dp(24);
			int width = Math.Max(Dp(600), base.ClientSize.Width);
			int contentW = Math.Max(Dp(420), width - margin * 2);
			_trafficLights.Location = new Point(Dp(20), Dp(14));
			_titleLabel.SetBounds(Dp(24), Dp(10), Math.Max(Dp(180), width - Dp(82) - Dp(90)), Dp(28));
			_settingsButton.SetBounds(width - margin - Dp(60), Dp(10), Dp(60), Dp(28));

			_statusCard.SetBounds(margin, Dp(58), contentW, Dp(148));
			_statusCard.Radius = Dp(16);
			int pad = Dp(22);
			_statusTitleLabel.Location = new Point(pad, Dp(16));
			int badgeW = TextRenderer.MeasureText(_adminBadge.Text ?? string.Empty, _adminBadge.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + Dp(24);
			_adminBadge.SetBounds(contentW - pad - badgeW, Dp(14), badgeW, Dp(24));
			int statusW = contentW - pad * 2 - Dp(10);
			SetMeasuredLabel(_statusLabel, pad, Dp(38), statusW, Dp(2));
			_statusDot.SetBounds(pad, _statusLabel.Bottom + Dp(6), Dp(10), Dp(10));
			SetMeasuredLabel(_statusDescription, pad + Dp(18), _statusLabel.Bottom + Dp(2), statusW - Dp(18), 0);

			int gap = Dp(12);
			int buttonW = Math.Max(Dp(120), (contentW - gap * 2) / 3);
			int buttonTop = _statusCard.Bottom + Dp(16);
			_startButton.SetBounds(margin, buttonTop, buttonW, Dp(40));
			_stopButton.SetBounds(margin + buttonW + gap, buttonTop, buttonW, Dp(40));
			_workbenchButton.SetBounds(margin + (buttonW + gap) * 2, buttonTop, buttonW, Dp(40));

			int detailsTop = buttonTop + Dp(40) + Dp(18);
			int rowH = Dp(42);
			int detailsH = rowH * 4 + Dp(4);
			_detailsCard.SetBounds(margin, detailsTop, contentW, detailsH);
			_detailsCard.Radius = Dp(16);
			int rowPad = Dp(20);
			int valLeft = rowPad + Dp(96);
			int valW = contentW - valLeft - Dp(20);
			SetMeasuredLabel(_serviceCaption, rowPad, Dp(11), Dp(84), 0);
			SetMeasuredLabel(_serviceValue, valLeft, Dp(9), valW, 0);
			SetMeasuredLabel(_startupCaption, rowPad, rowH + Dp(11), Dp(84), 0);
			SetMeasuredLabel(_startupValue, valLeft, rowH + Dp(9), valW, 0);
			SetMeasuredLabel(_accountCaption, rowPad, rowH * 2 + Dp(11), Dp(84), 0);
			SetMeasuredLabel(_accountValue, valLeft, rowH * 2 + Dp(9), valW, 0);
			SetMeasuredLabel(_pathCaption, rowPad, rowH * 3 + Dp(11), Dp(84), 0);
			SetMeasuredLabel(_pathValue, valLeft, rowH * 3 + Dp(9), valW, Dp(2));
			Panel[] array = new Panel[3] { _detailSeparator1, _detailSeparator2, _detailSeparator3 };
			for (int i = 0; i < array.Length; i++)
			{
				array[i].SetBounds(rowPad, rowH * (i + 1), contentW - rowPad * 2, 1);
			}

			_footerHintLabel.Location = new Point(margin, _detailsCard.Bottom + Dp(10));
			if (_workbenchUpdateLink.Visible)
			{
				_workbenchUpdateLink.SetBounds(margin, _footerHintLabel.Bottom + Dp(3), contentW, Dp(18));
			}
			int bannerTop = (_workbenchUpdateLink.Visible ? (_workbenchUpdateLink.Bottom + Dp(10)) : (_footerHintLabel.Bottom + Dp(12)));
			_bannerPanel.SetBounds(margin, bannerTop, contentW, _bannerPanel.Height);
			_bannerPanel.Radius = Dp(12);
			_bannerPanel.AccentWidth = Dp(3);
			int bannerPad = Dp(16);
			int actionW = (_bannerAction.Visible ? _bannerAction.Width : 0);
			int bannerTextLeft = Dp(42);
			int bannerTextW = Math.Max(Dp(160), contentW - bannerTextLeft - bannerPad - ((actionW > 0) ? (actionW + Dp(8)) : 0));
			_bannerText.AutoSize = false;
			_bannerText.SetBounds(bannerTextLeft, Dp(8), bannerTextW, Dp(38));
			int textH = MeasureLabelHeight(_bannerText, bannerTextW, Dp(8));
			_bannerText.Height = textH;
			int bannerH = Math.Max(Dp(52), textH + Dp(14));
			_bannerPanel.Height = bannerH;
			_bannerIcon.SetBounds(Dp(16), Math.Max(Dp(4), (bannerH - Dp(24)) / 2), Dp(20), Dp(24));
			if (_bannerAction.Visible)
			{
				_bannerAction.SetBounds(contentW - bannerPad - _bannerAction.Width, Math.Max(0, (bannerH - _bannerAction.Height) / 2), _bannerAction.Width, _bannerAction.Height);
			}

			int footerH = Math.Max(_lastRefreshLabel.Height, _configLink.Height);
			int footerTop = _bannerPanel.Bottom + Dp(16);
			int bottomPinned = base.ClientSize.Height - Dp(28) - footerH;
			if (bottomPinned > footerTop)
			{
				footerTop = bottomPinned;
			}
			_smallDot.SetBounds(margin, footerTop + (footerH - Dp(8)) / 2, Dp(8), Dp(8));
			_lastRefreshLabel.Location = new Point(margin + Dp(14), footerTop + (footerH - _lastRefreshLabel.Height) / 2);
			_configLink.Location = new Point(width - margin - _configLink.Width, footerTop + (footerH - _configLink.Height) / 2);
			_authorLabel.Location = new Point(_configLink.Left - _authorLabel.Width - Dp(14), footerTop + (footerH - _authorLabel.Height) / 2);

			int contentBottom = footerTop + footerH + Dp(20);
			int minH = Math.Max(Dp(600), contentBottom);
			if (!_userResizing)
			{
				if (!_initialLayoutDone)
				{
					int height = Math.Max(Dp(620), minH) + Dp(10);
					base.ClientSize = new Size(width, height);
					base.MinimumSize = new Size(base.Width, base.Height);
					_initialLayoutDone = true;
					_normalClientSize = base.ClientSize;
				}
				else if (base.ClientSize.Height < minH)
				{
					base.ClientSize = new Size(base.ClientSize.Width, minH + Dp(10));
					base.MinimumSize = new Size(base.Width, base.Height);
				}
			}
		}
		finally
		{
			ResumeLayout(performLayout: false);
			_layingOut = false;
		}
	}

	private int MeasureLabelHeight(Label label, int width, int extraHeight)
	{
		Size size = TextRenderer.MeasureText(label.Text ?? string.Empty, label.Font, new Size(Math.Max(Dp(20), width), int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
		return Math.Max(label.Font.Height + Dp(2), size.Height + extraHeight);
	}

	private void SetMeasuredLabel(Label label, int left, int top, int width, int extraHeight)
	{
		label.AutoSize = false;
		int num = Math.Max(Dp(40), width);
		Size size = TextRenderer.MeasureText(label.Text ?? string.Empty, label.Font, new Size(num, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
		int num2 = Math.Max(label.Font.Height + Dp(2), size.Height + extraHeight);
		label.SetBounds(left, top, num, num2);
	}

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	private static extern IntPtr SetCapture(IntPtr hwnd);

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern bool AnimateWindow(IntPtr hwnd, int time, int flags);

	private int Dp(int value)
	{
		int num = 96;
		try
		{
			if (base.IsHandleCreated)
			{
				uint dpi = GetDpiForWindow(base.Handle);
				if (dpi >= 48 && dpi <= 480)
				{
					num = (int)dpi;
				}
			}
		}
		catch
		{
		}
		return (int)Math.Round((double)(value * num) / 96.0);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		MinimumSize = new Size(Dp(600), Dp(520));
		ClientSize = new Size(Dp(640), Dp(620));
		_initialLayoutDone = false;
		LayoutMainContent();
	}

	private void HeaderDragStart(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left && base.WindowState != FormWindowState.Maximized)
		{
			_headerDragging = true;
			_headerDragStart = Cursor.Position;
			_headerFormStart = base.Location;
			base.Capture = true;
		}
	}

	private void HeaderDragMove(object sender, MouseEventArgs e)
	{
		if (_headerDragging)
		{
			Point position = Cursor.Position;
			base.Location = new Point(_headerFormStart.X + position.X - _headerDragStart.X, _headerFormStart.Y + position.Y - _headerDragStart.Y);
		}
	}

	private void HeaderDragEnd(object sender, MouseEventArgs e)
	{
		if (_headerDragging)
		{
			_headerDragging = false;
			base.Capture = false;
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left && e.Y < Dp(48) && e.X < base.ClientSize.Width - Dp(90))
		{
			HeaderDragStart(this, e);
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		HeaderDragMove(this, e);
		base.OnMouseMove(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		HeaderDragEnd(this, e);
		base.OnMouseUp(e);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (base.WindowState == FormWindowState.Minimized)
		{
			_wasMinimized = true;
			if (_restoreTargetSize == Size.Empty)
			{
				_restoreTargetSize = _normalClientSize;
			}
			return;
		}
		if (_restoringSize)
		{
			return;
		}
		if (!_userResizing && base.WindowState == FormWindowState.Normal && _normalClientSize != Size.Empty)
		{
			int num = Math.Abs(base.ClientSize.Width - _normalClientSize.Width);
			int num2 = Math.Abs(base.ClientSize.Height - _normalClientSize.Height);
			if ((_wasMinimized || (num > 0 && num <= 12 && num2 <= 12)) && (num > 0 || num2 > 0))
			{
				_wasMinimized = false;
				Size size = ((_restoreTargetSize == Size.Empty) ? _normalClientSize : _restoreTargetSize);
				_restoreTargetSize = Size.Empty;
				_restoringSize = true;
				try
				{
					base.ClientSize = size;
				}
				finally
				{
					_restoringSize = false;
				}
				return;
			}
			_wasMinimized = false;
			if (num > 12 || num2 > 12)
			{
				_normalClientSize = base.ClientSize;
			}
		}
		LayoutMainContent();
	}

	protected override void OnClientSizeChanged(EventArgs e)
	{
		base.OnClientSizeChanged(e);
		if (_restoringSize || _userResizing || _layingOut || base.WindowState != FormWindowState.Normal || _normalClientSize == Size.Empty)
		{
			return;
		}
		int num = Math.Abs(base.ClientSize.Width - _normalClientSize.Width);
		int num2 = Math.Abs(base.ClientSize.Height - _normalClientSize.Height);
		if ((_wasMinimized || (num > 0 && num <= 12 && num2 <= 12)) && (num > 0 || num2 > 0))
		{
			_wasMinimized = false;
			_restoringSize = true;
			try
			{
				base.ClientSize = _normalClientSize;
			}
			finally
			{
				_restoringSize = false;
			}
		}
	}

	protected override void OnResizeBegin(EventArgs e)
	{
		_userResizing = true;
		base.OnResizeBegin(e);
	}

	protected override void OnResizeEnd(EventArgs e)
	{
		_userResizing = false;
		base.OnResizeEnd(e);
		LayoutMainContent();
		_normalClientSize = base.ClientSize;
		base.MinimumSize = new Size(base.Width, base.Height);
		CenterToScreen();
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		MinimumSize = new Size(Dp(600), Dp(520));
		_initialLayoutDone = false;
		LayoutMainContent();
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 161 && (int)m.WParam == 2)
		{
			Activate();
			_captionDragging = true;
			_headerDragStart = Cursor.Position;
			_headerFormStart = base.Location;
			SetCapture(base.Handle);
			return;
		}
		if (m.Msg == 163 && (int)m.WParam == 2)
		{
			base.WindowState = ((base.WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized);
			return;
		}
		if (m.Msg == 533)
		{
			_captionDragging = false;
		}
		if (_captionDragging && (m.Msg == 512 || m.Msg == 160))
		{
			if (m.Msg == 512)
			{
				Point position = Cursor.Position;
				base.Location = new Point(_headerFormStart.X + position.X - _headerDragStart.X, _headerFormStart.Y + position.Y - _headerDragStart.Y);
			}
			else
			{
				_captionDragging = false;
				ReleaseCapture();
			}
			return;
		}
		base.WndProc(ref m);
		if (m.Msg == 0x001A && Theme.Mode == ThemeMode.System)
		{
			string text = null;
			try { text = Marshal.PtrToStringAuto(m.LParam); } catch { }
			if (string.Equals(text, "ImmersiveColorSet", StringComparison.Ordinal))
			{
				Theme.SetMode(ThemeMode.System);
			}
		}
	}

	private async void MainForm_Shown(object sender, EventArgs e)
	{
		ApplyTitleBarTheme();
		LayoutMainContent();
		_normalClientSize = base.ClientSize;
		base.MinimumSize = new Size(base.Width, base.Height);
		CenterToScreen();
		await InitializeAsync();
	}

	private async Task InitializeAsync()
	{
		UpdateAdminBadge();
		if (string.IsNullOrWhiteSpace(_config.ServiceName))
		{
			try
			{
				List<ServiceOption> options = await Task.Run(() => ServiceManager.FindMySqlServices());
				if (options.Count > 0)
				{
					_config.ServiceName = options[0].Name;
					if (string.IsNullOrWhiteSpace(_config.MySqlPath))
					{
						_config.MySqlPath = ServiceManager.TryGetImagePath(options[0].Name);
					}
					try
					{
						ConfigStore.Save(_config);
						AppLog.Info("首次运行自动选择服务：" + _config.ServiceName);
					}
					catch (Exception exception)
					{
						AppLog.Error("保存自动检测配置失败。", exception);
					}
				}
			}
			catch (Exception exception2)
			{
				AppLog.Error("初始化自动检测失败。", exception2);
			}
		}
		_initialized = true;
		await RefreshStatusAsync();
		if (!string.IsNullOrWhiteSpace(_startupWarning))
		{
			ShowBanner(BannerKind.Warning, _startupWarning + " 可在设置中重新配置。", "打开设置", "settings", 12);
		}
		await CheckWorkbenchUpdateIfDueAsync(manual: false);
	}

	private async void RefreshTimer_Tick(object sender, EventArgs e)
	{
		if (_initialized && !_busy)
		{
			await RefreshStatusAsync();
		}
	}

	private async Task RefreshStatusAsync()
	{
		if (_refreshing || _busy)
		{
			return;
		}
		_refreshing = true;
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				string serviceName = _config.ServiceName;
				string configuredPath = _config.MySqlPath;
				ServiceSnapshot snapshot = await Task.Run(() => ServiceManager.GetSnapshot(serviceName, configuredPath));
				if (!base.IsDisposed)
				{
					ApplySnapshot(snapshot);
				}
			}
			catch (Exception ex)
			{
				AppLog.Error("刷新服务状态失败。", ex);
				if (base.IsDisposed || base.Disposing)
				{
					return;
				}
				_statusLabel.Text = "读取失败";
				_statusLabel.ForeColor = Theme.Danger;
				_statusDot.DotColor = Theme.Danger;
				_statusDescription.Text = "刷新状态时发生异常，将在下次自动重试。";
				ShowBanner(BannerKind.Error, "刷新失败：" + ex.Message, "打开设置", "settings", 10);
			}
		}
		finally
		{
			_refreshing = false;
		}
	}

	private void ApplySnapshot(ServiceSnapshot snapshot)
	{
		_snapshot = snapshot;
		_lastRefreshLabel.Text = "每 " + _config.RefreshSeconds + " 秒自动刷新  ·  " + DateTime.Now.ToString("HH:mm:ss");
		if (snapshot != null)
		{
			string text = (string.IsNullOrWhiteSpace(snapshot.ServiceName) ? "尚未配置" : snapshot.ServiceName);
			if (!snapshot.Installed)
			{
				SetStatusVisual(snapshot.StatusText, Theme.Danger, "Windows 中未找到该服务，请检查服务名称或先安装 MySQL 服务。");
				_serviceValue.Text = text;
				_startupValue.Text = "—";
				_accountValue.Text = "—";
				_pathValue.Text = (string.IsNullOrWhiteSpace(snapshot.ImagePath) ? "未配置" : snapshot.ImagePath);
				_pathValue.ForeColor = Theme.Subtle;
				_toolTip.SetToolTip(_pathValue, _pathValue.Text);
				_toolTip.SetToolTip(_serviceValue, text);
				LayoutMainContent();
				SetActionState(canStart: false, canStop: false);
				ShowDefaultBanner(snapshot);
			}
			else
			{
				Color statusColor = GetStatusColor(snapshot);
				SetStatusVisual(snapshot.StatusText, statusColor, GetStatusDescription(snapshot));
				_serviceValue.Text = snapshot.ServiceName;
				_startupValue.Text = snapshot.StartupType;
				_accountValue.Text = snapshot.Account;
				_pathValue.Text = (string.IsNullOrWhiteSpace(snapshot.ImagePath) ? "未能从服务注册信息读取" : snapshot.ImagePath);
				_pathValue.ForeColor = (string.IsNullOrWhiteSpace(snapshot.ImagePath) ? Theme.Subtle : Theme.Muted);
				_toolTip.SetToolTip(_pathValue, _pathValue.Text);
				_toolTip.SetToolTip(_serviceValue, (string.Equals(snapshot.ServiceName, snapshot.DisplayName, StringComparison.OrdinalIgnoreCase) ? snapshot.ServiceName : (snapshot.ServiceName + " · " + snapshot.DisplayName)));
				LayoutMainContent();
				SetActionState(snapshot.CanStart, snapshot.IsRunning && snapshot.CanStop);
				ShowDefaultBanner(snapshot);
			}
		}
	}

	private void SetStatusVisual(string text, Color color, string description)
	{
		_statusLabel.Text = text;
		_statusLabel.ForeColor = color;
		_statusDot.DotColor = color;
		_statusCard.AccentColor = color;
		_statusDescription.Text = description;
	}

	private void SetActionState(bool canStart, bool canStop)
	{
		_startButton.Enabled = !_busy && canStart;
		_stopButton.Enabled = !_busy && canStop;
		if (_workbenchButton != null)
		{
			_workbenchButton.Enabled = !_busy;
		}
	}

	private static Color GetStatusColor(ServiceSnapshot snapshot)
	{
		if (snapshot.IsTransitioning)
		{
			return Theme.Accent;
		}
		if (snapshot.IsDisabled)
		{
			return Theme.Warning;
		}
		if (snapshot.IsRunning)
		{
			return Theme.Success;
		}
		if (snapshot.Status.HasValue && snapshot.Status.Value == ServiceControllerStatus.Stopped)
		{
			return Theme.Neutral;
		}
		if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
		{
			return Theme.Danger;
		}
		return Theme.Warning;
	}

	private static string GetStatusDescription(ServiceSnapshot snapshot)
	{
		if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
		{
			return snapshot.ErrorMessage;
		}
		if (snapshot.IsTransitioning)
		{
			return "Windows 正在处理服务状态，请稍候。";
		}
		if (snapshot.IsDisabled)
		{
			return "该服务已被禁用，需要先在“服务”管理器中启用。";
		}
		if (snapshot.IsRunning)
		{
			return "MySQL 已准备就绪，可以连接数据库。";
		}
		if (snapshot.Status.HasValue && snapshot.Status.Value == ServiceControllerStatus.Stopped)
		{
			return "服务当前未运行，点击下方“启动服务”即可开启。";
		}
		return "无法确认服务当前的工作状态。";
	}

	private void ShowDefaultBanner(ServiceSnapshot snapshot)
	{
		if (!(DateTime.Now < _bannerHoldUntil))
		{
			if (!snapshot.Installed)
			{
				ShowBanner(BannerKind.Warning, "未找到服务“" + snapshot.ServiceName + "”。请选择正确的服务名，或先安装 MySQL 服务。", "选择服务", "settings");
			}
			else if (snapshot.IsDisabled)
			{
				ShowBanner(BannerKind.Warning, "服务启动类型为“已禁用”。请先在 Windows 服务管理器中启用。", "打开服务", "services");
			}
			else if (snapshot.IsTransitioning)
			{
				ShowBanner(BannerKind.Info, "服务状态正在变化，完成后按钮会自动更新。", null, null);
			}
			else if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
			{
				ShowBanner(BannerKind.Error, snapshot.ErrorMessage, "打开设置", "settings");
			}
			else if (!ServiceManager.IsAdministrator())
			{
				ShowBanner(BannerKind.Info, "当前为普通权限：可以查看状态，点击启停时会请求管理员授权。", null, null);
			}
			else
			{
				ShowBanner(BannerKind.Info, "当前拥有管理员权限，可以直接启动或停止 MySQL 服务。", null, null);
			}
		}
	}

	private void ShowBanner(BannerKind kind, string text, string actionText, string actionKind, int holdSeconds = 0)
	{
		_lastBannerKind = kind;
		Color fillColor;
		Color color;
		string text2;
		switch (kind)
		{
		case BannerKind.Success:
			fillColor = Theme.SuccessSoft;
			color = Theme.Success;
			text2 = "✓";
			break;
		case BannerKind.Warning:
			fillColor = Theme.WarningSoft;
			color = Theme.Warning;
			text2 = "!";
			break;
		case BannerKind.Error:
			fillColor = Theme.DangerSoft;
			color = Theme.Danger;
			text2 = "!";
			break;
		default:
			fillColor = Theme.AccentSoft;
			color = Theme.Accent;
			text2 = "i";
			break;
		}
		_bannerPanel.FillColor = fillColor;
		_bannerPanel.AccentColor = color;
		_bannerIcon.Text = text2;
		_bannerIcon.ForeColor = color;
		_bannerText.Text = text;
		_bannerAction.Text = actionText ?? string.Empty;
		_bannerAction.Visible = !string.IsNullOrWhiteSpace(actionText);
		_bannerActionKind = actionKind;
		if (holdSeconds > 0)
		{
			_bannerHoldUntil = DateTime.Now.AddSeconds(holdSeconds);
		}
		LayoutMainContent();
	}

	private void UpdateAdminBadge()
	{
		if (ServiceManager.IsAdministrator())
		{
			_adminBadge.Text = "管理员模式";
			_adminBadge.BadgeColor = Theme.SuccessSoft;
			_adminBadge.TextColor = Theme.Success;
		}
		else
		{
			_adminBadge.Text = "普通权限 · 操作时授权";
			_adminBadge.BadgeColor = Theme.WarningSoft;
			_adminBadge.TextColor = Theme.Warning;
		}
	}

	private async void StartButton_Click(object sender, EventArgs e)
	{
		await ExecuteServiceActionAsync("start");
	}

	private async void StopButton_Click(object sender, EventArgs e)
	{
		await ExecuteServiceActionAsync("stop");
	}

	private async Task CheckWorkbenchUpdateIfDueAsync(bool manual)
	{
		if (_checkingWorkbenchUpdate || _workbenchUpdateLink == null)
		{
			return;
		}
		string text = WorkbenchLocator.Find(_config.WorkbenchPath);
		if (string.IsNullOrEmpty(text))
		{
			_workbenchUpdateLink.Visible = false;
			LayoutMainContent();
			return;
		}
		if (string.IsNullOrEmpty(_workbenchInstalledVersion))
		{
			_workbenchInstalledVersion = WorkbenchUpdateChecker.GetInstalledVersion(text);
		}
		DateTime result;
		bool flag = !DateTime.TryParse(_config.WorkbenchCheckedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result) || DateTime.UtcNow - result > TimeSpan.FromHours(24.0);
		if (!manual && !flag)
		{
			_workbenchLatestVersion = _config.WorkbenchLatestVersion;
			UpdateWorkbenchUpdateVisual();
			return;
		}
		_checkingWorkbenchUpdate = true;
		SetWorkbenchUpdateText("Workbench " + _workbenchInstalledVersion + " · 正在检查更新…", actionable: false);
		WorkbenchUpdateInfo workbenchUpdateInfo = await WorkbenchUpdateChecker.CheckAsync(text);
		_checkingWorkbenchUpdate = false;
		if (workbenchUpdateInfo.CheckSucceeded)
		{
			_workbenchLatestVersion = workbenchUpdateInfo.LatestVersion;
			_config.WorkbenchLatestVersion = workbenchUpdateInfo.LatestVersion;
			_config.WorkbenchCheckedUtc = DateTime.UtcNow.ToString("o");
			try
			{
				ConfigStore.Save(_config);
			}
			catch (Exception ex)
			{
				AppLog.Error("保存 Workbench 更新检查结果失败。", ex);
			}
		}
		else if (!string.IsNullOrEmpty(_config.WorkbenchLatestVersion))
		{
			_workbenchLatestVersion = _config.WorkbenchLatestVersion;
		}
		UpdateWorkbenchUpdateVisual(workbenchUpdateInfo);
	}

	private void UpdateWorkbenchUpdateVisual(WorkbenchUpdateInfo info = null)
	{
		if (string.IsNullOrEmpty(_workbenchInstalledVersion))
		{
			_workbenchUpdateLink.Visible = false;
			LayoutMainContent();
			return;
		}
		if (info != null && !info.CheckSucceeded)
		{
			SetWorkbenchUpdateText("Workbench " + _workbenchInstalledVersion + " · 检查更新失败，点击重试", actionable: true);
			return;
		}
		Version result;
		Version result2;
		bool flag = Version.TryParse(_workbenchInstalledVersion, out result) && Version.TryParse(_workbenchLatestVersion, out result2) && result2 > result;
		_workbenchUpdateAvailable = flag;
		if (flag)
		{
			SetWorkbenchUpdateText("Workbench " + _workbenchInstalledVersion + " → " + _workbenchLatestVersion + " 可用，点击前往下载", actionable: true);
		}
		else if (!string.IsNullOrEmpty(_workbenchLatestVersion))
		{
			SetWorkbenchUpdateText("Workbench " + _workbenchInstalledVersion + " · 已是最新版本", actionable: false);
		}
		else
		{
			SetWorkbenchUpdateText("Workbench " + _workbenchInstalledVersion + " · 点击检查更新", actionable: true);
		}
	}

	private void SetWorkbenchUpdateText(string text, bool actionable)
	{
		_workbenchUpdateLink.Text = text;
		_workbenchUpdateLink.LinkArea = (actionable ? new LinkArea(0, text.Length) : new LinkArea(0, 0));
		_workbenchUpdateLink.LinkColor = (actionable ? Theme.Accent : Theme.Muted);
		_workbenchUpdateLink.VisitedLinkColor = _workbenchUpdateLink.LinkColor;
		_workbenchUpdateLink.ForeColor = _workbenchUpdateLink.LinkColor;
		_workbenchUpdateLink.Visible = true;
		LayoutMainContent();
	}

	private async void WorkbenchUpdateLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
	{
		if (_workbenchUpdateAvailable)
		{
			try
			{
				Process.Start(new ProcessStartInfo(WorkbenchUpdateChecker.DownloadUrl)
				{
					UseShellExecute = true
				});
				AppLog.Info("已打开 MySQL Workbench 下载页。");
			}
			catch (Exception ex)
			{
				AppLog.Error("打开 Workbench 下载页失败。", ex);
				MessageBox.Show("无法打开浏览器。\n\n" + ex.Message + "\n\n下载地址：\n" + WorkbenchUpdateChecker.DownloadUrl, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			return;
		}
		await CheckWorkbenchUpdateIfDueAsync(manual: true);
	}

	private void WorkbenchButton_Click(object sender, EventArgs e)
	{
		if (_busy)
		{
			return;
		}
		string text = WorkbenchLocator.Find(_config.WorkbenchPath);
		if (string.IsNullOrEmpty(text))
		{
			DialogResult dialogResult = MessageBox.Show("未找到 MySQL Workbench。\n\n是否手动选择 MySQLWorkbench.exe？", "未找到 Workbench", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
			if (dialogResult != DialogResult.Yes)
			{
				return;
			}
			using OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Title = "选择 MySQL Workbench";
			openFileDialog.Filter = "MySQL Workbench (MySQLWorkbench.exe;MySQL Workbench.exe)|MySQLWorkbench.exe;MySQL Workbench.exe|可执行文件 (*.exe)|*.exe";
			openFileDialog.CheckFileExists = true;
			openFileDialog.Multiselect = false;
			if (openFileDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			text = openFileDialog.FileName;
			if (!WorkbenchLocator.IsWorkbenchExecutable(text))
			{
				MessageBox.Show("所选文件不是 MySQL Workbench 的可执行文件。\n\n请选择 MySQLWorkbench.exe（旧版本）或 MySQL Workbench.exe（新版本）。", "文件不匹配", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			_config.WorkbenchPath = text;
			try
			{
				ConfigStore.Save(_config);
			}
			catch (Exception ex)
			{
				AppLog.Error("保存 Workbench 路径失败。", ex);
			}
		}
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(text);
			processStartInfo.UseShellExecute = true;
			processStartInfo.WorkingDirectory = Path.GetDirectoryName(text);
			Process.Start(processStartInfo);
			AppLog.Info("已启动 MySQL Workbench：" + text);
			bool flag = _snapshot != null && _snapshot.IsRunning;
			ShowBanner(flag ? BannerKind.Success : BannerKind.Warning, flag ? "已启动 MySQL Workbench。" : "已启动 MySQL Workbench；MySQL 服务当前未运行，连接数据库前请先启动服务。", null, null, 8);
		}
		catch (Exception ex2)
		{
			AppLog.Error("启动 MySQL Workbench 失败。", ex2);
			MessageBox.Show("无法启动 MySQL Workbench。\n\n" + ex2.Message + "\n\n路径：" + text, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async Task ExecuteServiceActionAsync(string action)
	{
		if (_busy || _snapshot == null)
		{
			return;
		}
		if (!_snapshot.Installed)
		{
			MessageBox.Show("没有找到服务“" + _config.ServiceName + "”。\n\n请先在设置中选择正确的 Windows 服务名称。", "服务未找到", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		string verb = ((action == "start") ? "启动" : "停止");
		_busy = true;
		_statusLabel.Text = "正在" + verb + "…";
		_statusLabel.ForeColor = Theme.Accent;
		_statusDot.DotColor = Theme.Accent;
		_statusDescription.Text = "正在等待 Windows 服务管理器响应。";
		SetActionState(canStart: false, canStop: false);
		ShowBanner(BannerKind.Info, "正在" + verb + " MySQL 服务，请稍候。" + (ServiceManager.IsAdministrator() ? string.Empty : " 如出现 UAC 提示，请选择“是”。"), null, null);
		ServiceActionResult result;
		try
		{
			result = ((!ServiceManager.IsAdministrator()) ? (await Task.Run(() => ElevationHelper.RunElevatedAction(action, _config.ServiceName))) : (await Task.Run(() => (!(action == "start")) ? ServiceManager.StopService(_config.ServiceName) : ServiceManager.StartService(_config.ServiceName))));
		}
		catch (Exception ex)
		{
			AppLog.Error("执行服务操作失败。", ex);
			result = new ServiceActionResult
			{
				Success = false,
				Message = "执行操作时发生异常：" + ex.Message,
				Detail = "请查看程序日志：" + AppLog.LogPath
			};
		}
		finally
		{
			_busy = false;
		}
		await RefreshStatusAsync();
		if (base.IsDisposed || base.Disposing)
		{
			return;
		}
		if (result.Success)
		{
		_bannerHoldUntil = DateTime.Now.AddSeconds(8.0);
		ShowBanner(BannerKind.Success, result.Message, null, null);
			return;
		}
		ShowBanner(result.WasCancelled ? BannerKind.Warning : BannerKind.Error, result.Message, (result.Message.IndexOf("没有找到", StringComparison.Ordinal) >= 0) ? "打开设置" : null, "settings", 20);
		string text = result.Message;
		if (!string.IsNullOrWhiteSpace(result.Detail))
		{
			text = text + "\n\n" + result.Detail;
		}
		MessageBox.Show(text, result.WasCancelled ? "操作已取消" : (verb + "失败"), MessageBoxButtons.OK, result.WasCancelled ? MessageBoxIcon.Asterisk : MessageBoxIcon.Exclamation);
	}

	private async void SettingsButton_Click(object sender, EventArgs e)
	{
		if (_busy)
		{
			return;
		}
		using SettingsForm form = new SettingsForm(_config);
		if (form.ShowDialog(this) == DialogResult.OK)
		{
			_config = form.SavedConfig;
			Theme.SetMode(Theme.ParseMode(_config.ThemeMode));
			_refreshTimer.Interval = GetRefreshInterval();
			_bannerHoldUntil = DateTime.MinValue;
			await RefreshStatusAsync();
		}
	}

	private int GetRefreshInterval()
	{
		int num = ((_config == null) ? 2 : _config.RefreshSeconds);
		if (num < 1 || num > 60)
		{
			num = 2;
		}
		return num * 1000;
	}

	private void BannerAction_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
	{
		if (_bannerActionKind == "services")
		{
			OpenServicesManager();
		}
		else if (_bannerActionKind == "settings")
		{
			SettingsButton_Click(sender, EventArgs.Empty);
		}
	}

	private void ConfigLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
	{
		try
		{
			Directory.CreateDirectory(AppLog.AppDataDirectory);
			Process.Start("explorer.exe", "\"" + AppLog.AppDataDirectory + "\"");
		}
		catch (Exception ex)
		{
			AppLog.Error("打开配置文件夹失败。", ex);
			MessageBox.Show("无法打开配置文件夹。\n\n" + AppLog.AppDataDirectory + "\n\n" + ex.Message, "打开文件夹失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	private static void OpenServicesManager()
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo("services.msc");
			processStartInfo.UseShellExecute = true;
			Process.Start(processStartInfo);
		}
		catch (Exception ex)
		{
			AppLog.Error("打开服务管理器失败。", ex);
			MessageBox.Show("无法打开 Windows 服务管理器。\n\n" + ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
	{
		if (_busy)
		{
			e.Cancel = true;
			ShowBanner(BannerKind.Info, "服务操作正在进行，请等待完成后再关闭窗口。", null, null, 5);
		}
		else
		{
			_refreshTimer.Stop();
		}
	}

	private async void MainForm_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.F5 && !_busy)
		{
			e.Handled = true;
			await RefreshStatusAsync();
		}
		else if (e.Control && e.KeyCode == Keys.Oemcomma)
		{
			e.Handled = true;
			SettingsButton_Click(sender, EventArgs.Empty);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (_refreshTimer != null)
			{
				_refreshTimer.Dispose();
			}
			if (_toolTip != null)
			{
				_toolTip.Dispose();
			}
		}
		base.Dispose(disposing);
	}
}
