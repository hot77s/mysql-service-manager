using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MySqlServiceController.Setup;

internal static class SetupTheme
{
	public static readonly Color Background = Color.FromArgb(245, 247, 251);

	public static readonly Color Surface = Color.White;

	public static readonly Color Text = Color.FromArgb(23, 32, 51);

	public static readonly Color Muted = Color.FromArgb(102, 112, 133);

	public static readonly Color Subtle = Color.FromArgb(145, 156, 173);

	public static readonly Color Border = Color.FromArgb(229, 233, 240);

	public static readonly Color Accent = Color.FromArgb(36, 107, 253);

	public static readonly Color AccentDark = Color.FromArgb(24, 85, 214);

	public static readonly Color Danger = Color.FromArgb(214, 69, 69);

	public static Font Font(float size, FontStyle style)
	{
		return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
	}

	public static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = Math.Min(Math.Min(radius * 2f, rectangle.Width), rectangle.Height);
		RectangleF rect = new RectangleF(rectangle.X, rectangle.Y, num, num);
		graphicsPath.AddArc(rect, 180f, 90f);
		rect.X = rectangle.Right - num;
		graphicsPath.AddArc(rect, 270f, 90f);
		rect.Y = rectangle.Bottom - num;
		graphicsPath.AddArc(rect, 0f, 90f);
		rect.X = rectangle.Left;
		graphicsPath.AddArc(rect, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
