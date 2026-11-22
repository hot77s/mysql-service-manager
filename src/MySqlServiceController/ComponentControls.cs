using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController;

/// <summary>卡片内使用的圆角路径输入框。</summary>
internal sealed class PathBox : Control
{
	private readonly TextBox _box;

	public event EventHandler TextEdited;

	public PathBox()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		DoubleBuffered = true;
		BackColor = Theme.SurfaceAlt;
		base.Height = 30;
		_box = new TextBox
		{
			BorderStyle = BorderStyle.None,
			Font = Theme.Font(9f, FontStyle.Regular),
			BackColor = Theme.SurfaceAlt,
			ForeColor = Theme.Text
		};
		_box.TextChanged += OnBoxTextChanged;
		Controls.Add(_box);
	}

	public override string Text
	{
		get
		{
			return _box.Text;
		}
		set
		{
			_box.Text = value;
		}
	}

	public bool ReadOnly
	{
		get
		{
			return _box.ReadOnly;
		}
		set
		{
			_box.ReadOnly = value;
		}
	}

	public void SetTextSilently(string value)
	{
		_box.TextChanged -= OnBoxTextChanged;
		_box.Text = value;
		_box.TextChanged += OnBoxTextChanged;
		Invalidate();
	}

	private void OnBoxTextChanged(object sender, EventArgs e)
	{
		TextEdited?.Invoke(this, EventArgs.Empty);
		Invalidate();
	}

	public void ApplyTheme()
	{
		if (_box == null)
		{
			return;
		}
		BackColor = Theme.SurfaceAlt;
		_box.BackColor = BackColor;
		_box.ForeColor = ((!base.Enabled || _box.ReadOnly) ? Theme.Muted : Theme.Text);
		Invalidate();
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		base.OnEnabledChanged(e);
		ApplyTheme();
	}

	protected override void OnLayout(LayoutEventArgs levent)
	{
		base.OnLayout(levent);
		if (_box == null)
		{
			return;
		}
		int pad = 10;
		int height = _box.PreferredHeight;
		_box.SetBounds(pad, Math.Max(0, (base.Height - height) / 2), Math.Max(10, base.Width - pad * 2), height);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : Theme.Surface);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF rect = new RectangleF(0.5f, 0.5f, (float)base.Width - 1f, (float)base.Height - 1f);
		using GraphicsPath path = Theme.RoundedRectangle(rect, 8f);
		using (SolidBrush brush = new SolidBrush(BackColor))
		{
			e.Graphics.FillPath(brush, path);
		}
		using (Pen pen = new Pen(base.Enabled ? Theme.Border : Theme.Border))
		{
			e.Graphics.DrawPath(pen, path);
		}
	}
}

/// <summary>自绘复选框，跟随主题颜色。</summary>
internal sealed class SlimCheckBox : CheckBox
{
	public SlimCheckBox()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		DoubleBuffered = true;
		base.AutoSize = false;
		Font = Theme.Font(9.2f, FontStyle.Regular);
		ForeColor = Theme.Text;
		Cursor = Cursors.Hand;
		base.Height = 22;
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : Theme.Surface);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		// CheckBox 不会自动调用 OnPaintBackground，必须自己擦除整个控件区域，
		// 否则上一次的内容会残留，未覆盖处还会露出黑底。
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : Theme.Surface);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		int size = 16;
		int top = Math.Max(0, (base.Height - size) / 2);
		RectangleF box = new RectangleF(0.5f, top + 0.5f, size, size);
		using GraphicsPath path = Theme.RoundedRectangle(box, 4f);
		bool enabled = base.Enabled;
		Color fill = (Checked ? (enabled ? Theme.Accent : Theme.Subtle) : Theme.Surface);
		Color border = (Checked ? fill : Theme.Muted);
		using (SolidBrush brush = new SolidBrush(fill))
		{
			e.Graphics.FillPath(brush, path);
		}
		using (Pen pen = new Pen(border))
		{
			e.Graphics.DrawPath(pen, path);
		}
		if (Checked)
		{
			using Pen tick = new Pen(Color.White, 1.8f)
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			PointF[] points = new PointF[3]
			{
				new PointF(4.5f, top + 8.5f),
				new PointF(7.4f, top + 11.4f),
				new PointF(12.2f, top + 5.2f)
			};
			e.Graphics.DrawLines(tick, points);
		}
		Color textColor = (enabled ? ForeColor : Theme.Subtle);
		Rectangle textRect = new Rectangle(size + 8, 0, Math.Max(10, base.Width - size - 8), base.Height);
		TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		base.OnMouseEnter(e);
		Invalidate();
	}
}

/// <summary>细进度条，跟随主题颜色。</summary>
internal sealed class ProgressLine : Control
{
	private int _value;

	private bool _active;

	public int Value
	{
		get
		{
			return _value;
		}
		set
		{
			_value = Math.Max(0, Math.Min(100, value));
			if (_value > 0)
			{
				_active = true;
			}
			Invalidate();
		}
	}

	public void Reset()
	{
		_value = 0;
		_active = false;
		Invalidate();
	}

	public ProgressLine()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
		DoubleBuffered = true;
		base.Height = 4;
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : Theme.Surface);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear((base.Parent != null) ? base.Parent.BackColor : Theme.Surface);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF track = new RectangleF(0f, 0f, base.Width, base.Height);
		using (GraphicsPath path = Theme.RoundedRectangle(track, base.Height / 2f))
		{
			using SolidBrush brush = new SolidBrush(Theme.Border);
			e.Graphics.FillPath(brush, path);
		}
		if (!_active)
		{
			return;
		}
		float width = Math.Max(base.Height, base.Width * _value / 100f);
		RectangleF fill = new RectangleF(0f, 0f, width, base.Height);
		using GraphicsPath fillPath = Theme.RoundedRectangle(fill, base.Height / 2f);
		using SolidBrush fillBrush = new SolidBrush(Theme.Accent);
		e.Graphics.FillPath(fillBrush, fillPath);
	}
}

/// <summary>下拉菜单配色，跟随主题。</summary>
internal sealed class ComponentMenuColors : ProfessionalColorTable
{
	public override Color MenuItemSelected => Theme.SurfaceAlt;

	public override Color MenuItemSelectedGradientBegin => Theme.SurfaceAlt;

	public override Color MenuItemSelectedGradientEnd => Theme.SurfaceAlt;

	public override Color MenuItemBorder => Theme.Border;

	public override Color MenuBorder => Theme.Border;

	public override Color ToolStripDropDownBackground => Theme.Surface;

	public override Color ImageMarginGradientBegin => Theme.Surface;

	public override Color ImageMarginGradientMiddle => Theme.Surface;

	public override Color ImageMarginGradientEnd => Theme.Surface;

	public override Color SeparatorDark => Theme.Border;

	public override Color SeparatorLight => Theme.Border;
}
