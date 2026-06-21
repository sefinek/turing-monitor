using System.Drawing;
using System.Globalization;
using System.IO;
using TuringMonitor.Display;
using TuringMonitor.Sensors;

namespace TuringMonitor.Theme;

public sealed class ThemeEngine
{
	private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
	private static readonly string[] NetInterfaces = { "ETH", "WLO" };

	private readonly ThemeConfig _config;
	private readonly ThemePainter _painter;

	public ThemeEngine(ThemeConfig config, ThemePainter painter)
	{
		_config = config;
		_painter = painter;
	}

	public string NetUnits { get; set; } = "bytes";

	public static (int Width, int Height) GetSize(ThemeConfig config)
	{
		return SizeFromString(config.Display.GetString("DISPLAY_SIZE"));
	}

	public static (int Width, int Height) SizeFromString(string size)
	{
		return size switch
		{
			"0.96\"" => (80, 160),
			"2.1\"" => (480, 480),
			"2.8\"" => (480, 480),
			"3.5\"" => (320, 480),
			"4.6\"" => (320, 960),
			"5\"" => (480, 800),
			"5.2\"" => (720, 1280),
			"8\"" => (800, 1280),
			"8.8\"" => (480, 1920),
			"9.2\"" => (480, 1920),
			"12.3\"" => (720, 1920),
			_ => (320, 480)
		};
	}

	public static Orientation GetOrientation(ThemeConfig config)
	{
		return config.Display.GetString("DISPLAY_ORIENTATION", "portrait") == "landscape"
			? Orientation.Landscape
			: Orientation.Portrait;
	}

	public static (int Width, int Height) GetCanvasSize(ThemeConfig config)
	{
		var (baseW, baseH) = GetSize(config);
		return GetOrientation(config) is Orientation.Landscape or Orientation.ReverseLandscape
			? (baseH, baseW)
			: (baseW, baseH);
	}

	public void RenderStatic()
	{
		foreach ((var _, ThemeNode node) in _config.StaticImages.Children())
		{
			var path = _config.ResolvePath(node.GetString("PATH"));
			if (File.Exists(path))
				_painter.DisplayImage(path, node.GetInt("X"), node.GetInt("Y"), node.GetInt("WIDTH"), node.GetInt("HEIGHT"));
		}

		foreach ((var _, ThemeNode node) in _config.StaticText.Children())
			DrawText(node, node.GetString("TEXT"), string.Empty, false);
	}

	public void Update(SystemStats s)
	{
		ThemeNode stats = _config.Stats;

		ThemeNode cpu = stats["CPU"];
		DrawText(cpu["PERCENTAGE"]["TEXT"], F0(s.CpuLoadPercent), "%", true);
		DrawRadial(cpu["PERCENTAGE"]["RADIAL"], s.CpuLoadPercent, "%");
		DrawGraph(cpu["PERCENTAGE"]["GRAPH"], s.CpuLoadPercent);
		DrawText(cpu["FREQUENCY"]["TEXT"], F2(s.CpuClockMhz / 1000.0), " GHz", true);

		if (s.CpuTempAvailable)
		{
			DrawText(cpu["TEMPERATURE"]["TEXT"], F0(s.CpuTempC), "°C", true);
			DrawRadial(cpu["TEMPERATURE"]["RADIAL"], s.CpuTempC, "°C");
			DrawGraph(cpu["TEMPERATURE"]["GRAPH"], s.CpuTempC);
		}

		ThemeNode gpu = stats["GPU"];
		if (s.GpuAvailable)
		{
			DrawText(gpu["PERCENTAGE"]["TEXT"], F0(s.GpuLoadPercent), "%", true);
			DrawRadial(gpu["PERCENTAGE"]["RADIAL"], s.GpuLoadPercent, "%");
			DrawGraph(gpu["PERCENTAGE"]["GRAPH"], s.GpuLoadPercent);
			DrawText(gpu["MEMORY"]["TEXT"], F0(s.GpuMemUsedPercent), "%", true);
			DrawGraph(gpu["MEMORY"]["GRAPH"], s.GpuMemUsedPercent);
			DrawText(gpu["TEMPERATURE"]["TEXT"], F0(s.GpuTempC), "°C", true);
		}

		ThemeNode mem = stats["MEMORY"]["VIRTUAL"];
		DrawGraph(mem["GRAPH"], s.RamUsedPercent);
		DrawText(mem["PERCENT_TEXT"], F0(s.RamUsedPercent), "%", true);
		DrawText(mem["USED"], F1(s.RamUsedGb), " GB", true);
		DrawText(mem["FREE"], F1(Math.Max(0, s.RamTotalGb - s.RamUsedGb)), " GB", true);
		DrawText(mem["TOTAL"], F1(s.RamTotalGb), " GB", true);

		ThemeNode disk = stats["DISK"];
		DrawGraph(disk["USED"]["GRAPH"], s.DiskUsedPercent);
		DrawText(disk["USED"]["TEXT"], F0(s.DiskUsedGb), " GB", true);
		DrawText(disk["USED"]["PERCENT_TEXT"], F0(s.DiskUsedPercent), "%", true);
		DrawText(disk["TOTAL"]["TEXT"], F0(s.DiskTotalGb), " GB", true);
		DrawText(disk["FREE"]["TEXT"], F0(Math.Max(0, s.DiskTotalGb - s.DiskUsedGb)), " GB", true);

		ThemeNode net = stats["NET"];
		foreach (var ifaceName in NetInterfaces)
		{
			ThemeNode iface = net[ifaceName];
			DrawText(iface["UPLOAD"]["TEXT"], Rate(s.NetUpKbps), string.Empty, true);
			DrawText(iface["DOWNLOAD"]["TEXT"], Rate(s.NetDownKbps), string.Empty, true);
		}

		ThemeNode date = stats["DATE"];
		DateTime now = DateTime.Now;
		DrawText(date["DAY"]["TEXT"], now.ToString("ddd dd", Culture), string.Empty, true);
		DrawText(date["HOUR"]["TEXT"], now.ToString("HH:mm:ss", Culture), string.Empty, true);
	}

