using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class BadgeLabel : Control
{
	private Color _badgeColor = Theme.AccentSoft;

	private Color _textColor = Theme.Accent;

	private Color _surfaceColor = Theme.Surface;

	public Color BadgeColor
	{
		get
		{
			return _badgeColor;
		}
		set
		{
			_badgeColor = value;
			Invalidate();
		}
	}

	public Color TextColor
	{
		get
		{
			return _textColor;
		}
		set
		{
			_textColor = value;
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

	public BadgeLabel()
	{
		DoubleBuffered = true;
		Font = Theme.Font(8.5f, FontStyle.Bold);
		base.Height = 26;
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF rectangle = new RectangleF(0.5f, 0.5f, (float)base.Width - 1f, (float)base.Height - 1f);
		using (SolidBrush brush = new SolidBrush(_surfaceColor))
		{
			e.Graphics.FillRectangle(brush, base.ClientRectangle);
		}
		using (GraphicsPath path = Theme.RoundedRectangle(rectangle, (float)base.Height / 2f))
		{
			using SolidBrush brush2 = new SolidBrush(_badgeColor);
			e.Graphics.FillPath(brush2, path);
		}
		using StringFormat stringFormat = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			FormatFlags = StringFormatFlags.NoWrap
		};
		using SolidBrush brush3 = new SolidBrush(_textColor);
		e.Graphics.DrawString(Text, Font, brush3, new RectangleF(0f, 0f, base.Width, base.Height), stringFormat);
	}
}
