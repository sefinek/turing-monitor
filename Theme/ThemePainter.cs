using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using TuringMonitor.Display;

namespace TuringMonitor.Theme;

public sealed class ThemePainter : IDisposable
{
	private readonly Dictionary<(int X, int Y), (int Fill, int Color)> _barSig = new();
	private readonly Dictionary<(int X, int Y), (Bitmap Bitmap, Graphics Graphics)> _barSurfaces = new();

	private readonly Graphics _canvasGraphics;
	private readonly object _canvasLock = new();
	private readonly FontCache _fonts = new();
	private readonly Dictionary<string, Bitmap> _images = new();
	private readonly Dictionary<(int X, int Y), Rectangle> _lastTextRect = new();
	private readonly Dictionary<(int X, int Y), (int Sweep, string Text, int Color)> _radialSig = new();
	private readonly Dictionary<(int X, int Y), (Bitmap Bitmap, Graphics Graphics)> _radialSurfaces = new();
	private readonly TuringScreenRevA? _screen;
	private readonly Dictionary<(int X, int Y), (string Text, int Fg, int Bg, int W, int H, string? BgImage)> _textSig = new();
	private readonly Dictionary<(int X, int Y), (Bitmap Bitmap, Graphics Graphics)> _textSurfaces = new();

	public ThemePainter(TuringScreenRevA? screen, int width, int height)
	{
		_screen = screen;
		Canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		_canvasGraphics = Graphics.FromImage(Canvas);
	}

	public Bitmap Canvas { get; }

	public void Dispose()
	{
		_fonts.Dispose();
		foreach (Bitmap image in _images.Values)
			image.Dispose();
		_images.Clear();
		DisposeSurfaces(_textSurfaces);
		DisposeSurfaces(_radialSurfaces);
		DisposeSurfaces(_barSurfaces);
		_canvasGraphics.Dispose();
		Canvas.Dispose();
	}

	private static void DisposeSurfaces(Dictionary<(int X, int Y), (Bitmap Bitmap, Graphics Graphics)> surfaces)
	{
		foreach ((Bitmap bitmap, Graphics graphics) in surfaces.Values)
		{
			graphics.Dispose();
			bitmap.Dispose();
		}

		surfaces.Clear();
	}

	private static (Bitmap Bitmap, Graphics Graphics) RentSurface(
		Dictionary<(int X, int Y), (Bitmap Bitmap, Graphics Graphics)> surfaces,
		(int X, int Y) key, int width, int height)
	{
		if (surfaces.TryGetValue(key, out (Bitmap Bitmap, Graphics Graphics) existing))
		{
			if (existing.Bitmap.Width == width && existing.Bitmap.Height == height)
				return existing;

			existing.Graphics.Dispose();
			existing.Bitmap.Dispose();
		}

		var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		var surface = (bitmap, Graphics.FromImage(bitmap));
		surfaces[key] = surface;
		return surface;
	}

	public void DisplayImage(string path, int x, int y, int width, int height)
	{
		Bitmap source = LoadImage(path);
		Bitmap bitmap = source;
		var owns = false;

		if (width > 0 && height > 0 && (width != source.Width || height != source.Height))
		{
			bitmap = new Bitmap(source, width, height);
			owns = true;
		}

		try
		{
			Blit(bitmap, x, y);
		}
		finally
		{
			if (owns)
				bitmap.Dispose();
		}
	}

