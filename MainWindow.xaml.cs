using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Imaging;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TuringMonitor.Display;
using TuringMonitor.Logging;
using TuringMonitor.Monitor;
using TuringMonitor.Sensors;
using TuringMonitor.Theme;
using Drawing = System.Drawing;
using Orientation = TuringMonitor.Display.Orientation;

namespace TuringMonitor;

[SuppressMessage("Reliability", "CA1001",
	Justification = "MonitorService is disposed in OnClosed, per the WPF window lifecycle.")]
public partial class MainWindow : Window
{
	private const string BuiltInDashboardLabel = "Built-in dashboard";
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
	private volatile SystemStats? _lastStats;
	private bool _pendingUpdate;
	private volatile bool _previewActive = true;
	private bool _previewFrozen;
	private volatile bool _readingsActive;
	private CancellationTokenSource? _readingsCts;
	private Task? _readingsLoop;
	private TrayIcon? _tray;
	private ContextMenu? _trayMenu;
	private bool _trayReady;

	public MainWindow()
	{
		InitializeComponent();

		UiLogTarget.Logged += OnLog;
		_service.RunningChanged += OnRunningChanged;
		_service.FrameRendered += OnFrameRendered;
		_service.StatsUpdated += OnStatsUpdated;

		cmbOrientation.ItemsSource = new[]
		{
			new OrientationOption("Landscape", Orientation.Landscape),
			new OrientationOption("Portrait", Orientation.Portrait),
			new OrientationOption("Landscape (flipped)", Orientation.ReverseLandscape),
			new OrientationOption("Portrait (flipped)", Orientation.ReversePortrait)
		};
		cmbOrientation.SelectedIndex = 0;

		cmbInterval.ItemsSource = new[]
		{
			new IntervalOption("1 second", 1000),
			new IntervalOption("2 seconds", 2000),
			new IntervalOption("3 seconds", 3000),
			new IntervalOption("5 seconds", 5000)
		};

		cmbLocale.ItemsSource = new[]
		{
			new LocaleOption("System default", ""),
			new LocaleOption("English", "en-US"),
			new LocaleOption("Polski", "pl-PL")
		};

		cmbLinkSpeed.ItemsSource = new[]
		{
			new LinkSpeedOption("Auto-detect", 0),
			new LinkSpeedOption("100 Mb/s", 100),
			new LinkSpeedOption("1000 Mb/s", 1000),
			new LinkSpeedOption("2500 Mb/s", 2500)
		};

		cmbUnits.ItemsSource = new[]
		{
			new UnitsOption("Bytes (KB/s, MB/s)", "bytes"),
			new UnitsOption("Bits (Kbit/s, Mbit/s)", "bits")
		};

		var nics = new List<NetInterfaceOption> { new("Auto (busiest)", "") };
		try
		{
			foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (ni.OperationalStatus != OperationalStatus.Up)
					continue;
				if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
					continue;
				nics.Add(new NetInterfaceOption(ni.Name, ni.Id));
			}
		}
		catch
		{
		}

		cmbNetInterface.ItemsSource = nics;

		var disks = new List<DiskOption> { new("System drive", "") };
		try
		{
			foreach (DriveInfo drive in DriveInfo.GetDrives())
				if (drive is { IsReady: true, DriveType: DriveType.Fixed })
					disks.Add(new DiskOption(drive.Name.TrimEnd('\\'), drive.Name));
		}
		catch
		{
		}

		cmbDisk.ItemsSource = disks;

		var themes = new List<ThemeItem> { new(BuiltInDashboardLabel, true, true) };
		foreach (var name in ResourceLocator.ListThemes())
			themes.Add(new ThemeItem(name, IsThemeCompatible(name), false));
		cmbTheme.ItemsSource = themes;

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

	private void btnSettings_Click(object sender, RoutedEventArgs e)
	{
		settingsOverlay.Visibility = Visibility.Visible;
	}

