using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MySqlServiceController;

internal sealed class PreflightListView : Panel
{
	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	private List<PreflightItem> _items = new List<PreflightItem>();

	private string _placeholder = "正在检查…";

	private int[] _cardHeights = Array.Empty<int>();

	private int _contentHeight;

	private int _scrollOffset;

	private bool _draggingThumb;

	private int _dragStartY;

	private int _dragStartOffset;

	private bool _thumbHover;

	private static readonly TextFormatFlags WrapFlags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

	private float DpiScale
	{
		get
		{
			int num = 96;
			try
			{
				if (base.IsHandleCreated)
				{
					uint dpi = GetDpiForWindow(base.Handle);
					if (dpi >= 48 && dpi <= 480)
					{
						num = (int)dpi;
					}
				}
			}
			catch
			{
			}
			return (float)num / 96f;
		}
	}

	private int S(int value)
	{
		return (int)Math.Round((double)value * (double)DpiScale);
	}

	public PreflightListView()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, value: true);
		TabStop = true;
		BackColor = Theme.Background;
	}

	public void SetItems(List<PreflightItem> items)
	{
		_items = items ?? new List<PreflightItem>();
		_scrollOffset = 0;
		Recalculate();
		Invalidate();
	}

	public void SetPlaceholder(string text)
	{
		_placeholder = text ?? string.Empty;
		Recalculate();
		Invalidate();
	}

	public void ScrollBy(int delta)
	{
		int num = Math.Max(0, _contentHeight - base.ClientSize.Height);
		int num2 = Math.Max(0, Math.Min(num, _scrollOffset + delta));
		if (num2 != _scrollOffset)
		{
			_scrollOffset = num2;
			Invalidate();
		}
	}

	protected override void OnResize(EventArgs eventargs)
	{
		base.OnResize(eventargs);
		Recalculate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		Color backColor = base.Parent?.BackColor ?? Theme.Background;
		using (SolidBrush brush = new SolidBrush(backColor))
		{
			e.Graphics.FillRectangle(brush, base.ClientRectangle);
		}
		if (_items.Count == 0)
		{
			using SolidBrush brush2 = new SolidBrush(Theme.Muted);
			using StringFormat stringFormat = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			e.Graphics.DrawString(_placeholder, Theme.Font(9.5f, FontStyle.Regular), brush2, new RectangleF(0f, 0f, base.ClientSize.Width, base.ClientSize.Height), stringFormat);
			return;
		}
		int cardWidth = GetCardWidth();
		int num = S(2);
		int num2 = -_scrollOffset;
		for (int i = 0; i < _items.Count; i++)
		{
			int num3 = _cardHeights[i];
			if (num2 + num3 >= 0 && num2 <= base.ClientSize.Height)
			{
				PaintCard(e.Graphics, _items[i], num, num2, cardWidth, num3);
			}
			num2 += num3 + S(10);
		}
		if (_contentHeight > base.ClientSize.Height)
		{
			int num4 = S(24);
			Rectangle rect = new Rectangle(0, base.ClientSize.Height - num4, base.ClientSize.Width, num4);
			using LinearGradientBrush brush = new LinearGradientBrush(rect, Color.FromArgb(0, Theme.Background), Theme.Background, LinearGradientMode.Vertical);
			e.Graphics.FillRectangle(brush, rect);
		}
		PaintScrollThumb(e.Graphics);
	}

	private void PaintCard(Graphics graphics, PreflightItem item, int x, int y, int width, int height)
	{
		Color color = GetSeverityColor(item.Severity);
		Color color2 = GetSeveritySoftColor(item.Severity);
		RectangleF rectangle = new RectangleF(x, y, width, height);
		using GraphicsPath graphicsPath = Theme.RoundedRectangle(rectangle, S(12));
		using (SolidBrush brush = new SolidBrush(Theme.Surface))
		{
			graphics.FillPath(brush, graphicsPath);
		}
		GraphicsState state = graphics.Save();
		try
		{
			graphics.SetClip(graphicsPath);
			using SolidBrush brush2 = new SolidBrush(color);
			graphics.FillRectangle(brush2, rectangle.X, rectangle.Y, S(3), rectangle.Height);
		}
		finally
		{
			graphics.Restore(state);
		}
		int textLeft = x + S(50);
		int rightLabelWidth = S(48);
		int textWidth = Math.Max(S(80), width - S(50) - S(14) - rightLabelWidth);
		Rectangle iconRect = new Rectangle(x + S(14), y + S(13), S(22), S(22));
		using (SolidBrush brush3 = new SolidBrush(color2))
		{
			graphics.FillEllipse(brush3, iconRect);
		}
		TextRenderer.DrawText(graphics, GetSeverityGlyph(item.Severity), Theme.Font(9f, FontStyle.Bold), iconRect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
		TextRenderer.DrawText(graphics, PreflightReport.GetSeverityLabel(item.Severity), Theme.Font(8.2f, FontStyle.Bold), new Rectangle(x + width - rightLabelWidth - S(4), y + S(14), rightLabelWidth, S(18)), color, TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
		int num = y + S(12);
		Size size = TextRenderer.MeasureText(item.Title, Theme.Font(10f, FontStyle.Bold), new Size(textWidth, int.MaxValue), WrapFlags);
		int num2 = Math.Max(S(20), size.Height + S(1));
		TextRenderer.DrawText(graphics, item.Title, Theme.Font(10f, FontStyle.Bold), new Rectangle(textLeft, num, textWidth, num2), Theme.Text, WrapFlags);
		num += num2 + S(5);
		if (!string.IsNullOrWhiteSpace(item.Detail))
		{
			Size size2 = TextRenderer.MeasureText(item.Detail, Theme.Font(8.8f, FontStyle.Regular), new Size(textWidth, int.MaxValue), WrapFlags);
			int num3 = Math.Max(S(16), size2.Height + S(1));
			TextRenderer.DrawText(graphics, item.Detail, Theme.Font(8.8f, FontStyle.Regular), new Rectangle(textLeft, num, textWidth, num3), Theme.Muted, WrapFlags);
			num += num3 + S(5);
		}
		if (!string.IsNullOrWhiteSpace(item.Advice))
		{
			Color color3 = ((item.Severity == PreflightSeverity.Error || item.Severity == PreflightSeverity.Warning) ? color : Theme.Muted);
			TextRenderer.DrawText(graphics, "建议：" + item.Advice, Theme.Font(8.6f, FontStyle.Regular), new Rectangle(textLeft, num, textWidth, S(18)), color3, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
		}
	}

	private void PaintScrollThumb(Graphics graphics)
	{
		Rectangle thumb = GetThumbRect();
		if (thumb.IsEmpty)
		{
			return;
		}
		using SolidBrush brush = new SolidBrush(Color.FromArgb(_thumbHover || _draggingThumb ? 170 : 110, Theme.Subtle));
		using GraphicsPath graphicsPath = Theme.RoundedRectangle(new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height), thumb.Width / 2f);
		graphics.FillPath(brush, graphicsPath);
	}

	private Rectangle GetThumbRect()
	{
		if (_contentHeight <= base.ClientSize.Height)
		{
			return Rectangle.Empty;
		}
		int num = Math.Max(1, _contentHeight - base.ClientSize.Height);
		int num2 = base.ClientSize.Height - S(8);
		int num3 = Math.Max(S(36), (int)((double)num2 * (double)base.ClientSize.Height / (double)_contentHeight));
		int num4 = S(4) + (int)((double)(num2 - num3) * (double)_scrollOffset / (double)num);
		return new Rectangle(base.ClientSize.Width - S(7), num4, S(4), num3);
	}

	private int GetCardWidth()
	{
		return Math.Max(S(160), base.ClientSize.Width - S(14));
	}

	private void Recalculate()
	{
		if (_items.Count == 0)
		{
			_cardHeights = Array.Empty<int>();
			_contentHeight = 0;
			_scrollOffset = 0;
			return;
		}
		int cardWidth = GetCardWidth();
		_cardHeights = new int[_items.Count];
		int num = 0;
		for (int i = 0; i < _items.Count; i++)
		{
			_cardHeights[i] = MeasureCard(_items[i], cardWidth);
			num += _cardHeights[i] + S(10);
		}
		_contentHeight = num;
		int num2 = Math.Max(0, _contentHeight - base.ClientSize.Height);
		if (_scrollOffset > num2)
		{
			_scrollOffset = num2;
		}
	}

	private int MeasureCard(PreflightItem item, int width)
	{
		int num = Math.Max(S(80), width - S(50) - S(14) - S(48));
		Size size = TextRenderer.MeasureText(item.Title, Theme.Font(10f, FontStyle.Bold), new Size(num, int.MaxValue), WrapFlags);
		int num2 = Math.Max(S(20), size.Height + S(1));
		int num3 = 0;
		if (!string.IsNullOrWhiteSpace(item.Detail))
		{
			Size size2 = TextRenderer.MeasureText(item.Detail, Theme.Font(8.8f, FontStyle.Regular), new Size(num, int.MaxValue), WrapFlags);
			num3 = Math.Max(S(16), size2.Height + S(1)) + S(5);
		}
		int num4 = 0;
		if (!string.IsNullOrWhiteSpace(item.Advice))
		{
			Size size3 = TextRenderer.MeasureText("建议：" + item.Advice, Theme.Font(8.6f, FontStyle.Regular), new Size(num, int.MaxValue), WrapFlags);
			num4 = Math.Max(S(16), size3.Height + S(1)) + S(5);
		}
		return S(12) + num2 + num3 + num4 + S(12);
	}

	private static Color GetSeverityColor(PreflightSeverity severity)
	{
		return severity switch
		{
			PreflightSeverity.Error => Theme.Danger, 
			PreflightSeverity.Warning => Theme.Warning, 
			PreflightSeverity.Ok => Theme.Success, 
			_ => Theme.Accent, 
		};
	}

	private static Color GetSeveritySoftColor(PreflightSeverity severity)
	{
		return severity switch
		{
			PreflightSeverity.Error => Theme.DangerSoft, 
			PreflightSeverity.Warning => Theme.WarningSoft, 
			PreflightSeverity.Ok => Theme.SuccessSoft, 
			_ => Theme.AccentSoft, 
		};
	}

	private static string GetSeverityGlyph(PreflightSeverity severity)
	{
		return severity switch
		{
			PreflightSeverity.Ok => "✓", 
			PreflightSeverity.Info => "i", 
			_ => "!", 
		};
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		Focus();
		Rectangle thumb = GetThumbRect();
		if (!thumb.IsEmpty && thumb.Contains(e.Location))
		{
			_draggingThumb = true;
			_dragStartY = e.Y;
			_dragStartOffset = _scrollOffset;
			Capture = true;
			Invalidate();
			return;
		}
		if (!thumb.IsEmpty && e.X >= thumb.X - S(6))
		{
			int num = Math.Max(0, _contentHeight - base.ClientSize.Height);
			int num2 = base.ClientSize.Height - S(8);
			int num3 = S(4) + thumb.Height / 2;
			int num4 = Math.Max(1, num2 - thumb.Height);
			int num5 = (int)((double)num * (double)Math.Max(0, Math.Min(num4, e.Y - num3)) / (double)num4);
			ScrollBy(num5 - _scrollOffset);
			return;
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		if (_draggingThumb)
		{
			Rectangle thumb = GetThumbRect();
			int num = Math.Max(1, _contentHeight - base.ClientSize.Height);
			int num2 = Math.Max(1, base.ClientSize.Height - S(8) - thumb.Height);
			int num3 = _dragStartOffset + (int)((double)(e.Y - _dragStartY) * (double)num / (double)num2);
			ScrollBy(num3 - _scrollOffset);
			return;
		}
		bool flag = !GetThumbRect().IsEmpty && GetThumbRect().Contains(e.Location);
		if (flag != _thumbHover)
		{
			_thumbHover = flag;
			Cursor = (flag ? Cursors.Hand : Cursors.Default);
			Invalidate();
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		if (_draggingThumb)
		{
			_draggingThumb = false;
			Capture = false;
			Invalidate();
			return;
		}
		base.OnMouseUp(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		if (_thumbHover && !_draggingThumb)
		{
			_thumbHover = false;
			Cursor = Cursors.Default;
			Invalidate();
		}
		base.OnMouseLeave(e);
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		ScrollBy(-e.Delta / 120 * S(64));
	}
}
