using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController.Setup;

internal sealed class SetupButton : Button
{
	private bool _hovered;

	private bool _pressed;

	private Color _normalColor = SetupTheme.Accent;

	private Color _surfaceColor = SetupTheme.Background;

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

	public SetupButton()
	{
		DoubleBuffered = true;
		base.FlatStyle = FlatStyle.Flat;
		base.FlatAppearance.BorderSize = 0;
		base.UseVisualStyleBackColor = false;
		ForeColor = Color.White;
		Font = SetupTheme.Font(10f, FontStyle.Bold);
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

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Color color = ((!base.Enabled) ? Color.FromArgb(242, 244, 247) : (_pressed ? ControlPaint.Dark(_normalColor, 0.14f) : ((!_hovered) ? _normalColor : ControlPaint.Dark(_normalColor, 0.08f))));
		RectangleF rectangle = new RectangleF(0.5f, 0.5f, (float)base.Width - 1f, (float)base.Height - 1f);
		using (SolidBrush brush = new SolidBrush(_surfaceColor))
		{
			e.Graphics.FillRectangle(brush, base.ClientRectangle);
		}
		using (GraphicsPath path = SetupTheme.RoundedRectangle(rectangle, 9f))
		{
			using SolidBrush brush2 = new SolidBrush(color);
			e.Graphics.FillPath(brush2, path);
		}
		TextRenderer.DrawText(e.Graphics, Text, Font, base.ClientRectangle, base.Enabled ? ForeColor : SetupTheme.Subtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
	}
}
