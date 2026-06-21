using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using NLog;
using TuringMonitor.Monitor;

namespace TuringMonitor;

public partial class App : Application
{
	private const int AttachParentProcess = -1;

	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	[DllImport("kernel32.dll")]
	private static extern bool AttachConsole(int dwProcessId);

	protected override void OnStartup(StartupEventArgs e)
	{
		_ = AttachConsole(AttachParentProcess);

		try
		{
			Console.OutputEncoding = Encoding.UTF8;
		}
		catch
		{
		}

		base.OnStartup(e);
		Logger.Info("Application started");

		MonitorSettings settings = SettingsStore.Load();
		if (OutOfHomeGuard.ShouldExit(settings))
		{
			Logger.Info("Out-of-home conditions met - shutting down without showing the window");
			Shutdown();
			return;
		}

		new MainWindow().Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		Logger.Info("Application exiting");
		LogManager.Shutdown();
		base.OnExit(e);
	}
}
