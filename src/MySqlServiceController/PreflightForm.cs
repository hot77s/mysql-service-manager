using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class PreflightForm : Form
{
	private readonly string _serviceName;

	private Label _titleLabel;

	private Label _subtitleLabel;

	private BadgeLabel _summaryBadge;

	private PreflightListView _listView;

	private Label _statusLabel;

	private RoundedButton _recheckButton;

	private RoundedButton _copyButton;

	private RoundedButton _logButton;

	private RoundedButton _closeButton;

	private PreflightReport _report;

	private bool _running;

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	public PreflightReport Report => _report;

	public PreflightForm(string serviceName)
	{
		_serviceName = (serviceName ?? string.Empty).Trim();
		Text = "启动前体检";
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.ShowInTaskbar = false;
		base.AutoScaleMode = AutoScaleMode.None;
		BackColor = Theme.Background;
		Font = Theme.Font(9.5f, FontStyle.Regular);
		try
		{
			base.Icon = AppIcon.Get();
		}
		catch
		{
		}
		BuildInterface();
		base.Shown += PreflightForm_Shown;
		base.KeyPreview = true;
		base.KeyDown += PreflightForm_KeyDown;
	}

	private float DpiScale
	{
		get
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
			return Math.Max(1f, (float)num / 96f);
		}
	}

	private int S(int value)
	{
		return (int)Math.Round((double)value * (double)DpiScale);
	}

	private void BuildInterface()
	{
		SuspendLayout();
		_titleLabel = new Label();
		_titleLabel.Text = "启动前体检";
		_titleLabel.Font = Theme.Font(16f, FontStyle.Bold);
		_titleLabel.ForeColor = Theme.Text;
		_titleLabel.BackColor = Color.Transparent;
		_titleLabel.AutoSize = false;
		_titleLabel.TextAlign = ContentAlignment.MiddleLeft;
		base.Controls.Add(_titleLabel);
		_subtitleLabel = new Label();
		_subtitleLabel.Text = "检查服务注册、运行文件、my.ini、数据目录与端口占用";
		_subtitleLabel.Font = Theme.Font(8.8f, FontStyle.Regular);
		_subtitleLabel.ForeColor = Theme.Muted;
		_subtitleLabel.BackColor = Color.Transparent;
		_subtitleLabel.AutoSize = false;
		_subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
		base.Controls.Add(_subtitleLabel);
		_summaryBadge = new BadgeLabel();
		_summaryBadge.Text = "正在检查…";
		_summaryBadge.BadgeColor = Theme.AccentSoft;
		_summaryBadge.TextColor = Theme.Accent;
		_summaryBadge.SurfaceColor = Theme.Background;
		_summaryBadge.Font = Theme.Font(8.4f, FontStyle.Bold);
		base.Controls.Add(_summaryBadge);
		_listView = new PreflightListView();
		_listView.SetPlaceholder("正在检查服务、配置、数据目录与端口…");
		base.Controls.Add(_listView);
		_statusLabel = new Label();
		_statusLabel.Text = "准备检查…";
		_statusLabel.Font = Theme.Font(8.5f, FontStyle.Regular);
		_statusLabel.ForeColor = Theme.Muted;
		_statusLabel.BackColor = Color.Transparent;
		_statusLabel.AutoSize = false;
		_statusLabel.TextAlign = ContentAlignment.MiddleLeft;
		base.Controls.Add(_statusLabel);
		_logButton = MakeButton("错误日志", Theme.SurfaceAlt, Theme.Text, Theme.Border);
		_logButton.Click += delegate
		{
			using ErrorLogForm form = new ErrorLogForm(_serviceName);
			form.ShowDialog(this);
		};
		base.Controls.Add(_logButton);
		_copyButton = MakeButton("复制报告", Theme.SurfaceAlt, Theme.Text, Theme.Border);
		_copyButton.Enabled = false;
		_copyButton.Click += CopyButton_Click;
		base.Controls.Add(_copyButton);
		_recheckButton = MakeButton("重新检查", Theme.AccentSoft, Theme.Accent, Color.Transparent);
		_recheckButton.Click += async delegate
		{
			await RunCheckAsync();
		};
		base.Controls.Add(_recheckButton);
		_closeButton = MakeButton("关闭", Theme.Accent, Color.White, Theme.Border);
		_closeButton.DialogResult = DialogResult.Cancel;
		base.Controls.Add(_closeButton);
		base.CancelButton = _closeButton;
		ResumeLayout(performLayout: false);
	}

	private RoundedButton MakeButton(string text, Color normal, Color foreColor, Color border)
	{
		RoundedButton roundedButton = new RoundedButton();
		roundedButton.Text = text;
		roundedButton.NormalColor = normal;
		roundedButton.HoverColor = Theme.Mix(normal, Theme.Text, 0.08);
		roundedButton.PressedColor = Theme.Mix(normal, Theme.Text, 0.16);
		roundedButton.ForeColor = foreColor;
		roundedButton.ButtonBorderColor = border;
		roundedButton.SurfaceColor = Theme.Background;
		roundedButton.Font = Theme.Font(9f, FontStyle.Bold);
		roundedButton.Radius = 9f;
		return roundedButton;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		LayoutCustom();
		ApplyTitleBarTheme();
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		LayoutCustom();
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

	private void LayoutCustom()
	{
		int num = S(720);
		int num2 = S(600);
		base.MaximumSize = Size.Empty;
		base.MinimumSize = Size.Empty;
		if (base.ClientSize.Width != num || base.ClientSize.Height != num2)
		{
			base.ClientSize = new Size(num, num2);
		}
		int margin = S(24);
		int width = base.ClientSize.Width;
		int height = base.ClientSize.Height;
		_titleLabel.SetBounds(margin, S(12), width - margin * 2 - S(160), S(32));
		_subtitleLabel.SetBounds(margin + S(2), S(46), width - margin * 2 - S(160), S(20));
		int badgeWidth = TextRenderer.MeasureText(_summaryBadge.Text ?? string.Empty, _summaryBadge.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + S(28);
		_summaryBadge.SetBounds(width - margin - badgeWidth, S(18), badgeWidth, S(26));
		int footerTop = height - S(54);
		_listView.SetBounds(margin, S(80), width - margin * 2, Math.Max(S(200), footerTop - S(90)));
		int buttonY = height - S(46);
		int buttonHeight = S(36);
		_closeButton.SetBounds(width - margin - S(88), buttonY, S(88), buttonHeight);
		_recheckButton.SetBounds(_closeButton.Left - S(10) - S(100), buttonY, S(100), buttonHeight);
		_copyButton.SetBounds(_recheckButton.Left - S(10) - S(96), buttonY, S(96), buttonHeight);
		_logButton.SetBounds(_copyButton.Left - S(10) - S(92), buttonY, S(92), buttonHeight);
		_statusLabel.SetBounds(margin, buttonY + S(8), Math.Max(S(120), _logButton.Left - margin - S(16)), S(20));
		base.MinimumSize = base.Size;
		base.MaximumSize = base.Size;
		_listView.Invalidate();
	}

	private async void PreflightForm_Shown(object sender, EventArgs e)
	{
		ApplyTitleBarTheme();
		await RunCheckAsync();
	}

	private async Task RunCheckAsync()
	{
		if (_running)
		{
			return;
		}
		_running = true;
		_recheckButton.Enabled = false;
		_copyButton.Enabled = false;
		_summaryBadge.Text = "正在检查…";
		_summaryBadge.BadgeColor = Theme.AccentSoft;
		_summaryBadge.TextColor = Theme.Accent;
		_listView.SetItems(null);
		_listView.SetPlaceholder("正在检查服务、配置、数据目录与端口…");
		_statusLabel.Text = "正在检查，请稍候…";
		_statusLabel.ForeColor = Theme.Muted;
		LayoutCustom();
		try
		{
			string serviceName = _serviceName;
			PreflightReport report = await Task.Run(() => PreflightChecker.Run(serviceName));
			if (base.IsDisposed)
			{
				return;
			}
			_report = report;
			_listView.SetItems(report.Items);
			_listView.SetPlaceholder(report.Verdict);
			if (report.ErrorCount > 0)
			{
				_summaryBadge.BadgeColor = Theme.DangerSoft;
				_summaryBadge.TextColor = Theme.Danger;
			}
			else if (report.WarningCount > 0)
			{
				_summaryBadge.BadgeColor = Theme.WarningSoft;
				_summaryBadge.TextColor = Theme.Warning;
			}
			else
			{
				_summaryBadge.BadgeColor = Theme.SuccessSoft;
				_summaryBadge.TextColor = Theme.Success;
			}
			_summaryBadge.Text = report.Verdict;
			_statusLabel.Text = "检查时间 " + report.CheckedAt.ToString("HH:mm:ss") + " · " + (string.IsNullOrWhiteSpace(report.ServiceName) ? "未配置服务" : report.ServiceName);
			_statusLabel.ForeColor = Theme.Muted;
			_copyButton.Enabled = true;
			LayoutCustom();
			AppLog.Info("启动前体检完成：" + report.ServiceName + " · " + report.Verdict);
		}
		catch (Exception exception)
		{
			AppLog.Error("启动前体检窗口执行失败。", exception);
			if (!base.IsDisposed)
			{
				_summaryBadge.Text = "检查失败";
				_summaryBadge.BadgeColor = Theme.DangerSoft;
				_summaryBadge.TextColor = Theme.Danger;
				_statusLabel.Text = "检查失败：" + exception.Message;
				_statusLabel.ForeColor = Theme.Danger;
				LayoutCustom();
			}
		}
		finally
		{
			_running = false;
			if (!base.IsDisposed)
			{
				_recheckButton.Enabled = true;
			}
		}
	}

	private void CopyButton_Click(object sender, EventArgs e)
	{
		if (_report == null)
		{
			return;
		}
		try
		{
			Clipboard.SetText(_report.ToPlainText());
			_statusLabel.Text = "报告已复制到剪贴板 · " + DateTime.Now.ToString("HH:mm:ss");
			_statusLabel.ForeColor = Theme.Success;
		}
		catch (Exception exception)
		{
			AppLog.Error("复制体检报告失败。", exception);
			MessageBox.Show("无法写入剪贴板：\n\n" + exception.Message, "复制失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	private async void PreflightForm_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.F5 && !_running)
		{
			e.Handled = true;
			await RunCheckAsync();
		}
	}
}
