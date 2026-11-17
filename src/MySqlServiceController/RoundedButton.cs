using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class RoundedButton : Button
{
	private bool _hovered;

	private bool _pressed;

	private Color _normalColor = Theme.Accent;

	private Color _hoverColor = Theme.AccentDark;

	private Color _pressedColor = Color.FromArgb(18, 70, 179);

	private Color _borderColor = Color.Transparent;

	private Color _surfaceColor = Theme.Background;

	private Color _disabledColor = Theme.NeutralSoft;

	private Color _disabledTextColor = Theme.Subtle;

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
			return _hoverColor;
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
			return _pressedColor;
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

	public RoundedButton()
	{
		DoubleBuffered = true;
		base.FlatStyle = FlatStyle.Flat;
		base.FlatAppearance.BorderSize = 0;
		base.UseVisualStyleBackColor = false;
		Cursor = Cursors.Hand;
		ForeColor = Color.White;
		Font = Theme.Font(10.5f, FontStyle.Bold);
		base.Height = 46;
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
			color = _pressedColor;
			foreColor = ForeColor;
		}
		else if (_hovered)
		{
			color = _hoverColor;
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
		using (GraphicsPath path = Theme.RoundedRectangle(rectangle, _radius))
		{
			using SolidBrush brush2 = new SolidBrush(color);
			e.Graphics.FillPath(brush2, path);
			if (_borderColor.A > 0)
			{
				using Pen pen = new Pen(_borderColor);
				e.Graphics.DrawPath(pen, path);
			}
		}
		TextRenderer.DrawText(e.Graphics, Text, Font, base.ClientRectangle, foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		if (Focused && ShowFocusCues)
		{
			ControlPaint.DrawFocusRectangle(rectangle: new Rectangle(6, 6, base.Width - 12, base.Height - 12), graphics: e.Graphics, foreColor: foreColor, backColor: color);
		}
	}
}
