using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TuringMonitor.Configuration;
using TuringMonitor.Logging;
using TuringMonitor.Monitor;
using TuringMonitor.Platform;

namespace TuringMonitor;

[SuppressMessage("Reliability", "CA1001",
	Justification = "MonitorService is disposed in OnClosed, per the WPF window lifecycle.")]
public partial class MainWindow : Window
{
	private const int MaxLogLines = 300;
	private const int LogTrimSlack = 100;

	private const int DwmwaWindowCornerPreference = 33;
	private const int DwmwaBorderColor = 34;
	private const int DwmwcpRound = 2;
	private const int WindowBorderColor = 0x004E4947;

	private readonly Queue<string> _logLines = new();
	private readonly MonitorService _service = new();
	private readonly MonitorSettings _settings = SettingsStore.Load();

	private IntPtr _hwnd;
	private bool _exiting;

	public MainWindow()
	{
		InitializeComponent();

		UiLogTarget.Logged += OnLog;
		_service.RunningChanged += OnRunningChanged;
		_service.FrameRendered += OnFrameRendered;
		_service.StatsUpdated += OnStatsUpdated;

		InitializeOptionLists();
		InitializeVersion();

		RefreshPorts();
		ApplySettingsToUi();
		ShowThemePreview();
		UpdateControlState();

		if (_settings.StartMonitoringOnLaunch)
			btnStart_Click(this, new RoutedEventArgs());
	}

	private void btnMin_Click(object sender, RoutedEventArgs e)
	{
		WindowState = WindowState.Minimized;
	}

	private void btnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void OnLog(string line)
	{
		Dispatcher.BeginInvoke(() =>
		{
			_logLines.Enqueue(line);

			if (_logLines.Count > MaxLogLines + LogTrimSlack)
			{
				while (_logLines.Count > MaxLogLines)
					_logLines.Dequeue();
				txtLog.Text = string.Join(Environment.NewLine, _logLines);
			}
			else
			{
				if (txtLog.Text.Length > 0)
					txtLog.AppendText(Environment.NewLine);
				txtLog.AppendText(line);
			}

			txtLog.ScrollToEnd();
		});
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		_hwnd = new WindowInteropHelper(this).Handle;

		try
		{
			var preference = DwmwcpRound;
			_ = DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
			var borderColor = WindowBorderColor;
			_ = DwmSetWindowAttribute(_hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));
		}
		catch
		{
		}

		_tray = new TrayIcon(_hwnd, "Turing Monitor");
		_tray.LeftClick += RestoreFromTray;
		_tray.RightClick += ShowTrayMenu;
		_trayReady = true;

		if (WindowState == WindowState.Minimized)
			HideToTray();
	}

	protected override void OnStateChanged(EventArgs e)
	{
		_previewActive = WindowState != WindowState.Minimized;
		if (_trayReady && WindowState == WindowState.Minimized)
			HideToTray();
		base.OnStateChanged(e);
	}

	public void ExitApp()
	{
		_exiting = true;
		Close();
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		if (!_exiting)
		{
			e.Cancel = true;
			HideToTray();
		}

		base.OnClosing(e);
	}

	protected override void OnClosed(EventArgs e)
	{
		UiLogTarget.Logged -= OnLog;
		_service.StatsUpdated -= OnStatsUpdated;
		_service.RunningChanged -= OnRunningChanged;
		_service.FrameRendered -= OnFrameRendered;
		_readingsCts?.Cancel();
		_readingsCts?.Dispose();
		_tray?.Dispose();
		_service.Dispose();
		base.OnClosed(e);
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
