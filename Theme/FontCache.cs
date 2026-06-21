using System.Drawing;
using System.Drawing.Text;

namespace TuringMonitor.Theme;

public sealed class FontCache : IDisposable
{
	private readonly PrivateFontCollection _collection = new();
	private readonly Dictionary<string, FontFamily> _families = new();
	private readonly Dictionary<(string, float), Font> _fonts = new();

	public void Dispose()
	{
		foreach (Font font in _fonts.Values)
			font.Dispose();
		_fonts.Clear();
		_collection.Dispose();
	}

	public Font Get(string fontPath, float sizePx)
	{
		(string fontPath, float sizePx) key = (fontPath, sizePx);
		if (_fonts.TryGetValue(key, out Font? font))
			return font;

		FontFamily family = GetFamily(fontPath);
		font = new Font(family, sizePx, FontStyle.Regular, GraphicsUnit.Pixel);
		_fonts[key] = font;
		return font;
	}

	private FontFamily GetFamily(string fontPath)
	{
		if (_families.TryGetValue(fontPath, out FontFamily? family))
			return family;

		try
		{
			HashSet<string> before = _collection.Families.Select(f => f.Name).ToHashSet();
			_collection.AddFontFile(fontPath);
			family = _collection.Families.FirstOrDefault(f => !before.Contains(f.Name)) ?? _collection.Families[^1];
		}
		catch (Exception)
		{
			family = FontFamily.GenericMonospace;
		}

		_families[fontPath] = family;
		return family;
	}
}
