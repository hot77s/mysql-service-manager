using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MySqlServiceController.Setup;

internal sealed class SetupForm : Form
{
	private enum BannerKind
	{
		Info,
		Success,
		Warning,
		Error
	}

	private const int WindowWidth = 520;

	private readonly bool _uninstallMode;

	private readonly List<Action> _themeActions = new List<Action>();

	private readonly List<Action> _layoutActions = new List<Action>();

	private readonly System.Windows.Forms.Timer _pollTimer;

	private readonly System.Windows.Forms.Timer _autoCloseTimer;

	private Panel _header;

	private Panel _content;

	private Panel _footer;

	private Label _titleLabel;

	private Label _subtitleLabel;

	private PillButton[] _themeButtons;

	private CardPanel _scopeCard;

	private NumberBadge _scopeBadge;

	private Label _scopeTitle;

	private Label _scopeHint;

	private SegmentedControl _scopeSegment;

	private CardPanel _pathCard;

	private NumberBadge _pathBadge;

	private Label _pathTitle;

	private Label _pathHint;

	private PathField _appPathField;

	private PillButton _appBrowseButton;

	private LinkLabel _appRestoreLink;

	private CardPanel _summaryCard;

	private NumberBadge _summaryBadge;

	private Label _summaryTitle;

	private Label _summaryHint;

	private Label _uninstallSummaryLabel;

	private SetupCheckBox _desktopCheck;

	private SetupCheckBox _launchCheck;

	private CardPanel _bannerCard;

	private Label _bannerIcon;

	private Label _bannerText;

	private ProgressLine _progressLine;

	private Label _authorLabel;

	private PillButton _cancelButton;

	private PillButton _primaryButton;

	private InstallScope _scope = InstallScope.CurrentUser;

	private string _appDirectory;

	private bool _appPathCustom;

	private bool _busy;


	private string _statusText;

	private int _statusPercent = -1;

	private BannerKind _bannerKind;

	private Process _elevatedProcess;

	private string _jobPath;

	private string _resultPath;

	private string _progressPath;

	private string _lastProgressLine;

	private float _scale = 1f;

	private int _themeIndex;

	private readonly string[] _themeLabels = new string[3] { "系统", "浅色", "深色" };

	internal static string InstallDirectoryOverride;

	internal static InstallScope? ScopeOverride;

