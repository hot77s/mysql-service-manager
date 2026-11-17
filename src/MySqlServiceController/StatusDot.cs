using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class StatusDot : Control
{
	private Color _dotColor = Theme.Subtle;

	private Color _surfaceColor = Theme.Surface;

	public Color DotColor
	{
		get
		{
			return _dotColor;
		}
		set
		{
			_dotColor = value;
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

	public StatusDot()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (SolidBrush brush = new SolidBrush(_surfaceColor))
		{
			e.Graphics.FillRectangle(brush, base.ClientRectangle);
		}
		int num = Math.Min(base.Width, base.Height);
		RectangleF rect = new RectangleF((float)(base.Width - num) / 2f, (float)(base.Height - num) / 2f, (float)num - 1f, (float)num - 1f);
		using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(45, _dotColor)))
		{
			e.Graphics.FillEllipse(brush2, rect);
		}
		float num2 = (float)num * 0.25f;
		RectangleF rect2 = new RectangleF(rect.X + num2, rect.Y + num2, rect.Width - num2 * 2f, rect.Height - num2 * 2f);
		using SolidBrush brush3 = new SolidBrush(_dotColor);
		e.Graphics.FillEllipse(brush3, rect2);
	}
}
