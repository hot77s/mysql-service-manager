using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController.Setup;

/// <summary>圆角卡片，等价于主程序的 SurfacePanel。</summary>
internal class CardPanel : Panel
{
	private Color _fillColor = SetupTheme.Surface;

	private Color _borderColor = Color.Transparent;

	private int _radius = 16;

	public Color FillColor
	{
		get
		{
			return _fillColor;
		}
		set
		{
			_fillColor = value;
			BackColor = value;
			Invalidate();
		}
	}

	public Color BorderColor
	{
		get
		{
			return _borderColor;
		}
		set
		{
			_borderColor = value;
			Invalidate();
		}
	}

	public int Radius
	{
		get
		{
			return _radius;
		}
		set
		{
			_radius = value;
			Invalidate();
		}
	}

	public CardPanel()
	{
		DoubleBuffered = true;
		BackColor = _fillColor;
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		if (base.ClientSize.Width <= 0 || base.ClientSize.Height <= 0)
		{
			return;
		}
		Graphics graphics = e.Graphics;
		graphics.Clear(base.Parent?.BackColor ?? _fillColor);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF rectangle = new RectangleF(0.5f, 0.5f, (float)base.ClientSize.Width - 1f, (float)base.ClientSize.Height - 1f);
		using GraphicsPath path = SetupTheme.RoundedRectangle(rectangle, _radius);
		using (SolidBrush brush = new SolidBrush(_fillColor))
		{
			graphics.FillPath(brush, path);
		}
		if (_borderColor.A > 0)
		{
			using Pen pen = new Pen(_borderColor);
			graphics.DrawPath(pen, path);
		}
	}
}

/// <summary>卡片序号徽标（蓝色圆形数字），与主程序设置页一致。</summary>
internal sealed class NumberBadge : Control
{
	public NumberBadge(int number, int diameter)
	{
		_number = number;
		base.Size = new Size(diameter, diameter);
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, value: true);
		BackColor = SetupTheme.Surface;
		Font = SetupTheme.Font(9f, FontStyle.Bold);
	}

	private readonly int _number;

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : SetupTheme.Surface);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Rectangle rect = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
		using (SolidBrush brush = new SolidBrush(SetupTheme.Accent))
		{
			e.Graphics.FillEllipse(brush, rect);
		}
		TextRenderer.DrawText(e.Graphics, _number.ToString(), Font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
	}
}

/// <summary>胶囊按钮，等价于主程序的 RoundedButton。</summary>
internal sealed class PillButton : Button
{
	private bool _hovered;

	private bool _pressed;

	private Color _normalColor = SetupTheme.Accent;

	private Color _hoverColor = Color.Empty;

	private Color _pressedColor = Color.Empty;

	private Color _borderColor = Color.Transparent;

	private Color _surfaceColor = SetupTheme.Background;

	private Color _disabledColor = SetupTheme.NeutralSoft;

	private Color _disabledTextColor = SetupTheme.Subtle;

	private float _radius = 10f;

	public Color NormalColor
	{
		get
		{
			return _normalColor;
		}
		set
		{
			_normalColor = value;
			Invalidate();
		}
	}

	public Color HoverColor
	{
		get
		{
			return (_hoverColor == Color.Empty) ? SetupTheme.Mix(_normalColor, SetupTheme.Text, 0.06) : _hoverColor;
		}
		set
		{
			_hoverColor = value;
			Invalidate();
		}
	}

	public Color PressedColor
	{
		get
		{
			return (_pressedColor == Color.Empty) ? SetupTheme.Mix(_normalColor, SetupTheme.Text, 0.12) : _pressedColor;
		}
		set
		{
			_pressedColor = value;
			Invalidate();
		}
	}

	public Color ButtonBorderColor
	{
		get
		{
			return _borderColor;
		}
		set
		{
			_borderColor = value;
			Invalidate();
		}
	}

	public Color SurfaceColor
	{
		get
		{
			return _surfaceColor;
		}
		set
		{
			_surfaceColor = value;
			Invalidate();
		}
	}

