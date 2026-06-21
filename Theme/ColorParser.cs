using System.Drawing;
using System.Globalization;

namespace TuringMonitor.Theme;

public static class ColorParser
{
	public static Color Parse(object? value, Color fallback)
	{
		switch (value)
		{
			case string text:
				return ParseString(text, fallback);
			case IEnumerable<object> list:
			{
				var parts = list.Select(v => v?.ToString() ?? string.Empty).ToArray();
				return FromParts(parts, fallback);
			}
			default:
				return fallback;
		}
	}

	private static Color ParseString(string text, Color fallback)
	{
		text = text.Trim();
		if (text.StartsWith('#'))
		{
			if (int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
				return Color.FromArgb(unchecked((int)(0xFF000000 | (uint)hex)));
			return fallback;
		}

		return FromParts(text.Split(','), fallback);
	}

	private static Color FromParts(string[] parts, Color fallback)
	{
		if (parts.Length >= 3
		    && int.TryParse(parts[0].Trim(), out var r)
		    && int.TryParse(parts[1].Trim(), out var g)
		    && int.TryParse(parts[2].Trim(), out var b))
			return Color.FromArgb(Clamp(r), Clamp(g), Clamp(b));

		return fallback;
	}

	private static int Clamp(int v)
	{
		return Math.Clamp(v, 0, 255);
	}
}
