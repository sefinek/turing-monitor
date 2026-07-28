using System.IO;

namespace TuringMonitor.Theme;

public static class ResourceLocator
{
	private static string? _dataRoot;

	public static string DataRoot => _dataRoot ??= FindDataRoot();
	public static string ThemesDir => Path.Combine(DataRoot, "themes");
	public static string FontsDir => Path.Combine(DataRoot, "fonts");

	public static IEnumerable<string> ListThemes()
	{
		if (!Directory.Exists(ThemesDir))
			yield break;

		foreach (var dir in Directory.GetDirectories(ThemesDir))
			if (File.Exists(Path.Combine(dir, "theme.yaml")))
				yield return Path.GetFileName(dir);
	}

	public static string ThemeDir(string themeName)
	{
		return Path.Combine(ThemesDir, themeName);
	}

	public static string? ThemePreview(string themeName)
	{
		var path = Path.Combine(ThemeDir(themeName), "preview.png");
		return File.Exists(path) ? path : null;
	}

	private static string FindDataRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, "data");
			if (Directory.Exists(Path.Combine(candidate, "themes")))
				return candidate;
			dir = dir.Parent;
		}

		return Path.Combine(AppContext.BaseDirectory, "data");
	}
}
