using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class TrafficLights : Control
{
	private int _hoverIndex = -1;

	public event EventHandler CloseClicked;

	public event EventHandler MinimizeClicked;

	public event EventHandler ZoomClicked;

	public TrafficLights()
	{
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, value: true);
		base.Size = new Size(76, 18);
		base.Cursor = Cursors.Hand;
	}

	private static Color DotColor(int index)
	{
		switch (index)
		{
		case 0:
			return Color.FromArgb(255, 95, 87);
		case 1:
			return Color.FromArgb(254, 188, 46);
		default:
			return Color.FromArgb(40, 200, 64);
		}
	}

	private Rectangle DotBounds(int index)
	{
		return new Rectangle(index * 20, 1, 16, 16);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		int num = -1;
		for (int i = 0; i < 3; i++)
		{
			if (DotBounds(i).Contains(e.Location))
			{
				num = i;
			}
		}
		if (num != _hoverIndex)
		{
			_hoverIndex = num;
			Invalidate();
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);
		_hoverIndex = -1;
		Invalidate();
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		for (int i = 0; i < 3; i++)
		{
			if (!DotBounds(i).Contains(e.Location))
			{
				continue;
			}
			switch (i)
			{
			case 0:
				CloseClicked?.Invoke(this, EventArgs.Empty);
				break;
			case 1:
				MinimizeClicked?.Invoke(this, EventArgs.Empty);
				break;
			default:
				ZoomClicked?.Invoke(this, EventArgs.Empty);
				break;
			}
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		for (int i = 0; i < 3; i++)
		{
			Rectangle rectangle = DotBounds(i);
			using (SolidBrush brush = new SolidBrush(DotColor(i)))
			{
				graphics.FillEllipse(brush, rectangle);
			}
			if (_hoverIndex != i)
			{
				continue;
			}
			using Pen pen = new Pen(Color.FromArgb(90, 0, 0, 0), 1.6f);
			int num = rectangle.Left + 5;
			int num2 = rectangle.Top + 5;
			int num3 = rectangle.Right - 5;
			int num4 = rectangle.Bottom - 5;
			int num5 = rectangle.Left + 8;
			int num6 = rectangle.Top + 8;
			switch (i)
			{
			case 0:
				graphics.DrawLine(pen, num, num2, num3, num4);
				graphics.DrawLine(pen, num3, num2, num, num4);
				break;
			case 1:
				graphics.DrawLine(pen, num, num6, num3, num6);
				break;
			default:
				graphics.DrawLine(pen, num, num6, num3, num6);
				graphics.DrawLine(pen, num5, num2, num5, num4);
				break;
			}
		}
	}
}