	public Color DisabledColor
	{
		get
		{
			return _disabledColor;
		}
		set
		{
			_disabledColor = value;
			Invalidate();
		}
	}

	public Color DisabledTextColor
	{
		get
		{
			return _disabledTextColor;
		}
		set
		{
			_disabledTextColor = value;
			Invalidate();
		}
	}

	public float Radius
	{
		get
		{
			return _radius;
		}
		set
		{
			_radius = value;
			Invalidate();
		}
	}

	public PillButton()
	{
		DoubleBuffered = true;
		base.FlatStyle = FlatStyle.Flat;
		base.FlatAppearance.BorderSize = 0;
		base.UseVisualStyleBackColor = false;
		ForeColor = Color.White;
		Font = SetupTheme.Font(9.2f, FontStyle.Bold);
		Cursor = Cursors.Hand;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hovered = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hovered = false;
		_pressed = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		_pressed = true;
		Invalidate();
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_pressed = false;
		Invalidate();
		base.OnMouseUp(e);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		Cursor = (base.Enabled ? Cursors.Hand : Cursors.Default);
		Invalidate();
		base.OnEnabledChanged(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		Color color;
		Color foreColor;
		if (!base.Enabled)
		{
			color = _disabledColor;
			foreColor = _disabledTextColor;
		}
		else if (_pressed)
		{
			color = PressedColor;
			foreColor = ForeColor;
		}
		else if (_hovered)
		{
			color = HoverColor;
			foreColor = ForeColor;
		}
		else
		{
			color = _normalColor;
			foreColor = ForeColor;
		}
		RectangleF rectangle = new RectangleF(0.5f, 0.5f, (float)base.Width - 1f, (float)base.Height - 1f);
		using (SolidBrush brush = new SolidBrush(_surfaceColor))
		{
			e.Graphics.FillRectangle(brush, base.ClientRectangle);
		}
		using (GraphicsPath path = SetupTheme.RoundedRectangle(rectangle, _radius))
		{
			using (SolidBrush brush2 = new SolidBrush(color))
			{
				e.Graphics.FillPath(brush2, path);
			}
			if (_borderColor.A > 0)
			{
				using Pen pen = new Pen(_borderColor);
				e.Graphics.DrawPath(pen, path);
			}
		}
		TextRenderer.DrawText(e.Graphics, Text, Font, base.ClientRectangle, base.Enabled ? foreColor : _disabledTextColor, TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		if (Focused && ShowFocusCues)
		{
			ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(6, 6, base.Width - 12, base.Height - 12), foreColor, color);
		}
	}
}

/// <summary>两段/三段选择器，等价于主程序设置页的主题切换。</summary>
internal sealed class SegmentedControl : Control
{
	private string[] _items = Array.Empty<string>();

	private int _selectedIndex;

	public event EventHandler SelectedIndexChanged;

	public string[] Items
	{
		get
		{
			return _items;
		}
		set
		{
			_items = value ?? Array.Empty<string>();
			Invalidate();
		}
	}