	private void btnFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			var dir = SettingsStore.DataDirectory;
			Directory.CreateDirectory(dir);
			Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
		}
		catch (Exception ex)
		{
			AppLog.Error(ex, "Failed to open data folder");
		}
	}

	private void btnSettingsClose_Click(object sender, RoutedEventArgs e)
	{
		settingsOverlay.Visibility = Visibility.Collapsed;
	}

	private void btnReadings_Click(object sender, RoutedEventArgs e)
	{
		if (_lastStats is not null)
			RenderReadings(_lastStats);

		readingsOverlay.Visibility = Visibility.Visible;
		_readingsActive = true;
		EnsureReadingsLoop();
	}

	private void btnReadingsClose_Click(object sender, RoutedEventArgs e)
	{
		readingsOverlay.Visibility = Visibility.Collapsed;
		_readingsActive = false;
	}

	private void OnStatsUpdated(SystemStats stats)
	{
		_lastStats = stats;
		Dispatcher.BeginInvoke(() =>
		{
			if (readingsOverlay.Visibility == Visibility.Visible)
				RenderReadings(stats);
		});
	}

	private void EnsureReadingsLoop()
	{
		if (_readingsLoop is not null)
			return;

		_readingsCts = new CancellationTokenSource();
		CancellationToken token = _readingsCts.Token;
		_readingsLoop = Task.Run(() => ReadingsLoop(token));
	}

	private void ReadingsLoop(CancellationToken token)
	{
		SensorHub? hub = null;
		try
		{
			while (!token.IsCancellationRequested)
			{
				if (_readingsActive && !_service.IsRunning)
				{
					hub ??= new SensorHub(false)
					{
						NetInterfaceId = _settings.NetInterface,
						DiskRoot = _settings.DiskDrive
					};
					SystemStats stats = hub.Read();
					_lastStats = stats;
					Dispatcher.BeginInvoke(() =>
					{
						if (readingsOverlay.Visibility == Visibility.Visible)
							RenderReadings(stats);
					});
				}
				else if (hub is not null)
				{
					hub.Dispose();
					hub = null;
				}

				if (token.WaitHandle.WaitOne(1000))
					break;
			}
		}
		catch (Exception ex)
		{
			AppLog.Error(ex, "Readings sampler failed");
		}
		finally
		{
			hub?.Dispose();
		}
	}

	private void RenderReadings(SystemStats s)
	{
		rdCpuName.Text = s.CpuName;
		rdCpuLoad.Text = $"{s.CpuLoadPercent:0} %";
		rdCpuClock.Text = s.CpuClockMhz > 0 ? $"{s.CpuClockMhz / 1000.0:0.00} GHz" : "—";
		rdCpuTemp.Text = s.CpuTempAvailable ? $"{s.CpuTempC:0} °C" : "n/a";

		rdGpuName.Text = s.GpuAvailable ? s.GpuName : "GPU (none)";
		rdGpuLoad.Text = s.GpuAvailable ? $"{s.GpuLoadPercent:0} %" : "n/a";
		rdGpuTemp.Text = s.GpuAvailable ? $"{s.GpuTempC:0} °C" : "n/a";
		rdGpuMem.Text = s.GpuAvailable ? $"{s.GpuMemUsedPercent:0} %" : "n/a";

		rdRamUsed.Text = $"{s.RamUsedGb:0.0} GB";
		rdRamTotal.Text = $"{s.RamTotalGb:0.0} GB";
		rdRamPct.Text = $"{s.RamUsedPercent:0} %";

		rdDiskUsed.Text = $"{s.DiskUsedGb:0} GB";
		rdDiskTotal.Text = $"{s.DiskTotalGb:0} GB";
		rdDiskPct.Text = $"{s.DiskUsedPercent:0} %";

		rdNetDown.Text = FormatRate(s.NetDownKbps);
		rdNetUp.Text = FormatRate(s.NetUpKbps);

		var link = _settings.NetLinkMbps > 0 ? _settings.NetLinkMbps : (int)Math.Round(s.NetLinkMbps);
		rdNetLink.Text = link > 0
			? $"{link} Mb/s ({(_settings.NetLinkMbps > 0 ? "manual" : "auto")})"
			: "unknown";
	}

	private string FormatRate(double kbps)
	{
		if (_settings.NetUnits == "bits")
		{
			var kbit = kbps * 8.0;
			return kbit >= 1000 ? $"{kbit / 1000.0:0.0} Mbit/s" : $"{kbit:0} Kbit/s";
		}

		return kbps >= 1024 ? $"{kbps / 1024.0:0.0} MB/s" : $"{kbps:0} KB/s";
	}

	private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
	{
		_settings.ComPort = cmbPort.SelectedItem as string ?? "AUTO";
		_settings.Brightness = (int)sldBrightness.Value;
		_settings.Orientation = (cmbOrientation.SelectedItem as OrientationOption)?.Value ?? Orientation.Landscape;
		_settings.ResetOnStartup = chkResetOnStartup.IsChecked == true;
		_settings.StartMinimized = chkStartMinimized.IsChecked == true;
		_settings.StartMonitoringOnLaunch = chkAutoStartMonitor.IsChecked == true;
		_settings.IntervalMs = (cmbInterval.SelectedItem as IntervalOption)?.Ms ?? 1000;
		_settings.Autostart = chkAutostart.IsChecked == true;
		_settings.ThemeName = SelectedThemeName();
		_settings.TimeFormat = txtTimeFormat.Text;
		_settings.DateFormat = txtDateFormat.Text;
		_settings.DateCulture = (cmbLocale.SelectedItem as LocaleOption)?.Culture ?? "";
		_settings.NetLinkMbps = (cmbLinkSpeed.SelectedItem as LinkSpeedOption)?.Mbps ?? 0;
		_settings.NetUnits = (cmbUnits.SelectedItem as UnitsOption)?.Value ?? "bytes";
		_settings.NetInterface = (cmbNetInterface.SelectedItem as NetInterfaceOption)?.Id ?? "";
		_settings.DiskDrive = (cmbDisk.SelectedItem as DiskOption)?.Root ?? "";
		_settings.ExitWhenNoEthernet = chkExitWhenNoEthernet.IsChecked == true;
		_settings.ExitWhenAway = chkExitWhenAway.IsChecked == true;

		try
		{
			AutostartManager.SetEnabled(_settings.Autostart);
		}
		catch (Exception ex)
		{
			AppLog.Error(ex, "Failed to update autostart");
		}

		SettingsStore.Save(_settings);
		AppLog.Info("Settings saved");
		settingsOverlay.Visibility = Visibility.Collapsed;
		MarkPending();
	}

	private void ApplySettingsToUi()
	{
		if (!string.IsNullOrEmpty(_settings.ComPort))
			cmbPort.SelectedItem = _settings.ComPort;
		cmbPort.SelectedItem ??= "AUTO";

		sldBrightness.Value = _settings.Brightness;

		foreach (OrientationOption item in cmbOrientation.Items)
			if (item.Value == _settings.Orientation)
			{
				cmbOrientation.SelectedItem = item;
				break;
			}

		foreach (IntervalOption item in cmbInterval.Items)
			if (item.Ms == _settings.IntervalMs)
			{
				cmbInterval.SelectedItem = item;
				break;
			}

		cmbInterval.SelectedItem ??= cmbInterval.Items[0];

		chkResetOnStartup.IsChecked = _settings.ResetOnStartup;
		chkStartMinimized.IsChecked = _settings.StartMinimized;
		chkAutostart.IsChecked = AutostartManager.IsEnabled();
		chkAutoStartMonitor.IsChecked = _settings.StartMonitoringOnLaunch;
		chkExitWhenNoEthernet.IsChecked = _settings.ExitWhenNoEthernet;
		chkExitWhenAway.IsChecked = _settings.ExitWhenAway;

		txtTimeFormat.Text = _settings.TimeFormat;
		txtDateFormat.Text = _settings.DateFormat;

		foreach (LocaleOption item in cmbLocale.Items)
			if (item.Culture == _settings.DateCulture)
			{
				cmbLocale.SelectedItem = item;
				break;
			}

		cmbLocale.SelectedItem ??= cmbLocale.Items[0];

		foreach (LinkSpeedOption item in cmbLinkSpeed.Items)
			if (item.Mbps == _settings.NetLinkMbps)
			{
				cmbLinkSpeed.SelectedItem = item;
				break;
			}

		cmbLinkSpeed.SelectedItem ??= cmbLinkSpeed.Items[0];

		foreach (UnitsOption item in cmbUnits.Items)
			if (item.Value == _settings.NetUnits)
			{
				cmbUnits.SelectedItem = item;
				break;
			}

		cmbUnits.SelectedItem ??= cmbUnits.Items[0];

		foreach (NetInterfaceOption item in cmbNetInterface.Items)
			if (item.Id == _settings.NetInterface)
			{
				cmbNetInterface.SelectedItem = item;
				break;
			}

		cmbNetInterface.SelectedItem ??= cmbNetInterface.Items[0];
		_settings.NetInterface = (cmbNetInterface.SelectedItem as NetInterfaceOption)?.Id ?? "";

		foreach (DiskOption item in cmbDisk.Items)
			if (item.Root == _settings.DiskDrive)
			{
				cmbDisk.SelectedItem = item;
				break;
			}

		cmbDisk.SelectedItem ??= cmbDisk.Items[0];
		_settings.DiskDrive = (cmbDisk.SelectedItem as DiskOption)?.Root ?? "";

		foreach (ThemeItem item in cmbTheme.Items)
		{
			var match = string.IsNullOrEmpty(_settings.ThemeName)
				? item.IsDashboard
				: !item.IsDashboard && item.Name == _settings.ThemeName;
			if (match)
			{
				cmbTheme.SelectedItem = item;
				break;
			}
		}

		cmbTheme.SelectedItem ??= cmbTheme.Items[0];

		if (_settings.StartMinimized)
			WindowState = WindowState.Minimized;
	}

	private void RefreshPorts()
	{
		var items = new List<string> { "AUTO" };
		try
		{
			foreach (SerialPortLocator.SerialDevice device in SerialPortLocator.EnumerateSerialDevices())
				items.Add(device.PortName);
		}
		catch
		{
		}

		var current = cmbPort.SelectedItem as string;
		cmbPort.ItemsSource = items;
		cmbPort.SelectedItem = current is not null && items.Contains(current) ? current : "AUTO";
	}

	private MonitorSettings BuildSettings()
	{
		return new MonitorSettings
		{
			ComPort = cmbPort.SelectedItem as string ?? "AUTO",
			Brightness = (int)sldBrightness.Value,
			Orientation = (cmbOrientation.SelectedItem as OrientationOption)?.Value ?? Orientation.Landscape,
			IntervalMs = (cmbInterval.SelectedItem as IntervalOption)?.Ms ?? _settings.IntervalMs,
			ResetOnStartup = _settings.ResetOnStartup,
			StartMinimized = _settings.StartMinimized,
			Autostart = _settings.Autostart,
			ThemeName = SelectedThemeName(),
			TimeFormat = _settings.TimeFormat,
			DateFormat = _settings.DateFormat,
			DateCulture = _settings.DateCulture,
			NetLinkMbps = _settings.NetLinkMbps,
			NetUnits = _settings.NetUnits,
			NetInterface = _settings.NetInterface,
			DiskDrive = _settings.DiskDrive
		};
	}

	private string SelectedThemeName()
	{
		return cmbTheme.SelectedItem is ThemeItem { IsDashboard: false } item ? item.Name : string.Empty;
	}

	private static bool IsThemeCompatible(string name)
	{
		try
		{
			var (w, h) = ThemeConfig.QuickCanvasSize(name);
			return Math.Min(w, h) == 320 && Math.Max(w, h) == 480;
		}
		catch
		{
			return false;
		}
	}

	private void btnRefresh_Click(object sender, RoutedEventArgs e)
	{
		RefreshPorts();
		AppLog.Info("COM ports refreshed");
	}

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

	private void cmbOrientation_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!IsLoaded)
			return;
		_settings.Orientation = (cmbOrientation.SelectedItem as OrientationOption)?.Value ?? Orientation.Landscape;
		SettingsStore.Save(_settings);
		MarkPending();
	}

	private void cmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!IsLoaded)
			return;
		_settings.ThemeName = SelectedThemeName();
		SettingsStore.Save(_settings);
		AppLog.Info(string.IsNullOrEmpty(_settings.ThemeName)
			? "Theme: built-in dashboard (press Start/Update to apply)"
			: $"Theme: {_settings.ThemeName} (press Start/Update to apply)");

		ShowThemePreview();
		MarkPending();
		UpdateControlState();
	}

	private void MarkPending()
	{
		if (!_service.IsRunning)
			return;
		_pendingUpdate = true;
		UpdateControlState();
	}

	private void UpdateControlState()
	{
		var running = _service.IsRunning;
		var isDashboard = string.IsNullOrEmpty(SelectedThemeName());

		btnStop.IsEnabled = running;
		cmbPort.IsEnabled = !running;
		btnRefresh.IsEnabled = !running;
		cmbOrientation.IsEnabled = isDashboard;

		if (running && _pendingUpdate)
		{
			btnStart.IsEnabled = true;
			btnStart.Content = "Update";
		}
		else
		{
			btnStart.IsEnabled = !running;
			btnStart.Content = "Start";
		}
	}

	private void ShowThemePreview()
	{
		var theme = SelectedThemeName();
		var path = string.IsNullOrEmpty(theme) ? null : ResourceLocator.ThemePreview(theme);

		if (path is not null)
		{
			imgPreview.Source = LoadImageFile(path);
			previewHint.Visibility = Visibility.Collapsed;
			_previewFrozen = _service.IsRunning;
		}
		else
		{
			if (!_service.IsRunning)
			{
				imgPreview.Source = null;
				previewHint.Visibility = Visibility.Visible;
			}

			_previewFrozen = false;
		}
	}

	private static BitmapImage LoadImageFile(string path)
	{
		var image = new BitmapImage();
		image.BeginInit();
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.UriSource = new Uri(path);
		image.EndInit();
		image.Freeze();
		return image;
	}

	private void sldBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (lblBrightness is not null)
			lblBrightness.Text = $"{(int)e.NewValue}%";
		_service.SetBrightness((int)e.NewValue);

		if (!IsLoaded)
			return;
		_settings.Brightness = (int)e.NewValue;
		SettingsStore.Save(_settings);
	}

	private void cmbPort_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!IsLoaded)
			return;
		_settings.ComPort = cmbPort.SelectedItem as string ?? "AUTO";
		SettingsStore.Save(_settings);
		MarkPending();
	}

	private void OnFrameRendered(Drawing.Bitmap bitmap)
	{
		if (!_previewActive || _previewFrozen)
			return;

		BitmapImage source = ToBitmapSource(bitmap);
		Dispatcher.BeginInvoke(() =>
		{
			imgPreview.Source = source;
			previewHint.Visibility = Visibility.Collapsed;
		});
	}

	private static BitmapImage ToBitmapSource(Drawing.Bitmap bitmap)
	{
		using var stream = new MemoryStream();
		bitmap.Save(stream, ImageFormat.Bmp);
		stream.Position = 0;

		var image = new BitmapImage();
		image.BeginInit();
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.StreamSource = stream;
		image.EndInit();
		image.Freeze();
		return image;
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

	private void OnRunningChanged(bool running)
	{
		Dispatcher.Invoke(() =>
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

	private void RestoreFromTray()
	{
		Show();
		ShowInTaskbar = true;
		WindowState = WindowState.Normal;
		Activate();
	}

	private void HideToTray()
	{
		ShowInTaskbar = false;
		Hide();
	}

	private void ShowTrayMenu()
	{
		_trayMenu ??= BuildTrayMenu();
		SetForegroundWindow(_hwnd);
		_trayMenu.IsOpen = true;
	}

	private ContextMenu BuildTrayMenu()
	{
		var menu = new ContextMenu
		{
			Placement = PlacementMode.MousePoint,
			Background = (Brush)FindResource("SurfaceBrush"),
			Foreground = (Brush)FindResource("TextBrush")
		};

		var open = new MenuItem { Header = "Open" };
		open.Click += (_, _) => RestoreFromTray();

		var exit = new MenuItem { Header = "Exit" };
		exit.Click += (_, _) => Close();

		menu.Items.Add(open);
		menu.Items.Add(exit);
		return menu;
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	private sealed record OrientationOption(string Name, Orientation Value)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record IntervalOption(string Name, int Ms)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record LocaleOption(string Name, string Culture)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record LinkSpeedOption(string Name, int Mbps)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record UnitsOption(string Name, string Value)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record NetInterfaceOption(string Name, string Id)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record DiskOption(string Name, string Root)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record ThemeItem(string Name, bool Compatible, bool IsDashboard)
	{
		public override string ToString()
		{
			return Compatible || IsDashboard ? Name : "⚠  " + Name;
		}
	}
}
