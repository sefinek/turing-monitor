using System.IO;
using System.Text.Json;
using TuringMonitor.Logging;

namespace TuringMonitor.Configuration;

public static class SettingsStore
{
	private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

	public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuringMonitor");

	private static string FilePath => Path.Combine(DataDirectory, "settings.json");

	public static MonitorSettings Load()
	{
		try
		{
			if (File.Exists(FilePath))
				return JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(FilePath)) ?? new MonitorSettings();
		}
		catch (Exception ex)
		{
			AppLog.Warn($"Failed to load settings, using defaults: {ex.Message}");
		}

		return new MonitorSettings();
	}

	public static void Save(MonitorSettings settings)
	{
		try
		{
			Directory.CreateDirectory(DataDirectory);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
		}
		catch (Exception ex)
		{
			AppLog.Error(ex, "Failed to save settings");
		}
	}
}