	public int SelectedIndex
	{
		get
		{
			return _selectedIndex;
		}
		set
		{
			int num = Math.Max(0, Math.Min(_items.Length - 1, value));
			if (num != _selectedIndex)
			{
				_selectedIndex = num;
				Invalidate();
				SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public SegmentedControl()
	{
		DoubleBuffered = true;
		Cursor = Cursors.Hand;
		Font = SetupTheme.Font(9f, FontStyle.Bold);
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, value: true);
		BackColor = SetupTheme.Surface;
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : SetupTheme.Surface);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (_items.Length != 0)
		{
			int num = Math.Max(0, Math.Min(_items.Length - 1, e.X * _items.Length / Math.Max(1, base.Width)));
			SelectedIndex = num;
		}
		base.OnMouseDown(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF track = new RectangleF(0.5f, 0.5f, (float)base.Width - 1f, (float)base.Height - 1f);
		using (GraphicsPath path = SetupTheme.RoundedRectangle(track, base.Height / 2f))
		{
			using SolidBrush brush = new SolidBrush(SetupTheme.SurfaceAlt);
			e.Graphics.FillPath(brush, path);
		}
		if (_items.Length == 0)
		{
			return;
		}
		float num = (float)base.Width / (float)_items.Length;
		for (int i = 0; i < _items.Length; i++)
		{
			RectangleF rect = new RectangleF((float)i * num + 3f, 3f, num - 6f, (float)base.Height - 6f);
			bool flag = i == _selectedIndex;
			using (GraphicsPath path2 = SetupTheme.RoundedRectangle(rect, rect.Height / 2f))
			{
				using SolidBrush brush2 = new SolidBrush(flag ? SetupTheme.Accent : Color.Transparent);
				e.Graphics.FillPath(brush2, path2);
			}
			Color color = (flag ? Color.White : SetupTheme.Muted);
			TextRenderer.DrawText(e.Graphics, _items[i], Font, Rectangle.Round(rect), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
		}
	}
}

/// <summary>带圆角边框的输入框，用于展示/编辑安装路径。</summary>
internal sealed class PathField : Panel
{
	private readonly TextBox _box;

	private Color _fillColor = SetupTheme.Input;

	private bool _readOnly;

	public event EventHandler TextEdited;

	public override string Text
	{
		get
		{
			return _box.Text;
		}
		set
		{
			_box.Text = value ?? string.Empty;
		}
	}

	public bool ReadOnly
	{
		get
		{
			return _readOnly;
		}
		set
		{
			_readOnly = value;
			_box.ReadOnly = value;
			Invalidate();
		}
	}

	private bool _silent;

	/// <summary>程序内改值时不触发 TextEdited，避免把自动生成的路径误判为手工输入。</summary>
	public void SetTextSilently(string value)
	{
		_silent = true;
		try
		{
			_box.Text = value ?? string.Empty;
		}
		finally
		{
			_silent = false;
		}
	}

	public Color FillColor
	{
		get
		{
			return _fillColor;
		}
		set
		{
			_fillColor = value;
			_box.BackColor = value;
			Invalidate();
		}
	}

	public PathField()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		_box = new TextBox
		{
			BorderStyle = BorderStyle.None,
			Font = SetupTheme.Font(8.6f, FontStyle.Regular),
			BackColor = _fillColor,
			ForeColor = SetupTheme.Text
		};
		_box.GotFocus += delegate { Invalidate(); };
		_box.LostFocus += delegate { Invalidate(); };
		_box.TextChanged += delegate
		{
			if (!_silent)
			{
				TextEdited?.Invoke(this, EventArgs.Empty);
			}
			Invalidate();
		};
		base.Controls.Add(_box);
		base.Height = 32;
	}

	protected override void OnResize(EventArgs eventargs)
	{
		base.OnResize(eventargs);
		_box.SetBounds(11, Math.Max(1, (base.Height - _box.PreferredHeight) / 2), Math.Max(10, base.Width - 22), _box.PreferredHeight);
	}

	public void ApplyTheme()
	{
		bool dimmed = _readOnly || !base.Enabled;
		FillColor = (dimmed ? SetupTheme.SurfaceAlt : SetupTheme.Input);
		_box.ForeColor = (dimmed ? SetupTheme.Subtle : SetupTheme.Text);
		Invalidate();
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		base.OnEnabledChanged(e);
		ApplyTheme();
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(base.Parent?.BackColor ?? SetupTheme.Surface);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF rectangle = new RectangleF(0.5f, 0.5f, (float)base.ClientSize.Width - 1f, (float)base.ClientSize.Height - 1f);
		using GraphicsPath path = SetupTheme.RoundedRectangle(rectangle, 8f);
		using (SolidBrush brush = new SolidBrush(_fillColor))
		{
			graphics.FillPath(brush, path);
		}
		Color color = (_box.Focused ? SetupTheme.Accent : SetupTheme.Border);
		using (Pen pen = new Pen(color, _box.Focused ? 1.6f : 1f))
		{
			graphics.DrawPath(pen, path);
		}
	}
}

/// <summary>自绘复选框，深浅色主题下都保持可读。</summary>
internal sealed class SetupCheckBox : CheckBox
{
	public SetupCheckBox()
	{
		DoubleBuffered = true;
		AutoSize = false;
		Cursor = Cursors.Hand;
		Font = SetupTheme.Font(8.8f, FontStyle.Regular);
		base.Height = 22;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, value: true);
		BackColor = SetupTheme.Surface;
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : SetupTheme.Surface);
	}

