using System.Drawing;
using System.Globalization;
using System.IO;
using YamlDotNet.Serialization;

namespace TuringMonitor.Theme;

public sealed class ThemeNode
{
	private static readonly ThemeNode Empty = new(null);

	private readonly object? _value;

	public ThemeNode(object? value)
	{
		_value = value;
	}

	public bool Exists => _value is not null;

	private Dictionary<object, object>? Map => _value as Dictionary<object, object>;

	public ThemeNode this[string key]
	{
		get
		{
			if (Map is { } map && map.TryGetValue(key, out var child))
				return new ThemeNode(child);
			return Empty;
		}
	}

	public IEnumerable<KeyValuePair<string, ThemeNode>> Children()
	{
		if (Map is not { } map)
			yield break;

		foreach (KeyValuePair<object, object> pair in map)
			yield return new KeyValuePair<string, ThemeNode>(pair.Key?.ToString() ?? string.Empty, new ThemeNode(pair.Value));
	}

	public string GetString(string key, string fallback = "")
	{
		return this[key]._value?.ToString() ?? fallback;
	}

	public int GetInt(string key, int fallback = 0)
	{
		var raw = this[key]._value?.ToString();
		return int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
	}

	public bool GetBool(string key, bool fallback = false)
	{
		var raw = this[key]._value?.ToString();
		return bool.TryParse(raw, out var v) ? v : fallback;
	}

	public Color GetColor(string key, Color fallback)
	{
		return ColorParser.Parse(this[key]._value, fallback);
	}
}

public sealed class ThemeConfig
{

	private ThemeConfig(string path, string name, ThemeNode root)
	{
		Path = path;
		Name = name;
		Root = root;
	}

	public string Path { get; }
	public string Name { get; }
	public ThemeNode Root { get; }

	public ThemeNode Display => Root["display"];
	public ThemeNode StaticImages => Root["static_images"];
	public ThemeNode StaticText => Root["static_text"];
	public ThemeNode Stats => Root["STATS"];

	public static ThemeConfig Load(string themeName)
	{
		var dir = ResourceLocator.ThemeDir(themeName);
		var yaml = File.ReadAllText(System.IO.Path.Combine(dir, "theme.yaml"));
		IDeserializer deserializer = new DeserializerBuilder().Build();
		var root = deserializer.Deserialize<object>(yaml);
		return new ThemeConfig(dir, themeName, new ThemeNode(root));
	}

	public static (int Width, int Height) QuickCanvasSize(string themeName)
	{
		var path = System.IO.Path.Combine(ResourceLocator.ThemeDir(themeName), "theme.yaml");
		var size = string.Empty;
		var orientation = "portrait";

		foreach (var raw in File.ReadLines(path))
		{
			var line = raw.Trim();
			if (line.StartsWith("DISPLAY_SIZE:", StringComparison.OrdinalIgnoreCase))
				size = CleanValue(line[(line.IndexOf(':') + 1)..]);
			else if (line.StartsWith("DISPLAY_ORIENTATION:", StringComparison.OrdinalIgnoreCase))
				orientation = CleanValue(line[(line.IndexOf(':') + 1)..]);
			else if (line.StartsWith("STATS:", StringComparison.OrdinalIgnoreCase))
				break;
		}

		var (w, h) = ThemeEngine.SizeFromString(size);
		return orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase) ? (h, w) : (w, h);

		static string CleanValue(string raw)
		{
			var value = raw.Trim();
			var hash = value.IndexOf('#');
			if (hash >= 0)
				value = value[..hash].Trim();
			if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
				value = value[1..^1];
			return value;
		}
	}

	public string ResolvePath(string relative)
	{
		return System.IO.Path.Combine(Path, relative);
	}
}
