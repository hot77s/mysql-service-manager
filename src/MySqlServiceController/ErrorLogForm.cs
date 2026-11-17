using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class ErrorLogForm : Form
{
	private const int MaxTailBytes = 2 * 1024 * 1024;

	private const int MaxLines = 3000;

	private readonly string _serviceName;

	private string _explicitLogPath;

	private MySqlInstanceInfo _instance;

	private string _logPath = string.Empty;

	private string _rawText = string.Empty;

	private long _loadedLength = -1L;

	private DateTime _loadedWriteTime = DateTime.MinValue;

	private bool _truncated;

	private bool _autoRefresh = true;

	private bool _onlyErrors;

	private bool _stickToEnd = true;

	private bool _rendering;

	private string _lastMissingReason = string.Empty;

	private readonly ToolTip _toolTip = new ToolTip();

	private readonly Font _monoFont = new Font("Consolas", 9.2f, FontStyle.Regular, GraphicsUnit.Point);

	private readonly ContextMenuStrip _contextMenu = new ContextMenuStrip();

	private Label _titleLabel;

	private Label _pathLabel;

	private RoundedButton _refreshButton;

	private RoundedButton _autoButton;

	private RoundedButton _onlyButton;

	private RoundedButton _otherButton;

	private Label _filterCaption;

	private TextBox _filterBox;

	private SurfacePanel _logCard;

	private RichTextBox _richText;

	private Label _statusLabel;

	private RoundedButton _openButton;

	private RoundedButton _notepadButton;

	private RoundedButton _copyButton;

	private RoundedButton _closeButton;

	private readonly Timer _timer;

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref Point lParam);

	public ErrorLogForm(string serviceName)
		: this(serviceName, null)
	{
	}

	internal ErrorLogForm(string serviceName, string explicitLogPath)
	{
		_serviceName = (serviceName ?? string.Empty).Trim();
		_explicitLogPath = (string.IsNullOrWhiteSpace(explicitLogPath) ? null : explicitLogPath);
		Text = "MySQL 错误日志";
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
		_timer = new Timer();
		_timer.Interval = 2000;
		_timer.Tick += Timer_Tick;
		base.Shown += ErrorLogForm_Shown;
		base.FormClosed += delegate
		{
			_timer.Stop();
			_timer.Dispose();
			_toolTip.Dispose();
			_contextMenu.Dispose();
			_monoFont.Dispose();
		};
		base.KeyPreview = true;
		base.KeyDown += ErrorLogForm_KeyDown;
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
		_titleLabel.Text = "MySQL 错误日志";
		_titleLabel.Font = Theme.Font(16f, FontStyle.Bold);
		_titleLabel.ForeColor = Theme.Text;
		_titleLabel.BackColor = Color.Transparent;
		_titleLabel.AutoSize = false;
		_titleLabel.TextAlign = ContentAlignment.MiddleLeft;
		base.Controls.Add(_titleLabel);
		_pathLabel = new Label();
		_pathLabel.Text = "正在定位日志文件…";
		_pathLabel.Font = Theme.Font(8.6f, FontStyle.Regular);
		_pathLabel.ForeColor = Theme.Muted;
		_pathLabel.BackColor = Color.Transparent;
		_pathLabel.AutoSize = false;
		_pathLabel.AutoEllipsis = true;
		_pathLabel.TextAlign = ContentAlignment.MiddleLeft;
		base.Controls.Add(_pathLabel);
		_refreshButton = MakeButton("刷新", Theme.SurfaceAlt, Theme.Text, Theme.Border);
		_refreshButton.Click += delegate
		{
			Reload(force: true);
		};
		base.Controls.Add(_refreshButton);
		_autoButton = MakeButton("自动刷新", Theme.AccentSoft, Theme.Accent, Color.Transparent);
		_autoButton.Click += delegate
		{
			_autoRefresh = !_autoRefresh;
			UpdateAutoButton();
			if (_autoRefresh)
			{
				_timer.Start();
				Reload(force: true);
			}
			else
			{
				_timer.Stop();
			}
		};
		base.Controls.Add(_autoButton);
		_onlyButton = MakeButton("只看错误", Theme.SurfaceAlt, Theme.Muted, Theme.Border);
		_onlyButton.Click += delegate
		{
			_onlyErrors = !_onlyErrors;
			UpdateOnlyButton();
			RenderLog();
		};
		base.Controls.Add(_onlyButton);
		_filterCaption = new Label();
		_filterCaption.Text = "筛选";
		_filterCaption.Font = Theme.Font(8.6f, FontStyle.Bold);
		_filterCaption.ForeColor = Theme.Muted;
		_filterCaption.BackColor = Color.Transparent;
		_filterCaption.AutoSize = false;
		_filterCaption.TextAlign = ContentAlignment.MiddleLeft;
		base.Controls.Add(_filterCaption);
		_filterBox = new TextBox();
		_filterBox.Font = Theme.Font(9f, FontStyle.Regular);
		_filterBox.BackColor = Theme.SurfaceAlt;
		_filterBox.ForeColor = Theme.Text;
		_filterBox.BorderStyle = BorderStyle.FixedSingle;
		_filterBox.TextChanged += delegate
		{
			RenderLog();
		};
		base.Controls.Add(_filterBox);
		_otherButton = MakeButton("打开其它日志…", Theme.SurfaceAlt, Theme.Text, Theme.Border);
		_otherButton.Click += delegate
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Title = "选择 MySQL 错误日志";
			openFileDialog.Filter = "MySQL 错误日志 (*.err)|*.err|日志文件 (*.log;*.txt)|*.log;*.txt|所有文件 (*.*)|*.*";
			openFileDialog.CheckFileExists = true;
			if (openFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				_explicitLogPath = openFileDialog.FileName;
				_logPath = _explicitLogPath;
				_loadedLength = -1L;
				_loadedWriteTime = DateTime.MinValue;
				_lastMissingReason = string.Empty;
				UpdatePathLabel();
				Reload(force: true);
			}
		};
		base.Controls.Add(_otherButton);
		_logCard = new SurfacePanel();
		_logCard.FillColor = Theme.Surface;
		_logCard.Radius = 14;
		_logCard.BorderColor = Color.Transparent;
		base.Controls.Add(_logCard);
		_richText = new RichTextBox();
		_richText.ReadOnly = true;
		_richText.BorderStyle = BorderStyle.None;
		_richText.BackColor = Theme.SurfaceAlt;
		_richText.ForeColor = Theme.Text;
		_richText.Font = _monoFont;
		_richText.ScrollBars = RichTextBoxScrollBars.Both;
		_richText.WordWrap = false;
		_richText.DetectUrls = false;
		_richText.HideSelection = true;
		_richText.VScroll += delegate
		{
			UpdateStickToEnd();
		};
		_richText.MouseWheel += delegate
		{
			UpdateStickToEnd();
		};
		_richText.KeyUp += delegate
		{
			UpdateStickToEnd();
		};
		_logCard.Controls.Add(_richText);
		_statusLabel = new Label();
		_statusLabel.Text = "准备读取日志…";
		_statusLabel.Font = Theme.Font(8.4f, FontStyle.Regular);
		_statusLabel.ForeColor = Theme.Muted;
		_statusLabel.BackColor = Color.Transparent;
		_statusLabel.AutoSize = false;
		_statusLabel.TextAlign = ContentAlignment.MiddleLeft;
		base.Controls.Add(_statusLabel);
		_openButton = MakeButton("打开位置", Theme.SurfaceAlt, Theme.Text, Theme.Border);
		_openButton.Click += delegate
		{
			OpenLocation();
		};
		base.Controls.Add(_openButton);
		_notepadButton = MakeButton("记事本打开", Theme.SurfaceAlt, Theme.Text, Theme.Border);
		_notepadButton.Click += delegate
		{
			OpenInNotepad();
		};
		base.Controls.Add(_notepadButton);
		_copyButton = MakeButton("复制全部", Theme.SurfaceAlt, Theme.Text, Theme.Border);
		_copyButton.Click += delegate
		{
			CopyAll();
		};
		base.Controls.Add(_copyButton);
		_closeButton = MakeButton("关闭", Theme.Accent, Color.White, Theme.Border);
		_closeButton.DialogResult = DialogResult.Cancel;
		base.Controls.Add(_closeButton);
		base.CancelButton = _closeButton;
		ContextMenuStrip contextMenuStrip = _contextMenu;
		contextMenuStrip.Items.Add("复制选中内容", null, delegate
		{
			if (_richText.SelectionLength > 0)
			{
				Clipboard.SetText(_richText.SelectedText);
			}
		});
		contextMenuStrip.Items.Add("复制全部显示内容", null, delegate
		{
			CopyAll();
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("全选", null, delegate
		{
			_richText.SelectAll();
		});
		_richText.ContextMenuStrip = contextMenuStrip;
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
		roundedButton.Font = Theme.Font(8.8f, FontStyle.Bold);
		roundedButton.Radius = 9f;
		return roundedButton;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		LayoutCustom();
		ApplyTitleBarTheme();
		try
		{
			SetWindowTheme(_richText.Handle, (Theme.IsDark ? "DarkMode_Explorer" : "Explorer"), null);
		}
		catch
		{
		}
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
		int num = S(800);
		int num2 = S(620);
		base.MaximumSize = Size.Empty;
		base.MinimumSize = Size.Empty;
		if (base.ClientSize.Width != num || base.ClientSize.Height != num2)
		{
			base.ClientSize = new Size(num, num2);
		}
		int margin = S(24);
		int width = base.ClientSize.Width;
		int height = base.ClientSize.Height;
		_titleLabel.SetBounds(margin, S(12), width - margin * 2, S(32));
		_pathLabel.SetBounds(margin + S(2), S(47), width - margin * 2 - S(4), S(18));
		int num3 = S(77);
		int num4 = S(30);
		_refreshButton.SetBounds(margin, num3, S(72), num4);
		_autoButton.SetBounds(_refreshButton.Right + S(8), num3, S(104), num4);
		_onlyButton.SetBounds(_autoButton.Right + S(8), num3, S(104), num4);
		_filterCaption.SetBounds(_onlyButton.Right + S(16), num3, S(44), num4);
		_filterBox.SetBounds(_filterCaption.Right + S(6), num3 + S(2), S(170), num4 - S(4));
		_otherButton.SetBounds(_filterBox.Right + S(12), num3, S(112), num4);
		int footerTop = height - S(54);
		int num5 = height - S(46);
		int buttonHeight = S(36);
		int num6 = Math.Max(S(200), footerTop - S(119));
		_logCard.SetBounds(margin, S(115), width - margin * 2, num6);
		_richText.SetBounds(S(12), S(12), Math.Max(S(80), _logCard.ClientSize.Width - S(24)), Math.Max(S(60), _logCard.ClientSize.Height - S(24)));
		_closeButton.SetBounds(width - margin - S(80), num5, S(80), buttonHeight);
		_copyButton.SetBounds(_closeButton.Left - S(8) - S(92), num5, S(92), buttonHeight);
		_notepadButton.SetBounds(_copyButton.Left - S(8) - S(104), num5, S(104), buttonHeight);
		_openButton.SetBounds(_notepadButton.Left - S(8) - S(100), num5, S(100), buttonHeight);
		_statusLabel.SetBounds(margin, num5 + S(8), Math.Max(S(120), _openButton.Left - margin - S(16)), S(20));
		base.MinimumSize = base.Size;
		base.MaximumSize = base.Size;
	}

	private void ErrorLogForm_Shown(object sender, EventArgs e)
	{
		ApplyTitleBarTheme();
		UpdateAutoButton();
		UpdateOnlyButton();
		_timer.Start();
		Reload(force: true);
	}

	private void UpdateAutoButton()
	{
		_autoButton.Text = (_autoRefresh ? "自动刷新 开" : "自动刷新 关");
		_autoButton.NormalColor = (_autoRefresh ? Theme.AccentSoft : Theme.SurfaceAlt);
		_autoButton.ForeColor = (_autoRefresh ? Theme.Accent : Theme.Muted);
	}

	private void UpdateOnlyButton()
	{
		_onlyButton.Text = (_onlyErrors ? "只看错误 开" : "只看错误 关");
		_onlyButton.NormalColor = (_onlyErrors ? Theme.AccentSoft : Theme.SurfaceAlt);
		_onlyButton.ForeColor = (_onlyErrors ? Theme.Accent : Theme.Muted);
	}

	private void Timer_Tick(object sender, EventArgs e)
	{
		if (_autoRefresh)
		{
			Reload(force: false);
		}
	}

	private void Reload(bool force)
	{
		if (_rendering || base.IsDisposed)
		{
			return;
		}
		try
		{
			if (!string.IsNullOrWhiteSpace(_explicitLogPath))
			{
				if (!string.Equals(_logPath, _explicitLogPath, StringComparison.OrdinalIgnoreCase))
				{
					_logPath = _explicitLogPath;
					_lastMissingReason = string.Empty;
					UpdatePathLabel();
				}
			}
			else if (_instance == null)
			{
				_instance = MySqlInstanceLocator.Resolve(_serviceName);
				_logPath = _instance.LogErrorPath ?? string.Empty;
				_lastMissingReason = string.Empty;
				UpdatePathLabel();
			}
			if (string.IsNullOrWhiteSpace(_logPath))
			{
				ShowMissingLog("没有定位到 MySQL 错误日志文件。");
				return;
			}
			if (!File.Exists(_logPath))
			{
				ShowMissingLog("日志文件当前不存在：" + _logPath);
				return;
			}
			FileInfo fileInfo = new FileInfo(_logPath);
			if (!force && fileInfo.Length == _loadedLength && fileInfo.LastWriteTimeUtc == _loadedWriteTime)
			{
				return;
			}
			if (!TextDecoder.TryReadTail(_logPath, MaxTailBytes, out string text, out long fileLength, out bool truncated, out string error))
			{
				_statusLabel.Text = "读取失败：" + error;
				_statusLabel.ForeColor = Theme.Danger;
				return;
			}
			_rawText = text;
			_loadedLength = fileLength;
			_loadedWriteTime = fileInfo.LastWriteTimeUtc;
			_truncated = truncated;
			_lastMissingReason = string.Empty;
			RenderLog();
		}
		catch (Exception exception)
		{
			AppLog.Error("读取 MySQL 错误日志失败。", exception);
			_statusLabel.Text = "读取失败：" + exception.Message;
			_statusLabel.ForeColor = Theme.Danger;
		}
	}

	private void UpdatePathLabel()
	{
		string text = (string.IsNullOrWhiteSpace(_logPath) ? "未定位到错误日志文件" : _logPath);
		_pathLabel.Text = text;
		_toolTip.SetToolTip(_pathLabel, text);
	}

	private void ShowMissingLog(string reason)
	{
		_rawText = string.Empty;
		_loadedLength = -1L;
		_loadedWriteTime = DateTime.MinValue;
		UpdatePathLabel();
		if (reason == _lastMissingReason && _richText.TextLength > 0)
		{
			_statusLabel.Text = "未找到日志文件 · 每 2 秒自动重试";
			_statusLabel.ForeColor = Theme.Warning;
			return;
		}
		_lastMissingReason = reason;
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("未找到 MySQL 错误日志");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine(reason);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("常见原因：");
		stringBuilder.AppendLine("  · MySQL 服务从未成功启动过，日志文件尚未创建");
		stringBuilder.AppendLine("  · my.ini 中的 log-error 指向了其它位置");
		stringBuilder.AppendLine("  · 数据目录已被移动、重命名或删除");
		if (_instance != null && !string.IsNullOrWhiteSpace(_instance.DataDirectory))
		{
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("数据目录：" + _instance.DataDirectory);
		}
		if (_instance != null && _instance.ConfigCandidates.Count > 0)
		{
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("已检查的配置位置：");
			int num = 0;
			foreach (string configCandidate in _instance.ConfigCandidates)
			{
				if (num >= 4)
				{
					break;
				}
				stringBuilder.AppendLine("  " + configCandidate);
				num++;
			}
		}
		SetRichTextPlain(stringBuilder.ToString(), Theme.Muted);
		_statusLabel.Text = "未找到日志文件 · 每 2 秒自动重试";
		_statusLabel.ForeColor = Theme.Warning;
	}

	private void RenderLog()
	{
		if (_richText == null || base.IsDisposed)
		{
			return;
		}
		_rendering = true;
		try
		{
			Point scroll = GetScrollPosition();
			if (string.IsNullOrEmpty(_rawText))
			{
				if (_richText.TextLength == 0)
				{
					SetRichTextPlain("日志文件为空。\n\nMySQL 启动后才会写入内容。", Theme.Muted);
				}
				_statusLabel.Text = "日志为空";
				_statusLabel.ForeColor = Theme.Muted;
				return;
			}
			string[] array = _rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			string keyword = _filterBox.Text.Trim();
			bool hasKeyword = keyword.Length > 0;
			int num = Math.Max(0, array.Length - MaxLines);
			int num2 = 0;
			int num3 = 0;
			_richText.SuspendLayout();
			try
			{
				_richText.Clear();
				Color color = Color.Empty;
				StringBuilder stringBuilder = new StringBuilder();
				for (int i = num; i < array.Length; i++)
				{
					string text = array[i];
					if (i == array.Length - 1 && text.Length == 0)
					{
						continue;
					}
					num3++;
					if (_onlyErrors && !IsProblemLine(text))
					{
						continue;
					}
					if (hasKeyword && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					num2++;
					Color lineColor = GetLineColor(text);
					if (lineColor != color)
					{
						Flush(stringBuilder, color);
						color = lineColor;
					}
					stringBuilder.Append(text);
					stringBuilder.Append('\n');
				}
				Flush(stringBuilder, color);
			}
			finally
			{
				_richText.ResumeLayout();
			}
			if (num2 == 0)
			{
				SetRichTextPlain((_onlyErrors ? "没有错误或警告行。" : "没有匹配筛选关键字的日志行。") + "\n\n可以清空筛选条件，或关闭“只看错误”。", Theme.Muted);
			}
			if (_stickToEnd && num2 > 0)
			{
				_richText.SelectionStart = _richText.TextLength;
				_richText.SelectionLength = 0;
				_richText.ScrollToCaret();
			}
			else
			{
				SetScrollPosition(scroll);
			}
			string text2 = "显示 " + num2 + " 行";
			if (num2 != num3)
			{
				text2 = text2 + " / " + num3 + " 行";
			}
			if (_truncated)
			{
				text2 += " · 仅读取末尾 2 MB";
			}
			if (num > 0)
			{
				text2 += " · 仅最近 " + MaxLines + " 行";
			}
			try
			{
				FileInfo fileInfo = new FileInfo(_logPath);
				text2 = text2 + " · " + FormatSize(fileInfo.Length) + " · 最后写入 " + fileInfo.LastWriteTime.ToString("HH:mm:ss");
			}
			catch
			{
			}
			_statusLabel.Text = text2;
			_statusLabel.ForeColor = Theme.Muted;
		}
		finally
		{
			_rendering = false;
		}
	}

	private void Flush(StringBuilder builder, Color color)
	{
		if (builder.Length > 0)
		{
			_richText.SelectionStart = _richText.TextLength;
			_richText.SelectionLength = 0;
			_richText.SelectionColor = ((color == Color.Empty) ? Theme.Text : color);
			_richText.AppendText(builder.ToString());
			builder.Clear();
		}
	}

	private void SetRichTextPlain(string text, Color color)
	{
		_richText.SuspendLayout();
		try
		{
			_richText.Clear();
			_richText.SelectionColor = color;
			_richText.AppendText(text);
			_richText.SelectionStart = 0;
			_richText.SelectionLength = 0;
		}
		finally
		{
			_richText.ResumeLayout();
		}
	}

	private static bool IsProblemLine(string line)
	{
		return ContainsTag(line, "[ERROR]") || ContainsTag(line, "[Warning]");
	}

	private static bool ContainsTag(string line, string tag)
	{
		return line.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static Color GetLineColor(string line)
	{
		if (ContainsTag(line, "[ERROR]"))
		{
			return Theme.Danger;
		}
		if (ContainsTag(line, "[Warning]"))
		{
			return Theme.Warning;
		}
		if (line.IndexOf("ready for connections", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return Theme.Success;
		}
		if (ContainsTag(line, "[Note]") || ContainsTag(line, "[System]"))
		{
			return Theme.Muted;
		}
		return Theme.Text;
	}

	private Point GetScrollPosition()
	{
		try
		{
			Point point = default(Point);
			SendMessage(_richText.Handle, 1245, IntPtr.Zero, ref point);
			return point;
		}
		catch
		{
			return default(Point);
		}
	}

	private void SetScrollPosition(Point point)
	{
		try
		{
			SendMessage(_richText.Handle, 1246, IntPtr.Zero, ref point);
		}
		catch
		{
		}
	}

	private void UpdateStickToEnd()
	{
		try
		{
			Point point = new Point(S(6), Math.Max(S(4), _richText.ClientSize.Height - S(8)));
			int charIndexFromPosition = _richText.GetCharIndexFromPosition(point);
			_stickToEnd = charIndexFromPosition >= _richText.TextLength - 2;
		}
		catch
		{
			_stickToEnd = true;
		}
	}

	private void CopyAll()
	{
		if (string.IsNullOrEmpty(_richText.Text))
		{
			return;
		}
		try
		{
			Clipboard.SetText(_richText.Text);
			_statusLabel.Text = "已复制显示的 " + _richText.Lines.Length + " 行到剪贴板";
			_statusLabel.ForeColor = Theme.Success;
		}
		catch (Exception exception)
		{
			AppLog.Error("复制错误日志失败。", exception);
			MessageBox.Show("无法写入剪贴板：\n\n" + exception.Message, "复制失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	private void OpenLocation()
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(_logPath) && File.Exists(_logPath))
			{
				Process.Start("explorer.exe", "/select,\"" + _logPath + "\"");
				return;
			}
			string text = ((_instance != null && !string.IsNullOrWhiteSpace(_instance.DataDirectory)) ? _instance.DataDirectory : string.Empty);
			if (string.IsNullOrWhiteSpace(text) || !Directory.Exists(text))
			{
				text = AppLog.AppDataDirectory;
				Directory.CreateDirectory(text);
			}
			Process.Start("explorer.exe", "\"" + text + "\"");
		}
		catch (Exception exception)
		{
			AppLog.Error("打开日志所在位置失败。", exception);
			MessageBox.Show("无法打开文件夹。\n\n" + exception.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	private void OpenInNotepad()
	{
		if (string.IsNullOrWhiteSpace(_logPath) || !File.Exists(_logPath))
		{
			MessageBox.Show("当前没有可打开的日志文件。", "日志不存在", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		try
		{
			Process.Start("notepad.exe", "\"" + _logPath + "\"");
		}
		catch (Exception exception)
		{
			AppLog.Error("用记事本打开日志失败。", exception);
			MessageBox.Show("无法启动记事本。\n\n" + exception.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	private void ErrorLogForm_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.F5)
		{
			e.Handled = true;
			Reload(force: true);
		}
		else if (e.Control && e.KeyCode == Keys.F)
		{
			e.Handled = true;
			_filterBox.Focus();
			_filterBox.SelectAll();
		}
	}

	private static string FormatSize(long bytes)
	{
		if (bytes >= 1048576)
		{
			return (bytes / 1048576.0).ToString("0.0") + " MB";
		}
		if (bytes >= 1024)
		{
			return (bytes / 1024.0).ToString("0.0") + " KB";
		}
		return bytes + " B";
	}
}
