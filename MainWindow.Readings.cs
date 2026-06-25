using System.Windows;
using TuringMonitor.Logging;
using TuringMonitor.Sensors;

namespace TuringMonitor;

public partial class MainWindow
{
	private volatile SystemStats? _lastStats;
	private volatile bool _readingsActive;
	private CancellationTokenSource? _readingsCts;
	private Task? _readingsLoop;

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

		rdNetDown.Text = NetRateFormatter.Format(s.NetDownKbps, _settings.NetUnits);
		rdNetUp.Text = NetRateFormatter.Format(s.NetUpKbps, _settings.NetUnits);

		var link = _settings.NetLinkMbps > 0 ? _settings.NetLinkMbps : (int)Math.Round(s.NetLinkMbps);
		rdNetLink.Text = link > 0
			? $"{link} Mb/s ({(_settings.NetLinkMbps > 0 ? "manual" : "auto")})"
			: "unknown";
	}
}
