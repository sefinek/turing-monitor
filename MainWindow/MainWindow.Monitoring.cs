using System.Windows;
using System.Windows.Media;
using TuringMonitor.Configuration;
using TuringMonitor.Logging;

namespace TuringMonitor;

public partial class MainWindow
{
	private void btnStart_Click(object sender, RoutedEventArgs e)
	{
		MonitorSettings settings = BuildSettings();
		_previewFrozen = false;
		_pendingUpdate = false;

		if (_service.IsRunning)
		{
			AppLog.Info("Applying changes...");
			Task.Run(() =>
			{
				_service.Stop();
				_service.Start(settings);
			});
		}
		else
		{
			_logLines.Clear();
			txtLog.Clear();
			_service.Start(settings);
		}

		UpdateControlState();
	}

	private void btnStop_Click(object sender, RoutedEventArgs e)
	{
		AppLog.Info("Disconnecting...");
		Task.Run(() => _service.Stop());
	}

	private void OnRunningChanged(bool running)
	{
		Dispatcher.BeginInvoke(() =>
		{
			statusDot.Fill = running ? Brushes.LimeGreen : (Brush)FindResource("MutedBrush");
			lblConn.Text = running ? "Connected" : "Disconnected";

			if (!running)
			{
				_pendingUpdate = false;
				_previewFrozen = false;
				ShowThemePreview();
			}

			UpdateControlState();
		});
	}
}
