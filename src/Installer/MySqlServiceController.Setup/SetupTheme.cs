using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MySqlServiceController.Setup;

internal enum SetupThemeMode
{
	System,
	Light,
	Dark
}

/// <summary>
/// 安装器调色板，取值与主程序的 ThemePalette 保持一致，保证安装界面和主界面同风格。
/// </summary>
internal sealed class SetupPalette
{
	public bool IsDark { get; }

	public Color Background { get; }

	public Color Surface { get; }

	public Color SurfaceAlt { get; }

	public Color Input { get; }

	public Color Text { get; }

	public Color Muted { get; }

	public Color Subtle { get; }

	public Color Border { get; }

	public Color Accent { get; }

	public Color AccentDark { get; }

	public Color AccentSoft { get; }

	public Color Success { get; }

	public Color SuccessSoft { get; }

	public Color Warning { get; }

	public Color WarningSoft { get; }

	public Color Danger { get; }

	public Color DangerSoft { get; }

	public Color Neutral { get; }

	public Color NeutralSoft { get; }

	public SetupPalette(bool isDark, Color background, Color surface, Color surfaceAlt, Color input, Color text, Color muted, Color subtle, Color border, Color accent, Color accentDark, Color accentSoft, Color success, Color successSoft, Color warning, Color warningSoft, Color danger, Color dangerSoft, Color neutral, Color neutralSoft)
	{
		IsDark = isDark;
		Background = background;
		Surface = surface;
		SurfaceAlt = surfaceAlt;
		Input = input;
		Text = text;
		Muted = muted;
		Subtle = subtle;
		Border = border;
		Accent = accent;
		AccentDark = accentDark;
		AccentSoft = accentSoft;
		Success = success;
		SuccessSoft = successSoft;
		Warning = warning;
		WarningSoft = warningSoft;
		Danger = danger;
		DangerSoft = dangerSoft;
		Neutral = neutral;
		NeutralSoft = neutralSoft;
	}

	public static SetupPalette Light { get; } = new SetupPalette(isDark: false, Color.FromArgb(242, 242, 247), Color.White, Color.FromArgb(246, 246, 248), Color.FromArgb(255, 255, 255), Color.FromArgb(29, 29, 31), Color.FromArgb(110, 110, 115), Color.FromArgb(134, 134, 139), Color.FromArgb(229, 229, 234), Color.FromArgb(0, 122, 255), Color.FromArgb(0, 102, 204), Color.FromArgb(232, 242, 255), Color.FromArgb(52, 199, 89), Color.FromArgb(232, 248, 236), Color.FromArgb(255, 159, 10), Color.FromArgb(255, 244, 229), Color.FromArgb(255, 59, 48), Color.FromArgb(255, 235, 234), Color.FromArgb(142, 142, 147), Color.FromArgb(242, 242, 247));

	public static SetupPalette Dark { get; } = new SetupPalette(isDark: true, Color.FromArgb(28, 28, 30), Color.FromArgb(44, 44, 46), Color.FromArgb(58, 58, 60), Color.FromArgb(36, 36, 38), Color.FromArgb(245, 245, 247), Color.FromArgb(161, 161, 166), Color.FromArgb(142, 142, 147), Color.FromArgb(58, 58, 60), Color.FromArgb(10, 132, 255), Color.FromArgb(64, 156, 255), Color.FromArgb(26, 43, 69), Color.FromArgb(48, 209, 88), Color.FromArgb(19, 48, 28), Color.FromArgb(255, 214, 10), Color.FromArgb(51, 42, 14), Color.FromArgb(255, 69, 58), Color.FromArgb(58, 26, 24), Color.FromArgb(152, 152, 157), Color.FromArgb(44, 44, 46));
}

internal static class SetupTheme
{
	private static readonly Dictionary<long, Font> FontCache = new Dictionary<long, Font>();

	private static SetupPalette _current = SetupPalette.Light;

	public static SetupThemeMode Mode { get; private set; } = SetupThemeMode.System;

	public static event EventHandler Changed;

	public static SetupPalette Palette => _current;

	public static bool IsDark => _current.IsDark;

	public static Color Background => _current.Background;

	public static Color Surface => _current.Surface;

	public static Color SurfaceAlt => _current.SurfaceAlt;

	public static Color Input => _current.Input;

	public static Color Text => _current.Text;

	public static Color Muted => _current.Muted;

	public static Color Subtle => _current.Subtle;

	public static Color Border => _current.Border;

	public static Color Accent => _current.Accent;

	public static Color AccentDark => _current.AccentDark;

	public static Color AccentSoft => _current.AccentSoft;

	public static Color Success => _current.Success;

	public static Color SuccessSoft => _current.SuccessSoft;

	public static Color Warning => _current.Warning;

	public static Color WarningSoft => _current.WarningSoft;

	public static Color Danger => _current.Danger;

	public static Color DangerSoft => _current.DangerSoft;

	public static Color Neutral => _current.Neutral;

	public static Color NeutralSoft => _current.NeutralSoft;

	public static void SetMode(SetupThemeMode mode)
	{
		Mode = mode;
		SetupPalette palette = ((mode == SetupThemeMode.Dark || (mode == SetupThemeMode.System && SystemUsesDarkTheme())) ? SetupPalette.Dark : SetupPalette.Light);
		if (!ReferenceEquals(palette, _current))
		{
			_current = palette;
			DarkMode.Apply(_current.IsDark);
			Changed?.Invoke(null, EventArgs.Empty);
		}
	}

	public static bool SystemUsesDarkTheme()
	{
		try
		{
			object value = Microsoft.Win32.Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 1);
			if (value is int)
			{
				return (int)value == 0;
			}
		}
		catch
		{
		}
		return false;
	}

	public static Color Mix(Color first, Color second, double amount)
	{
		amount = Math.Max(0.0, Math.Min(1.0, amount));
		return Color.FromArgb((int)Math.Round((double)first.R + (double)(second.R - first.R) * amount), (int)Math.Round((double)first.G + (double)(second.G - first.G) * amount), (int)Math.Round((double)first.B + (double)(second.B - first.B) * amount));
	}

	public static Font Font(float size, FontStyle style)
	{
		long key = ((long)Math.Round(size * 100.0) << 16) | (uint)(int)style;
		lock (FontCache)
		{
			if (!FontCache.TryGetValue(key, out Font font))
			{
				font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
				FontCache[key] = font;
			}
			return font;
		}
	}

	public static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (radius <= 0f || rectangle.Width <= 0f || rectangle.Height <= 0f)
		{
			graphicsPath.AddRectangle(rectangle);
			return graphicsPath;
		}
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
