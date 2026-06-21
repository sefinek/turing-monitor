using System.IO;
using System.Text.Json;

namespace TuringMonitor.Monitor;

public static class SettingsStore
{

	private static readonly string FilePath = Path.Combine(DataDirectory, "settings.json");

	private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

	public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuringMonitor");

	public static MonitorSettings Load()
	{
		try
		{
			if (File.Exists(FilePath))
				return JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(FilePath)) ?? new MonitorSettings();
		}
		catch
		{
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
		catch
		{
		}
	}
}
