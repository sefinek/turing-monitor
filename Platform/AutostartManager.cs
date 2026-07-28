using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using TuringMonitor.Logging;
using TuringMonitor.Theme;

namespace TuringMonitor.Platform;

public static class AutostartManager
{
	private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "TuringMonitor";
	private const string TaskName = "TuringMonitor";
	private const int UacDeclinedWin32Error = 1223;

	private const string TaskDescription =
		"Launches Turing Monitor elevated at logon, so sensors that need administrator rights (e.g. CPU temperature) "
		+ "work without a UAC prompt every time. Managed by Turing Monitor - disable this from the app's Settings "
		+ "instead of deleting it here.";

	// schtasks.exe /Create has no way to set a task Description (its /D flag is only the
	// day-of-week/month selector for WEEKLY/MONTHLY schedules), so task creation goes through
	// data/scripts/autostart-register.ps1 via PowerShell's ScheduledTasks module instead, which
	// supports it directly. Query/Delete stay on schtasks.exe since it already handles those fine.
	private static readonly string RegisterScriptPath = Path.Combine(ResourceLocator.DataRoot, "scripts", "autostart-register.ps1");

	public static bool IsEnabled()
	{
		return IsRegistryEnabled() || IsAdminEnabled();
	}

	public static bool IsAdminEnabled()
	{
		return IsTaskRegistered();
	}

	// Registers autostart via the normal Run key (no elevation) or, when asAdmin is true, via a
	// scheduled task with "run with highest privileges" - the only way to auto-elevate at logon
	// without a UAC prompt every single time. Creating/removing that task does need one UAC prompt
	// right now, since granting future silent elevation is itself a privileged operation.
	public static void SetEnabled(bool enabled, bool asAdmin)
	{
		if (!enabled)
		{
			RemoveRegistryEntry();
			RemoveTask();
			return;
		}

		if (asAdmin)
		{
			RemoveRegistryEntry();
			if (!IsTaskRegistered())
				CreateElevatedTask();
		}
		else
		{
			RemoveTask();
			SetRegistryEntry();
		}
	}

	private static bool IsRegistryEnabled()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
			return key?.GetValue(ValueName) is string;
		}
		catch (Exception ex)
		{
			AppLog.Error(ex, "Failed to read autostart registry state");
			return false;
		}
	}

	private static void SetRegistryEntry()
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)
		                        ?? Registry.CurrentUser.CreateSubKey(RunKey);

		var exePath = Environment.ProcessPath;
		if (!string.IsNullOrEmpty(exePath))
			key.SetValue(ValueName, $"\"{exePath}\"");
	}

	private static void RemoveRegistryEntry()
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
		if (key?.GetValue(ValueName) is not null)
			key.DeleteValue(ValueName, false);
	}

	private static bool IsTaskRegistered()
	{
		try
		{
			var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\"")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using Process? process = Process.Start(psi);
			process?.WaitForExit(5000);
			return process?.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static void CreateElevatedTask()
	{
		var exePath = Environment.ProcessPath;
		if (string.IsNullOrEmpty(exePath))
			throw new InvalidOperationException("Could not resolve the application path.");

		if (!File.Exists(RegisterScriptPath))
			throw new InvalidOperationException($"Missing autostart script: {RegisterScriptPath}");

		var args = "-NoProfile -ExecutionPolicy Bypass -File "
		           + $"\"{RegisterScriptPath}\" -TaskName \"{TaskName}\" -ExePath \"{exePath}\" -Description \"{TaskDescription}\"";
		RunElevated("powershell.exe", args, "register the elevated autostart task");
	}

	private static void RemoveTask()
	{
		if (!IsTaskRegistered())
			return;

		RunElevated("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", "remove the elevated autostart task");
	}

	private static void RunElevated(string fileName, string arguments, string action)
	{
		var psi = new ProcessStartInfo(fileName, arguments)
		{
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = ProcessWindowStyle.Hidden
		};

		Process process;
		try
		{
			process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName} to {action}.");
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == UacDeclinedWin32Error)
		{
			throw new InvalidOperationException("The UAC prompt was declined.", ex);
		}

		using (process)
		{
			if (!process.WaitForExit(15000))
				throw new InvalidOperationException($"Timed out trying to {action}.");
			if (process.ExitCode != 0)
				throw new InvalidOperationException($"Failed to {action} ({fileName} exit code {process.ExitCode}).");
		}
	}
}