	public void DisplayText(string text, int x, int y, int width, int height, string fontPath, float fontSize,
		Color fontColor, Color backgroundColor, string? backgroundImage, string align, string anchor)
	{
		if (string.IsNullOrEmpty(text))
			return;

		(string text, int, int, int width, int height, string? backgroundImage) sig = (text, fontColor.ToArgb(), backgroundColor.ToArgb(), width, height, backgroundImage);
		if (_textSig.TryGetValue((x, y), out (string Text, int Fg, int Bg, int W, int H, string? BgImage) prevSig) && prevSig == sig)
			return;
		_textSig[(x, y)] = sig;

		Font font = _fonts.Get(fontPath, fontSize);

		using var path = new GraphicsPath();
		path.AddString(text, font.FontFamily, (int)font.Style, fontSize, new PointF(0, 0), StringFormat.GenericTypographic);
		RectangleF ink = path.GetBounds();

		var contentW = (int)Math.Ceiling(ink.Width);
		var contentH = (int)Math.Ceiling(ink.Height);
		var boxW = width > 0 ? width : contentW + 1;
		var boxH = height > 0 ? height : contentH + 1;
		if (boxW <= 0 || boxH <= 0)
			return;

		var ah = anchor.Length > 0 ? anchor[0] : 'l';
		var av = anchor.Length > 1 ? anchor[1] : 't';
		var left = x - (ah == 'm' ? boxW / 2 : ah == 'r' ? boxW : 0);
		var top = y - (av == 'm' ? boxH / 2 : av is 'b' or 's' or 'd' ? boxH : 0);

		var current = new Rectangle(left, top, boxW, boxH);
		Rectangle region = current;
		if (_lastTextRect.TryGetValue((x, y), out Rectangle previous))
			region = Rectangle.Union(current, previous);
		_lastTextRect[(x, y)] = current;

		var originX = Math.Max(0, region.X);
		var originY = Math.Max(0, region.Y);
		var canvasW = region.Right - originX;
		var canvasH = region.Bottom - originY;
		if (canvasW <= 0 || canvasH <= 0)
			return;

		var alignDx = align == "center" ? (boxW - contentW) / 2f
			: align == "right" ? boxW - contentW : 0f;

		(Bitmap bitmap, Graphics g) = RentSurface(_textSurfaces, (x, y), canvasW, canvasH);
		g.SmoothingMode = SmoothingMode.AntiAlias;

		if (backgroundImage is not null)
			DrawBackgroundRegionOpaque(g, backgroundImage, originX, originY, canvasW, canvasH);
		else
			g.Clear(backgroundColor);

		using (var transform = new Matrix())
		{
			transform.Translate(left - originX + alignDx - ink.X, top - originY - ink.Y);
			path.Transform(transform);
		}

		using (var brush = new SolidBrush(fontColor))
			g.FillPath(brush, path);

		Blit(bitmap, originX, originY);
	}

	public void DisplayRadialBar(int xc, int yc, int radius, int barWidth, double minValue, double maxValue,
		double value, double angleStart, double angleEnd, bool clockwise, Color barColor, Color barBackgroundColor,
		bool drawBarBackground, string? text, string fontPath, float fontSize, Color fontColor,
		Color backgroundColor, string? backgroundImage)
	{
		if (radius <= 0 || barWidth <= 0)
			return;

		var diameter = radius * 2;
		value = Math.Clamp(value, minValue, maxValue);
		var fraction = maxValue > minValue ? (value - minValue) / (maxValue - minValue) : 0;
		var innerRadius = Math.Max(0, radius - barWidth);
		var span = AngularSpan(angleStart, angleEnd, clockwise);

		(int, string, int) radialSig = ((int)Math.Round(span * fraction * 10.0), text ?? string.Empty, barColor.ToArgb());
		if (_radialSig.TryGetValue((xc, yc), out (int Sweep, string Text, int Color) prevRadial) && prevRadial == radialSig)
			return;
		_radialSig[(xc, yc)] = radialSig;

		(Bitmap bitmap, Graphics g) = RentSurface(_radialSurfaces, (xc, yc), diameter, diameter);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

		if (backgroundImage is not null)
			DrawBackgroundRegionOpaque(g, backgroundImage, xc - radius, yc - radius, diameter, diameter);
		else
			g.Clear(backgroundColor);

		if (drawBarBackground)
			FillRing(g, diameter, radius, innerRadius, angleStart, span, clockwise, barBackgroundColor);

		FillRing(g, diameter, radius, innerRadius, angleStart, span * fraction, clockwise, barColor);

		if (!string.IsNullOrEmpty(text))
		{
			Font font = _fonts.Get(fontPath, fontSize);
			using var brush = new SolidBrush(fontColor);
			using var format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			g.DrawString(text, font, brush, new RectangleF(0, 0, diameter, diameter), format);
		}

		Blit(bitmap, xc - radius, yc - radius);
	}

