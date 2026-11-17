using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController.Setup;

internal sealed class HeaderPanel : Panel
{
	public HeaderPanel()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		using (LinearGradientBrush brush = new LinearGradientBrush(base.ClientRectangle, Color.FromArgb(34, 112, 247), Color.FromArgb(23, 77, 196), 20f))
		{
			e.Graphics.FillRectangle(brush, base.ClientRectangle);
		}
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(24, 255, 255, 255));
		e.Graphics.FillEllipse(brush2, base.ClientSize.Width - 190, -100, 300, 300);
	}
}
