using Microsoft.Win32;
using TuringMonitor.Logging;

namespace TuringMonitor.Platform;

public static class AutostartManager
{
	private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "TuringMonitor";

	public static bool IsEnabled()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
			return key?.GetValue(ValueName) is string;
		}
		catch (Exception ex)
		{
			AppLog.Error(ex, "Failed to read autostart state");
			return false;
		}
	}

	public static void SetEnabled(bool enabled)
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)
		                        ?? Registry.CurrentUser.CreateSubKey(RunKey);

		if (enabled)
		{
			var exePath = Environment.ProcessPath;
			if (!string.IsNullOrEmpty(exePath))
				key.SetValue(ValueName, $"\"{exePath}\"");
		}
		else if (key.GetValue(ValueName) is not null)
		{
			key.DeleteValue(ValueName, false);
		}
	}
}
