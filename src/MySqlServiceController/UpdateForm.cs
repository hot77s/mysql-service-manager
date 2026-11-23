using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MySqlServiceController;

/// <summary>「检查更新」窗口：查看新版本说明、下载安装包并在软件内完成安装。</summary>
internal sealed class UpdateForm : Form
{
	private enum StatusKind
	{
		Info,
		Success,
		Warning,
		Error
	}

	private const int ContentWidth = 552;

	private readonly AppConfig _config;

	private float _scale = 1f;

	private AppUpdateInfo _info;

	private bool _busy;

	private bool _downloading;

	private Label _titleLabel;

	private Label _subtitleLabel;

	private SurfacePanel _card;

	private Label _statusLabel;

	private Label _detailLabel;

	private Panel _separator;

	private Label _notesCaption;

	private ScrollTextView _notesBox;

	private ProgressLine _progressLine;

	private Label _progressLabel;

	private Label _targetLabel;

	private LinkLabel _releaseLink;

	private RoundedButton _closeButton;

	private RoundedButton _primaryButton;

	private StatusKind _statusKind = StatusKind.Info;

	private DateTime _lastChecked = DateTime.MinValue;

	private readonly ToolTip _toolTip = new ToolTip();

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	/// <summary>最近一次检查结果，供主界面更新页脚状态。</summary>
	public AppUpdateInfo Info => _info;

	public UpdateForm(AppConfig config, AppUpdateInfo initial)
	{
		_config = config;
		_info = initial;
		Text = "检查更新";
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.ShowInTaskbar = false;
		BackColor = Theme.Background;
		Font = Theme.Font(9.5f, FontStyle.Regular);
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
		BuildInterface();
		ApplyTheme();
		LayoutContent();
		// 主界面缓存里可能只有版本号（没有安装包地址与更新说明），这种情况要重新查一次。
		if (_info != null && _info.CheckSucceeded && (!_info.HasUpdate || _info.CanInstall))
		{
			_lastChecked = DateTime.Now;
			ApplyInfo();
		}
		else
		{
			_info = null;
			SetCheckingState();
		}
		base.Shown += UpdateForm_Shown;
	}

	/// <summary>更新窗口打开时如果还没有结果，自动开始一次检查。</summary>
	private async void UpdateForm_Shown(object sender, EventArgs e)
	{
		if (_info == null || !_info.CheckSucceeded || (_info.HasUpdate && !_info.CanInstall))
		{
			await RunCheckAsync();
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		// 句柄创建后才能拿到真实 DPI，DeviceDpi 在高分屏下可能仍是 96。
		_scale = CurrentScale();
		LayoutContent();
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		_scale = CurrentScale();
		LayoutContent();
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		_scale = (float)e.DeviceDpiNew / 96f;
		LayoutContent();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_toolTip.Dispose();
		}
		base.Dispose(disposing);
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (_downloading && e.CloseReason == CloseReason.UserClosing)
		{
			e.Cancel = true;
			SetStatus(StatusKind.Info, "正在下载安装包，下载完成后就可以安装。", 0);
			return;
		}
		base.OnFormClosing(e);
	}

	private int Dp(int value)
	{
		return (int)Math.Round((double)(value * _scale));
	}

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

