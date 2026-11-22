using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MySqlServiceController;

/// <summary>主界面里的“安装组件”窗口：安装 / 升级 MySQL 服务与 MySQL Workbench。</summary>
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

	/// <summary>本次安装的 MySQL 服务名称（没有注册服务时为 null）。</summary>
	public string InstalledServiceName { get; private set; }

	/// <summary>本次安装的 Workbench 目录（没有安装时为 null）。</summary>
	public string InstalledWorkbenchDirectory { get; private set; }

	public ComponentInstallForm()
	{
		Text = "安装组件";
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
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
		_scale = (float)DeviceDpi / 96f;
		_serverOptions.AddRange(ComponentCatalog.GetOptions(ComponentCatalog.KindServer));
		_workbenchOptions.AddRange(ComponentCatalog.GetOptions(ComponentCatalog.KindWorkbench));
		BuildInterface();
		ApplyTheme();
		RefreshStates();
		LayoutContent();
		SetStatus(StatusKind.Info, "勾选要安装的组件，然后点击“开始安装”。");
		base.Shown += ComponentInstallForm_Shown;
	}

	private int Dp(int value)
	{
		return (int)Math.Round((double)(value * _scale));
	}

	private void BuildInterface()
	{
		SuspendLayout();
		_titleLabel = MakeLabel("安装 MySQL 组件", 24, 18, 380, 32, Theme.Font(16.5f, FontStyle.Bold), Theme.Text);
		base.Controls.Add(_titleLabel);
		_subtitleLabel = MakeLabel("自动下载官方安装包并静默安装，可选版本与安装位置", 24, 54, 520, 20, Theme.Font(9f, FontStyle.Regular), Theme.Muted);
		base.Controls.Add(_subtitleLabel);

		// ---- MySQL 服务 ----
		_serverCard = new SurfacePanel
		{
			FillColor = Theme.Surface,
			BorderColor = Color.Transparent
		};
		base.Controls.Add(_serverCard);
		_serverTitle = MakeLabel("MySQL 服务", 52, 16, 200, 24, Theme.Font(10.2f, FontStyle.Bold), Theme.Text);
		_serverCard.Controls.Add(_serverTitle);
		_serverVersionCaption = MakeLabel("版本", 0, 0, 38, 20, Theme.Font(8.4f, FontStyle.Regular), Theme.Subtle);
		_serverVersionCaption.TextAlign = ContentAlignment.MiddleRight;
		_serverCard.Controls.Add(_serverVersionCaption);
		_serverHint = MakeLabel(string.Empty, 0, 0, 520, 18, Theme.Font(8.2f, FontStyle.Regular), Theme.Muted);
		_serverHint.AutoEllipsis = true;
		_serverCard.Controls.Add(_serverHint);
		_serverCheck = new SlimCheckBox
		{
			Text = "安装 MySQL 服务",
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
			Text = "初始化数据目录并注册为 Windows 服务（root 初始密码为空）",
			Checked = true
		};
		_serverServiceCheck.CheckedChanged += delegate
		{
			RefreshStates();
		};
		_serverCard.Controls.Add(_serverServiceCheck);
		_serverState = MakeLabel(string.Empty, 40, 0, 320, 20, Theme.Font(8.4f, FontStyle.Regular), Theme.Subtle);
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
		_workbenchState = MakeLabel(string.Empty, 40, 0, 320, 20, Theme.Font(8.4f, FontStyle.Regular), Theme.Subtle);
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
		base.Controls.Add(_authorLabel);
		base.AcceptButton = _installButton;
		base.CancelButton = _closeButton;
		ResumeLayout(performLayout: true);
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
			int serverH = Dp(206);
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
			_serverState.SetBounds(Dp(22), Dp(172), contentW - Dp(44), Dp(20));

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
			_workbenchState.SetBounds(Dp(22), Dp(124), contentW - Dp(44), Dp(20));

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
		_serverCheck.Enabled = !_busy;
		_workbenchVersionButton.Enabled = !_busy;
		_workbenchPath.Enabled = workbenchOn && !_busy;
		_workbenchBrowse.Enabled = workbenchOn && !_busy;
		_workbenchCheck.Enabled = !_busy;
		string installedServer = ComponentCatalog.DescribeInstalled(ComponentCatalog.KindServer, out string serverDirectory);
		string installedWorkbench = ComponentCatalog.DescribeInstalled(ComponentCatalog.KindWorkbench, out string workbenchDirectory);
		if (serverOn)
		{
			string serviceName = ComponentCatalog.DefaultServiceName(_serverOptions[_serverIndex].Version);
			_serverState.Text = (_serverServiceCheck.Checked ? ("安装后初始化数据目录，并注册为 Windows 服务 " + serviceName) : "仅安装程序文件，不初始化数据目录、不注册服务");
			_serverState.ForeColor = Theme.Muted;
		}
		else if (installedServer != null)
		{
			_serverState.Text = installedServer + "  ·  " + serverDirectory;
			_serverState.ForeColor = Theme.Success;
		}
		else
		{
			_serverState.Text = "当前未检测到已安装的 MySQL 服务";
			_serverState.ForeColor = Theme.Subtle;
		}
		if (workbenchOn)
		{
			_workbenchState.Text = "安装后可在主界面直接打开 Workbench";
			_workbenchState.ForeColor = Theme.Muted;
		}
		else if (installedWorkbench != null)
		{
			_workbenchState.Text = installedWorkbench + "  ·  " + workbenchDirectory;
			_workbenchState.ForeColor = Theme.Success;
		}
		else
		{
			_workbenchState.Text = "当前未检测到已安装的 MySQL Workbench";
			_workbenchState.ForeColor = Theme.Subtle;
		}
		_installButton.Enabled = !_busy;
		_closeButton.Enabled = !_busy;
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

	private void ShowVersionMenu(RoundedButton owner, List<ComponentOption> options, int selected, Action<int> onPicked)
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

	protected override void Dispose(bool disposing)
	{
		if (disposing && _versionMenu != null)
		{
			_versionMenu.Dispose();
			_versionMenu = null;
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

	private async void InstallButton_Click(object sender, EventArgs e)
	{
		if (_busy)
		{
			return;
		}
		List<ComponentJob> jobs = new List<ComponentJob>();
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
				result = await Task.Run(() => ComponentInstaller.Run(job, ReportProgress));
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
		_scale = (float)e.DeviceDpiNew / 96f;
		LayoutContent();
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
			SetStatus(StatusKind.Info, "安装进行中，请等待完成后再关闭。", -1, 5);
		}
		base.OnFormClosing(e);
	}
}