	public int PreferredWidth
	{
		get
		{
			Size size = TextRenderer.MeasureText(Text ?? string.Empty, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
			return 26 + size.Width;
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		// CheckBox 不会自动调用 OnPaintBackground：必须自己擦除区域，
		// 否则会残留上一次的文字、并在未覆盖处露出 BackColor 白块。
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : SetupTheme.Background);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		int num = 16;
		Rectangle rect = new Rectangle(1, (base.Height - num) / 2, num, num);
		using (GraphicsPath path = SetupTheme.RoundedRectangle(new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f), 5f))
		{
			if (Checked)
			{
				using SolidBrush brush = new SolidBrush(SetupTheme.Accent);
				e.Graphics.FillPath(brush, path);
			}
			else
			{
				using Pen pen = new Pen(SetupTheme.Neutral);
				e.Graphics.DrawPath(pen, path);
			}
		}
		if (Checked)
		{
			using Pen pen2 = new Pen(Color.White, 2f)
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			e.Graphics.DrawLines(pen2, new Point[3]
			{
				new Point(5, rect.Y + 8),
				new Point(8, rect.Y + 11),
				new Point(13, rect.Y + 5)
			});
		}
		Color foreColor = (base.Enabled ? SetupTheme.Text : SetupTheme.Subtle);
		TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(24, 0, base.Width - 24, base.Height), foreColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
	}
}

/// <summary>细进度条，Value 小于 0 时显示循环动画。</summary>
internal sealed class ProgressLine : Control
{
	private double _value;

	private readonly Timer _timer;

	private int _offset;

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : SetupTheme.Background);
	}

	public double Value
	{
		get
		{
			return _value;
		}
		set
		{
			_value = value;
			Invalidate();
		}
	}

	public ProgressLine()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = SetupTheme.Background;
		base.Height = 4;
		_timer = new Timer
		{
			Interval = 40
		};
		_timer.Tick += delegate
		{
			_offset = (_offset + 6) % Math.Max(1, base.Width + 80);
			Invalidate();
		};
	}

	protected override void OnVisibleChanged(EventArgs e)
	{
		base.OnVisibleChanged(e);
		if (base.Visible)
		{
			_timer.Start();
		}
		else
		{
			_timer.Stop();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_timer.Dispose();
		}
		base.Dispose(disposing);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF rect = new RectangleF(0f, 0f, base.Width, base.Height);
		using (GraphicsPath path = SetupTheme.RoundedRectangle(rect, base.Height / 2f))
		{
			using (SolidBrush brush = new SolidBrush(SetupTheme.SurfaceAlt))
			{
				e.Graphics.FillPath(brush, path);
			}
			GraphicsState state = e.Graphics.Save();
			try
			{
				e.Graphics.SetClip(path);
				if (_value < 0.0)
				{
					int num = Math.Max(40, base.Width / 4);
					using LinearGradientBrush brush2 = new LinearGradientBrush(new RectangleF((float)_offset - (float)num, 0f, (float)num, (float)base.Height), Color.FromArgb(0, SetupTheme.Accent), SetupTheme.Accent, LinearGradientMode.Horizontal);
					e.Graphics.FillRectangle(brush2, (float)_offset - (float)num, 0f, num, base.Height);
					using SolidBrush brush3 = new SolidBrush(SetupTheme.Accent);
					e.Graphics.FillRectangle(brush3, _offset, 0f, num, base.Height);
				}
				else
				{
					float num2 = (float)Math.Max(0.0, Math.Min(1.0, _value)) * (float)base.Width;
					using SolidBrush brush4 = new SolidBrush(SetupTheme.Accent);
					e.Graphics.FillRectangle(brush4, 0f, 0f, num2, base.Height);
				}
			}
			finally
			{
				e.Graphics.Restore(state);
			}
		}
	}
}
