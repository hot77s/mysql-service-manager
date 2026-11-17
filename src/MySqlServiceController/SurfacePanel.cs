using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class SurfacePanel : Panel
{
	private Color _fillColor = Color.White;

	private Color _accentColor = Color.Transparent;

	private int _accentWidth;

	private Color _borderColor = Color.Transparent;

	private int _radius = 12;

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

	public Color AccentColor
	{
		get
		{
			return _accentColor;
		}
		set
		{
			_accentColor = value;
			Invalidate();
		}
	}

	public int AccentWidth
	{
		get
		{
			return _accentWidth;
		}
		set
		{
			_accentWidth = value;
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

	public SurfacePanel()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = _fillColor;
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		if (base.ClientSize.Width <= 0 || base.ClientSize.Height <= 0)
		{
			return;
		}
		Graphics graphics = e.Graphics;
		Color color = base.Parent?.BackColor ?? _fillColor;
		graphics.Clear(color);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (SolidBrush brush = new SolidBrush(color))
		{
			graphics.FillRectangle(brush, base.ClientRectangle);
		}
		RectangleF rectangle = new RectangleF(0.5f, 0.5f, (float)base.ClientSize.Width - 1f, (float)base.ClientSize.Height - 1f);
		using GraphicsPath graphicsPath = Theme.RoundedRectangle(rectangle, _radius);
		using (SolidBrush brush2 = new SolidBrush(_fillColor))
		{
			graphics.FillPath(brush2, graphicsPath);
		}
		if (_accentWidth > 0 && _accentColor.A > 0)
		{
			GraphicsState state = graphics.Save();
			try
			{
				graphics.SetClip(graphicsPath);
				using SolidBrush brush3 = new SolidBrush(_accentColor);
				graphics.FillRectangle(brush3, 0, 0, _accentWidth, base.ClientSize.Height);
			}
			finally
			{
				graphics.Restore(state);
			}
		}
		if (_borderColor.A > 0)
		{
			using Pen pen = new Pen(_borderColor, 1f);
			graphics.DrawPath(pen, graphicsPath);
		}
	}
}
