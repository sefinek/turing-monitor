using System.Diagnostics.CodeAnalysis;
using System.Windows;
using NLog;
using TuringMonitor.Configuration;
using TuringMonitor.Logging;
using TuringMonitor.Monitor;
using TuringMonitor.Platform;

namespace TuringMonitor;

[SuppressMessage("Reliability", "CA1001",
	Justification = "_singleInstance is disposed in OnExit, per the WPF application lifecycle.")]
public partial class App : Application
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	private MainWindow? _mainWindow;
	private SingleInstance? _singleInstance;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		Logger.Info("Application started");

		MonitorSettings settings = SettingsStore.Load();
		if (OutOfHomeGuard.ShouldExit(settings))
		{
			Logger.Info("Out-of-home conditions met - shutting down without showing the window");
			Shutdown();
			return;
		}

		_singleInstance = new SingleInstance();
		_singleInstance.Superseded += OnSuperseded;

		_mainWindow = new MainWindow();
		_mainWindow.Show();
	}

	private void OnSuperseded()
	{
		Dispatcher.BeginInvoke(() =>
		{
			AppLog.Info("Another instance was launched. Closing this instance.");
			if (_mainWindow is not null)
				_mainWindow.ExitApp();
			else
				Shutdown();
		});
	}

	protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
	{
		_mainWindow?.ExitApp();
		base.OnSessionEnding(e);
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_singleInstance?.Dispose();
		Logger.Info("Application exiting");
		LogManager.Shutdown();
		base.OnExit(e);
	}
}
