using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using TuringMonitor.Sensors;

namespace TuringMonitor.Rendering;

public sealed class DashboardRenderer : IDisposable
{
	private static readonly Color Background = Color.FromArgb(16, 18, 24);
	private static readonly Color CardColor = Color.FromArgb(26, 30, 40);
	private static readonly Color TrackColor = Color.FromArgb(44, 50, 64);
	private static readonly Color TextColor = Color.FromArgb(235, 238, 245);
	private static readonly Color MutedColor = Color.FromArgb(150, 158, 175);

	private static readonly Color CpuColor = Color.FromArgb(80, 170, 255);
	private static readonly Color RamColor = Color.FromArgb(140, 120, 255);
	private static readonly Color GpuColor = Color.FromArgb(110, 220, 140);
	private static readonly Color DiskColor = Color.FromArgb(255, 180, 80);
	private static readonly Color NetColor = Color.FromArgb(255, 110, 150);

	private static readonly (double Pos, Color Color)[] GradientStops =
	{
		(0.0, Color.FromArgb(95, 220, 130)),
		(0.5, Color.FromArgb(235, 215, 90)),
		(0.8, Color.FromArgb(245, 160, 70)),
		(1.0, Color.FromArgb(240, 85, 85))
	};

	private readonly Dictionary<Color, SolidBrush> _brushes = new();
	private readonly Font _dateFont = new("Segoe UI", 30f, FontStyle.Regular, GraphicsUnit.Pixel);
	private readonly Font _labelFont = new("Segoe UI Semibold", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
	private readonly GraphicsPath _path = new();

	private readonly Font _percentFont = new("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
	private readonly SolidBrush _segmentBrush = new(Color.White);
	private readonly Font _titleFont = new("Segoe UI", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
	private readonly Font _valueFont = new("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);

	private Bitmap? _bitmap;
	private string _cpuLabel = "CPU";

	private string _cpuName = "";
	private string _gpuLabel = "GPU";
	private string _gpuName = "";
	private Graphics? _graphics;
	private int _height;
	private int _width;

	public string TimeFormat { get; set; } = "HH:mm:ss";
	public string DateFormat { get; set; } = "ddd, dd MMM";
	public string Culture { get; set; } = "";
	public int NetLinkMbps { get; set; }
	public string NetUnits { get; set; } = "bytes";

	public void Dispose()
	{
		_graphics?.Dispose();
		_bitmap?.Dispose();
		_path.Dispose();
		_percentFont.Dispose();
		_titleFont.Dispose();
		_dateFont.Dispose();
		_labelFont.Dispose();
		_valueFont.Dispose();
		_segmentBrush.Dispose();
		foreach (SolidBrush brush in _brushes.Values)
			brush.Dispose();
		_brushes.Clear();
	}

	public Bitmap Render(SystemStats s, int width, int height)
	{
		Graphics g = EnsureCanvas(width, height);
		g.Clear(Background);

		DateTime now = s.Timestamp;
		DrawString(g, FormatTime(now), _titleFont, TextColor, 16, 8);
		DrawStringRight(g, FormatDate(now), _dateFont, MutedColor, width - 16, 8);

		const int top = 64;
		const int gap = 8;
		const int netCardH = 34;
		var cardW = width - 32;

		var barCardH = Math.Max(34, (height - top - gap - netCardH - 4 * gap) / 4);

		var y = top;
		var cpu = new List<(string, Color)> { ($"{FormatClock(s.CpuClockMhz)}", TextColor) };
		if (s.CpuTempAvailable)
			cpu.Add(($"{s.CpuTempC:0}°C", TempColor(s.CpuTempC)));
		DrawMetric(g, 16, y, cardW, barCardH, CpuLabel(s.CpuName), cpu, s.CpuLoadPercent, CpuColor);
		y += barCardH + gap;

		DrawMetric(g, 16, y, cardW, barCardH, "RAM",
			new[] { ($"{s.RamUsedGb:0.0}/{s.RamTotalGb:0.0} GB", TextColor) }, s.RamUsedPercent, RamColor);
		y += barCardH + gap;

		if (s.GpuAvailable)
			DrawMetric(g, 16, y, cardW, barCardH, GpuLabel(s.GpuName),
				new[] { ($"{s.GpuTempC:0}°C", TempColor(s.GpuTempC)) }, s.GpuLoadPercent, GpuColor);
		else
			DrawMetric(g, 16, y, cardW, barCardH, "GPU", new[] { ("no NVIDIA", TextColor) }, 0, GpuColor, false);
		y += barCardH + gap;

		var diskLabel = string.IsNullOrEmpty(s.DiskName) ? "DISK" : $"DISK {s.DiskName}";
		DrawMetric(g, 16, y, cardW, barCardH, diskLabel,
			new[] { ($"{s.DiskUsedGb:0}/{s.DiskTotalGb:0} GB", TextColor) }, s.DiskUsedPercent, DiskColor);
		y += barCardH + gap;

		var linkKBps = (NetLinkMbps > 0 ? NetLinkMbps : s.NetLinkMbps) * 125.0;
		DrawMetric(g, 16, y, cardW, netCardH, "NETWORK", new[]
		{
			($"↓ {FormatRate(s.NetDownKbps)}", RateColor(s.NetDownKbps, linkKBps)),
			($"↑ {FormatRate(s.NetUpKbps)}", RateColor(s.NetUpKbps, linkKBps))
		}, 0, NetColor, false);

		return _bitmap!;
	}

	private Graphics EnsureCanvas(int width, int height)
	{
		if (_bitmap is not null && _width == width && _height == height)
			return _graphics!;

		_graphics?.Dispose();
		_bitmap?.Dispose();

		_bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		_graphics = Graphics.FromImage(_bitmap);
		_graphics.SmoothingMode = SmoothingMode.AntiAlias;
		_graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
		_graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		_width = width;
		_height = height;
		return _graphics;
	}

	private void DrawMetric(Graphics g, int x, int y, int w, int h,
		string label, IReadOnlyList<(string Text, Color Color)> value, double percent, Color accent, bool drawBar = true)
	{
		FillRoundedRect(g, new Rectangle(x, y, w, h), 8, CardColor);

		const int pad = 14;

		if (!drawBar)
		{
			DrawString(g, label, _labelFont, accent, x + pad, y + (h - _labelFont.GetHeight()) / 2f);
			DrawSegmentsRight(g, value, _valueFont, x + w - pad, y + (h - _valueFont.GetHeight()) / 2f);
			return;
		}

		const int percentZone = 78;
		var infoRight = x + w - pad - percentZone;

		var percentText = $"{percent:0}%";
		_segmentBrush.Color = LoadColor(percent);
		var percentW = MeasureWidth(g, percentText, _percentFont);
		g.DrawString(percentText, _percentFont, _segmentBrush,
			x + w - pad - percentW, y + (h - _percentFont.GetHeight()) / 2f - 2, StringFormat.GenericTypographic);

		DrawString(g, label, _labelFont, accent, x + pad, y + 2);
		DrawSegmentsRight(g, value, _valueFont, infoRight, y + 4);

		var barX = x + pad;
		var barY = y + h - 15;
		var barW = infoRight - barX;
		const int barH = 10;

		if (barW < barH)
			return;

		FillRoundedRect(g, new Rectangle(barX, barY, barW, barH), 5, TrackColor);

		var fill = (int)Math.Round(barW * Math.Clamp(percent, 0, 100) / 100.0);
		if (fill > 0)
			FillRoundedRect(g, new Rectangle(barX, barY, Math.Max(fill, barH), barH), 5, accent);
	}

	private string FormatTime(DateTime now)
	{
		return SafeFormat(now, TimeFormat, "HH:mm:ss", ResolveCulture());
	}

	private string FormatDate(DateTime now)
	{
		return SafeFormat(now, DateFormat, "ddd, dd MMM", ResolveCulture());
	}

	private CultureInfo ResolveCulture()
	{
		if (string.IsNullOrWhiteSpace(Culture))
			return CultureInfo.CurrentCulture;
		try
		{
			return CultureInfo.GetCultureInfo(Culture);
		}
		catch (CultureNotFoundException)
		{
			return CultureInfo.CurrentCulture;
		}
	}

	private static string SafeFormat(DateTime now, string? format, string fallback, CultureInfo culture)
	{
		if (string.IsNullOrWhiteSpace(format))
			return now.ToString(fallback, culture);
		try
		{
			return now.ToString(format, culture);
		}
		catch (FormatException)
		{
			return now.ToString(fallback, culture);
		}
	}

	private static Color LoadColor(double percent)
	{
		return Gradient(percent / 100.0);
	}

	private static Color TempColor(double celsius)
	{
		return Gradient((celsius - 35.0) / 55.0);
	}

	private static Color RateColor(double kbps, double linkKBps)
	{
		return linkKBps > 0 ? Gradient(kbps / linkKBps) : TextColor;
	}

	private static Color Gradient(double t)
	{
		t = Math.Clamp(t, 0, 1);
		for (var i = 1; i < GradientStops.Length; i++)
		{
			if (t > GradientStops[i].Pos)
				continue;

			(var p0, Color c0) = GradientStops[i - 1];
			(var p1, Color c1) = GradientStops[i];
			return Lerp(c0, c1, (t - p0) / (p1 - p0));
		}

		return GradientStops[^1].Color;
	}

	private static Color Lerp(Color a, Color b, double f)
	{
		return Color.FromArgb(
			(int)(a.R + (b.R - a.R) * f),
			(int)(a.G + (b.G - a.G) * f),
			(int)(a.B + (b.B - a.B) * f));
	}

	private SolidBrush Brush(Color color)
	{
		if (!_brushes.TryGetValue(color, out SolidBrush? brush))
		{
			brush = new SolidBrush(color);
			_brushes[color] = brush;
		}

		return brush;
	}

	private void DrawString(Graphics g, string text, Font font, Color color, float x, float y)
	{
		g.DrawString(text, font, Brush(color), x, y);
	}

	private void DrawStringRight(Graphics g, string text, Font font, Color color, float right, float y)
	{
		SizeF size = g.MeasureString(text, font);
		g.DrawString(text, font, Brush(color), right - size.Width, y);
	}

	private void DrawSegmentsRight(Graphics g, IReadOnlyList<(string Text, Color Color)> segments, Font font, float right, float y)
	{
		const float gap = 12f;
		Span<float> widths = segments.Count <= 8 ? stackalloc float[segments.Count] : new float[segments.Count];
		var total = 0f;
		for (var i = 0; i < segments.Count; i++)
		{
			widths[i] = MeasureWidth(g, segments[i].Text, font);
			total += widths[i];
			if (i > 0)
				total += gap;
		}

		var cx = right - total;
		for (var i = 0; i < segments.Count; i++)
		{
			if (i > 0)
				cx += gap;
			_segmentBrush.Color = segments[i].Color;
			g.DrawString(segments[i].Text, font, _segmentBrush, cx, y, StringFormat.GenericTypographic);
			cx += widths[i];
		}
	}

	private static float MeasureWidth(Graphics g, string text, Font font)
	{
		return g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic).Width;
	}

	private void FillRoundedRect(Graphics g, Rectangle rect, int radius, Color color)
	{
		var d = radius * 2;
		_path.Reset();
		_path.AddArc(rect.X, rect.Y, d, d, 180, 90);
		_path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
		_path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
		_path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
		_path.CloseFigure();
		g.FillPath(Brush(color), _path);
	}

	private string CpuLabel(string name)
	{
		if (name != _cpuName)
		{
			_cpuName = name;
			_cpuLabel = CleanCpu(name);
		}

		return _cpuLabel;
	}

	private string GpuLabel(string name)
	{
		if (name != _gpuName)
		{
			_gpuName = name;
			_gpuLabel = CleanGpu(name);
		}

		return _gpuLabel;
	}

	private static string CleanCpu(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return "CPU";

		var s = name;
		var at = s.IndexOf('@');
		if (at >= 0)
			s = s[..at];
		var with = s.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
		if (with >= 0)
			s = s[..with];

		s = s.Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
			.Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
			.Replace(" CPU", "", StringComparison.OrdinalIgnoreCase)
			.Replace(" Processor", "", StringComparison.OrdinalIgnoreCase);

		s = CollapseSpaces(s);
		return s.Length == 0 ? "CPU" : s;
	}

	private static string CleanGpu(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return "GPU";

		var s = name
			.Replace("NVIDIA ", "", StringComparison.OrdinalIgnoreCase)
			.Replace(" GPU", "", StringComparison.OrdinalIgnoreCase);

		s = CollapseSpaces(s);
		return s.Length == 0 ? "GPU" : s;
	}

	private static string CollapseSpaces(string s)
	{
		return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
	}

	private static string FormatClock(double mhz)
	{
		return mhz <= 0 ? "— GHz" : $"{mhz / 1000.0:0.00} GHz";
	}

	private string FormatRate(double kbps)
	{
		if (NetUnits == "bits")
		{
			var kbit = kbps * 8.0;
			return kbit >= 1000 ? $"{kbit / 1000.0:0.0} Mbit/s" : $"{kbit:0} Kbit/s";
		}

		return kbps >= 1024 ? $"{kbps / 1024.0:0.0} MB/s" : $"{kbps:0} KB/s";
	}
}