	private void BuildInterface()
	{
		SuspendLayout();
		_titleLabel = MakeLabel("检查更新", 24, 18, 380, 32, Theme.Font(16.5f, FontStyle.Bold), Theme.Text);
		base.Controls.Add(_titleLabel);
		_subtitleLabel = MakeLabel("从 GitHub Releases 获取最新版本，可直接下载并安装", 24, 54, 560, 20, Theme.Font(9f, FontStyle.Regular), Theme.Muted);
		base.Controls.Add(_subtitleLabel);

		_card = new SurfacePanel
		{
			FillColor = Theme.Surface,
			BorderColor = Color.Transparent
		};
		base.Controls.Add(_card);
		_statusLabel = MakeLabel("正在检查更新…", 22, 18, 508, 30, Theme.Font(13f, FontStyle.Bold), Theme.Text);
		_card.Controls.Add(_statusLabel);
		_detailLabel = MakeLabel("正在连接 GitHub Releases…", 22, 54, 508, 20, Theme.Font(8.8f, FontStyle.Regular), Theme.Muted);
		_card.Controls.Add(_detailLabel);
		_separator = new Panel
		{
			BackColor = Theme.Border
		};
		_card.Controls.Add(_separator);
		_notesCaption = MakeLabel("更新内容", 22, 94, 200, 18, Theme.Font(8.6f, FontStyle.Bold), Theme.Muted);
		_card.Controls.Add(_notesCaption);
		_notesBox = new ScrollTextView
		{
			Text = "正在获取更新说明…"
		};
		_card.Controls.Add(_notesBox);
		_progressLine = new ProgressLine
		{
			Visible = false
		};
		_card.Controls.Add(_progressLine);
		_progressLabel = MakeLabel(string.Empty, 22, 258, 508, 18, Theme.Font(8.4f, FontStyle.Regular), Theme.Muted);
		_card.Controls.Add(_progressLabel);
		_targetLabel = MakeLabel(string.Empty, 22, 284, 508, 18, Theme.Font(8.3f, FontStyle.Regular), Theme.Subtle);
		_targetLabel.AutoEllipsis = true;
		_card.Controls.Add(_targetLabel);

		_releaseLink = new LinkLabel
		{
			Text = "打开发布页面",
			Font = Theme.Font(8.6f, FontStyle.Regular),
			LinkBehavior = LinkBehavior.HoverUnderline,
			BackColor = Color.Transparent,
			AutoSize = true
		};
		_releaseLink.LinkClicked += delegate
		{
			OpenReleasePage();
		};
		base.Controls.Add(_releaseLink);

		_closeButton = new RoundedButton
		{
			Text = "以后再说",
			Font = Theme.Font(9.5f, FontStyle.Bold),
			Radius = 9f,
			DialogResult = DialogResult.Cancel
		};
		base.Controls.Add(_closeButton);
		_primaryButton = new RoundedButton
		{
			Text = "下载并安装",
			Font = Theme.Font(9.5f, FontStyle.Bold),
			Radius = 9f
		};
		_primaryButton.Click += PrimaryButton_Click;
		base.Controls.Add(_primaryButton);
		base.CancelButton = _closeButton;
		ResumeLayout(performLayout: false);
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
			AutoSize = false,
			TextAlign = ContentAlignment.MiddleLeft
		};
		return label;
	}

	private void ApplyTheme()
	{
		SuspendLayout();
		try
		{
			BackColor = Theme.Background;
			_titleLabel.ForeColor = Theme.Text;
			_subtitleLabel.ForeColor = Theme.Muted;
			_card.FillColor = Theme.Surface;
			_card.BorderColor = Color.Transparent;
			_notesCaption.ForeColor = Theme.Muted;
			_detailLabel.ForeColor = Theme.Muted;
			_targetLabel.ForeColor = Theme.Subtle;
			_progressLabel.ForeColor = Theme.Muted;
			_separator.BackColor = Theme.Border;
			_notesBox.BackColor = Theme.SurfaceAlt;
			_notesBox.ForeColor = Theme.Text;
			_notesBox.Invalidate();
			_releaseLink.LinkColor = Theme.Accent;
			_releaseLink.ActiveLinkColor = Theme.AccentDark;
			_releaseLink.VisitedLinkColor = Theme.Accent;
			_closeButton.NormalColor = Theme.SurfaceAlt;
			_closeButton.HoverColor = Theme.Mix(Theme.SurfaceAlt, Theme.Text, 0.06);
			_closeButton.PressedColor = Theme.Mix(Theme.SurfaceAlt, Theme.Text, 0.12);
			_closeButton.DisabledColor = Theme.NeutralSoft;
			_closeButton.DisabledTextColor = Theme.Subtle;
			_closeButton.ForeColor = Theme.Text;
			_closeButton.ButtonBorderColor = Theme.Border;
			_closeButton.SurfaceColor = Theme.Background;
			_primaryButton.NormalColor = Theme.Accent;
			_primaryButton.HoverColor = Theme.Mix(Theme.Accent, Color.White, 0.12);
			_primaryButton.PressedColor = Theme.Mix(Theme.Accent, Color.Black, 0.1);
			_primaryButton.DisabledColor = Theme.NeutralSoft;
			_primaryButton.DisabledTextColor = Theme.Subtle;
			_primaryButton.ForeColor = Color.White;
			_primaryButton.ButtonBorderColor = Theme.Border;
			_primaryButton.SurfaceColor = Theme.Background;
			ApplyStatusColor();
		}
		finally
		{
			ResumeLayout(performLayout: true);
		}
		Invalidate(true);
	}

	private void LayoutContent()
	{
		if (_card == null)
		{
			return;
		}
		SuspendLayout();
		try
		{
			int margin = Dp(24);
			int contentW = Dp(ContentWidth);
			_titleLabel.SetBounds(margin, Dp(18), contentW - margin, Dp(32));
			_subtitleLabel.SetBounds(margin, Dp(54), contentW + Dp(8), Dp(20));
			int cardTop = Dp(88);
			int cardH = Dp(344);
			_card.SetBounds(margin, cardTop, contentW, cardH);
			_card.Radius = Dp(16);
			int pad = Dp(22);
			int innerW = contentW - pad * 2;
			_statusLabel.SetBounds(pad, Dp(18), innerW, Dp(30));
			_detailLabel.SetBounds(pad, Dp(54), innerW, Dp(20));
			_separator.SetBounds(pad, Dp(82), innerW, Math.Max(1, Dp(1)));
			_notesCaption.SetBounds(pad, Dp(94), innerW, Dp(18));
			_notesBox.SetBounds(pad, Dp(118), innerW, Dp(142));
			_progressLine.SetBounds(pad, Dp(268), innerW, Dp(4));
			_progressLabel.SetBounds(pad, Dp(278), innerW, Dp(18));
			_targetLabel.SetBounds(pad, Dp(304), innerW, Dp(18));

			int buttonTop = _card.Bottom + Dp(20);
			int primaryW = Dp(118);
			int closeW = Dp(88);
			int buttonH = Dp(36);
			_primaryButton.SetBounds(margin + contentW - primaryW, buttonTop, primaryW, buttonH);
			_closeButton.SetBounds(_primaryButton.Left - Dp(10) - closeW, buttonTop, closeW, buttonH);
			_releaseLink.Location = new Point(margin, buttonTop + (buttonH - _releaseLink.Height) / 2);

			base.ClientSize = new Size(contentW + margin * 2, buttonTop + buttonH + Dp(22));
		}
		finally
		{
			ResumeLayout(performLayout: true);
		}
	}

	private async void PrimaryButton_Click(object sender, EventArgs e)
	{
		if (_busy)
		{
			return;
		}
		if (_info != null && _info.HasUpdate)
		{
			await InstallAsync();
			return;
		}
		await RunCheckAsync();
	}

	private async Task RunCheckAsync()
	{
		if (_busy)
		{
			return;
		}
		_busy = true;
		SetCheckingState();
		try
		{
			AppUpdateInfo info = await AppUpdateChecker.CheckAsync();
			if (base.IsDisposed)
			{
				return;
			}
			_info = info;
			_lastChecked = DateTime.Now;
			_config.AppCheckedUtc = DateTime.UtcNow.ToString("o");
			if (info.CheckSucceeded)
			{
				_config.AppLatestVersion = info.LatestVersion;
			}
			try
			{
				ConfigStore.Save(_config);
			}
			catch (Exception ex)
			{
				AppLog.Error("保存更新检查结果失败。", ex);
			}
			ApplyInfo();
		}
		catch (Exception ex2)
		{
			AppLog.Error("检查更新失败。", ex2);
			if (!base.IsDisposed)
			{
				_info = new AppUpdateInfo
				{
					ErrorMessage = "检查更新失败：" + ex2.Message
				};
				ApplyInfo();
			}
		}
		finally
		{
			_busy = false;
			RefreshButtons();
		}
	}

	private async Task InstallAsync()
	{
		if (_info == null || !_info.CanInstall)
		{
			OpenReleasePage();
			return;
		}
		_busy = true;
		_downloading = true;
		_progressLine.Reset();
		_progressLine.Visible = true;
		_progressLabel.Text = "正在连接下载服务器…";
		SetStatus(StatusKind.Info, "正在下载 v" + _info.LatestVersion + " 安装包", 0);
		RefreshButtons();
		string installer;
		try
		{
			installer = await AppUpdateChecker.DownloadAsync(_info, OnDownloadProgress);
		}
		catch (Exception ex)
		{
			AppLog.Error("下载更新安装包失败。", ex);
			if (!base.IsDisposed)
			{
				_downloading = false;
				_busy = false;
				_progressLine.Visible = false;
				_progressLabel.Text = string.Empty;
				SetStatus(StatusKind.Error, "下载失败：" + ex.Message, 0);
				RefreshButtons();
			}
			return;
		}
		_downloading = false;
		_busy = false;
		if (base.IsDisposed)
		{
			return;
		}
		_progressLine.Value = 100;
		_progressLabel.Text = "安装包已下载（" + AppUpdateChecker.FormatSize(new System.IO.FileInfo(installer).Length) + "）";
		RefreshButtons();
		bool machineTarget;
		AppUpdateChecker.ResolveInstallDirectory(out machineTarget);
		string confirmText = "MySQL管家 v" + _info.LatestVersion + " 安装包已下载完成。\n\n现在安装吗？\n\n· 程序会自动关闭并静默覆盖安装，装完自动重新打开\n· 服务配置、日志与已安装的 MySQL 组件不受影响"
			+ (machineTarget ? "\n· 安装到所有用户目录，会请求一次管理员授权" : string.Empty);
		DialogResult confirm = MessageBox.Show(this, confirmText, "开始更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
		if (confirm != DialogResult.Yes)
		{
			SetStatus(StatusKind.Success, "已下载 v" + _info.LatestVersion + "，可随时点「下载并安装」继续", 0);
			return;
		}
		string error;
		if (AppUpdateChecker.TryLaunchInstaller(installer, out error))
		{
			AppLog.Info("更新已开始，主程序即将退出。");
			Application.Exit();
			return;
		}
		SetStatus(StatusKind.Warning, error ?? "无法启动安装程序。", 0);
		MessageBox.Show(this, error ?? "无法启动安装程序。", "更新未开始", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
	}

	private void OnDownloadProgress(int percent, long received, long total)
	{
		if (base.IsDisposed || base.Disposing)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			try
			{
				BeginInvoke(new Action<int, long, long>(OnDownloadProgress), percent, received, total);
			}
			catch
			{
			}
			return;
		}
		_progressLine.Value = Math.Max(2, percent);
		_progressLabel.Text = "正在下载安装包 " + percent + "%（" + AppUpdateChecker.FormatSize(received) + " / " + (total > 0 ? AppUpdateChecker.FormatSize(total) : "大小未知") + "）";
	}

	private void SetCheckingState()
	{
		SetStatus(StatusKind.Info, "正在检查更新…", 0);
		_detailLabel.Text = "当前版本 v" + UpdateVersion.CurrentText + " · 正在连接 GitHub Releases…";
		_notesBox.Text = "正在获取更新说明…";
		_progressLine.Visible = false;
		_progressLabel.Text = string.Empty;
		RefreshTargetLabel();
		RefreshButtons();
	}

	private void ApplyInfo()
	{
		if (_info == null)
		{
			SetStatus(StatusKind.Warning, "暂时无法检查更新", 0);
			return;
		}
		RefreshTargetLabel();
		if (!_info.CheckSucceeded)
		{
			SetStatus(StatusKind.Warning, "暂时无法检查更新", 0);
			_detailLabel.Text = string.IsNullOrWhiteSpace(_info.ErrorMessage) ? "网络不可用或更新接口限流。" : _info.ErrorMessage;
			_notesBox.Text = "可以点左下角「打开发布页面」手动下载安装包，或稍后重试。";
			RefreshButtons();
			return;
		}
		string detail = "当前版本 v" + _info.CurrentVersion;
		if (!string.IsNullOrWhiteSpace(_info.PublishedText))
		{
			detail += " · 发布 " + _info.PublishedText;
		}
		if (_info.DownloadSize > 0)
		{
			detail += " · 安装包 " + AppUpdateChecker.FormatSize(_info.DownloadSize);
		}
		_detailLabel.Text = detail;
		if (_info.HasUpdate)
		{
			SetStatus(StatusKind.Info, "发现新版本 v" + _info.LatestVersion, 0);
			_notesBox.Text = string.IsNullOrWhiteSpace(_info.ReleaseNotes) ? "该版本没有附带更新说明。" : _info.ReleaseNotes;
			UpdateNotesHint();
		}
		else
		{
			SetStatus(StatusKind.Success, "已是最新版本 v" + _info.CurrentVersion, 0);
			string text = "当前版本与发布渠道一致，无需更新。";
			if (_lastChecked != DateTime.MinValue)
			{
				text += "\r\n\r\n检查时间：" + _lastChecked.ToString("yyyy-MM-dd HH:mm");
			}
			if (!string.IsNullOrWhiteSpace(_info.ReleaseNotes))
			{
				text += "\r\n\r\n最新发布 v" + _info.LatestVersion + " 的说明：\r\n\r\n" + _info.ReleaseNotes;
			}
			_notesBox.Text = text;
			UpdateNotesHint();
		}
		RefreshButtons();
	}

	/// <summary>说明内容超出可视范围时提示可以滚动查看。</summary>
	private void UpdateNotesHint()
	{
		_toolTip.SetToolTip(_notesBox, _notesBox.Overflowed ? "更新说明较长，可在框内用滚轮滚动查看" : string.Empty);
	}

	private void RefreshButtons()
	{
		if (_primaryButton == null || base.IsDisposed)
		{
			return;
		}
		if (_downloading)
		{
			_primaryButton.Text = "正在下载…";
			_primaryButton.Enabled = false;
			_closeButton.Enabled = false;
			_releaseLink.Enabled = false;
			return;
		}
		_closeButton.Enabled = true;
		_releaseLink.Enabled = true;
		if (_busy)
		{
			_primaryButton.Text = "正在检查…";
			_primaryButton.Enabled = false;
			return;
		}
		if (_info == null)
		{
			_primaryButton.Text = "检查更新";
		}
		else if (!_info.CheckSucceeded)
		{
			_primaryButton.Text = "重试";
		}
		else if (_info.HasUpdate)
		{
			_primaryButton.Text = "下载并安装";
		}
		else
		{
			_primaryButton.Text = "重新检查";
		}
		_primaryButton.Enabled = true;
	}

	private void SetStatus(StatusKind kind, string text, int holdSeconds)
	{
		_statusKind = kind;
		_statusLabel.Text = text;
		ApplyStatusColor();
	}

	private void ApplyStatusColor()
	{
		if (_statusLabel == null)
		{
			return;
		}
		Color color = _statusKind switch
		{
			StatusKind.Success => Theme.Success,
			StatusKind.Warning => Theme.Warning,
			StatusKind.Error => Theme.Danger,
			_ => (_info != null && _info.HasUpdate) ? Theme.Accent : Theme.Text,
		};
		_statusLabel.ForeColor = color;
		_card.AccentColor = color;
		_card.FillColor = Theme.Surface;
	}

	/// <summary>提前告知会安装到哪个目录、是否需要管理员授权。</summary>
	private void RefreshTargetLabel()
	{
		try
		{
			bool machine;
			string directory = AppUpdateChecker.ResolveInstallDirectory(out machine);
			_targetLabel.Text = "安装位置：" + directory;
			_targetLabel.ForeColor = Theme.Subtle;
			_toolTip.SetToolTip(_targetLabel, (machine ? "所有用户安装，更新时需要管理员授权：\r\n" : "仅当前用户安装：\r\n") + directory);
		}
		catch
		{
			_targetLabel.Text = string.Empty;
		}
	}

	private void OpenReleasePage()
	{
		string url = ((_info != null && !string.IsNullOrWhiteSpace(_info.ReleasePageUrl)) ? _info.ReleasePageUrl : AppUpdateChecker.ReleasePageUrl);
		try
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
			AppLog.Info("已打开发布页面：" + url);
		}
		catch (Exception ex)
		{
			AppLog.Error("打开发布页面失败。", ex);
			MessageBox.Show(this, "无法打开浏览器。\n\n" + ex.Message + "\n\n发布页面：\n" + url, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}
}