	private void DrawText(ThemeNode node, string value, string unit, bool requireShow)
	{
		if (!node.Exists)
			return;
		if (requireShow && !node.GetBool("SHOW"))
			return;

		var text = node.GetBool("SHOW_UNIT") ? value + unit : value;

		var fontName = node.GetString("FONT", "roboto-mono/RobotoMono-Regular.ttf");
		var fontPath = Path.Combine(ResourceLocator.FontsDir, fontName);

		_painter.DisplayText(
			text,
			node.GetInt("X"),
			node.GetInt("Y"),
			node.GetInt("WIDTH"),
			node.GetInt("HEIGHT"),
			fontPath,
			node.GetInt("FONT_SIZE", 10),
			node.GetColor("FONT_COLOR", Color.Black),
			node.GetColor("BACKGROUND_COLOR", Color.White),
			BackgroundImage(node),
			node.GetString("ALIGN", "left"),
			node.GetString("ANCHOR", "lt"));
	}

	private void DrawGraph(ThemeNode node, double value)
	{
		if (!node.Exists || !node.GetBool("SHOW"))
			return;

		_painter.DisplayProgressBar(
			node.GetInt("X"),
			node.GetInt("Y"),
			node.GetInt("WIDTH"),
			node.GetInt("HEIGHT"),
			node.GetInt("MIN_VALUE"),
			node.GetInt("MAX_VALUE", 100),
			value,
			node.GetColor("BAR_COLOR", Color.Black),
			node.GetBool("BAR_OUTLINE"),
			node.GetColor("BACKGROUND_COLOR", Color.White),
			BackgroundImage(node));
	}

	private void DrawRadial(ThemeNode node, double value, string unit)
	{
		if (!node.Exists || !node.GetBool("SHOW"))
			return;

		var text = string.Empty;
		if (node.GetBool("SHOW_TEXT"))
		{
			var number = ((int)Math.Round(value)).ToString(Culture).PadLeft(3);
			text = node.GetBool("SHOW_UNIT", true) ? number + unit : number;
		}

		var fontName = node.GetString("FONT", "roboto-mono/RobotoMono-Regular.ttf");
		var fontPath = Path.Combine(ResourceLocator.FontsDir, fontName);

		_painter.DisplayRadialBar(
			node.GetInt("X"),
			node.GetInt("Y"),
			node.GetInt("RADIUS", 1),
			node.GetInt("WIDTH", 1),
			node.GetInt("MIN_VALUE"),
			node.GetInt("MAX_VALUE", 100),
			value,
			node.GetInt("ANGLE_START"),
			node.GetInt("ANGLE_END", 360),
			node.GetBool("CLOCKWISE"),
			node.GetColor("BAR_COLOR", Color.Black),
			node.GetColor("BAR_BACKGROUND_COLOR", Color.Black),
			node.GetBool("DRAW_BAR_BACKGROUND"),
			text,
			fontPath,
			node.GetInt("FONT_SIZE", 10),
			node.GetColor("FONT_COLOR", Color.Black),
			node.GetColor("BACKGROUND_COLOR", Color.Black),
			BackgroundImage(node));
	}

	private string? BackgroundImage(ThemeNode node)
	{
		var name = node.GetString("BACKGROUND_IMAGE");
		return string.IsNullOrEmpty(name) ? null : _config.ResolvePath(name);
	}

	private static string F0(double v)
	{
		return v.ToString("0", Culture);
	}

	private static string F1(double v)
	{
		return v.ToString("0.0", Culture);
	}

	private static string F2(double v)
	{
		return v.ToString("0.00", Culture);
	}

	private string Rate(double kbps)
	{
		if (NetUnits == "bits")
		{
			var kbit = kbps * 8.0;
			return kbit >= 1000 ? $"{kbit / 1000.0:0.0} Mbit/s" : $"{kbit:0} Kbit/s";
		}

		return kbps >= 1024 ? $"{kbps / 1024.0:0.0} MB/s" : $"{kbps:0} KB/s";
	}
}
