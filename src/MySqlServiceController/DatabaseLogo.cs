using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class DatabaseLogo : Control
{
	private Color _surfaceColor = Theme.Background;

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

	public DatabaseLogo()
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
		RectangleF rectangleF = new RectangleF(0.5f, 0.5f, (float)base.Width - 1f, (float)base.Height - 1f);
		using (GraphicsPath path = Theme.RoundedRectangle(rectangleF, (float)base.Width * 0.24f))
		{
			using LinearGradientBrush brush2 = new LinearGradientBrush(rectangleF, Color.FromArgb(49, 126, 247), Color.FromArgb(28, 87, 210), 45f);
			e.Graphics.FillPath(brush2, path);
		}
		float num = (float)base.Width / 2f;
		float num2 = (float)base.Width * 0.43f;
		float num3 = (float)base.Height * 0.28f;
		float num4 = (float)base.Height * 0.74f;
		float num5 = (float)base.Height * 0.16f;
		RectangleF rect = new RectangleF(num - num2 / 2f, num3, num2, num5);
		using Pen pen = new Pen(Color.White, Math.Max(1.8f, (float)base.Width * 0.05f));
		pen.StartCap = LineCap.Round;
		pen.EndCap = LineCap.Round;
		e.Graphics.DrawEllipse(pen, rect);
		e.Graphics.DrawArc(pen, num - num2 / 2f, num3 + (num4 - num3) / 2f - num5 / 2f, num2, num5, 0f, 180f);
		e.Graphics.DrawArc(pen, num - num2 / 2f, num4 - num5 / 2f, num2, num5, 0f, 180f);
		e.Graphics.DrawLine(pen, num - num2 / 2f, num3 + num5 / 2f, num - num2 / 2f, num4 - num5 / 2f);
		e.Graphics.DrawLine(pen, num + num2 / 2f, num3 + num5 / 2f, num + num2 / 2f, num4 - num5 / 2f);
	}
}
