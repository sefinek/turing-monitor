using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TuringMonitor.Logging;
using TuringMonitor.Platform;

namespace TuringMonitor;

public partial class MainWindow
{

	private UpdateInfo? _updateInfo;
	private static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

	private void InitializeVersion()
	{
		lblVersion.Text = $"v{AppVersion}";
	}

	private async void CheckForUpdatesIfEnabled()
	{
		if (!_settings.CheckForUpdates)
			return;

		AppLog.Info("Checking for updates...");
		UpdateInfo? update = await UpdateChecker.CheckAsync(AppVersion);
		if (update is null)
			return;

		_updateInfo = update;
		lblVersion.Text = $"Nowa wersja jest dostępna! {update.CurrentVersion} → {update.LatestVersion}";
		lblVersion.Foreground = (Brush)FindResource("AccentBrush");
		lblVersion.ToolTip = "Open release page";
	}

	private void lblVersion_Click(object sender, MouseButtonEventArgs e)
	{
		if (_updateInfo is { ReleaseUrl: var url })
		{
			try
			{
				Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
			}
			catch (Exception ex)
			{
				AppLog.Error(ex, "Failed to open release page");
			}

			return;
		}

		MessageBox.Show(this,
			$"Turing Monitor\nVersion {AppVersion}\n\n© 2026 Sefinek",
			"About", MessageBoxButton.OK, MessageBoxImage.Information);
	}
}
