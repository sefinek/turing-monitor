using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using TuringMonitor.Configuration;
using TuringMonitor.Display;
using TuringMonitor.Logging;
using TuringMonitor.Platform;
using TuringMonitor.Theme;
using Orientation = TuringMonitor.Display.Orientation;

namespace TuringMonitor;

public partial class MainWindow
{
	private const string BuiltInDashboardLabel = "Built-in dashboard";

	private bool _pendingUpdate;

	private void InitializeOptionLists()
	{
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
	}

	private void btnSettings_Click(object sender, RoutedEventArgs e)
	{
		settingsOverlay.Visibility = Visibility.Visible;
	}

	private void btnSettingsClose_Click(object sender, RoutedEventArgs e)
	{
		settingsOverlay.Visibility = Visibility.Collapsed;
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

	private void btnRefresh_Click(object sender, RoutedEventArgs e)
	{
		RefreshPorts();
		AppLog.Info("COM ports refreshed");
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
		_settings.AutoReconnect = chkAutoReconnect.IsChecked == true;
		_settings.CheckForUpdates = chkCheckForUpdates.IsChecked == true;

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

		SelectOrDefault<OrientationOption>(cmbOrientation, o => o.Value == _settings.Orientation);
		SelectOrDefault<IntervalOption>(cmbInterval, i => i.Ms == _settings.IntervalMs);

		chkResetOnStartup.IsChecked = _settings.ResetOnStartup;
		chkStartMinimized.IsChecked = _settings.StartMinimized;
		chkAutostart.IsChecked = AutostartManager.IsEnabled();
		chkAutoStartMonitor.IsChecked = _settings.StartMonitoringOnLaunch;
		chkExitWhenNoEthernet.IsChecked = _settings.ExitWhenNoEthernet;
		chkExitWhenAway.IsChecked = _settings.ExitWhenAway;
		chkAutoReconnect.IsChecked = _settings.AutoReconnect;
		chkCheckForUpdates.IsChecked = _settings.CheckForUpdates;

		txtTimeFormat.Text = _settings.TimeFormat;
		txtDateFormat.Text = _settings.DateFormat;

		SelectOrDefault<LocaleOption>(cmbLocale, l => l.Culture == _settings.DateCulture);
		SelectOrDefault<LinkSpeedOption>(cmbLinkSpeed, l => l.Mbps == _settings.NetLinkMbps);
		SelectOrDefault<UnitsOption>(cmbUnits, u => u.Value == _settings.NetUnits);

		SelectOrDefault<NetInterfaceOption>(cmbNetInterface, n => n.Id == _settings.NetInterface);
		_settings.NetInterface = (cmbNetInterface.SelectedItem as NetInterfaceOption)?.Id ?? "";

		SelectOrDefault<DiskOption>(cmbDisk, d => d.Root == _settings.DiskDrive);
		_settings.DiskDrive = (cmbDisk.SelectedItem as DiskOption)?.Root ?? "";

		SelectOrDefault<ThemeItem>(cmbTheme, t => string.IsNullOrEmpty(_settings.ThemeName)
			? t.IsDashboard
			: !t.IsDashboard && t.Name == _settings.ThemeName);

		if (_settings.StartMinimized)
			WindowState = WindowState.Minimized;
	}

	private static void SelectOrDefault<T>(ComboBox combo, Func<T, bool> predicate) where T : class
	{
		combo.SelectedItem = combo.Items.OfType<T>().FirstOrDefault(predicate) ?? combo.Items[0];
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
			DiskDrive = _settings.DiskDrive,
			AutoReconnect = _settings.AutoReconnect
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

	private void cmbPort_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!IsLoaded)
			return;
		_settings.ComPort = cmbPort.SelectedItem as string ?? "AUTO";
		SettingsStore.Save(_settings);
		MarkPending();
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
}
