using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController;

/// <summary>只读滚动文本块：圆角浅底 + 跟随主题的细滚动条，用于展示更新说明。</summary>
internal sealed class ScrollTextView : Control
{
	private const int PaddingSize = 10;

	private const int ScrollBarWidth = 4;

	private static readonly StringFormat TextFormat = CreateFormat();

	private string _content = string.Empty;

	private int _scroll;

	private int _contentHeight;

	private int _lineHeight = 18;

	private readonly System.Collections.Generic.List<Paragraph> _paragraphs = new System.Collections.Generic.List<Paragraph>();

	private sealed class Paragraph
	{
		public string Text;

		public float Height;
	}

	public ScrollTextView()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, value: true);
		DoubleBuffered = true;
		TabStop = true;
		Font = Theme.Font(8.8f, FontStyle.Regular);
		ForeColor = Theme.Text;
		BackColor = Theme.SurfaceAlt;
	}

	public override string Text
	{
		get
		{
			return _content;
		}
		set
		{
			_content = value ?? string.Empty;
			_scroll = 0;
			Recalculate();
			Invalidate();
		}
	}

	public override Font Font
	{
		get
		{
			return base.Font;
		}
		set
		{
			base.Font = value;
			Recalculate();
		}
	}

	/// <summary>内容是否超出可视范围（用于提示可以滚动）。</summary>
	public bool Overflowed => _contentHeight > ClipHeight;

	/// <summary>可视区高度（不含内边距）。</summary>
	private int ViewHeight => Math.Max(1, base.ClientSize.Height - PaddingSize * 2);

	/// <summary>可视区高度；只按完整段落绘制，不会出现半行字。</summary>
	private int ClipHeight => ViewHeight;

	private int TextWidth => Math.Max(20, base.ClientSize.Width - PaddingSize * 2 - (Overflowed ? ScrollBarWidth + 6 : 0));

	private int MaxScroll => Math.Max(0, _contentHeight - ClipHeight);

	private static StringFormat CreateFormat()
	{
		StringFormat format = new StringFormat(StringFormat.GenericDefault);
		format.Trimming = StringTrimming.None;
		format.FormatFlags = StringFormatFlags.LineLimit;
		return format;
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		Recalculate();
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		ScrollBy(-e.Delta / 120 * Math.Max(24, _lineHeight));
	}

	protected override bool IsInputKey(Keys keyData)
	{
		if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.PageUp || keyData == Keys.PageDown)
		{
			return true;
		}
		return base.IsInputKey(keyData);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		switch (e.KeyCode)
		{
		case Keys.Up:
			ScrollBy(-_lineHeight);
			e.Handled = true;
			break;
		case Keys.Down:
			ScrollBy(_lineHeight);
			e.Handled = true;
			break;
		case Keys.PageUp:
			ScrollBy(-Math.Max(_lineHeight, ClipHeight - _lineHeight));
			e.Handled = true;
			break;
		case Keys.PageDown:
			ScrollBy(Math.Max(_lineHeight, ClipHeight - _lineHeight));
			e.Handled = true;
			break;
		case Keys.Home:
			_scroll = 0;
			Invalidate();
			e.Handled = true;
			break;
		case Keys.End:
			_scroll = MaxScroll;
			Invalidate();
			e.Handled = true;
			break;
		}
	}

	private void ScrollBy(int delta)
	{
		int target = Math.Max(0, Math.Min(MaxScroll, _scroll + delta));
		if (target != _scroll)
		{
			_scroll = target;
			Invalidate();
		}
	}

	private void Recalculate()
	{
		if (string.IsNullOrEmpty(_content) || base.ClientSize.Width <= 0)
		{
			_contentHeight = 0;
			return;
		}
		using Graphics graphics = CreateGraphics();
		int height = (int)Math.Ceiling(Font.GetHeight(graphics));
		if (height >= 4)
		{
			_lineHeight = height;
		}
		_paragraphs.Clear();
		float total = 0f;
		foreach (string raw in _content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
		{
			string text = raw.Length == 0 ? " " : raw;
			float blockHeight = (float)Math.Ceiling(graphics.MeasureString(text, Font, new SizeF(TextWidth, 100000f), TextFormat).Height);
			_paragraphs.Add(new Paragraph
			{
				Text = text,
				Height = blockHeight
			});
			total += blockHeight;
		}
		_contentHeight = (int)Math.Ceiling(total);
		if (_scroll > MaxScroll)
		{
			_scroll = MaxScroll;
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((base.Parent != null) ? base.Parent.BackColor : Theme.Surface);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF bounds = new RectangleF(0.5f, 0.5f, base.ClientSize.Width - 1f, base.ClientSize.Height - 1f);
		using (GraphicsPath path = Theme.RoundedRectangle(bounds, 8f))
		{
			using SolidBrush brush = new SolidBrush(BackColor);
			graphics.FillPath(brush, path);
			using Pen pen = new Pen(Theme.Border);
			graphics.DrawPath(pen, path);
		}
		if (string.IsNullOrEmpty(_content))
		{
			return;
		}
		Rectangle clip = new Rectangle(PaddingSize, PaddingSize, TextWidth, ClipHeight);
		GraphicsState state = graphics.Save();
		graphics.SetClip(clip);
		graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
		float y = PaddingSize - _scroll;
		using (SolidBrush textBrush = new SolidBrush(ForeColor))
		{
			foreach (Paragraph paragraph in _paragraphs)
			{
				if (y >= clip.Bottom)
				{
					break;
				}
				if (y + paragraph.Height + 1f > clip.Bottom)
				{
					break;
				}
				graphics.DrawString(paragraph.Text, Font, textBrush, new RectangleF(PaddingSize, y, TextWidth, paragraph.Height + 1f), TextFormat);
				y += paragraph.Height;
			}
		}
		graphics.Restore(state);
		if (Overflowed)
		{
			DrawScrollBar(graphics);
		}
	}

	private void DrawScrollBar(Graphics graphics)
	{
		int trackX = base.ClientSize.Width - PaddingSize - ScrollBarWidth + 1;
		int trackTop = PaddingSize;
		int trackHeight = Math.Max(10, base.ClientSize.Height - PaddingSize * 2);
		float thumbHeight = Math.Max(18f, (float)trackHeight * ClipHeight / Math.Max(1, _contentHeight));
		float travel = trackHeight - thumbHeight;
		float thumbTop = trackTop + travel * _scroll / Math.Max(1, MaxScroll);
		RectangleF track = new RectangleF(trackX, trackTop, ScrollBarWidth, trackHeight);
		using (GraphicsPath trackPath = Theme.RoundedRectangle(track, ScrollBarWidth / 2f))
		{
			using SolidBrush trackBrush = new SolidBrush(Theme.Border);
			graphics.FillPath(trackBrush, trackPath);
		}
		RectangleF thumb = new RectangleF(trackX, thumbTop, ScrollBarWidth, thumbHeight);
		using GraphicsPath thumbPath = Theme.RoundedRectangle(thumb, ScrollBarWidth / 2f);
		using SolidBrush thumbBrush = new SolidBrush(Theme.Muted);
		graphics.FillPath(thumbBrush, thumbPath);
	}
}