	public SetupForm(bool uninstallMode)
	{
		_uninstallMode = uninstallMode;
		Text = (uninstallMode ? "卸载 MySQL管家" : "安装 MySQL管家");
		base.StartPosition = FormStartPosition.CenterScreen;
		base.FormBorderStyle = FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.BackColor = SetupTheme.Background;
		base.Font = SetupTheme.Font(9.2f, FontStyle.Regular);
		base.AutoScaleMode = AutoScaleMode.None;
		base.KeyPreview = false;
		try
		{
			base.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch
		{
		}
		SetupInstaller.TryFindInstalled(out string installed, out InstallScope installedScope);
		_scope = (ScopeOverride ?? installedScope);
		_appDirectory = ((!string.IsNullOrWhiteSpace(installed)) ? installed : SetupInfo.DefaultAppDirectory(_scope));
		if (!string.IsNullOrWhiteSpace(InstallDirectoryOverride))
		{
			_appDirectory = Path.GetFullPath(InstallDirectoryOverride);
			_appPathCustom = true;
		}
		_themeIndex = (int)SetupTheme.Mode;
		_pollTimer = new System.Windows.Forms.Timer
		{
			Interval = 300
		};
		_pollTimer.Tick += PollTimer_Tick;
		_autoCloseTimer = new System.Windows.Forms.Timer
		{
			Interval = 1400
		};
		_autoCloseTimer.Tick += AutoCloseTimer_Tick;
		BuildInterface();
		ApplyTheme();
		SetupTheme.Changed += Theme_Changed;
		PrepareWindow();
	}

	private void Theme_Changed(object sender, EventArgs e)
	{
		if (base.IsHandleCreated && !base.IsDisposed)
		{
			ApplyTheme();
		}
	}

	private void BuildInterface()
	{
		SuspendLayout();
		_scale = 1f;
		_header = new Panel
		{
			Dock = DockStyle.Top,
			Height = Dp(68),
			BackColor = SetupTheme.Background
		};
		base.Controls.Add(_header);
		_content = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			BackColor = SetupTheme.Background
		};
		base.Controls.Add(_content);
		_footer = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = Dp(84),
			BackColor = SetupTheme.Background
		};
		base.Controls.Add(_footer);
		_content.BringToFront();
		_titleLabel = MakeLabel(_header, _uninstallMode ? "卸载 MySQL管家" : "安装 MySQL管家", 24, 16, 420, 34, SetupTheme.Font(16f, FontStyle.Bold), () => SetupTheme.Text);
		_subtitleLabel = MakeLabel(_header, _uninstallMode ? "移除程序、快捷方式与配置，不影响数据库" : ("选择安装范围与程序位置  ·  v" + SetupInfo.AppVersion), 24, 36, 480, 18, SetupTheme.Font(8.6f, FontStyle.Regular), () => SetupTheme.Muted);
		_themeButtons = new PillButton[3];
		for (int i = 0; i < 3; i++)
		{
			PillButton pillButton = new PillButton
			{
				Text = _themeLabels[i],
				Font = SetupTheme.Font(8.4f, FontStyle.Bold),
				Radius = 8f
			};
			int index = i;
			pillButton.Click += delegate
			{
				_themeIndex = index;
				SetupTheme.SetMode((SetupThemeMode)index);
				ApplyTheme();
			};
			_themeActions.Add(delegate
			{
				bool flag = index == _themeIndex;
				pillButton.NormalColor = (flag ? SetupTheme.Accent : SetupTheme.Surface);
				pillButton.HoverColor = (flag ? SetupTheme.AccentDark : SetupTheme.Mix(SetupTheme.Surface, SetupTheme.Accent, 0.12));
				pillButton.PressedColor = (flag ? SetupTheme.AccentDark : SetupTheme.Mix(SetupTheme.Surface, SetupTheme.Accent, 0.22));
				pillButton.ForeColor = (flag ? Color.White : SetupTheme.Muted);
				pillButton.ButtonBorderColor = (flag ? Color.Transparent : SetupTheme.Border);
				pillButton.SurfaceColor = SetupTheme.Background;
			});
			_themeButtons[i] = pillButton;
			_header.Controls.Add(pillButton);
		}
		if (_uninstallMode)
		{
			BuildUninstallLayout();
		}
		else
		{
			BuildInstallLayout();
		}
		// 标题左侧的序号徽标在紧凑版式里会挤压文字对齐，统一隐藏，改用纯文字层级。
		if (_scopeBadge != null)
		{
			_scopeBadge.Visible = false;
		}
		if (_pathBadge != null)
		{
			_pathBadge.Visible = false;
		}
		if (_summaryBadge != null)
		{
			_summaryBadge.Visible = false;
		}
		_bannerCard = new CardPanel
		{
			Radius = Dp(12),
			FillColor = SetupTheme.AccentSoft
		};
		_footer.Controls.Add(_bannerCard);
		_bannerIcon = MakeLabel(_bannerCard, "i", 12, 9, 16, 20, SetupTheme.Font(9.6f, FontStyle.Bold), () => SetupTheme.Accent);
		_bannerIcon.TextAlign = ContentAlignment.MiddleCenter;
		_bannerText = MakeLabel(_bannerCard, string.Empty, 34, 2, 400, 34, SetupTheme.Font(8.2f, FontStyle.Regular), () => SetupTheme.Text);
		_bannerText.AutoSize = false;
		_bannerText.TextAlign = ContentAlignment.MiddleLeft;
		_progressLine = new ProgressLine
		{
			Visible = false
		};
		_footer.Controls.Add(_progressLine);
		_authorLabel = MakeLabel(_footer, "作者  77's  ·  v" + SetupInfo.AppVersion, 24, 52, 200, 20, SetupTheme.Font(8.2f, FontStyle.Regular), () => SetupTheme.Muted);
		_cancelButton = new PillButton
		{
			Text = "取消",
			Font = SetupTheme.Font(8.6f, FontStyle.Bold),
			Radius = 10f
		};
		_cancelButton.Click += delegate
		{
			if (!_busy)
			{
				Close();
			}
		};
		_themeActions.Add(delegate
		{
			// 次要按钮用「白底 + 细描边」：NeutralSoft 在浅色主题下与背景同色，会把按钮画没。
			_cancelButton.NormalColor = SetupTheme.Surface;
			_cancelButton.HoverColor = SetupTheme.Mix(SetupTheme.Surface, SetupTheme.Text, 0.06);
			_cancelButton.PressedColor = SetupTheme.Mix(SetupTheme.Surface, SetupTheme.Text, 0.12);
			_cancelButton.ForeColor = SetupTheme.Text;
			_cancelButton.ButtonBorderColor = SetupTheme.Border;
			_cancelButton.DisabledColor = SetupTheme.Surface;
			_cancelButton.DisabledTextColor = SetupTheme.Subtle;
			_cancelButton.SurfaceColor = SetupTheme.Background;
		});
		_footer.Controls.Add(_cancelButton);
		_primaryButton = new PillButton
		{
			Text = (_uninstallMode ? "卸载" : "开始安装"),
			Font = SetupTheme.Font(8.8f, FontStyle.Bold),
			Radius = 10f
		};
		_primaryButton.Click += PrimaryButton_Click;
		_themeActions.Add(delegate
		{
			_primaryButton.NormalColor = (_uninstallMode ? SetupTheme.Danger : SetupTheme.Accent);
			_primaryButton.HoverColor = (_uninstallMode ? SetupTheme.Mix(SetupTheme.Danger, Color.White, 0.1) : SetupTheme.Mix(SetupTheme.Accent, Color.White, 0.12));
			_primaryButton.PressedColor = (_uninstallMode ? SetupTheme.Mix(SetupTheme.Danger, Color.Black, 0.12) : SetupTheme.Mix(SetupTheme.Accent, Color.Black, 0.12));
			_primaryButton.ForeColor = Color.White;
			_primaryButton.DisabledColor = SetupTheme.NeutralSoft;
			_primaryButton.DisabledTextColor = SetupTheme.Subtle;
			_primaryButton.SurfaceColor = SetupTheme.Background;
		});
		_footer.Controls.Add(_primaryButton);
		base.AcceptButton = _primaryButton;
		base.CancelButton = _cancelButton;
		base.FormClosing += SetupForm_FormClosing;
		ResumeLayout(performLayout: true);
		LayoutAll();
		SetStatus(BannerKind.Info, DefaultHint(), -1);
	}

	private void BuildInstallLayout()
	{
		_scopeCard = new CardPanel();
		_content.Controls.Add(_scopeCard);
		_themeActions.Add(delegate
		{
			_scopeCard.FillColor = SetupTheme.Surface;
			_scopeCard.BorderColor = Color.Transparent;
		});
		_scopeBadge = new NumberBadge(1, Dp(22));
		_scopeCard.Controls.Add(_scopeBadge);
		_scopeTitle = MakeLabel(_scopeCard, "安装范围", 0, 0, 200, 24, SetupTheme.Font(9.8f, FontStyle.Bold), () => SetupTheme.Text);
		_scopeTitle.TextAlign = ContentAlignment.MiddleLeft;
		_scopeHint = MakeLabel(_scopeCard, "需要管理员授权", 0, 0, 260, 18, SetupTheme.Font(8.2f, FontStyle.Regular), () => SetupTheme.Subtle);
		_scopeHint.TextAlign = ContentAlignment.MiddleRight;
		_scopeSegment = new SegmentedControl
		{
			Items = new string[2] { "仅当前用户", "所有用户" },
			SelectedIndex = ((_scope == InstallScope.AllUsers) ? 1 : 0)
		};
		_scopeSegment.SelectedIndexChanged += delegate
		{
			InstallScope scope = ((_scopeSegment.SelectedIndex == 1) ? InstallScope.AllUsers : InstallScope.CurrentUser);
			SetScope(scope);
		};
		_scopeCard.Controls.Add(_scopeSegment);
		_pathCard = new CardPanel();
		_content.Controls.Add(_pathCard);
		_themeActions.Add(delegate
		{
			_pathCard.FillColor = SetupTheme.Surface;
			_pathCard.BorderColor = Color.Transparent;
		});
		_pathBadge = new NumberBadge(2, Dp(22));
		_pathCard.Controls.Add(_pathBadge);
		_pathTitle = MakeLabel(_pathCard, "程序安装位置", 0, 0, 240, 24, SetupTheme.Font(9.8f, FontStyle.Bold), () => SetupTheme.Text);
		_pathHint = MakeLabel(_pathCard, "文件夹名称：" + SetupInfo.FolderName, 0, 0, 260, 18, SetupTheme.Font(8.2f, FontStyle.Regular), () => SetupTheme.Subtle);
		_pathHint.TextAlign = ContentAlignment.MiddleRight;
		_pathHint.TextAlign = ContentAlignment.MiddleRight;
		_appPathField = new PathField();
		_themeActions.Add(_appPathField.ApplyTheme);
		_pathCard.Controls.Add(_appPathField);
		_appBrowseButton = new PillButton
		{
			Text = "浏览…",
			Font = SetupTheme.Font(8.6f, FontStyle.Bold),
			Radius = 8f
		};
		_appBrowseButton.Click += delegate
		{
			BrowseDirectory(_appPathField, "选择 MySQL管家 的安装文件夹", delegate(string path)
			{
				_appDirectory = path;
				_appPathCustom = true;
				_appPathField.Text = path;
			});
		};
		RegisterNeutralButton(_appBrowseButton);
		_pathCard.Controls.Add(_appBrowseButton);
		_appRestoreLink = new LinkLabel
		{
			Text = "恢复默认位置",
			Font = SetupTheme.Font(8.3f, FontStyle.Regular),
			LinkBehavior = LinkBehavior.HoverUnderline,
			BackColor = Color.Transparent,
			AutoSize = true
		};
		_appRestoreLink.LinkClicked += delegate
		{
			_appPathCustom = false;
			_appDirectory = SetupInfo.DefaultAppDirectory(_scope);
			_appPathField.Text = _appDirectory;
		};
		_themeActions.Add(delegate
		{
			_appRestoreLink.LinkColor = SetupTheme.Accent;
			_appRestoreLink.ActiveLinkColor = SetupTheme.AccentDark;
			_appRestoreLink.VisitedLinkColor = SetupTheme.Accent;
		});
		_pathCard.Controls.Add(_appRestoreLink);
		_desktopCheck = new SetupCheckBox
		{
			Text = "创建桌面快捷方式",
			Checked = true
		};
		_themeActions.Add(_desktopCheck.Invalidate);
		_content.Controls.Add(_desktopCheck);
		_launchCheck = new SetupCheckBox
		{
			Text = "安装完成后启动程序",
			Checked = true
		};
		_themeActions.Add(_launchCheck.Invalidate);
		_content.Controls.Add(_launchCheck);
		_appPathField.Text = _appDirectory;
	}

	private void BuildUninstallLayout()
	{
		_pathCard = new CardPanel();
		_content.Controls.Add(_pathCard);
		_themeActions.Add(delegate
		{
			_pathCard.FillColor = SetupTheme.Surface;
			_pathCard.BorderColor = Color.Transparent;
		});
		_pathBadge = new NumberBadge(1, Dp(22));
		_pathCard.Controls.Add(_pathBadge);
		_pathTitle = MakeLabel(_pathCard, "安装范围与位置", 0, 0, 240, 24, SetupTheme.Font(9.8f, FontStyle.Bold), () => SetupTheme.Text);
		_pathHint = MakeLabel(_pathCard, string.Empty, 0, 0, 300, 18, SetupTheme.Font(8.2f, FontStyle.Regular), () => SetupTheme.Subtle);
		_pathHint.TextAlign = ContentAlignment.MiddleRight;
		_appPathField = new PathField
		{
			ReadOnly = true,
			Text = _appDirectory
		};
		_themeActions.Add(_appPathField.ApplyTheme);
		_pathCard.Controls.Add(_appPathField);
		Label scopeLabel = MakeLabel(_pathCard, string.Empty, 0, 0, 300, 20, SetupTheme.Font(8.4f, FontStyle.Regular), () => SetupTheme.Muted);
		scopeLabel.Name = "ScopeLabel";
		scopeLabel.Text = ((_scope == InstallScope.AllUsers) ? "当前为「所有用户」安装，卸载需要管理员授权" : "当前为「仅当前用户」安装，卸载不需要管理员权限");
		_summaryCard = new CardPanel();
		_content.Controls.Add(_summaryCard);
		_themeActions.Add(delegate
		{
			_summaryCard.FillColor = SetupTheme.Surface;
			_summaryCard.BorderColor = Color.Transparent;
		});
		_summaryBadge = new NumberBadge(2, Dp(22));
		_summaryCard.Controls.Add(_summaryBadge);
		_summaryTitle = MakeLabel(_summaryCard, "卸载内容", 0, 0, 200, 24, SetupTheme.Font(10.2f, FontStyle.Bold), () => SetupTheme.Text);
		_summaryHint = MakeLabel(_summaryCard, string.Empty, 0, 0, 240, 18, SetupTheme.Font(8.2f, FontStyle.Regular), () => SetupTheme.Subtle);
		_summaryHint.TextAlign = ContentAlignment.MiddleRight;
		_uninstallSummaryLabel = MakeLabel(_summaryCard, "会移除：程序文件、开始菜单与桌面快捷方式、程序配置与日志。\n不会移除：MySQL 服务、数据库文件、MySQL 组件与 Workbench。", 0, 0, 500, 50, SetupTheme.Font(8.8f, FontStyle.Regular), () => SetupTheme.Muted);
	}

	private void RegisterNeutralButton(PillButton button)
	{
		_themeActions.Add(delegate
		{
			button.NormalColor = SetupTheme.NeutralSoft;
			button.HoverColor = SetupTheme.Mix(SetupTheme.NeutralSoft, SetupTheme.Text, 0.06);
			button.PressedColor = SetupTheme.Mix(SetupTheme.NeutralSoft, SetupTheme.Text, 0.12);
			button.ForeColor = SetupTheme.Text;
			button.DisabledColor = SetupTheme.NeutralSoft;
			button.DisabledTextColor = SetupTheme.Subtle;
			button.SurfaceColor = SetupTheme.Surface;
		});
	}

	private Label MakeLabel(Control parent, string text, int x, int y, int width, int height, Font font, Func<Color> color)
	{
		Label label = new Label
		{
			Text = text,
			Location = new Point(Dp(x), Dp(y)),
			Size = new Size(Dp(width), Dp(height)),
			Font = font,
			AutoSize = false,
			BackColor = Color.Transparent
		};
		_themeActions.Add(delegate
		{
			label.ForeColor = color();
		});
		parent.Controls.Add(label);
		return label;
	}

	private void SetScope(InstallScope scope)
	{
		_scope = scope;
		if (!_appPathCustom)
		{
			_appDirectory = SetupInfo.DefaultAppDirectory(scope);
			_appPathField.Text = _appDirectory;
		}
		if (_scopeSegment != null && _scopeSegment.SelectedIndex != ((scope == InstallScope.AllUsers) ? 1 : 0))
		{
			_scopeSegment.SelectedIndex = ((scope == InstallScope.AllUsers) ? 1 : 0);
		}
		SetStatus(BannerKind.Info, (scope == InstallScope.AllUsers) ? "将安装到所有用户共用的目录，开始安装时 Windows 会请求管理员授权。" : "仅安装给当前登录用户，不需要管理员权限。", -1);
	}

	private void BrowseDirectory(PathField target, string description, Action<string> onSelected)
	{
		using FolderBrowserDialog dialog = new FolderBrowserDialog
		{
			Description = description,
			ShowNewFolderButton = true
		};
		string text = (target.Text ?? string.Empty).Trim().Trim('"');
		if (Directory.Exists(text))
		{
			dialog.SelectedPath = text;
		}
		else
		{
			try
			{
				string parent = Path.GetDirectoryName(text);
				if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
				{
					dialog.SelectedPath = parent;
				}
			}
			catch
			{
			}
		}
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			onSelected(Path.GetFullPath(dialog.SelectedPath));
		}
	}

	private string DefaultHint()
	{
		if (_uninstallMode)
		{
			return "卸载只清理本程序的文件与配置，MySQL 服务和数据库文件保持不变。";
		}
		return "安装程序约 2 MB  ·  MySQL 组件可在主界面「安装组件」中添加";
	}

	private void SetupForm_FormClosing(object sender, FormClosingEventArgs e)
	{
		if (_busy)
		{
			e.Cancel = true;
			return;
		}
		_pollTimer.Stop();
		_autoCloseTimer.Stop();
		SetupTheme.Changed -= Theme_Changed;
	}

	private void PrimaryButton_Click(object sender, EventArgs e)
	{
		if (_busy)
		{
			return;
		}
		if (_uninstallMode)
		{
			StartUninstall();
		}
		else
		{
			StartInstall();
		}
	}

	private void StartInstall()
	{
		if (IsApplicationRunning() && !TryCloseRunningApplication())
		{
			MessageBox.Show(this, "MySQL管家 正在运行，且未能关闭。\n\n请先手动退出程序后再安装。", "无法继续安装", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		SetupJob job = new SetupJob
		{
			Scope = ((_scope == InstallScope.AllUsers) ? "machine" : "user"),
			AppDirectory = (_appPathField.Text ?? string.Empty).Trim().Trim('"'),
			DesktopShortcut = (_desktopCheck == null || _desktopCheck.Checked),
			Launch = (_launchCheck != null && _launchCheck.Checked)
		};
		if (!ValidateTargetDirectory(job.AppDirectory, "程序安装位置", showMessage: true))
		{
			return;
		}
		_appDirectory = job.AppDirectory;
		bool needsElevation = job.InstallScope == InstallScope.AllUsers;
		if (needsElevation && !SetupElevation.IsAdministrator)
		{
			RunElevated(job);
		}
		else
		{
			RunInProcess(job);
		}
	}

	private void StartUninstall()
	{
		string appDirectory = (_appPathField.Text ?? string.Empty).Trim().Trim('"');
		if (!SetupInstaller.DirectoryHasInstallation(appDirectory))
		{
			MessageBox.Show(this, "无法确认有效的安装目录，已取消卸载。\n\n" + appDirectory, "无法卸载", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		DialogResult confirm = MessageBox.Show(this, "确定要卸载 MySQL管家 吗？\n\n此操作会移除程序文件、快捷方式、程序配置与日志，但不会删除 MySQL 服务、数据库或已安装的 MySQL 组件。", "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
		if (confirm != DialogResult.Yes)
		{
			return;
		}
		if (IsApplicationRunning() && !TryCloseRunningApplication())
		{
			MessageBox.Show(this, "MySQL管家 正在运行，且未能关闭。\n\n请先手动退出程序后再卸载。", "无法继续卸载", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		SetupJob job = new SetupJob
		{
			Uninstall = true,
			Scope = ((_scope == InstallScope.AllUsers) ? "machine" : "user"),
			AppDirectory = appDirectory
		};
		if (_scope == InstallScope.AllUsers && !SetupElevation.IsAdministrator)
		{
			RunElevated(job);
		}
		else
		{
			RunInProcess(job);
		}
	}

	private bool ValidateTargetDirectory(string directory, string caption, bool showMessage, bool allowExisting = false)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			return Fail(caption, "请填写" + caption + "。", showMessage);
		}
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(directory);
		}
		catch (Exception ex)
		{
			return Fail(caption, "路径格式无效：\n\n" + ex.Message, showMessage);
		}
		if (string.Equals(fullPath.TrimEnd('\\'), Path.GetPathRoot(fullPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
		{
			return Fail(caption, "不能直接使用磁盘根目录，请选择一个专用文件夹。", showMessage);
		}
		string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		if (IsSameOrChildPath(fullPath, windows))
		{
			return Fail(caption, "不能安装到 Windows 系统目录，请选择其他位置。", showMessage);
		}
		if (!allowExisting)
		{
			try
			{
				if (Directory.Exists(fullPath) && !IsDirectoryEmpty(fullPath) && !SetupInstaller.DirectoryHasInstallation(fullPath))
				{
					return Fail(caption, "所选文件夹不是空文件夹：\n\n" + fullPath + "\n\n请选择空文件夹，或使用默认的专用目录。", showMessage);
				}
			}
			catch (Exception ex2)
			{
				return Fail(caption, "无法检查所选目录：\n\n" + ex2.Message, showMessage);
			}
		}
		return true;
	}

	private bool Fail(string caption, string message, bool showMessage)
	{
		if (showMessage)
		{
			MessageBox.Show(this, message, caption + "无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
		return false;
	}

	private static bool IsSameOrChildPath(string path, string parent)
	{
		if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parent))
		{
			return false;
		}
		string text = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string text2 = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(text, text2, StringComparison.OrdinalIgnoreCase) || text.StartsWith(text2 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

	private void RunInProcess(SetupJob job)
	{
		SetBusy(busy: true, _uninstallMode ? "正在卸载…" : "正在安装…");
		SetStatus(BannerKind.Info, "正在准备…", 0);
		ThreadPool.QueueUserWorkItem(delegate
		{
			SetupResult result = null;
			try
			{
				result = SetupRunner.Run(job, new DelegateProgress(delegate(int percent, string message)
				{
					PostProgress(percent, message);
				}));
			}
			catch (Exception ex)
			{
				result = new SetupResult
				{
					Code = 1,
					Title = "安装失败",
					Message = ex.Message,
					NeedsAttention = true
				};
			}
			try
			{
				BeginInvoke((Action)delegate
				{
					Finish(result);
				});
			}
			catch
			{
			}
		});
	}

	private void RunElevated(SetupJob job)
	{
		try
		{
			Directory.CreateDirectory(SetupInfo.WorkRoot);
			string token = Guid.NewGuid().ToString("N");
			_jobPath = Path.Combine(SetupInfo.WorkRoot, "job-" + token + ".json");
			_resultPath = Path.Combine(SetupInfo.WorkRoot, "result-" + token + ".json");
			_progressPath = Path.Combine(SetupInfo.WorkRoot, "progress-" + token + ".log");
			job.Save(_jobPath);
			_lastProgressLine = null;
			SetBusy(busy: true, "正在等待管理员授权…");
			SetStatus(BannerKind.Info, "如果出现 Windows 授权窗口，请选择“是”。", -1);
			_elevatedProcess = SetupElevation.StartElevatedJob(_jobPath, _resultPath, _progressPath);
			if (_elevatedProcess == null)
			{
				SetBusy(busy: false, string.Empty);
				SetStatus(BannerKind.Warning, "已取消管理员授权，未做任何更改。", -1);
				return;
			}
			_pollTimer.Start();
		}
		catch (Exception ex)
		{
			SetBusy(busy: false, string.Empty);
			SetStatus(BannerKind.Error, "无法启动安装进程：" + ex.Message, -1);
		}
	}

	private void PostProgress(int percent, string message)
	{
		if (base.IsDisposed || !base.IsHandleCreated)
		{
			return;
		}
		try
		{
			BeginInvoke((Action)delegate
			{
				if (!_busy)
				{
					return;
				}
				SetStatus(BannerKind.Info, message, percent);
				_progressLine.Visible = true;
			});
		}
		catch
		{
		}
	}

	private void PollTimer_Tick(object sender, EventArgs e)
	{
		if (_busy)
		{
			string line = ReadLastProgress();
			if (!string.IsNullOrWhiteSpace(line) && !string.Equals(line, _lastProgressLine, StringComparison.Ordinal))
			{
				_lastProgressLine = line;
				int tab = line.IndexOf('\t');
				if (tab > 0 && int.TryParse(line.Substring(0, tab), NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
				{
					SetStatus(BannerKind.Info, line.Substring(tab + 1), percent);
				}
			}
		}
		if (File.Exists(_resultPath))
		{
			SetupResult result = null;
			try
			{
				result = SetupResult.Load(_resultPath);
			}
			catch
			{
				return;
			}
			_pollTimer.Stop();
			Finish(result);
			return;
		}
		bool exited = false;
		try
		{
			exited = _elevatedProcess != null && _elevatedProcess.HasExited;
		}
		catch
		{
		}
		if (exited)
		{
			_pollTimer.Stop();
			Finish(new SetupResult
			{
				Code = 1,
				Title = "安装未完成",
				Message = "安装进程意外结束，未收到结果。\n请查看日志：" + SetupLog.LogPath,
				NeedsAttention = true
			});
		}
	}

	private string ReadLastProgress()
	{
		try
		{
			if (string.IsNullOrWhiteSpace(_progressPath) || !File.Exists(_progressPath))
			{
				return null;
			}
			using FileStream stream = new FileStream(_progressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using StreamReader reader = new StreamReader(stream);
			string text = reader.ReadToEnd().TrimEnd('\r', '\n');
			if (text.Length == 0)
			{
				return null;
			}
			int start = text.LastIndexOf('\n');
			return (start >= 0) ? text.Substring(start + 1).Trim() : text.Trim();
		}
		catch
		{
			return null;
		}
	}

	private void Finish(SetupResult result)
	{
		_busy = false;
		CancelButtonState(busy: false);
		_primaryButton.Enabled = false;
		_progressLine.Visible = false;
		if (result == null)
		{
			result = new SetupResult
			{
				Code = 1,
				Title = "未完成",
				Message = "安装过程没有返回结果。",
				NeedsAttention = true
			};
		}
		if (result.Code == 0 && !result.NeedsAttention)
		{
			CleanupWorkFiles(includeCache: true);
			string message = _uninstallMode ? "卸载完成，窗口即将自动关闭。" : "安装完成，窗口即将自动关闭。";
			if (!_uninstallMode && _launchCheck != null && _launchCheck.Checked)
			{
				LaunchInstalledApplication();
				message = "安装完成，正在启动 MySQL管家，窗口即将自动关闭。";
			}
			SetStatus(BannerKind.Success, message, 100);
			_autoCloseTimer.Start();
			return;
		}
		if (result.Code == 0)
		{
			CleanupWorkFiles(includeCache: true);
			string text = (result.Message ?? string.Empty).Replace("\n", "  ");
			if (text.Length > 260)
			{
				text = text.Substring(0, 260) + "…";
			}
			SetStatus(BannerKind.Warning, (_uninstallMode ? "卸载完成，但需要注意：" : "安装完成，但需要注意：") + text, 100);
			_primaryButton.Text = "关闭";
			_primaryButton.Enabled = true;
			_primaryButton.Click -= PrimaryButton_Click;
			_primaryButton.Click += delegate
			{
				Close();
			};
			_pollTimer.Stop();
			try
			{
				MessageBox.Show(this, result.Message, result.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			catch
			{
			}
			if (!_uninstallMode && _launchCheck != null && _launchCheck.Checked)
			{
				LaunchInstalledApplication();
			}
			return;
		}
		SetStatus(BannerKind.Error, (result.Title ?? "未完成") + "：" + (result.Message ?? string.Empty).Replace("\n", " "), 100);
		_primaryButton.Text = "重试";
		_primaryButton.Enabled = true;
	}

	private void LaunchInstalledApplication()
	{
		try
		{
			string executable = SetupInstaller.InstalledExecutable(_appDirectory);
			if (!File.Exists(executable))
			{
				return;
			}
			ProcessStartInfo startInfo = new ProcessStartInfo(executable)
			{
				UseShellExecute = true,
				WorkingDirectory = _appDirectory
			};
			Process.Start(startInfo);
		}
		catch (Exception ex)
		{
			SetupLog.Write("启动程序失败：" + ex.Message);
		}
	}

	/// <summary>清理本次安装任务文件；成功时一并清掉下载缓存，避免长期占用磁盘。</summary>
	private void CleanupWorkFiles(bool includeCache)
	{
		try
		{
			foreach (string path in new string[3] { _jobPath, _resultPath, _progressPath })
			{
				if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
				{
					File.Delete(path);
				}
			}
			if (includeCache)
			{
				string cache = Path.Combine(SetupInfo.WorkRoot, "cache");
				if (Directory.Exists(cache))
				{
					Directory.Delete(cache, recursive: true);
				}
			}
		}
		catch (Exception ex)
		{
			SetupLog.Write("清理临时文件失败：" + ex.Message);
		}
	}
	private void AutoCloseTimer_Tick(object sender, EventArgs e)
	{
		_autoCloseTimer.Stop();
		if (!_busy)
		{
			Close();
		}
	}

	private void CancelButtonState(bool busy)
	{
		_cancelButton.Enabled = !busy;
		_busy = busy;
		_primaryButton.Enabled = !busy;
		if (_scopeSegment != null)
		{
			_scopeSegment.Enabled = !busy;
		}
		foreach (Control control in _content.Controls)
		{
			SetControlsEnabled(control, !busy);
		}
	}

	private void SetControlsEnabled(Control parent, bool enabled)
	{
		foreach (Control child in parent.Controls)
		{
			if (child is SetupCheckBox || child is PathField || child is PillButton || child is SegmentedControl)
			{
				child.Enabled = enabled;
			}
			if (child.HasChildren)
			{
				SetControlsEnabled(child, enabled);
			}
		}
		if (parent is SetupCheckBox || parent is PathField)
		{
			parent.Enabled = enabled;
		}
	}

	private void SetBusy(bool busy, string statusText)
	{
		_statusText = statusText;
		CancelButtonState(busy);
		_progressLine.Visible = busy;
	}

	private void SetStatus(BannerKind kind, string message, int percent)
	{
		_bannerKind = kind;
		_statusText = message ?? string.Empty;
		_statusPercent = percent;
		ApplyBanner();
	}

	private void ApplyBanner()
	{
		if (_bannerCard == null)
		{
			return;
		}
		Color fill;
		Color accent;
		string icon;
		switch (_bannerKind)
		{
		case BannerKind.Success:
			fill = SetupTheme.SuccessSoft;
			accent = SetupTheme.Success;
			icon = "✓";
			break;
		case BannerKind.Warning:
			fill = SetupTheme.WarningSoft;
			accent = SetupTheme.Warning;
			icon = "!";
			break;
		case BannerKind.Error:
			fill = SetupTheme.DangerSoft;
			accent = SetupTheme.Danger;
			icon = "!";
			break;
		default:
			fill = SetupTheme.AccentSoft;
			accent = SetupTheme.Accent;
			icon = "i";
			break;
		}
		_bannerCard.FillColor = fill;
		_bannerIcon.ForeColor = accent;
		_bannerIcon.Text = icon;
		_bannerText.Text = _statusText;
		_progressLine.Value = ((_statusPercent < 0) ? (-1.0) : ((double)_statusPercent / 100.0));
	}

	private void ApplyTheme()
	{
		if (_header == null)
		{
			return;
		}
		SuspendLayout();
		try
		{
			DarkMode.Apply(SetupTheme.IsDark);
			base.BackColor = SetupTheme.Background;
			_header.BackColor = SetupTheme.Background;
			_content.BackColor = SetupTheme.Background;
			_footer.BackColor = SetupTheme.Background;
			foreach (Action action in _themeActions)
			{
				try
				{
					action();
				}
				catch
				{
				}
			}
			ApplyBanner();
			if (base.IsHandleCreated)
			{
				SetupNative.ApplyTitleBar(base.Handle, SetupTheme.IsDark, SetupTheme.Background, SetupTheme.Text);
			}
			_content.BackColor = SetupTheme.Background;
		}
		finally
		{
			ResumeLayout(performLayout: true);
		}
		Invalidate(true);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		SetupNative.ApplyTitleBar(base.Handle, SetupTheme.IsDark, SetupTheme.Background, SetupTheme.Text);
		ApplyDpiScale();
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		ApplyDpiScale();
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		ApplyDpiScale();
		RecenterAfterDpi();
	}

	/// <summary>按窗口真实 DPI 重算比例、重排并重新定尺寸，避免文字比控件大导致重叠。</summary>
	private void ApplyDpiScale()
	{
		if (_header == null || _content == null || _footer == null || !base.IsHandleCreated)
		{
			return;
		}
		float scale = SetupNative.GetScale(base.Handle);
		if (Math.Abs(scale - _scale) < 0.01f)
		{
			return;
		}
		_scale = scale;
		PrepareWindow();
		Invalidate(true);
	}

	private void RecenterAfterDpi()
	{
		try
		{
			base.StartPosition = FormStartPosition.Manual;
			Rectangle area = Screen.FromControl(this).WorkingArea;
			base.Location = new Point(area.Left + Math.Max(0, (area.Width - base.Width) / 2), area.Top + Math.Max(0, (area.Height - base.Height) / 2));
		}
		catch
		{
		}
	}

	private int Dp(int value)
	{
		return (int)Math.Round((double)value * (double)_scale);
	}

	private void LayoutAll()
	{
		if (_header == null || _content == null || _footer == null)
		{
			return;
		}
		int width = Math.Max(Dp(WindowWidth), base.ClientSize.Width);
		int contentWidth = width - Dp(48);
		int right = contentWidth;
		int pad = Dp(20);
		_header.SetBounds(0, 0, width, Dp(68));
		_titleLabel.SetBounds(Dp(24), Dp(10), Math.Max(Dp(200), width - Dp(24) - Dp(196)), Dp(28));
		_subtitleLabel.SetBounds(Dp(24), Dp(40), Math.Max(Dp(240), right - Dp(192)), Dp(18));
		int themeWidth = Dp(46);
		int themeGap = Dp(6);
		int themeRight = width - Dp(24);
		for (int i = 2; i >= 0; i--)
		{
			_themeButtons[i].SetBounds(themeRight - themeWidth, Dp(15), themeWidth, Dp(22));
			themeRight -= themeWidth + themeGap;
		}
		if (!_uninstallMode)
		{
			int y = Dp(8);
			LayoutCard(_scopeCard, y, Dp(80), contentWidth);
			_scopeTitle.SetBounds(pad, Dp(14), Dp(200), Dp(22));
			_scopeHint.SetBounds(right - pad - Dp(160), Dp(14), Dp(160), Dp(18));
			_scopeSegment.SetBounds(pad, Dp(40), right - pad * 2, Dp(32));
			y += Dp(80) + Dp(10);
			LayoutCard(_pathCard, y, Dp(100), contentWidth);
			_pathTitle.SetBounds(pad, Dp(14), Dp(150), Dp(22));
			_pathHint.SetBounds(right - pad - Dp(224), Dp(14), Dp(224), Dp(18));
			_appPathField.SetBounds(pad, Dp(40), right - pad * 2 - Dp(84) - Dp(8), Dp(32));
			_appBrowseButton.SetBounds(right - pad - Dp(84), Dp(40), Dp(84), Dp(32));
			// 链接与输入框左边缘对齐，读起来是「这一项的附加操作」。
			_appRestoreLink.Location = new Point(pad, Dp(76));
			y += Dp(100) + Dp(12);
			int half = (contentWidth - Dp(12)) / 2;
			_desktopCheck.SetBounds(Dp(24), y, half, Dp(22));
			_launchCheck.SetBounds(Dp(24) + half + Dp(12), y, half, Dp(22));
			_content.AutoScrollMinSize = new Size(0, y + Dp(16));
		}
		else
		{
			int y2 = Dp(8);
			LayoutCard(_pathCard, y2, Dp(100), contentWidth);
			_pathTitle.SetBounds(pad, Dp(14), Dp(150), Dp(22));
			_pathHint.SetBounds(right - pad - Dp(224), Dp(14), Dp(224), Dp(18));
			_appPathField.SetBounds(pad, Dp(40), right - pad * 2, Dp(32));
			foreach (Control control in _pathCard.Controls)
			{
				if (control.Name == "ScopeLabel")
				{
					control.SetBounds(pad, Dp(78), right - pad * 2, Dp(18));
				}
			}
			y2 += Dp(100) + Dp(10);
			LayoutCard(_summaryCard, y2, Dp(86), contentWidth);
			_summaryTitle.SetBounds(pad, Dp(14), Dp(200), Dp(22));
			_summaryHint.SetBounds(right - pad - Dp(160), Dp(14), Dp(160), Dp(18));
			_summaryHint.Text = "卸载不会影响数据库";
			_uninstallSummaryLabel?.SetBounds(pad, Dp(40), right - pad * 2, Dp(42));
			_summaryHint.Visible = true;
			_content.AutoScrollMinSize = new Size(0, y2 + Dp(86) + Dp(16));
		}
		// 底栏：提示条、作者行、按钮共用 24 边距；作者行与按钮垂直居中。
		int footerHeight = Dp(88);
		int footerTop = base.ClientSize.Height - footerHeight;
		_footer.SetBounds(0, footerTop, width, footerHeight);
		_bannerCard.SetBounds(Dp(24), Dp(8), width - Dp(48), Dp(34));
		_bannerIcon.SetBounds(Dp(12), Dp(7), Dp(16), Dp(18));
		_bannerText.SetBounds(Dp(32), Dp(0), width - Dp(48) - Dp(54), Dp(32));
		_progressLine.SetBounds(Dp(32), _bannerCard.Bottom - Dp(4), width - Dp(48) - Dp(54), Dp(3));
		int buttonHeight = Dp(32);
		int buttonTop = Dp(48);
		_primaryButton.SetBounds(width - Dp(24) - Dp(108), buttonTop, Dp(108), buttonHeight);
		_cancelButton.SetBounds(_primaryButton.Left - Dp(8) - Dp(78), buttonTop, Dp(78), buttonHeight);
		_authorLabel.SetBounds(Dp(24), buttonTop + Dp(6), Dp(200), Dp(20));
	}

	private void LayoutCard(CardPanel card, int top, int height, int contentWidth)
	{
		card.SetBounds(Dp(24), top, contentWidth, height);
		card.Radius = Dp(16);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (_header != null && _content != null)
		{
			LayoutAll();
		}
	}

	private Size ComputeWindowSize()
	{
		int contentHeight = _uninstallMode ? Dp(8 + 100 + 10 + 86 + 16) : Dp(8 + 80 + 10 + 100 + 12 + 22 + 16);
		int height = Dp(68) + contentHeight + Dp(88);
		return new Size(Dp(WindowWidth), height);
	}

	public void PrepareWindow()
	{
		Size size = ComputeWindowSize();
		try
		{
			Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
			int maxHeight = Math.Max(Dp(520), area.Height - Dp(32));
			if (size.Height > maxHeight)
			{
				size.Height = maxHeight;
			}
			if (size.Width > area.Width - Dp(16))
			{
				size.Width = Math.Max(Dp(600), area.Width - Dp(16));
			}
		}
		catch
		{
		}
		base.ClientSize = size;
		LayoutAll();
	}

	private static bool IsApplicationRunning()
	{
		Process[] processes = Process.GetProcessesByName("MySQLManager");
		foreach (Process process in processes)
		{
			try
			{
				if (process.Id != Process.GetCurrentProcess().Id)
				{
					return true;
				}
			}
			catch
			{
			}
			finally
			{
				process.Dispose();
			}
		}
		return false;
	}

	private static bool TryCloseRunningApplication()
	{
		foreach (Process process in Process.GetProcessesByName("MySQLManager"))
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
			finally
			{
				process.Dispose();
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

	internal int RunSilentInstall(bool createDesktopShortcut, bool launchApplication)
	{
		SetupJob job = new SetupJob
		{
			Scope = ((_scope == InstallScope.AllUsers) ? "machine" : "user"),
			AppDirectory = _appDirectory,
			DesktopShortcut = createDesktopShortcut,
			Launch = launchApplication
		};
		SetupResult result = SetupRunner.Run(job, new DelegateProgress(delegate
		{
		}));
		if (result.Code == 0 && launchApplication)
		{
			LaunchInstalledApplication();
		}
		return result.Code;
	}

	internal int RunSilentUninstall()
	{
		SetupJob job = new SetupJob
		{
			Uninstall = true,
			Scope = ((_scope == InstallScope.AllUsers) ? "machine" : "user"),
			AppDirectory = _appDirectory
		};
		SetupResult result = SetupRunner.Run(job, new DelegateProgress(delegate
		{
		}));
		return result.Code;
	}

	internal string InstallDirectory => _appDirectory;
}