	private static double AngularSpan(double angleStart, double angleEnd, bool clockwise)
	{
		var start = ((angleStart % 360) + 360) % 360;
		var end = ((angleEnd % 360) + 360) % 360;
		var span = clockwise
			? end <= start ? 360 - start + end : end - start
			: start <= end
				? 360 - end + start
				: start - end;
		return span <= 0 ? 360 : span;
	}

	private static void FillRing(Graphics g, int diameter, int outerRadius, int innerRadius,
		double startAngle, double sweep, bool clockwise, Color color)
	{
		if (sweep <= 0)
			return;
		if (sweep > 360)
			sweep = 360;

		var signedSweep = (float)(clockwise ? sweep : -sweep);
		var start = (float)startAngle;
		var outer = new RectangleF(0, 0, diameter - 1, diameter - 1);

		using var brush = new SolidBrush(color);

		if (innerRadius <= 0)
		{
			if (sweep >= 360)
				g.FillEllipse(brush, outer);
			else
				g.FillPie(brush, outer, start, signedSweep);
			return;
		}

		var inset = outerRadius - innerRadius;
		var inner = new RectangleF(inset, inset, diameter - 1 - 2 * inset, diameter - 1 - 2 * inset);

		using var path = new GraphicsPath();
		if (sweep >= 360)
		{
			path.AddEllipse(outer);
			path.AddEllipse(inner);
		}
		else
		{
			path.AddArc(outer, start, signedSweep);
			path.AddArc(inner, start + signedSweep, -signedSweep);
			path.CloseFigure();
		}

		g.FillPath(brush, path);
	}

	public void DisplayProgressBar(int x, int y, int width, int height, double minValue, double maxValue,
		double value, Color barColor, bool barOutline, Color backgroundColor, string? backgroundImage)
	{
		if (width <= 0 || height <= 0)
			return;

		value = Math.Clamp(value, minValue, maxValue);
		var fraction = maxValue > minValue ? (value - minValue) / (maxValue - minValue) : 0;

		var fill = width > height ? (int)(fraction * width) - 1 : (int)(fraction * height) - 1;
		(int fill, int) barSig = (fill, barColor.ToArgb());
		if (_barSig.TryGetValue((x, y), out (int Fill, int Color) prevBar) && prevBar == barSig)
			return;
		_barSig[(x, y)] = barSig;

		(Bitmap bitmap, Graphics g) = RentSurface(_barSurfaces, (x, y), width, height);

		if (backgroundImage is not null)
			DrawBackgroundRegionOpaque(g, backgroundImage, x, y, width, height);
		else
			g.Clear(backgroundColor);

		using (var brush = new SolidBrush(barColor))
		{
			if (width > height)
			{
				if (fill > 0)
					g.FillRectangle(brush, 0, 0, fill, height - 1);
			}
			else
			{
				if (fill > 0)
					g.FillRectangle(brush, 0, height - 1 - fill, width - 1, fill);
			}
		}

		if (barOutline)
		{
			using var pen = new Pen(barColor);
			g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
		}

		Blit(bitmap, x, y);
	}

	private void Blit(Bitmap bitmap, int x, int y)
	{
		lock (_canvasLock)
		{
			_canvasGraphics.DrawImage(bitmap, x, y, bitmap.Width, bitmap.Height);
		}

		_screen?.DisplayBitmap(bitmap, x, y);
	}

	private void DrawBackgroundRegionOpaque(Graphics g, string backgroundImage, int x, int y, int width, int height)
	{
		Bitmap background = LoadImage(backgroundImage);
		var source = new Rectangle(x, y, width, height);
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImage(background, new Rectangle(0, 0, width, height), source, GraphicsUnit.Pixel);
		g.CompositingMode = CompositingMode.SourceOver;
	}

	private Bitmap LoadImage(string path)
	{
		if (_images.TryGetValue(path, out Bitmap? image))
			return image;

		image = new Bitmap(path);
		_images[path] = image;
		return image;
	}
}
